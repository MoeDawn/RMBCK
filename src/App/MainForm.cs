using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

class MainForm : Form
{
    readonly AppRuntime _rt;
    WebView2 _wv;
    bool _ready;
    bool _pageReady;
    bool _autostartCached;
    volatile bool _autostartRefreshing;

    public MainForm(AppRuntime rt)
    {
        _rt = rt;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1040, 720);
        MinimumSize = new Size(900, 620);
        BackColor = Color.FromArgb(18, 20, 28);
        KeyPreview = true;
        Text = "右键长按联动器";

        Padding = new Padding(BORDER);   // 6px 原生缩放边：WebView2 不覆盖窗体边缘
        _wv = new WebView2();
        _wv.Dock = DockStyle.Fill;
        Controls.Add(_wv);

        // 鼠标钩子：10ms 轮询触发判定（与 1.1.0 一致）
        _rt.Hook.RightButtonDown += delegate { _rt.Ctrl.OnRightDown(Environment.TickCount); };
        _rt.Hook.RightButtonUp += delegate { _rt.Ctrl.OnRightUp(); };
        _rt.Ctrl.ModifierHeld = ModifierBlocked;
        _tick = new Timer();
        _tick.Interval = 10;
        _tick.Tick += delegate { OnTickHandler(); };
        _tick.Start();

