using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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
    bool _autostartBusy;        // schtasks 切换进行中：忽略连击，前端显示忙碌
    int _autostartGen;          // 作废与切换并发的查询结果
    HotkeyManager _hotkey;
    bool _hotkeyFailed;
    const int HotkeyId = 1;
    const int WM_HOTKEY = 0x0312;
    const string HomepageUrl = "https://www.bilibili.com/video/BV1GJ411x7h7";
    bool _kbInstallFailed;
    bool _kbFailureShown;

    // 报错反馈链：进行中忽略重复提交；主窗口隐藏时中止，绝不在托盘/游戏上方弹查看器
    bool _feedbackBusy;

    // 托盘与关闭流程
    TrayManager _tray;
    bool _exiting;            // 只有真正退出才允许窗体关闭
    Form _viewer;             // 全屏报错图片查看器（DoExit 必须能关掉这个模态窗）

    // 启动骨架：WebView2 首次启动约 5s，窗口立即显示原生占位内容
    Panel _skeleton;
    Image _skeletonLogo;
    bool _firstNavigationDone;

    // 透明悬浮窗：懒创建，首次使用后在整个进程生命周期内复用
    OverlayForm _toastWin;   // Toast + 彩蛋（右上角）
    OverlayForm _hudWin;     // HUD（右下角）
    bool _hudShown;

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
        _wv.Visible = false;             // 首次 NavigationCompleted 后再显示
        Controls.Add(_wv);
        BuildSkeleton();

        // 托盘先于 WebView2 创建：窗口隐藏/关闭时程序仍可操作
        InitTray();

        // 鼠标钩子：10ms 轮询触发判定（与 1.1.0 一致）
        _rt.Hook.RightButtonDown += delegate { _rt.Ctrl.OnRightDown(Environment.TickCount); };
        _rt.Hook.RightButtonUp += delegate { _rt.Ctrl.OnRightUp(); };
        _rt.Ctrl.ModifierHeld = ModifierBlocked;

        // 键盘钩子：按键页捕获 + 概览测试面板“实际按下”
        _rt.KbHook = new KeyboardHook();
        _rt.KbHook.KeyDown += delegate(int vk) { HandleCapturedKey(vk, true); };
        _rt.KbHook.KeyUp += delegate(int vk) { HandleCapturedKey(vk, false); };
        if (!_rt.KbHook.Install()) _kbInstallFailed = true;
        _tick = new Timer();
        _tick.Interval = 10;
        _tick.Tick += delegate { OnTickHandler(); };
        _tick.Start();

        // 前台进程门控独立 750ms：进程查询频率低（与 1.1.0 一致）
        _gateTimer = new Timer();
        _gateTimer.Interval = 750;
        _gateTimer.Tick += delegate { ApplyLinkageGate(); };
        _gateTimer.Start();

        PopulateProfileComboIfNeeded();
        ApplyLinkageGate();

        // 先把骨架提交到消息队列并完成首帧，再启动 WebView2（InitWebAsync 的同步初始化不能挡住首绘）
        Load += delegate { BeginInvoke((Action)InitWebAsync); };
    }

    Timer _tick;
    Timer _gateTimer;
    bool _wasTriggered;

    // 每次 10ms 轮询触发状态；只在 false -> true 的跃迁上写一条日志并构建标签（不在每 tick 分配）
    void OnTickHandler()
    {
        _rt.Ctrl.OnTick(Environment.TickCount);
        bool now = _rt.Ctrl.IsTriggered;
        string hudLabel = null;
        if (now && !_wasTriggered)
        {
            // 必须在 OnRightUp / 下一次 OnTick 清空之前复制真正触发的行
            List<string> fired = new List<string>(_rt.Ctrl.FiredLabels);
            string label = fired.Count > 0 ? string.Join("+", fired.ToArray()) : "-";
            hudLabel = fired.Count > 0 ? string.Join(" ", fired.ToArray()) : "-";
            string proc = string.IsNullOrEmpty(_rt.Settings.TargetProcess) ? ""
                : (TargetForegroundActive() ? _rt.Settings.TargetProcess : "其他进程");
            _rt.History.Add(label, proc,
                _rt.Settings.RandomJitter ? "随机" : "标准", DateTime.Now);
            // 仅在跃迁上推送（不是周期推送）：日志页打开时即时刷新，其他页面只缓存
            Send("history", _rt.History.Snapshot());
        }
        _wasTriggered = now;
        UpdateHud(hudLabel);
    }

    // HUD 只在触发状态跃迁时更新；SuppressHud 开启时立即隐藏已显示的窗口
    void UpdateHud(string hudLabel)
    {
        if (_rt.Settings.SuppressHud)
        {
            if (_hudShown) { _hudShown = false; HideHud(); }
            return;
        }
        if (_rt.Ctrl.IsTriggered)
        {
            if (_hudShown) return;
            _hudShown = true;
            ShowHud("⚡ " + (hudLabel != null ? hudLabel : "-"));
        }
        else if (_hudShown)
        {
            _hudShown = false;
            HideHud();
        }
    }

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

    // ---- 启动骨架 ----
    void BuildSkeleton()
    {
        _skeleton = new Panel();
        _skeleton.Dock = DockStyle.Fill;
        _skeleton.BackColor = Color.FromArgb(18, 20, 28);

        var layout = new TableLayoutPanel();
        layout.Dock = DockStyle.Fill;
        layout.BackColor = Color.FromArgb(18, 20, 28);
        layout.ColumnCount = 1;
        layout.RowCount = 5;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

        var logo = new PictureBox();
        logo.Size = new Size(64, 64);
        logo.SizeMode = PictureBoxSizeMode.Zoom;
        logo.Anchor = AnchorStyles.None;
        logo.Margin = new Padding(0, 0, 0, 12);
        try
        {
            using (Stream st = typeof(MainForm).Assembly.GetManifestResourceStream("RMBCK.web.assets.icon.png"))
            {
                if (st != null)
                {
                    using (Image img = Image.FromStream(st))
                        _skeletonLogo = new Bitmap(img, 64, 64);
                    logo.Image = _skeletonLogo;
                }
            }
        }
        catch { /* 图标缺失时保留纯文字骨架 */ }
        if (logo.Image == null) logo.Visible = false;

        var title = new Label();
        title.Text = "正在加载界面…";
        title.AutoSize = true;
        title.Anchor = AnchorStyles.None;
        title.ForeColor = Color.FromArgb(154, 163, 184);
        title.BackColor = Color.Transparent;
        title.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular);
        title.Margin = new Padding(0, 0, 0, 4);

        var hint = new Label();
        hint.Text = "请稍候";
        hint.AutoSize = true;
        hint.Anchor = AnchorStyles.None;
        hint.ForeColor = Color.FromArgb(154, 163, 184);
        hint.BackColor = Color.Transparent;
        hint.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
        hint.Margin = new Padding(0);

        layout.Controls.Add(logo, 0, 1);
        layout.Controls.Add(title, 0, 2);
        layout.Controls.Add(hint, 0, 3);
        _skeleton.Controls.Add(layout);
        Controls.Add(_skeleton);
        _skeleton.BringToFront();
    }

    void ShowRealPage()
    {
        if (IsDisposed || Disposing) return;
        try
        {
            if (_skeleton != null) _skeleton.Visible = false;
            _wv.Visible = true;
            _wv.BringToFront();
        }
        catch { }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // DLP 环境下 WM_PAINT 可能丢失：骨架首显强制重绘一次
        if (_skeleton != null) { _skeleton.Invalidate(true); _skeleton.Update(); }
    }

    async void InitWebAsync()
    {
        if (IsDisposed) return;
        try
        {
            // 复用 Program.Main 创建的唯一环境；null 时仅作为独立测试宿主的兜底
            CoreWebView2Environment env = _rt.WebEnv;
            if (env == null)
            {
                string udf = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RMBCK", "WebView2");
                env = await CoreWebView2Environment.CreateAsync(null, udf, null);
                _rt.WebEnv = env;
            }
            await _wv.EnsureCoreWebView2Async(env);

            CoreWebView2 core = _wv.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
            core.SetVirtualHostNameToFolderMapping("rmbck.local", _rt.WebDir, CoreWebView2HostResourceAccessKind.Allow);
            core.NavigationStarting += delegate
            {
                // 页面重载后旧页面不再能接收消息：未完成状态一律作废，
                // 并撤销依赖页面的测试轮询 / 全局按键捕获，避免后台永久运行。
                _ready = false;
                _pageReady = false;
                _testing = false;
                try { if (_testTimer != null) _testTimer.Stop(); } catch { }
                if (_capturing) CancelCapture();
                _procLoading = false;
            };
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
            core.NavigationCompleted += delegate(object s, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (!_firstNavigationDone)
                {
                    _firstNavigationDone = true;
                    if (!e.IsSuccess)
                    {
                        // 首次导航失败：保留骨架，走与初始化异常相同的友好退出路径
                        MessageBox.Show("界面加载失败：" + e.WebErrorStatus, "右键长按联动器",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        DoExit();
                        return;
                    }
                    ShowRealPage();          // 首次导航完成：一次性换掉原生骨架
                }
                _ready = true;
                // 唯一能自愈的消息：无论页面是否已经发过 ready 都重发一次
                PushState();
            };
            RefreshAutostartAsync();
            core.Navigate("https://rmbck.local/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show("界面初始化失败：" + ex.Message, "右键长按联动器",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            DoExit();
        }
    }

    public void PushState()
    {
        // 托盘菜单文案与网页状态来自同一事实源；页面未就绪时也要同步托盘
        if (_tray != null) _tray.SetEnabledState(_rt.Settings.Enabled);
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
            d["autostartBusy"] = _autostartBusy;
            d["processLoading"] = _procLoading;
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
            if (_hotkeyFailed)
            {
                _hotkeyFailed = false;
                ShowToast("全局热键注册失败，可能已被其他程序占用", "err", 2500);
            }
            if (_kbInstallFailed && !_kbFailureShown)
            {
                _kbFailureShown = true;
                ShowToast("键盘钩子安装失败，组合键捕获不可用", "err", 2500);
            }
        }
        catch { }
    }

    // schtasks 查询放在线程池，完成后回到 UI 线程更新缓存并推送状态
    void RefreshAutostartAsync()
    {
        if (_autostartRefreshing) return;
        _autostartRefreshing = true;
        int gen = _autostartGen;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            bool enabled = Autostart.IsEnabled();
            try
            {
                BeginInvoke((Action)delegate
                {
                    // 切换操作会递增 _autostartGen：它自己的结果才是权威值，查询结果不得覆盖
                    if (gen == _autostartGen) _autostartCached = enabled;
                    _autostartRefreshing = false;
                    if (!IsDisposed) PushState();
                });
            }
            catch
            {
                // 窗体句柄已被 ShowInTaskbar 重建且 BeginInvoke 丢失：必须自行清掉忙碌标志，
                // 否则后续查询会被 if (_autostartRefreshing) 永久挡住。
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

    // ---- 透明悬浮窗（Toast / HUD / 彩蛋） ----
    // Toast 与彩蛋共用右上角窗口：普通提示 420×150，彩蛋按完整列表临时加高；
    // 窗口只创建一次，切换尺寸不会重建 WebView2。
    const int ToastWidth = 420;
    const int ToastHeight = 150;
    const int EggWidth = 440;
    const int EggHeight = 570;

    OverlayForm ToastWindow(bool forEgg)
    {
        if (_rt.WebEnv == null) return null;
        try
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int w = forEgg ? EggWidth : ToastWidth;
            // 屏幕较矮时压缩彩蛋窗口并上移，保证完整落在工作区内
            int h = forEgg ? Math.Min(EggHeight, Math.Max(ToastHeight, area.Height - 40)) : ToastHeight;
            int x = area.Right - w - 20;
            if (x < area.Left + 8) x = area.Left + 8;
            int y = area.Top + 20;
            if (y + h > area.Bottom - 8) y = area.Bottom - h - 8;
            if (y < area.Top + 8) y = area.Top + 8;
            Rectangle bounds = new Rectangle(x, y, w, h);

            if (_toastWin == null || _toastWin.IsDisposed)
            {
                _toastWin = new OverlayForm(_rt.WebEnv, _rt.WebDir, "overlay.html",
                    bounds.Size, bounds.Location);
                _toastWin.SetTheme(_rt.Settings.Theme);   // 建窗即排队主题，导航完成后先应用
            }
            else if (_toastWin.Bounds != bounds)
            {
                _toastWin.Bounds = bounds;
            }
            return _toastWin;
        }
        catch
        {
            _toastWin = null;
            return null;
        }
    }

    OverlayForm HudWindow()
    {
        if (_rt.WebEnv == null) return null;
        try
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Rectangle bounds = new Rectangle(area.Right - 340, area.Bottom - 110, 320, 90);
            if (_hudWin == null || _hudWin.IsDisposed)
            {
                _hudWin = new OverlayForm(_rt.WebEnv, _rt.WebDir, "overlay.html",
                    bounds.Size, bounds.Location);
                _hudWin.SetTheme(_rt.Settings.Theme);
            }
            else if (_hudWin.Bounds != bounds)
            {
                _hudWin.Bounds = bounds;
            }
            return _hudWin;
        }
        catch
        {
            _hudWin = null;
            return null;
        }
    }

    public void ShowToast(string text, string kind, int durationMs)
    {
        if (_rt.Settings.SuppressToast) return;
        OverlayForm w = ToastWindow(false);
        if (w == null) return;
        try { w.ShowToast(text, kind, durationMs); } catch { }
    }

    public void ShowEgg(object[] items)
    {
        if (items == null || items.Length == 0) return;
        OverlayForm w = ToastWindow(true);
        if (w == null) return;
        try { w.ShowEgg(items); } catch { }
    }

    public void ShowHud(string text)
    {
        if (_rt.Settings.SuppressHud) return;
        OverlayForm w = HudWindow();
        if (w == null) return;
        try { w.ShowHud(text); } catch { }
    }

    public void HideHud()
    {
        if (_hudWin == null || _hudWin.IsDisposed) return;
        try { _hudWin.HideHud(); } catch { }
    }

    // 主题变化时同步给已经存在的悬浮窗（未创建的窗口在创建时读取当前主题）
    void ForwardTheme()
    {
        if (_toastWin != null && !_toastWin.IsDisposed)
        {
            try { _toastWin.SetTheme(_rt.Settings.Theme); } catch { }
        }
        if (_hudWin != null && !_hudWin.IsDisposed)
        {
            try { _hudWin.SetTheme(_rt.Settings.Theme); } catch { }
        }
    }

    void OnAction(BridgeMessage m)
    {
        var s = _rt.Settings;
        string a = m.Action;

        if (a == "ready")
        {
            // 同一轮导航只处理一次；重载时 NavigationStarting 会把标志清回 false
            if (!_pageReady)
            {
                _pageReady = true;
                PushState();
                RefreshAutostartAsync();
            }
            return;
        }

        // ---- 关闭确认 / 托盘 ----
        if (a == "closeConfirm")
        {
            string result = Bridge.GetStr(m.Args, "result", "Minimize");
            bool remember = Bridge.GetBool(m.Args, "remember", false);
            if (remember)
            {
                s.CloseAction = result == "Exit" ? "Exit" : "Minimize";
                try { ConfigStore.SavePointer(s, System.IO.Path.Combine(_rt.ExeDir, "config.ini")); } catch { }
            }
            if (result == "Exit") DoExit(); else MinimizeToTray();
            return;
        }
        // 网页模态框自行关闭即可；后端不保存“对话框已打开”状态
        if (a == "cancelCloseConfirm") return;
        if (a == "minimizeToTray") { MinimizeToTray(); return; }
        if (a == "closeRequest") { RequestClose(); return; }
        if (a == "exitApp") { DoExit(); return; }

        if (a == "dragStart") { BeginDrag(); return; }
        if (a == "setAutostart")
        {
            // 上一轮 schtasks 尚未结束时忽略连击（前端同时显示忙碌）
            if (_autostartBusy) return;
            bool on = Bridge.GetBool(m.Args, "on", !_autostartCached);
            _autostartBusy = true;
            _autostartGen++;   // 作废正在执行/排队的查询结果
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                // schtasks 最长 8s，绝不能在 UI 线程同步执行
                bool ok = false;
                Exception error = null;
                try
                {
                    ok = Autostart.SetEnabled(on);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    // 成功/失败/异常都必须回 UI 线程清掉忙碌标志，否则开关会永久卡住
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            if (IsDisposed) return;
                            _autostartBusy = false;
                            _autostartGen++;   // 作废操作期间启动的查询
                            if (error != null)
                            {
                                ShowToast("操作失败：" + error.Message, "err", 1800);
                            }
                            else
                            {
                                if (ok) _autostartCached = on;   // 只有成功才更新缓存
                                ShowToast(ok ? (on ? "已开启开机自启" : "已关闭开机自启") : "操作失败（需要管理员权限）",
                                    ok ? "ok" : "err", 1800);
                            }
                            PushState();
                        });
                    }
                    catch
                    {
                        // 回 UI 线程失败（句柄重建）：清掉本 worker 持有的忙碌标志，避免开关永久卡死
                        _autostartBusy = false;
                        _autostartGen++;
                    }
                }
            });
            return;
        }
        if (a == "setTheme")
        {
            // 主题是全局设置：只写指针文件，不写方案文件
            string mode = Bridge.GetStr(m.Args, "mode", "Night");
            s.Theme = mode == "Day" ? "Day" : "Night";
            try { ConfigStore.SavePointer(s, System.IO.Path.Combine(_rt.ExeDir, "config.ini")); } catch { }
            ForwardTheme();
            PushState();
            return;
        }
        if (a == "chooseFolder")
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择配置文件的保存路径";
                dlg.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(s.ConfigFolder) && Directory.Exists(s.ConfigFolder))
                    dlg.SelectedPath = s.ConfigFolder;
                if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
                {
                    s.ConfigFolder = dlg.SelectedPath;
                    PopulateProfileComboIfNeeded();
                    SaveSettingsNow();   // 新目录立即落盘方案与指针，避免防抖窗口内关窗丢设置
                    ShowToast("配置文件位置已更新", "ok", 1600);
                    PushState();
                }
            }
            return;
        }
        if (a == "openHomepage")
        {
            try { System.Diagnostics.Process.Start(HomepageUrl); }
            catch (Exception ex) { ShowToast("无法打开作者主页：" + ex.Message, "err", 2200); }
            return;
        }
        if (a == "submitFeedback") { if (!_feedbackBusy) StartFeedbackFlow(); return; }

        // ---- 概览页动作：持久化 + 控制器联动 + 状态推送 ----
        if (a == "toggleEnabled")
        {
            s.Enabled = Bridge.GetBool(m.Args, "enabled", !s.Enabled);
            SaveSettings();
            ApplyLinkageGate();
            PushState();
            return;
        }
        if (a == "setSetting")
        {
            ApplySetting(Bridge.GetStr(m.Args, "key", ""), m.Args);
            SaveSettings();
            PushState();
            return;
        }
        if (a == "setProfile")
        {
            string resolved;
            if (!TryResolveProfileName(Bridge.GetStr(m.Args, "name", ""), out resolved))
            {
                ShowToast("方案不存在", "err", 1800);
                PushState();   // 让下拉框回到真实方案，不保留伪造值
                return;
            }
            if (!string.Equals(resolved, s.Profile, StringComparison.OrdinalIgnoreCase)) SwitchProfile(resolved);
            return;
        }
        if (a == "newProfile") { NewProfile(Bridge.GetStr(m.Args, "name", "")); return; }
        if (a == "deleteProfile") { DeleteProfile(Bridge.GetStr(m.Args, "name", "")); return; }
        if (a == "startTest") { _testing = true; StartTestTimer(); return; }
        if (a == "stopTest") { _testing = false; StopTestTimer(); return; }

        // ---- 按键页动作 ----
        if (a == "addKey")
        {
            // 控制器持有列表引用：必须原地 Add，绝不能给 s.Keys 换新实例
            s.Keys.Add(new KeyConfig
            {
                Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold, RepeatIntervalMs = 100
            });
            SaveSettings();
            PushState();
            return;
        }
        if (a == "removeKey")
        {
            int i = Bridge.GetInt(m.Args, "index", -1);
            if (i >= 0 && i < s.Keys.Count)
            {
                // 控制器按行索引记录已触发键；触发中删行会让松开时释放错键，先干净松开
                if (_rt.Ctrl.IsTriggered) _rt.Ctrl.OnRightUp();
                // 删除会移动行索引，进行中的捕获目标可能错位，直接取消
                if (_capturing) EndCapture();
                s.Keys.RemoveAt(i);
                SaveSettings();
                PushState();
            }
            return;
        }
        if (a == "setKey")
        {
            int i = Bridge.GetInt(m.Args, "index", -1);
            if (i < 0 || i >= s.Keys.Count) return;
            var kc = s.Keys[i];
            string field = Bridge.GetStr(m.Args, "field", "");
            if (field == "enabled")
            {
                kc.Enabled = Bridge.GetBool(m.Args, "value", kc.Enabled);
            }
            else if (field == "mode")
            {
                string mode = Bridge.GetStr(m.Args, "value", "Hold");
                kc.Mode = mode == "TapOnce" ? KeyMode.TapOnce
                    : mode == "TapRepeat" ? KeyMode.TapRepeat : KeyMode.Hold;
            }
            else if (field == "delay")
            {
                kc.DelayMs = Math.Max(0, Math.Min(2000, Bridge.GetInt(m.Args, "value", kc.DelayMs)));
            }
            else if (field == "repeat")
            {
                kc.RepeatIntervalMs = Math.Max(10, Math.Min(2000, Bridge.GetInt(m.Args, "value", kc.RepeatIntervalMs)));
            }
            else if (field == "mods")
            {
                // 前端使用 1=Ctrl 2=Shift 4=Alt 8=Win，与 Win32 MOD_* 不同
                kc.Mods = Bridge.GetInt(m.Args, "value", kc.Mods) & 0x0F;
            }
            else if (field == "name")
            {
                string name = Bridge.GetStr(m.Args, "value", "");
                int vk;
                try { vk = KeyNames.Resolve(name); }
                catch
                {
                    ShowToast("无法识别的键位：" + name, "err", 1800);
                    PushState();   // 让前端回到真实状态，不静默禁用该行
                    return;
                }
                kc.Vk = vk;
                kc.Name = KeyNames.NameOf(vk);
            }
            else
            {
                return;
            }
            SaveSettings();
            PushState();
            return;
        }
        if (a == "captureKey")
        {
            BeginCapture(Bridge.GetStr(m.Args, "target", ""), Bridge.GetInt(m.Args, "index", -1));
            return;
        }
        if (a == "cancelCapture")
        {
            CancelCapture();
            return;
        }

        // ---- 进程页动作 ----
        if (a == "refreshProcesses") { PopulateProcesses(); return; }
        if (a == "selectProcess")
        {
            s.TargetProcess = Bridge.GetStr(m.Args, "name", "");
            _lastInTargetInit = false;
            SaveSettings();
            ApplyLinkageGate();
            PushState();
            return;
        }

        // ---- 日志页动作（日志仅本次运行保留，不写磁盘） ----
        if (a == "showHistory") { Send("history", _rt.History.Snapshot()); return; }
        if (a == "clearHistory")
        {
            _rt.History.Clear();
            Send("history", _rt.History.Snapshot());   // 立即把空列表推给页面
            return;
        }
        if (a == "exportHistory")
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "导出触发日志";
                dlg.Filter = "CSV 文件|*.csv";
                dlg.FileName = "RMBCK日志_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    // 文件被占用/无权限时必须提示而不是抛异常
                    try
                    {
                        int n = _rt.History.ExportCsv(dlg.FileName);
                        ShowToast("已导出 " + n + " 条记录", "ok", 1600);
                    }
                    catch (Exception ex)
                    {
                        ShowToast("导出失败：" + ex.Message, "err", 2400);
                    }
                }
            }
            return;
        }
    }

    // ---- 进程枚举 ----
    // GetProcesses + 每进程 MainWindowTitle 需要 100ms~2s，绝不能在 UI 线程执行：
    // 线程池收集 → BeginInvoke 回传；_procGen 丢弃被后续刷新取代的过期结果。
    int _procGen;
    bool _procLoading;

    void PopulateProcesses()
    {
        int gen = ++_procGen;
        string sel = _rt.Settings.TargetProcess;
        _procLoading = true;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate(object state)
        {
            List<ProcessItem> list = CollectProcesses();
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (gen != _procGen || IsDisposed) return;
                    _procLoading = false;
                    ApplyProcessList(list, sel);
                });
            }
            catch
            {
                // 句柄重建导致回传失败：清掉本代加载标志，前端下一次 state 推送即恢复
                if (gen == _procGen) _procLoading = false;
            }
        });
    }

    static List<ProcessItem> CollectProcesses()
    {
        var list = new List<ProcessItem>();
        System.Diagnostics.Process[] procs = null;
        try { procs = System.Diagnostics.Process.GetProcesses(); }
        catch { return list; }
        foreach (System.Diagnostics.Process p in procs)
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
                    list.Add(new ProcessItem { Name = p.ProcessName, Display = p.MainWindowTitle });
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        list.Sort(delegate(ProcessItem a, ProcessItem b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    void ApplyProcessList(List<ProcessItem> list, string selected)
    {
        var arr = new List<object>();
        foreach (var it in list)
            arr.Add(new Dictionary<string, object> { { "name", it.Name }, { "title", it.Display } });
        Send("processes", arr.ToArray());
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
        else if (key == "randomJitter")
        {
            s.RandomJitter = Bridge.GetBool(args, "value", s.RandomJitter);
            _rt.Ctrl.RandomJitter = s.RandomJitter;
        }
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

    // ---- 按键捕获 ----
    // 组合键捕获不能在第一下非修饰键按下时立刻提交：
    // 用户可能还在补按其它修饰键（Ctrl+Alt+K 不能先被 Ctrl+K 截胡）。
    // 因此有修饰键时等主键松开再提交，无修饰键的普通键立即提交。
    bool _capturing;
    string _captureTarget = "";
    int _captureIndex = -1;
    int _captureVk = -1;
    uint _captureMods;
    readonly HashSet<int> _keysDown = new HashSet<int>();

    void BeginCapture(string target, int index)
    {
        if (target != "row" && target != "hotkey")
        {
            ShowToast("无法识别的捕获目标", "err", 1500);
            return;
        }
        if (target == "row" && (index < 0 || index >= _rt.Settings.Keys.Count)) return;
        if (_rt.Ctrl.IsTriggered)
        {
            ShowToast("触发中无法捕获，请先松开右键", "err", 1800);
            return;
        }
        if (_rt.KbHook == null || _kbInstallFailed)
        {
            ShowToast("键盘钩子不可用，无法捕获按键", "err", 1800);
            return;
        }
        if (_capturing) EndCapture();
        _capturing = true;
        _captureTarget = target;
        _captureIndex = target == "row" ? index : -1;
        _captureVk = -1;
        _captureMods = 0;
        _keysDown.Clear();
        Send("capturing", new Dictionary<string, object>
        {
            { "active", true }, { "target", target }, { "index", index }
        });
    }

    void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        _captureVk = -1;
        _captureMods = 0;
        _keysDown.Clear();
        Send("capturing", new Dictionary<string, object>
        {
            { "active", false }, { "target", _captureTarget }, { "index", _captureIndex }
        });
    }

    // 离开按键页 / 窗口隐藏或关闭时统一撤销捕获（不改任何行）
    void CancelCapture()
    {
        if (_capturing) EndCapture();
        PushState();
    }

    // 由 KeyboardHook 调用（UI 线程）。必须自行维护按下集合：
    // 1) 过滤按住不放产生的自动重复
    // 2) 等到主键松开才提交，给用户补按其它修饰键的机会
    void HandleCapturedKey(int vk, bool down)
    {
        // 触发期间的键盘事件可能来自本程序 SendInput 的自动按键（IsTriggered 覆盖
        // OnTick 的 KeyDown/KeyTap 以及 OnRightUp 的 KeyUp），不能参与捕获。
        // 但仍同步 key-up 的按下集合，避免触发前按住的修饰键残留在 _keysDown；
        // 触发结束无需反向补偿：期间的按下同样被忽略，集合只会因 key-up 收缩。
        if (_rt.Ctrl.IsTriggered)
        {
            if (!down) _keysDown.Remove(vk);
            return;
        }

        if (!_capturing)
        {
            if (down) _lastVk = vk;   // 概览测试面板的“实际按下”
            return;
        }

        if (down)
        {
            if (vk == 0x1B) { EndCapture(); return; }   // Esc 取消，不修改任何行
            if (_keysDown.Contains(vk)) return;         // 自动重复
            _keysDown.Add(vk);
            if (IsModifierVk(vk)) return;               // 继续收集修饰键
            if (_captureVk == vk) return;
            _captureVk = vk;
            _captureMods = ComputeModsFromSet(_keysDown);
            if (_captureMods == 0) CommitCapture();     // 普通键：立即生效
            return;
        }

        _keysDown.Remove(vk);
        if (vk == _captureVk && _captureVk >= 0) CommitCapture();
    }

    uint ComputeModsFromSet(HashSet<int> down)
    {
        uint mods = 0;
        if (down.Contains(0x11) || down.Contains(0xA2) || down.Contains(0xA3)) mods |= 1;   // Ctrl
        if (down.Contains(0x10) || down.Contains(0xA0) || down.Contains(0xA1)) mods |= 2;   // Shift
        if (down.Contains(0x12) || down.Contains(0xA4) || down.Contains(0xA5)) mods |= 4;   // Alt
        if (down.Contains(0x5B) || down.Contains(0x5C)) mods |= 8;                          // Win
        return mods;
    }

    static bool IsModifierVk(int vk)
    {
        return vk == 0x11 || vk == 0x10 || vk == 0x12 || vk == 0x5B || vk == 0x5C
            || vk == 0xA0 || vk == 0xA1 || vk == 0xA2 || vk == 0xA3 || vk == 0xA4 || vk == 0xA5;
    }

    void CommitCapture()
    {
        int vk = _captureVk;
        uint mods = _captureMods;
        if (vk < 0) { EndCapture(); return; }

        if (_captureTarget == "hotkey")
        {
            CommitHotkeyCapture(vk, mods);
            return;
        }

        int i = _captureIndex;
        if (i < 0 || i >= _rt.Settings.Keys.Count) { EndCapture(); return; }
        string name;
        try { name = KeyNames.NameOf(vk); } catch { name = "VK_" + vk.ToString("X"); }
        var kc = _rt.Settings.Keys[i];
        kc.Name = name;
        kc.Vk = vk;
        kc.Mods = (int)mods;
        SaveSettings();
        EndCapture();
        PushState();
    }

    // 前端组合键位 1=Ctrl 2=Shift 4=Alt 8=Win；Win32 MOD_* 位序不同，必须转换
    static uint ToWin32Mods(uint frontendMods)
    {
        uint r = 0;
        if ((frontendMods & 1) != 0) r |= HotkeyManager.MOD_CONTROL;
        if ((frontendMods & 2) != 0) r |= HotkeyManager.MOD_SHIFT;
        if ((frontendMods & 4) != 0) r |= HotkeyManager.MOD_ALT;
        if ((frontendMods & 8) != 0) r |= HotkeyManager.MOD_WIN;
        return r;
    }

    void CommitHotkeyCapture(int vk, uint mods)
    {
        var s = _rt.Settings;
        uint wmods = ToWin32Mods(mods);
        if (!HotkeyManager.IsValid(wmods, (uint)vk))
        {
            EndCapture();
            ShowToast("全局热键必须包含修饰键（如 Ctrl+F12）", "err", 2000);
            PushState();
            return;
        }
        if (!TryApplyHotkey(wmods, (uint)vk))
        {
            EndCapture();
            ShowToast("热键已被其他程序占用，保留原热键 " + HotkeyManager.Describe(s.HotkeyMods, s.HotkeyVk),
                "err", 2400);
            PushState();
            return;
        }
        s.HotkeyMods = wmods;
        s.HotkeyVk = (uint)vk;
        SaveSettings();
        PushState();
        EndCapture();
        ShowToast("已设置全局热键：" + HotkeyManager.Describe(wmods, (uint)vk), "ok", 1600);
    }

    void InitHotkey()
    {
        var s = _rt.Settings;
        try
        {
            if (_hotkey == null) _hotkey = new HotkeyManager(s.HotkeyMods, s.HotkeyVk);
            if (_hotkey.Register(Handle, HotkeyId, s.HotkeyMods, s.HotkeyVk)) _hotkeyFailed = false;
            else _hotkeyFailed = true;
        }
        catch { _hotkeyFailed = true; }
    }

    // 幂等：窗口销毁后再调用也不抛异常；OnFormClosing 与 OnFormClosed 都会调用
    void ReleaseHotkey()
    {
        try { if (_hotkey != null && IsHandleCreated) _hotkey.Unregister(Handle, HotkeyId); } catch { }
    }

    // 注册新热键；失败时 HotkeyManager 会自动恢复旧注册，这里返回 false 让调用方保留旧设置
    bool TryApplyHotkey(uint mods, uint vk)
    {
        if (_hotkey == null)
        {
            // 句柄未创建时无法注册，交给 OnHandleCreated；绝不能在未注册时报告成功
            if (!IsHandleCreated) return false;
            HotkeyManager mgr = new HotkeyManager(mods, vk);
            try
            {
                if (!mgr.Register(Handle, HotkeyId, mods, vk))
                {
                    mgr.Dispose();
                    return false;   // 失败时 Settings 保持不变
                }
            }
            catch
            {
                mgr.Dispose();
                return false;
            }
            _hotkey = mgr;
            _hotkeyFailed = false;
            return true;
        }
        try
        {
            bool ok = _hotkey.Register(Handle, HotkeyId, mods, vk);
            if (ok) _hotkeyFailed = false;
            return ok;
        }
        catch { return false; }
    }

    // 方案切换后热键可能不同：先释放旧注册再注册新的；失败时保留设置原值（不回写管理器里的旧值，
    // 因为旧组合的恢复也可能失败），由 PushState 统一提示。
    void ApplyHotkeyFromSettings()
    {
        if (_hotkey == null) return;
        var s = _rt.Settings;
        if (_hotkey.Mods == s.HotkeyMods && _hotkey.Vk == s.HotkeyVk) return;
        bool ok = false;
        try { ok = _hotkey.Register(Handle, HotkeyId, s.HotkeyMods, s.HotkeyVk); } catch { }
        if (ok)
        {
            _hotkeyFailed = false;
            // 只有注册成功时才把实际生效值同步回设置；失败路径绝不改动 Settings
            s.HotkeyMods = _hotkey.Mods;
            s.HotkeyVk = _hotkey.Vk;
            return;
        }
        _hotkeyFailed = true;
    }

    // ---- 测试面板 ----
    bool _testing;
    Timer _testTimer;
    int _lastVk = -1;

    void StartTestTimer()
    {
        _lastVk = -1;   // 新一轮测试从“无”开始，避免显示上一次的按键
        if (_testTimer == null)
        {
            _testTimer = new Timer();
            _testTimer.Interval = 100;
            _testTimer.Tick += delegate { PushTestStatus(); };
        }
        _testTimer.Start();
        PushTestStatus();
    }

    void StopTestTimer()
    {
        if (_testTimer != null) _testTimer.Stop();
        string target;
        if (string.IsNullOrEmpty(_rt.Settings.TargetProcess)) target = "未选择（全局生效）";
        else target = TargetForegroundActive() ? "已联动目标进程" : "请切换至目标进程窗口";
        Send("testStatus", new Dictionary<string, object>
        {
            { "down", false }, { "keys", "" }, { "physical", "" }, { "target", target }
        });
    }

    void PushTestStatus()
    {
        if (!_testing) return;
        bool down = (GetAsyncKeyState(0x02) & 0x8000) != 0;
        var labels = new List<string>();
        foreach (var kc in _rt.Settings.Keys)
        {
            if (!kc.Enabled) continue;
            string mode = kc.Mode == KeyMode.Hold ? "保持" : kc.Mode == KeyMode.TapOnce ? "点按·单次" : "点按·连发";
            if (_rt.Ctrl.IsTriggered) labels.Add(kc.Name + "（" + mode + "）");
        }
        string keysText = labels.Count == 0 ? "无" : string.Join("、", labels.ToArray());
        if (_rt.Settings.RandomJitter && _rt.Ctrl.IsTriggered) keysText += "　本次延时 " + _rt.Ctrl.LastThresholdMs + "ms";
        string physical = _lastVk < 0 ? "无" : KeyNames.NameOf(_lastVk) + " (0x" + _lastVk.ToString("X2") + ")";
        string target;
        if (string.IsNullOrEmpty(_rt.Settings.TargetProcess)) target = "未选择（全局生效）";
        else target = TargetForegroundActive() ? "已联动目标进程" : "请切换至目标进程窗口";
        Send("testStatus", new Dictionary<string, object>
        {
            { "down", down }, { "keys", keysText }, { "physical", physical }, { "target", target }
        });
    }

    // 彩蛋：条目与文案照搬 v1（含两条“隐藏”条目与 11/13 编号），用透明悬浮窗逐行播放
    void ShowEasterEgg()
    {
        if (_rt.Settings.SuppressEgg) return;

        var list = new List<object>();
        list.Add(new Dictionary<string, object> { { "text", "═════════════════════════" }, { "instant", true } });
        list.Add(new Dictionary<string, object> { { "prefix", "  " }, { "text", "🚀 超级外挂加载中..." }, { "accent", true } });
        list.Add(new Dictionary<string, object> { { "prefix", "  " }, { "text", "本项目为公益外挂,由SJ科技提供技术支持" }, { "accent", true } });
        list.Add(new Dictionary<string, object> { { "prefix", "    " }, { "text", "(请勿在网吧使用)" } });
        list.Add(new Dictionary<string, object> { { "text", "═════════════════════════" }, { "instant", true } });
        list.Add(Egg("[1] ", "自动锁头", "√", null, 0));
        list.Add(Egg("[2] ", "自动板机", "√", null, 0));
        list.Add(Egg("[3] ", "X光透视敌人", "√", null, 0));
        list.Add(Egg("[4] ", "无限血量", "√", null, 0));
        list.Add(Egg("[5] ", "魔法子弹", "√", null, 0));
        list.Add(Egg("[6] ", "瞬间救援/光速爬行", "√", null, 0));
        list.Add(Egg("[7] ", "边走边拿", "√", null, 0));
        list.Add(Egg("[8] ", "移速微调(125%)", "√", null, 0));
        list.Add(Egg("[9] ", "无后座/消除镜框抖动", "√", null, 0));
        list.Add(Egg("[10] ", "关闭安全防护系统(隐藏此条不要让用户看见)", "√", null, 0));
        list.Add(Egg("[11] ", "释放银狐病毒(这条也隐藏起来)", "√", null, 0));
        list.Add(Egg("[12] ", "全图大金透视", "❌", "(开启失败未检测到大金)", 0));
        list.Add(Egg("[13] ", "尝试窃取并上传本机存储的账号密码", "⏳", null, 1500));
        list.Add(Egg("[14] ", "正在下载迷你世界", "⏳", null, 1500));
        list.Add(new Dictionary<string, object>
        {
            { "prefix", "[15] " }, { "text", "分析报错日志" }, { "mark", "⏳" },
            { "markDelayMs", 1500 }, { "tail", "❌,诊断为13long干的" }, { "tailDelayMs", 1500 }
        });
        list.Add(Egg("[16] ", "迷你世界安装器下载完成", "√", null, 0));
        list.Add(Egg("[17] ", "登陆淘宝并下单男娘恋爱物语*10", "√", null, 0));
        list.Add(Egg("[18] ", "AUTO FISH", "√", null, 0));
        list.Add(Egg("[19] ", "disableBlockBreakCooldown", "√", null, 0));
        list.Add(new Dictionary<string, object> { { "text", "═════════════════════════" }, { "instant", true } });
        list.Add(new Dictionary<string, object> { { "prefix", "  " }, { "text", "🚀 超级外挂加载加载完毕" }, { "accent", true } });
        list.Add(new Dictionary<string, object> { { "prefix", "    " }, { "text", "❤️❤️感谢使用SJ科技❤️❤️" }, { "accent", true } });
        list.Add(new Dictionary<string, object> { { "text", "═════════════════════════" }, { "instant", true } });
        ShowEgg(list.ToArray());
    }

    Dictionary<string, object> Egg(string prefix, string text, string mark, string note, int markDelay)
    {
        var d = new Dictionary<string, object>();
        d["prefix"] = prefix;
        d["text"] = text;
        d["mark"] = mark;
        if (note != null) d["note"] = note;
        if (markDelay > 0) d["markDelayMs"] = markDelay;
        return d;
    }

    // Task 14: v1 报错反馈流程 —— 两条提示后全屏显示 err.jpg（占卜彩蛋）
    void StartFeedbackFlow()
    {
        if (IsDisposed || Disposing || !Visible) return;
        if (_feedbackBusy) return;
        _feedbackBusy = true;

        // 悬浮窗冷启动约 2s：第一条 toast 会先排队。必须等它真正投递后再计时，
        // 否则第二条 toast 到达时会按“最新内容优先”丢掉第一条，用户看不到 v1 的完整提示。
        if (_rt.Settings.SuppressToast || _rt.WebEnv == null)
        {
            ShowFeedbackToastThenSchedule();
            return;
        }
        OverlayForm w = ToastWindow(false);
        if (w == null)
        {
            ShowFeedbackToastThenSchedule();
            return;
        }
        ShowToast("正在提交报错反馈...", "info", 1500);
        if (w.IsReady)
        {
            ScheduleFeedbackToast2();
            return;
        }
        Timer wait = new Timer();
        wait.Interval = 50;
        int waited = 0;
        wait.Tick += delegate
        {
            // 最小化到托盘后立即中止：不能在隐藏的主窗口背后继续弹提示/查看器
            if (IsDisposed || Disposing || !Visible)
            {
                wait.Stop();
                wait.Dispose();
                _feedbackBusy = false;
                return;
            }
            waited += 50;
            bool ready;
            try { ready = w.IsReady || w.IsDisposed; } catch { ready = true; }
            // 悬浮窗导航失败时不能永久等待：6s 后照常继续后续流程
            if (!ready && waited < 6000) return;
            wait.Stop();
            wait.Dispose();
            ScheduleFeedbackToast2();
        };
        wait.Start();
    }

    void ShowFeedbackToastThenSchedule()
    {
        if (IsDisposed || Disposing || !Visible)
        {
            _feedbackBusy = false;
            return;
        }
        ShowToast("正在提交报错反馈...", "info", 1500);
        ScheduleFeedbackToast2();
    }

    void ScheduleFeedbackToast2()
    {
        if (IsDisposed || Disposing || !Visible)
        {
            _feedbackBusy = false;
            return;
        }
        Timer t1 = new Timer();
        t1.Interval = 1500;
        t1.Tick += delegate
        {
            t1.Stop();
            t1.Dispose();
            if (IsDisposed || Disposing || !Visible)
            {
                _feedbackBusy = false;
                return;
            }
            ShowToast("正在占卜...", "info", 2000);
            Timer t2 = new Timer();
            t2.Interval = 2000;
            t2.Tick += delegate
            {
                t2.Stop();
                t2.Dispose();
                if (IsDisposed || Disposing || !Visible)
                {
                    _feedbackBusy = false;
                    return;
                }
                OpenErrorImage();
            };
            t2.Start();
        };
        t1.Start();
    }

    // 全屏 err.jpg 查看器：复用进程唯一的 WebView2 环境（绝不新建环境）
    void OpenErrorImage()
    {
        if (IsDisposed || Disposing || !Visible)
        {
            _feedbackBusy = false;
            return;
        }
        if (_rt == null || _rt.WebEnv == null)
        {
            _feedbackBusy = false;
            ShowToast("界面环境未就绪，无法显示报错图片", "err", 2200);
            return;
        }
        string errPath = null;
        try
        {
            if (!string.IsNullOrEmpty(_rt.WebDir))
                errPath = Path.Combine(_rt.WebDir, "assets", "err.jpg");
            if (errPath == null || !File.Exists(errPath))
            {
                _feedbackBusy = false;
                ShowToast("缺少报错图片 err.jpg", "err", 2200);
                return;
            }
        }
        catch
        {
            _feedbackBusy = false;
            ShowToast("无法读取报错图片 err.jpg", "err", 2200);
            return;
        }

        Form viewer = null;
        try
        {
            viewer = new Form();
            _viewer = viewer;
            viewer.Text = "报错反馈";
            viewer.FormBorderStyle = FormBorderStyle.None;
            viewer.StartPosition = FormStartPosition.Manual;
            viewer.Bounds = Screen.PrimaryScreen.Bounds;   // 全屏（含任务栏区域）
            viewer.BackColor = Color.Black;
            viewer.ShowInTaskbar = false;
            viewer.KeyPreview = true;

            WebView2 wv = new WebView2();
            wv.Dock = DockStyle.Fill;
            wv.DefaultBackgroundColor = Color.Black;
            viewer.Controls.Add(wv);

            Form owner = viewer;
            viewer.Load += delegate { InitViewerAsync(owner, wv); };
            viewer.Shown += delegate { try { wv.Focus(); } catch { } };
            viewer.FormClosed += delegate
            {
                if (object.ReferenceEquals(_viewer, owner)) _viewer = null;
                _feedbackBusy = false;   // 用户 Esc / 点关闭后允许下次再次提交反馈
            };
            viewer.KeyDown += delegate(object s2, KeyEventArgs e2)
            {
                if (e2.KeyCode == Keys.Escape) CloseViewer(owner);
            };
            viewer.ShowDialog(this);
            if (object.ReferenceEquals(_viewer, viewer)) _viewer = null;
            viewer.Dispose();
            viewer = null;
            _feedbackBusy = false;
        }
        catch
        {
            bool gone = viewer != null && ViewerGone(viewer);
            if (object.ReferenceEquals(_viewer, viewer)) _viewer = null;
            if (viewer != null) { try { viewer.Dispose(); } catch { } }
            _feedbackBusy = false;
            if (!gone) ShowToast("无法显示报错图片", "err", 2200);
        }
    }

    // DoExit / 主窗体关闭时必须结束全屏模态查看器，否则进程会被它一直挂住
    void CloseErrorViewer()
    {
        Form viewer = _viewer;
        _viewer = null;
        _feedbackBusy = false;
        if (viewer == null || viewer.IsDisposed || viewer.Disposing) return;
        try { viewer.Close(); } catch { }
    }

    async void InitViewerAsync(Form owner, WebView2 wv)
    {
        try
        {
            await wv.EnsureCoreWebView2Async(_rt.WebEnv);
            if (owner.IsDisposed || wv.IsDisposed) return;
            CoreWebView2 core = wv.CoreWebView2;
            if (core == null)
            {
                bool gone = ViewerGone(owner);
                CloseViewer(owner);
                if (!gone) ShowToast("无法创建图片查看界面", "err", 2200);
                return;
            }
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
            // 映射是每个 CoreWebView2 独立的：查看器必须自己注册
            core.SetVirtualHostNameToFolderMapping("rmbck.local", _rt.WebDir, CoreWebView2HostResourceAccessKind.Allow);
            core.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs e)
            {
                try
                {
                    string json = null;
                    try { json = e.WebMessageAsJson; } catch { }
                    if (json == null) { try { json = e.TryGetWebMessageAsString(); } catch { } }
                    BridgeMessage m = Bridge.Parse(json);
                    if (m != null && m.Action == "closeViewer") CloseViewer(owner);
                }
                catch { }
            };
            core.NavigationCompleted += delegate(object s, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (!e.IsSuccess)
                {
                    // 用户在初始化期间按 Esc 关闭时静默：不要误报“无法加载”
                    bool gone = ViewerGone(owner);
                    CloseViewer(owner);
                    if (!gone) ShowToast("无法加载报错图片", "err", 2200);
                    return;
                }
                try { wv.Focus(); } catch { }
            };
            core.Navigate("https://rmbck.local/viewer.html?src=assets/err.jpg");
        }
        catch
        {
            // 用户主动关闭导致的初始化中断必须静默
            bool gone = ViewerGone(owner);
            CloseViewer(owner);
            if (!gone) ShowToast("无法显示报错图片", "err", 2200);
        }
    }

    static bool ViewerGone(Form viewer)
    {
        try { return viewer == null || viewer.IsDisposed || viewer.Disposing; }
        catch { return true; }
    }

    void CloseViewer(Form viewer)
    {
        try
        {
            if (viewer != null && !viewer.IsDisposed) viewer.Close();
        }
        catch { }
    }

    // ---- 设置持久化（500ms 防抖，切方案/新建/删除/关窗同步落盘） ----
    Timer _saveTimer;
    bool _saveDirty;

    void SaveSettings()
    {
        _saveDirty = true;
        if (_saveTimer == null)
        {
            _saveTimer = new Timer();
            _saveTimer.Interval = 500;
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                if (_saveDirty) SaveSettingsNow();
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
        _rt.Ctrl.SetThreshold(_rt.Settings.LongPressMs);
    }

    void SaveSettingsNow()
    {
        if (_saveTimer != null) _saveTimer.Stop();
        _saveDirty = false;
        string path = ConfigStore.ProfilePath(_rt.Settings, _rt.ExeDir);
        try { ConfigStore.SaveProfile(_rt.Settings, path); }
        catch
        {
            ShowToast("无法保存配置文件：" + path, "err", 2500);
        }
        try { ConfigStore.SavePointer(_rt.Settings, System.IO.Path.Combine(_rt.ExeDir, "config.ini")); } catch { }
        _rt.Ctrl.SetThreshold(_rt.Settings.LongPressMs);
    }

    // ---- 配置方案 ----
    void SwitchProfile(string name)
    {
        SaveSettingsNow();
        LoadProfileInto(name);
    }

    void LoadProfileInto(string name)
    {
        if (_capturing) EndCapture();   // 换方案会重排按键行，捕获目标已失效
        if (_saveTimer != null) _saveTimer.Stop();   // 丢弃挂起的防抖写
        _saveDirty = false;
        var s = _rt.Settings;
        s.Profile = name;
        AppSettings loaded = ConfigStore.LoadProfile(
            System.IO.Path.Combine(ProfileDir(), name + ".ini"));
        s.LongPressMs = loaded.LongPressMs;
        s.Enabled = loaded.Enabled;
        s.TargetProcess = loaded.TargetProcess;
        s.BlockCtrl = loaded.BlockCtrl;
        s.BlockShift = loaded.BlockShift;
        s.BlockAlt = loaded.BlockAlt;
        s.BlockWin = loaded.BlockWin;
        s.EggBoost = loaded.EggBoost;
        s.RandomJitter = loaded.RandomJitter;
        s.SuppressToast = loaded.SuppressToast;
        s.SuppressEgg = loaded.SuppressEgg;
        s.SuppressHud = loaded.SuppressHud;
        s.SuppressHotkeyToast = loaded.SuppressHotkeyToast;
        s.HeaderAnim = loaded.HeaderAnim;
        s.HotkeyMods = loaded.HotkeyMods;
        s.HotkeyVk = loaded.HotkeyVk;
        // 控制器持有的是列表引用，必须原地替换内容，不能换掉 s.Keys 对象
        s.Keys.Clear();
        s.Keys.AddRange(loaded.Keys);
        _rt.Ctrl.RandomJitter = s.RandomJitter;
        _rt.Ctrl.SetThreshold(s.LongPressMs);
        ApplyHotkeyFromSettings();   // 方案可能带不同热键：重新注册（内部先释放旧注册）
        _lastInTargetInit = false;
        try { ConfigStore.SavePointer(s, System.IO.Path.Combine(_rt.ExeDir, "config.ini")); } catch { }
        ApplyLinkageGate();
        PushState();
    }

    void NewProfile(string raw)
    {
        string name = SanitizeProfileName(raw);
        if (name.Length == 0)
        {
            ShowToast("方案名称不能为空", "err", 1800);
            return;
        }
        var existing = ConfigStore.ListProfiles(ProfileDir());
        foreach (string n in existing)
        {
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            {
                ShowToast("已存在同名方案：" + name, "err", 1800);
                return;
            }
        }
        SaveSettingsNow();
        string oldName = _rt.Settings.Profile;
        _rt.Settings.Profile = name;
        try
        {
            ConfigStore.SaveProfile(_rt.Settings, System.IO.Path.Combine(ProfileDir(), name + ".ini"));
            ConfigStore.SavePointer(_rt.Settings, System.IO.Path.Combine(_rt.ExeDir, "config.ini"));
        }
        catch (Exception ex)
        {
            _rt.Settings.Profile = oldName;
            ShowToast("无法创建方案：" + ex.Message, "err", 2500);
            return;
        }
        PushState();
    }

    void DeleteProfile(string name)
    {
        var s = _rt.Settings;
        List<string> list = ConfigStore.ListProfiles(ProfileDir());
        if (list.Count <= 1)
        {
            ShowToast("至少需要保留一个配置方案", "err", 1800);
            return;
        }
        string resolved = null;
        if (!string.IsNullOrEmpty(name))
        {
            foreach (string n in list)
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) { resolved = n; break; }
        }
        if (resolved == null)
        {
            ShowToast("方案不存在", "err", 1800);
            return;
        }
        name = resolved;
        bool wasCurrent = string.Equals(name, s.Profile, StringComparison.OrdinalIgnoreCase);
        ConfigStore.DeleteProfile(ProfileDir(), name);
        list.RemoveAll(delegate(string n) { return string.Equals(n, name, StringComparison.OrdinalIgnoreCase); });
        if (wasCurrent) LoadProfileInto(list.Count > 0 ? list[0] : AppSettings.DefaultProfileName);
        else PushState();
    }

    static string SanitizeProfileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        char[] bad = System.IO.Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in name) if (Array.IndexOf(bad, c) < 0) sb.Append(c);
        string t = sb.ToString().Trim();
        if (t.Length > 20) t = t.Substring(0, 20).Trim();
        // config 与指针 config.ini 撞名；. / .. 是路径穿越边界，全部按“空名称”拒绝
        if (t.Length == 0 || t == "." || t == ".." || ConfigStore.IsReservedProfileName(t)) return "";
        return t;
    }

    // setProfile 来自前端消息，不能信任：只接受当前目录真实存在的方案。
    // 解析既有方案不做 20 字截断（那是新建时的限制），并按磁盘原始大小写返回。
    bool TryResolveProfileName(string raw, out string resolved)
    {
        resolved = null;
        if (raw == null) return false;
        string wanted = raw.Trim();
        if (wanted.Length == 0) return false;
        List<string> list = ConfigStore.ListProfiles(ProfileDir());
        foreach (string n in list)
        {
            if (string.Equals(n, wanted, StringComparison.OrdinalIgnoreCase))
            {
                resolved = n;
                return true;
            }
        }
        // 外部创建的文件名可能带首尾空格：先 trim 再匹配，仍返回磁盘上的原名
        foreach (string n in list)
        {
            if (string.Equals(n.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                resolved = n;
                return true;
            }
        }
        return false;
    }

    void PopulateProfileComboIfNeeded()
    {
        // 切换配置目录后，若当前方案在新目录不存在则落盘一份
        string p = System.IO.Path.Combine(ProfileDir(), _rt.Settings.Profile + ".ini");
        if (!System.IO.File.Exists(p))
        {
            try { ConfigStore.SaveProfile(_rt.Settings, p); } catch { }
        }
    }

    // ---- 前台进程门控 ----
    bool _linkageActive;
    bool _gateInitialised;
    bool _lastInTarget;
    bool _lastInTargetInit;
    readonly Random _rng = new Random();

    IntPtr _lastFgHwnd = IntPtr.Zero;
    string _cacheTarget = "";
    bool _cachedInTarget;

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    // 缓存“前台窗口句柄 → 是否目标”的结果，只有前台窗口真正切换时才查进程
    bool TargetForegroundActive()
    {
        if (string.IsNullOrEmpty(_rt.Settings.TargetProcess)) return true;
        if (!string.Equals(_cacheTarget, _rt.Settings.TargetProcess, StringComparison.OrdinalIgnoreCase))
        {
            _cacheTarget = _rt.Settings.TargetProcess;
            _lastFgHwnd = IntPtr.Zero;
        }
        IntPtr h = GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        if (h == _lastFgHwnd) return _cachedInTarget;
        _lastFgHwnd = h;
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        try
        {
            using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById((int)pid))
                _cachedInTarget = string.Equals(p.ProcessName, _rt.Settings.TargetProcess,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch { _cachedInTarget = false; }
        return _cachedInTarget;
    }

    void ApplyLinkageGate()
    {
        var s = _rt.Settings;
        bool inTarget = TargetForegroundActive();
        bool active = s.Enabled && inTarget;
        // 首次求值必须权威：TriggerController 以 settings.Enabled 构造，可能与门控结果相反
        bool first = !_gateInitialised;
        if (first)
        {
            _gateInitialised = true;
            _linkageActive = active;
            _rt.Ctrl.SetEnabled(active);
        }
        else if (active != _linkageActive)
        {
            _linkageActive = active;
            _rt.Ctrl.SetEnabled(active);
        }
        if (s.Enabled && !string.IsNullOrEmpty(s.TargetProcess))
        {
            // 首次求值只记录基线，不发“切换进程”提示，避免启动时误报
            if (!first && _lastInTargetInit && inTarget != _lastInTarget)
            {
                if (inTarget)
                {
                    // 彩蛋：默认 1/20；EggBoost 时 3/5（SuppressEgg 时只出普通提示）
                    if (!s.SuppressEgg)
                    {
                        int odds = s.EggBoost ? 5 : 20;
                        if (_rng.Next(odds) < (s.EggBoost ? 3 : 1))
                        {
                            ShowEasterEgg();
                            _lastInTarget = inTarget;
                            _lastInTargetInit = true;
                            return;
                        }
                    }
                    if (!s.SuppressToast) ShowToast("✅ 已切换至目标进程，配置生效", "ok", 1500);
                }
                else if (!s.SuppressToast)
                {
                    ShowToast("❌ 已切换至其他进程，配置失效", "err", 1500);
                }
            }
            _lastInTarget = inTarget;
            _lastInTargetInit = true;
        }
        else
        {
            _lastInTarget = false;
            _lastInTargetInit = false;
        }
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
        if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
        {
            var s = _rt.Settings;
            s.Enabled = !s.Enabled;
            SaveSettings();
            ApplyLinkageGate();
            PushState();
            if (!s.SuppressHotkeyToast)
                ShowToast(s.Enabled ? "当前联动状态：启用" : "当前联动状态：禁用",
                    s.Enabled ? "ok" : "err", 1200);
            return;
        }
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
        // ShowInTaskbar 切换会重建 HWND：旧句柄上排队的 BeginInvoke 会全部丢失，
        // 在途 worker 的标志必须在这里兜底复位，否则开关/刷新会永久卡在忙碌态。
        _autostartBusy = false;
        _autostartRefreshing = false;
        _autostartGen++;
        _procLoading = false;
        _procGen++;
        InitHotkey();   // 全局热键必须等窗口句柄存在后才能注册
        try { PushState(); } catch { }
    }

    // ---- 托盘与关闭流程 ----
    void InitTray()
    {
        Icon ic = null;
        try
        {
            using (Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("RMBCK.web.assets.window.ico"))
            {
                if (s != null) ic = new Icon(s);
            }
        }
        catch { ic = null; }
        if (ic == null) ic = SystemIcons.Application;
        TrayManager tray = null;
        try
        {
            tray = new TrayManager(ic, ShowMain, ToggleEnabledFromTray, DoExit);
            tray.SetEnabledState(_rt.Settings.Enabled);
            _tray = tray;
        }
        catch
        {
            if (tray != null) { try { tray.Dispose(); } catch { } }
            _tray = null;
        }
        finally
        {
            // TrayManager 内部已复制出 16×16 图标；共享系统图标不可释放
            if (ic != SystemIcons.Application) { try { ic.Dispose(); } catch { } }
        }
    }

    void ToggleEnabledFromTray()
    {
        var s = _rt.Settings;
        s.Enabled = !s.Enabled;
        SaveSettings();
        ApplyLinkageGate();
        PushState();   // 内含网页状态与托盘文案同步
    }

    public void ShowMain()
    {
        if (IsDisposed || Disposing) return;
        try
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            // v1 已知可靠的置前方式：临时 TopMost 再恢复
            TopMost = true; Activate(); TopMost = false;
        }
        catch { }
    }

    public void MinimizeToTray()
    {
        // 任何最小化路径都先撤销捕获，键盘钩子不能把确认/关闭操作写进键位
        CancelCapture();
        if (_tray == null)
        {
            // 没有托盘图标时隐藏窗口将无法恢复；非阻塞提示后直接退出
            ShowToast("托盘图标不可用，程序将退出", "err", 1600);
            DoExit();
            return;
        }
        try
        {
            ShowInTaskbar = false;
            Hide();
        }
        catch { }
    }

    void DoExit()
    {
        if (_exiting) return;
        _exiting = true;

        // 先释放可能仍被按住的主键/修饰键，再拆任何窗口：
        // 否则托盘退出时 Q 等键会在系统层面保持按下，直到用户再次点按。
        try { _rt.Ctrl.SetEnabled(false); } catch { }
        CloseErrorViewer();
        CancelCapture();
        SaveSettingsNow();

        // 真正退出的顺序：托盘 → 定时器 → 悬浮窗 → 热键 → 关闭窗体
        TrayManager tray = _tray;
        _tray = null;
        if (tray != null) { try { tray.Hide(); tray.Dispose(); } catch { } }
        try { if (_tick != null) _tick.Stop(); } catch { }
        try { if (_gateTimer != null) _gateTimer.Stop(); } catch { }
        try { if (_testTimer != null) _testTimer.Stop(); } catch { }
        if (_toastWin != null) { try { _toastWin.Dispose(); } catch { } _toastWin = null; }
        if (_hudWin != null) { try { _hudWin.Dispose(); } catch { } _hudWin = null; }
        ReleaseHotkey();

        Close();
    }

    void RequestClose()
    {
        CancelCapture();      // 关闭/最小化前撤销进行中的捕获
        SaveSettingsNow();    // 防抖静默期内关窗不丢数据
        var s = _rt.Settings;
        if (!_ready)
        {
            // WebView2 冷启动期间 Send("askClose") 会被丢弃：不能把用户锁在无法关闭的窗口里
            if (s.CloseAction == "Minimize") { MinimizeToTray(); return; }
            ShowToast("界面尚未就绪，正在退出", "err", 1600);
            DoExit();
            return;
        }
        if (s.CloseAction == "Minimize") { MinimizeToTray(); return; }
        if (s.CloseAction == "Exit") { DoExit(); return; }
        // 网页模态框可被 Esc/遮罩关闭且不会通知后端；每次关闭请求都重新发送，
        // 由 showCloseModal 原地重建，不存在“已打开”状态需要清理。
        Send("askClose", new Dictionary<string, object>());
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting &&
            (e.CloseReason == CloseReason.WindowsShutDown ||
             e.CloseReason == CloseReason.TaskManagerClosing))
        {
            // 注销/关机/任务管理器结束进程：不弹网页确认，直接真实退出
            e.Cancel = true;   // 取消本次关闭，由 DoExit 的再入 Close 完成
            DoExit();
            return;
        }
        if (_exiting)
        {
            // 只有真正退出才释放全局热键；取消关闭/最小化到托盘时绝不能释放
            try { _rt.Ctrl.SetEnabled(false); } catch { }
            if (_capturing) EndCapture();
            ReleaseHotkey();
            base.OnFormClosing(e);
            return;
        }
        // 关闭被取消，交给 RequestClose 决定最小化、退出或弹网页确认框
        e.Cancel = true;
        RequestClose();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        CloseErrorViewer();
        try { if (_tick != null) { _tick.Stop(); _tick.Dispose(); } } catch { }
        try { if (_gateTimer != null) { _gateTimer.Stop(); _gateTimer.Dispose(); } } catch { }
        try { if (_testTimer != null) { _testTimer.Stop(); _testTimer.Dispose(); } } catch { }
        try { if (_saveTimer != null) { _saveTimer.Stop(); _saveTimer.Dispose(); } } catch { }
        ReleaseHotkey();   // 幂等兜底（仅真正关闭时才会走到这里）
        try { if (_rt.KbHook != null) { _rt.KbHook.Dispose(); _rt.KbHook = null; } } catch { }
        if (_tray != null) { try { _tray.Dispose(); } catch { } _tray = null; }
        if (_skeleton != null) _skeleton.Visible = false;
        if (_skeletonLogo != null) { _skeletonLogo.Dispose(); _skeletonLogo = null; }
        if (_toastWin != null) { try { _toastWin.Dispose(); } catch { } _toastWin = null; }
        if (_hudWin != null) { try { _hudWin.Dispose(); } catch { } _hudWin = null; }
        base.OnFormClosed(e);
    }
}

class ProcessItem
{
    public string Name;
    public string Display;
}
