using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// 透明悬浮窗：Toast / 彩蛋共用一窗，HUD 单独一窗。
// 必须在 Program.Main 创建的唯一 CoreWebView2Environment 上运行，
// 第二个控制器复用已启动的浏览器进程（否则每次约 2.9s）。
class OverlayForm : Form
{
    const int WM_MOUSEACTIVATE = 0x0021;
    const int MA_NOACTIVATE = 3;
    const int MaxPending = 8;
    const int SendQueued = 0;
    const int SendPosted = 1;
    const int SendPostFailed = -1;

    class PendingItem
    {
        public string Json;
        public bool IsContent;
        public PendingItem(string json, bool isContent)
        {
            Json = json;
            IsContent = isContent;
        }
    }

    // WebView2 控制器创建时可能把线程焦点移到悬浮窗；记住悬浮窗出现前
    // 的焦点窗口，只在焦点确实被悬浮窗拿走时还原，绝不主动激活任何窗口。
    static IntPtr _rememberedFocus;

    [DllImport("user32.dll")] static extern IntPtr GetFocus();
    [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    readonly CoreWebView2Environment _env;
    readonly string _webDir;
    readonly string _page;
    WebView2 _wv;
    bool _ready;
    bool _everShown;
    string _theme = "Night";

    // 内容窗（Toast / 彩蛋）：消息真正投递后才显示，避免导航未完成时计时器先隐藏
    bool _wantVisible;
    string _wantKind = "";
    int _toastDurationMs = 2000;
    bool _contentPending;

    // HUD：只记录“想不想显示”，不把 hud/hud-hide 成对塞进队列
    bool _hudWantVisible;
    bool _hudVisible;
    string _hudText = "";

    Queue<PendingItem> _pending = new Queue<PendingItem>();
    int _queuedContent;
    Timer _hideTimer;
    Timer _retryTimer;

    public OverlayForm(CoreWebView2Environment env, string webDir, string page, Size size, Point location)
    {
        _env = env;
        _webDir = webDir;
        _page = page;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(location, size);
        BackColor = Color.Fuchsia;
        TransparencyKey = Color.Fuchsia;

        _wv = new WebView2();
        _wv.Dock = DockStyle.Fill;
        _wv.DefaultBackgroundColor = Color.Transparent;
        _wv.TabStop = false;
        Controls.Add(_wv);

        Load += delegate { InitAsync(); };
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    // WS_EX_NOACTIVATE：WebView2 控制器创建时可能请求焦点，加上该样式后
    // 悬浮窗永远不会被激活，前台窗口（游戏/主窗口）不受影响。
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // DLP 环境下 WM_PAINT 可能丢失：首显强制重绘
        try { Invalidate(true); Update(); } catch { }
        // WebView2 初始化可能异步抢焦点：首帧后再尝试还原一次
        try { BeginInvoke((Action)RestoreFocusIfStolen); } catch { }
    }

    // 点击悬浮窗也不激活它（避免抢走游戏/主窗口焦点）
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_NOACTIVATE;
            return;
        }
        base.WndProc(ref m);
    }

    // 仅用于冒烟验证（CDP / 预览截图），不参与业务逻辑
    public CoreWebView2 WebViewCore
    {
        get
        {
            try { return _wv == null ? null : _wv.CoreWebView2; }
            catch { return null; }
        }
    }

    public bool IsReady { get { return _ready; } }

    public void SetTheme(string mode)
    {
        _theme = mode == "Day" ? "Day" : "Night";
        Dictionary<string, object> data = new Dictionary<string, object>();
        data["mode"] = _theme;
        Dictionary<string, object> msg = new Dictionary<string, object>();
        msg["kind"] = "theme";
        msg["mode"] = _theme;   // 兼容 overlay.js 读取顶层字段
        msg["data"] = data;
        Send(msg, false);
    }

    public void ShowToast(string text, string kind, int durationMs)
    {
        ShowToast(text, kind, durationMs, false);
    }

    // center=true：窗口内居中显示大字号卡片（宿主负责把窗口移到屏幕中央）
    public void ShowToast(string text, string kind, int durationMs, bool center)
    {
        DropQueuedContent();
        _contentPending = false;
        _wantVisible = true;
        _wantKind = "toast";
        _toastDurationMs = Math.Max(300, durationMs);
        StopHideTimer();

        Dictionary<string, object> data = new Dictionary<string, object>();
        data["text"] = text;
        data["kind"] = kind;
        if (center) data["center"] = true;
        Dictionary<string, object> msg = new Dictionary<string, object>();
        msg["kind"] = "toast";
        msg["data"] = data;

        int sent = Send(msg, true);
        if (sent == SendPosted)
        {
            // 已就绪：消息已投递，此时才显示并开始计时
            StopRetry();
            PresentWindow();
            ArmHideTimer();
        }
        else
        {
            // 未就绪：排队等 Flush；已就绪但投递失败：排队并做一次有界重试
            _contentPending = true;
            EnsureInitialized();
            if (sent == SendPostFailed) ScheduleRetry();
        }
    }

    public void ShowEgg(object[] items)
    {
        DropQueuedContent();
        _contentPending = false;
        _wantVisible = true;
        _wantKind = "egg";
        StopHideTimer();

        Dictionary<string, object> data = new Dictionary<string, object>();
        data["items"] = items;
        Dictionary<string, object> msg = new Dictionary<string, object>();
        msg["kind"] = "egg";
        msg["data"] = data;

        int sent = Send(msg, true);
        if (sent == SendPosted)
        {
            StopRetry();
            PresentWindow();
        }
        else
        {
            _contentPending = true;
            EnsureInitialized();
            if (sent == SendPostFailed) ScheduleRetry();
        }
    }

    public void ShowHud(string text)
    {
        _hudWantVisible = true;
        _hudText = text;
        EnsureInitialized();

        if (_ready && PostHud(text))
        {
            _hudVisible = true;
            PresentWindow();
        }
    }

    public void HideHud()
    {
        _hudWantVisible = false;
        if (_ready && _hudVisible)
        {
            PostHudHide();
            _hudVisible = false;
        }
        HideSafely();
    }

    // 首次使用时显示透明空白页以启动 WebView2 导航；不显示任何内容、不启动隐藏计时
    void EnsureInitialized()
    {
        if (_everShown || IsDisposed || Disposing) return;
        try
        {
            RememberFocus();
            Show();
            _everShown = true;
            RestoreFocusIfStolen();
            try { BeginInvoke((Action)RestoreFocusIfStolen); } catch { }
        }
        catch { }
    }

    // 内容已真正投递后再显示窗口；首次初始化时窗口已可见，此时是 no-op
    void PresentWindow()
    {
        if (IsDisposed || Disposing) return;
        try
        {
            if (!Visible)
            {
                RememberFocus();
                Show();
                RestoreFocusIfStolen();
                try { BeginInvoke((Action)RestoreFocusIfStolen); } catch { }
            }
            else
            {
                RestoreFocusIfStolen();
            }
            _everShown = true;
        }
        catch { }
    }

    void ArmHideTimer()
    {
        if (_hideTimer == null)
        {
            _hideTimer = new Timer();
            _hideTimer.Tick += delegate
            {
                _hideTimer.Stop();
                _wantVisible = false;
                _wantKind = "";
                ClearContent();
                HideSafely();
            };
        }
        _hideTimer.Stop();
        _hideTimer.Interval = Math.Max(300, _toastDurationMs);
        _hideTimer.Start();
    }

    void StopHideTimer()
    {
        if (_hideTimer != null) _hideTimer.Stop();
    }

    // 隐藏前清空旧 DOM：下次显示新 Toast 时不会闪出上一条内容
    void ClearContent()
    {
        if (!_ready) return;
        PostRaw("{\"kind\":\"clear\",\"data\":{}}");
    }

    // 新一轮内容取代所有尚未投递的旧内容；主题消息保留不动
    void DropQueuedContent()
    {
        if (_queuedContent <= 0) return;
        Queue<PendingItem> keep = new Queue<PendingItem>();
        while (_pending.Count > 0)
        {
            PendingItem item = _pending.Dequeue();
            if (!item.IsContent) keep.Enqueue(item);
        }
        _pending = keep;
        _queuedContent = 0;
    }

    // 驱逐可能丢掉唯一的待投递内容：清掉待显示状态并隐藏，避免 Flush 显示空窗口
    // （彩蛋空窗口永远不会收到 eggDone，会永久置顶）
    void ReconcileContentIfGone()
    {
        if (_queuedContent > 0) return;
        if (!_contentPending) return;
        _contentPending = false;
        _wantVisible = false;
        _wantKind = "";
        StopHideTimer();
        StopRetry();
        HideSafely();
    }

    // 已就绪却投递失败时做一次有界重试；Flush 再次失败会直接隐藏
    void ScheduleRetry()
    {
        if (_retryTimer == null)
        {
            _retryTimer = new Timer();
            _retryTimer.Interval = 150;
            _retryTimer.Tick += delegate
            {
                _retryTimer.Stop();
                if (_ready) Flush();   // 导航中被重置则留给 NavigationCompleted
            };
        }
        _retryTimer.Stop();
        _retryTimer.Start();
    }

    void StopRetry()
    {
        if (_retryTimer != null) _retryTimer.Stop();
    }

    static bool FocusInOverlay(IntPtr h)
    {
        if (h == IntPtr.Zero) return false;
        try
        {
            foreach (Form f in Application.OpenForms)
            {
                OverlayForm o = f as OverlayForm;
                if (o == null) continue;
                IntPtr root = IntPtr.Zero;
                try { root = o.Handle; } catch { continue; }
                if (root == IntPtr.Zero) continue;
                if (root == h || IsChild(root, h)) return true;
            }
        }
        catch { }
        return false;
    }

    static void RememberFocus()
    {
        try
        {
            IntPtr cur = GetFocus();
            if (cur == IntPtr.Zero) return;
            if (FocusInOverlay(cur)) return;   // 焦点已经在另一个悬浮窗：保留更早记住的目标
            uint pid;
            if (GetWindowThreadProcessId(cur, out pid) != GetCurrentThreadId()) return;
            _rememberedFocus = cur;
        }
        catch { }
    }

    static void RestoreFocusIfStolen()
    {
        try
        {
            IntPtr target = _rememberedFocus;
            if (target == IntPtr.Zero) return;
            if (!IsWindow(target)) { _rememberedFocus = IntPtr.Zero; return; }
            IntPtr cur = GetFocus();
            if (cur == IntPtr.Zero || !FocusInOverlay(cur)) return;
            SetFocus(target);
        }
        catch { }
    }

    void HideSafely()
    {
        try
        {
            if (!IsDisposed && !Disposing) Hide();
        }
        catch { }
    }

    async void InitAsync()
    {
        try
        {
            if (_env == null || _wv == null || _wv.IsDisposed) return;
            await _wv.EnsureCoreWebView2Async(_env);
            RestoreFocusIfStolen();
            if (IsDisposed || _wv == null || _wv.IsDisposed) return;
            CoreWebView2 core = _wv.CoreWebView2;
            if (core == null) return;

            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
            // 映射是每个 CoreWebView2 独立的：悬浮窗必须先在自己的控制器上注册
            core.SetVirtualHostNameToFolderMapping("rmbck.local", _webDir, CoreWebView2HostResourceAccessKind.Allow);
            core.NavigationStarting += delegate { _ready = false; };
            core.NavigationCompleted += delegate(object s, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (!e.IsSuccess) return;
                _ready = true;
                Flush();
                RestoreFocusIfStolen();
            };
            core.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs e)
            {
                try
                {
                    string json = null;
                    try { json = e.WebMessageAsJson; } catch { }
                    if (json == null) return;
                    BridgeMessage m = Bridge.Parse(json);
                    if (m == null) return;
                    if (m.Action == "overlayReady")
                    {
                        _ready = true;
                        Flush();
                    }
                    else if (m.Action == "eggDone")
                    {
                        _wantVisible = false;
                        _wantKind = "";
                        HideSafely();
                    }
                }
                catch { }
            };
            core.Navigate("https://rmbck.local/" + _page);
        }
        catch
        {
            // 悬浮窗失败只降级为静默：绝不阻塞主窗口或抛出未处理异常
        }
    }

    int Send(Dictionary<string, object> payload, bool isContent)
    {
        string json = Bridge.Serialize(payload);
        if (_ready)
        {
            if (PostRaw(json)) return SendPosted;
            Enqueue(json, isContent);
            return SendPostFailed;
        }
        Enqueue(json, isContent);
        return SendQueued;
    }

    void Enqueue(string json, bool isContent)
    {
        _pending.Enqueue(new PendingItem(json, isContent));
        if (isContent) _queuedContent++;
        // 导航始终失败时也不能无限增长：丢弃最旧的，保留最新内容
        while (_pending.Count > MaxPending)
        {
            PendingItem old = _pending.Dequeue();
            if (old.IsContent) _queuedContent--;
        }
        ReconcileContentIfGone();
    }

    bool PostRaw(string json)
    {
        CoreWebView2 core = WebViewCore;
        if (core == null) return false;
        try
        {
            core.PostWebMessageAsJson(json);
            return true;
        }
        catch { return false; }
    }

    bool PostHud(string text)
    {
        Dictionary<string, object> data = new Dictionary<string, object>();
        data["text"] = text;
        Dictionary<string, object> msg = new Dictionary<string, object>();
        msg["kind"] = "hud";
        msg["data"] = data;
        return PostRaw(Bridge.Serialize(msg));
    }

    bool PostHudHide()
    {
        Dictionary<string, object> msg = new Dictionary<string, object>();
        msg["kind"] = "hud-hide";
        msg["data"] = new Dictionary<string, object>();
        return PostRaw(Bridge.Serialize(msg));
    }

    void Flush()
    {
        try
        {
            bool claimed = _contentPending;
            bool hadContent = claimed && _queuedContent > 0;
            bool contentPosted = false;
            while (_pending.Count > 0)
            {
                PendingItem item = _pending.Dequeue();
                if (item.IsContent)
                {
                    if (_queuedContent > 0) _queuedContent--;
                    contentPosted = PostRaw(item.Json);
                }
                else
                {
                    PostRaw(item.Json);
                }
            }
            _queuedContent = 0;
            _contentPending = false;

            // 内容消息确实投递成功后才显示并启动隐藏计时；
            // 如果在此期间已被新一轮操作取代/取消，则不再显示。
            if (hadContent && _wantVisible)
            {
                if (contentPosted)
                {
                    StopRetry();
                    if (_wantKind == "toast")
                    {
                        PresentWindow();
                        ArmHideTimer();
                    }
                    else if (_wantKind == "egg")
                    {
                        PresentWindow();
                    }
                }
                else
                {
                    // 投递失败：不显示空窗口，也不让旧内容无限停留
                    _wantVisible = false;
                    _wantKind = "";
                    StopHideTimer();
                    StopRetry();
                    HideSafely();
                }
            }
            else if (claimed && !hadContent)
            {
                // 没有可投递内容（例如被驱逐）：保持隐藏，绝不显示空窗口
                _wantVisible = false;
                _wantKind = "";
                StopHideTimer();
                StopRetry();
                HideSafely();
            }

            // HUD 只按最终状态投递一次：按下期间已松开则不投递 hud/hud-hide，
            // 绝不让一次已结束的长按显示 HUD。投递失败时不显示空 HUD。
            if (_hudWantVisible)
            {
                if (PostHud(_hudText))
                {
                    _hudVisible = true;
                    PresentWindow();
                }
                else if (!_hudVisible)
                {
                    // 投递失败且没有旧 HUD：不要留下空置顶窗口
                    HideSafely();
                }
            }
            else if (_hudVisible)
            {
                PostHudHide();
                _hudVisible = false;
                HideSafely();
            }
        }
        catch
        {
            // 悬浮窗任何失败都只降级为静默
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _wantVisible = false;
            _hudWantVisible = false;
            _hudVisible = false;
            _contentPending = false;
            _queuedContent = 0;
            if (_hideTimer != null)
            {
                _hideTimer.Stop();
                _hideTimer.Dispose();
                _hideTimer = null;
            }
            if (_retryTimer != null)
            {
                _retryTimer.Stop();
                _retryTimer.Dispose();
                _retryTimer = null;
            }
            _pending.Clear();
        }
        base.Dispose(disposing);
    }
}
