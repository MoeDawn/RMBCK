using System;
using System.Drawing;
using System.Windows.Forms;

// 托盘图标与右键菜单。菜单沿用 v1 的樱花配色渲染器。
class TrayManager : IDisposable
{
    readonly NotifyIcon _tray;
    readonly ToolStripMenuItem _toggle;
    readonly Action _onOpen;
    readonly Action _onToggle;
    readonly Action _onExit;
    Icon _icon;

    public TrayManager(Icon windowIcon, Action onOpen, Action onToggle, Action onExit)
    {
        _onOpen = onOpen; _onToggle = onToggle; _onExit = onExit;
        try { _icon = new Icon(windowIcon, 16, 16); } catch { _icon = null; }
        _tray = new NotifyIcon();
        _tray.Icon = _icon != null ? _icon : SystemIcons.Application;
        _tray.Text = "Senren＊Banka";
        _tray.Visible = true;

        var menu = new ContextMenuStrip();
        menu.RenderMode = ToolStripRenderMode.ManagerRenderMode;
        var rs = new ToolStripProfessionalRenderer(new SakuraProfessionalColors());
        rs.RoundedEdges = true;
        menu.Renderer = rs;
        menu.ShowImageMargin = false;
        menu.Items.Add("🌸 打开主界面", null, delegate { _onOpen(); });
        _toggle = new ToolStripMenuItem();
        _toggle.Click += delegate { _onToggle(); };
        menu.Items.Add(_toggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("✕ 退出", null, delegate { _onExit(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += delegate { _onOpen(); };
    }

    // 供冒烟测试断言菜单文案与实际状态保持一致
    public string ToggleText { get { return _toggle.Text; } }

    // 托盘图标是否仍然可见（Dispose 后返回 false）
    public bool Visible
    {
        get
        {
            try { return _tray.Visible; }
            catch { return false; }
        }
    }

    public void SetEnabledState(bool enabled)
    {
        _toggle.Text = enabled ? "🔁 联动：全局生效" : "⏸ 联动：全局禁用";
    }

    public void Hide()
    {
        try { _tray.Visible = false; } catch { }
    }

    public void Dispose()
    {
        try { _tray.Visible = false; _tray.Dispose(); } catch { }
        try { if (_icon != null) _icon.Dispose(); } catch { }
    }
}

// v1 Theme 类里菜单渲染所需的两项颜色（原样保留常量值）
static class Theme
{
    public static readonly Color GridLine = Color.FromArgb(250, 215, 227); // #FAD7E3
    public static readonly Color Sel = Color.FromArgb(255, 217, 232);      // #FFD9E8
}

class SakuraProfessionalColors : ProfessionalColorTable
{
    public override Color ToolStripGradientBegin { get { return Color.FromArgb(255, 240, 245); } }
    public override Color ToolStripGradientMiddle { get { return Color.White; } }
    public override Color ToolStripGradientEnd { get { return Color.FromArgb(255, 240, 245); } }
    public override Color ToolStripBorder { get { return Theme.GridLine; } }
    public override Color MenuBorder { get { return Theme.GridLine; } }
    public override Color MenuItemBorder { get { return Theme.GridLine; } }
    public override Color MenuItemSelected { get { return Theme.Sel; } }
    public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(255, 222, 236); } }
    public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(238, 224, 255); } }
    public override Color MenuItemPressedGradientBegin { get { return Theme.Sel; } }
    public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(232, 216, 252); } }
    public override Color SeparatorDark { get { return Theme.GridLine; } }
    public override Color SeparatorLight { get { return Color.White; } }
    public override Color ImageMarginGradientBegin { get { return Color.White; } }
    public override Color ImageMarginGradientMiddle { get { return Color.White; } }
    public override Color ImageMarginGradientEnd { get { return Color.White; } }
}