        Load += delegate { InitWebAsync(); };
    }

    Timer _tick;

    // Task 7 阶段：仅轮询触发；Task 11 扩展为写入触发日志
    void OnTickHandler()
    {
        _rt.Ctrl.OnTick(Environment.TickCount);
        UpdateHud();
    }

    // Task 12 replaces this with the native transparent HUD window
    void UpdateHud() { }

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    bool ModifierBlocked()
    {
        var s = _rt.Settings;
        if (s.BlockCtrl && (GetAsyncKeyState(0x11) & 0x8000) != 0) return true;
        if (s.BlockShift && (GetAsyncKeyState(0x10) & 0x8000) != 0) return true;
        if (s.BlockAlt && (GetAsyncKeyState(0x12) & 0x8000) != 0) return true;
        if (s.BlockWin && ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0)) return true;
        return false;
    }

    async void InitWebAsync()
    {
        try
        {
            string udf = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RMBCK", "WebView2");
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, udf, null);
            await _wv.EnsureCoreWebView2Async(env);

            CoreWebView2 core = _wv.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
            core.SetVirtualHostNameToFolderMapping("rmbck.local", _rt.WebDir, CoreWebView2HostResourceAccessKind.Allow);
            core.NavigationStarting += delegate { _pageReady = false; };
            core.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs e)
            {
                string json = null;
                try { json = e.WebMessageAsJson; } catch { }
                if (json == null) { try { json = e.TryGetWebMessageAsString(); } catch { } }
                BridgeMessage m = Bridge.Parse(json);
                if (m == null) return;
                try { OnAction(m); }
                catch (Exception ex) { ShowToast("操作失败：" + ex.Message, "err", 2000); }
            };
            core.NavigationCompleted += delegate
            {
                _ready = true;
                if (_pageReady) PushState();
            };
            RefreshAutostartAsync();
            core.Navigate("https://rmbck.local/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show("界面初始化失败：" + ex.Message, "右键长按联动器",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    public void PushState()
    {
        if (!_ready) return;
        try
        {
            var s = _rt.Settings;
            var d = new Dictionary<string, object>();
            d["enabled"] = s.Enabled;
            d["longPressMs"] = s.LongPressMs;
            d["randomJitter"] = s.RandomJitter;
            d["profile"] = s.Profile;
            d["profiles"] = ConfigStore.ListProfiles(ProfileDir());
            d["targetProcess"] = s.TargetProcess;
            d["configFolder"] = ProfileDir();
            d["blockCtrl"] = s.BlockCtrl;
            d["blockShift"] = s.BlockShift;
            d["blockAlt"] = s.BlockAlt;
            d["blockWin"] = s.BlockWin;
            d["suppressToast"] = s.SuppressToast;
            d["suppressEgg"] = s.SuppressEgg;
            d["suppressHud"] = s.SuppressHud;
            d["suppressHotkeyToast"] = s.SuppressHotkeyToast;
            d["headerAnim"] = s.HeaderAnim;
            d["theme"] = s.Theme;
            d["hotkey"] = HotkeyManager.Describe(s.HotkeyMods, s.HotkeyVk);
            d["autostart"] = _autostartCached;
            d["eggBoost"] = s.EggBoost;
            var arr = new List<object>();
            for (int i = 0; i < s.Keys.Count; i++)
            {
                var kc = s.Keys[i];
                var o = new Dictionary<string, object>();
                o["index"] = i;
                o["enabled"] = kc.Enabled;
                o["name"] = kc.Name;
                o["mode"] = kc.Mode.ToString();
                o["mods"] = kc.Mods;
                o["repeat"] = kc.RepeatIntervalMs;
                o["delay"] = kc.DelayMs;
                arr.Add(o);
            }
            d["keys"] = arr.ToArray();
            d["conflicts"] = FindConflicts();
            d["keyNames"] = KeyNames.CommonKeys;
            Send("state", d);
        }
        catch { }
    }

    // schtasks 查询放在线程池，完成后回到 UI 线程更新缓存并推送状态
    void RefreshAutostartAsync()
    {
        if (_autostartRefreshing) return;
        _autostartRefreshing = true;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            bool enabled = Autostart.IsEnabled();
            try
            {
                BeginInvoke((Action)delegate
                {
                    _autostartCached = enabled;
                    _autostartRefreshing = false;
                    if (!IsDisposed) PushState();
                });
            }
            catch
            {
                _autostartRefreshing = false;
            }
        });
    }

    public string ProfileDir()
    {
        return string.IsNullOrEmpty(_rt.Settings.ConfigFolder) ? _rt.ExeDir : _rt.Settings.ConfigFolder;
    }

    List<object> FindConflicts()
    {
        var seen = new Dictionary<string, int>();
        var res = new List<object>();
        for (int i = 0; i < _rt.Settings.Keys.Count; i++)
        {
            var kc = _rt.Settings.Keys[i];
            if (!kc.Enabled || kc.Name.Length == 0) continue;
            string key = kc.Mods + ":" + kc.Vk;
            int first;
            if (seen.TryGetValue(key, out first)) res.Add(new Dictionary<string, object> { { "a", first }, { "b", i } });
            else seen[key] = i;
        }
        return res;
    }

    public void Send(string type, object data)
    {
        if (!_ready) return;
        var o = new Dictionary<string, object>();
        o["type"] = type;
        o["data"] = data;
        try { _wv.CoreWebView2.PostWebMessageAsJson(Bridge.Serialize(o)); } catch { }
    }

    // Task 12 会替换为原生透明悬浮窗实现；在此之前 Toast 显示在网页右上角
    public void ShowToast(string text, string kind, int durationMs)
    {
        Send("toast", new Dictionary<string, object>
        {
            { "text", text }, { "kind", kind }, { "ms", durationMs }
        });
    }

    void OnAction(BridgeMessage m)
    {
        var s = _rt.Settings;
        string a = m.Action;

        if (a == "ready")
        {
            _pageReady = true;
            PushState();
            RefreshAutostartAsync();
            return;
        }
        if (a == "dragStart") { BeginDrag(); return; }
        if (a == "minimizeToTray" || a == "closeRequest")
        {
            // Task 13 实现真正的托盘/关闭逻辑
            ShowToast("该功能将在后续版本启用", "info", 1200);
            return;
        }
        if (a == "setAutostart")
        {
            bool on = Bridge.GetBool(m.Args, "on", !_autostartCached);
            bool ok = Autostart.SetEnabled(on);
            ShowToast(ok ? (on ? "已开启开机自启" : "已关闭开机自启") : "操作失败（需要管理员权限）",
                ok ? "ok" : "err", 1800);
            RefreshAutostartAsync();
            return;
        }

        // Task 5 仅回显内存状态；Task 7 接管持久化与控制器联动
        if (a == "toggleEnabled")
        {
            s.Enabled = Bridge.GetBool(m.Args, "enabled", !s.Enabled);
            PushState();
            return;
        }
        if (a == "setSetting")
        {
            ApplySetting(Bridge.GetStr(m.Args, "key", ""), m.Args);
            PushState();
            return;
        }
        if (a == "setProfile")
        {
            string name = Bridge.GetStr(m.Args, "name", "");
            if (name.Length > 0) s.Profile = name;
            PushState();
            return;
        }
        if (a == "newProfile")
        {
            string name = Bridge.GetStr(m.Args, "name", "").Trim();
            if (name.Length > 0) s.Profile = name;
            PushState();
            return;
        }
        if (a == "deleteProfile")
        {
            string name = Bridge.GetStr(m.Args, "name", "");
            if (name.Length > 0 && name == s.Profile) s.Profile = AppSettings.DefaultProfileName;
            PushState();
            return;
        }
        if (a == "startTest" || a == "stopTest")
        {
            PushState();
            return;
        }
    }

    void ApplySetting(string key, Dictionary<string, object> args)
    {
        var s = _rt.Settings;
        if (key == "longPressMs")
        {
            int v = Math.Max(100, Math.Min(2000, Bridge.GetInt(args, "value", s.LongPressMs)));
            s.LongPressMs = v;
            _rt.Ctrl.SetThreshold(v);
        }
        else if (key == "randomJitter") s.RandomJitter = Bridge.GetBool(args, "value", s.RandomJitter);
        else if (key == "blockCtrl") s.BlockCtrl = Bridge.GetBool(args, "value", s.BlockCtrl);
        else if (key == "blockShift") s.BlockShift = Bridge.GetBool(args, "value", s.BlockShift);
        else if (key == "blockAlt") s.BlockAlt = Bridge.GetBool(args, "value", s.BlockAlt);
        else if (key == "blockWin") s.BlockWin = Bridge.GetBool(args, "value", s.BlockWin);
        else if (key == "suppressToast") s.SuppressToast = Bridge.GetBool(args, "value", s.SuppressToast);
        else if (key == "suppressEgg") s.SuppressEgg = Bridge.GetBool(args, "value", s.SuppressEgg);
        else if (key == "suppressHud") s.SuppressHud = Bridge.GetBool(args, "value", s.SuppressHud);
        else if (key == "suppressHotkeyToast") s.SuppressHotkeyToast = Bridge.GetBool(args, "value", s.SuppressHotkeyToast);
        else if (key == "headerAnim") s.HeaderAnim = Bridge.GetBool(args, "value", s.HeaderAnim);
    }

    // ---- 无边框：缩放 + 拖动 ----
    const int WM_NCHITTEST = 0x0084;
    const int WM_NCLBUTTONDOWN = 0x00A1;
    const int HTCLIENT = 1, HTCAPTION = 2;
    const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
              HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    const int BORDER = 6;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HTCLIENT)
            {
                long lp = m.LParam.ToInt64();
                short sx = unchecked((short)(lp & 0xFFFF));
                short sy = unchecked((short)((lp >> 16) & 0xFFFF));
                Point p = PointToClient(new Point(sx, sy));
                int w = ClientSize.Width, h = ClientSize.Height;
                bool l = p.X <= BORDER, r = p.X >= w - BORDER;
                bool t = p.Y <= BORDER, b = p.Y >= h - BORDER;
                if (t && l) { m.Result = (IntPtr)HTTOPLEFT; return; }
                if (t && r) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                if (b && l) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                if (b && r) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                if (l) { m.Result = (IntPtr)HTLEFT; return; }
                if (r) { m.Result = (IntPtr)HTRIGHT; return; }
                if (t) { m.Result = (IntPtr)HTTOP; return; }
                if (b) { m.Result = (IntPtr)HTBOTTOM; return; }
            }
            return;
        }
        base.WndProc(ref m);
    }

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public void BeginDrag()
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    // ---- Win11 圆角 ----
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int pref = 2;   // DWMWCP_ROUND
            DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
        }
        catch { }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tick.Stop();
        _tick.Dispose();
        base.OnFormClosed(e);
    }
}
