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
    bool _kbInstallFailed;
    bool _kbFailureShown;

    // 启动骨架：WebView2 首次启动约 5s，窗口立即显示原生占位内容
    Panel _skeleton;
    Image _skeletonLogo;
    bool _firstNavigationDone;

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
                        Close();
                        return;
                    }
                    ShowRealPage();          // 首次导航完成：一次性换掉原生骨架
                }
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
            // Task 13 实现真正的托盘/关闭逻辑；离开界面前先撤销进行中的捕获
            CancelCapture();
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
            string name = Bridge.GetStr(m.Args, "name", "");
            if (name.Length > 0 && name != s.Profile) SwitchProfile(name);
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
    }

    // ---- 进程枚举 ----
    // GetProcesses + 每进程 MainWindowTitle 需要 100ms~2s，绝不能在 UI 线程执行：
    // 线程池收集 → BeginInvoke 回传；_procGen 丢弃被后续刷新取代的过期结果。
    int _procGen;

    void PopulateProcesses()
    {
        int gen = ++_procGen;
        string sel = _rt.Settings.TargetProcess;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate(object state)
        {
            List<ProcessItem> list = CollectProcesses();
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (gen != _procGen || IsDisposed) return;
                    ApplyProcessList(list, sel);
                });
            }
            catch { }
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
        if (target != "row")
        {
            // 热键捕获属于设置页（后续任务）
            ShowToast("该功能将在后续版本启用", "info", 1200);
            return;
        }
        if (index < 0 || index >= _rt.Settings.Keys.Count) return;
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
        _captureIndex = index;
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

    // Task 12 会替换为原生透明悬浮窗实现
    void ShowEasterEgg() { }

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
        if (t.Length > 20) t = t.Substring(0, 20);
        return t;
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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 捕获不能随窗口关闭而残留；钩子会在 OnFormClosed 中卸载
        if (_capturing) EndCapture();
        // 防抖静默期内关窗不丢数据
        SaveSettingsNow();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _tick.Stop();
        _tick.Dispose();
        if (_gateTimer != null) { _gateTimer.Stop(); _gateTimer.Dispose(); }
        if (_testTimer != null) { _testTimer.Stop(); _testTimer.Dispose(); }
        if (_saveTimer != null) { _saveTimer.Stop(); _saveTimer.Dispose(); }
        if (_rt.KbHook != null) { _rt.KbHook.Dispose(); _rt.KbHook = null; }
        if (_skeleton != null) _skeleton.Visible = false;
        if (_skeletonLogo != null) { _skeletonLogo.Dispose(); _skeletonLogo = null; }
        base.OnFormClosed(e);
    }
}

class ProcessItem
{
    public string Name;
    public string Display;
}
