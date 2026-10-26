using System;
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
            core.NavigationCompleted += delegate { _ready = true; PushState(); };
            core.Navigate("https://rmbck.local/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show("界面初始化失败：" + ex.Message, "右键长按联动器",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    public void PushState() { }

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
                Point p = PointToClient(new Point(m.LParam.ToInt32() & 0xFFFF,
                    (m.LParam.ToInt32() >> 16) & 0xFFFF));
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
}
