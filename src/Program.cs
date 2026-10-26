using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

[assembly: AssemblyCompany("SSJJ2.5")]
[assembly: AssemblyProduct("右键长按联动器")]
[assembly: AssemblyTitle("千恋万花安装器(反MC)")]
[assembly: AssemblyDescription("千恋万花安装器(反MC)")]
[assembly: AssemblyVersion("2.0.0")]
[assembly: AssemblyFileVersion("2.0.0")]
[assembly: AssemblyInformationalVersion("2.0.0")]

class AppRuntime
{
    public AppSettings Settings;
    public TriggerController Ctrl;
    public MouseHook Hook;
    public KeyboardHook KbHook;
    public string ExeDir;
    public string WebDir;
}

static class Bootstrap
{
    public static AppSettings Load(string exeDir)
    {
        string ptrPath = Path.Combine(exeDir, "config.ini");
        AppSettings s = ConfigStore.LoadPointer(ptrPath);

        if (ConfigStore.IsOldFormat(ptrPath))
        {
            AppSettings old = ConfigStore.LoadProfile(ptrPath);
            s = old;
            s.Profile = "默认";
            s.CloseAction = "Ask";
            string dir = string.IsNullOrEmpty(s.ConfigFolder) ? exeDir : s.ConfigFolder;
            try
            {
                ConfigStore.SaveProfile(s, Path.Combine(dir, s.Profile + ".ini"));
                ConfigStore.SavePointer(s, ptrPath);
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(s.ConfigFolder) && Directory.Exists(s.ConfigFolder))
        {
            string profileFile = Path.Combine(s.ConfigFolder, s.Profile + ".ini");
            if (!File.Exists(profileFile))
            {
                string oldCfg = Path.Combine(s.ConfigFolder, "config.ini");
                if (ConfigStore.IsOldFormat(oldCfg))
                {
                    AppSettings old = ConfigStore.LoadProfile(oldCfg);
                    old.Profile = s.Profile;
                    old.CloseAction = s.CloseAction;
                    old.ConfigFolder = s.ConfigFolder;
                    old.Theme = s.Theme;
                    try { ConfigStore.SaveProfile(old, profileFile); } catch { }
                }
            }
            AppSettings profile = ConfigStore.LoadProfile(profileFile);
            profile.ConfigFolder = s.ConfigFolder;
            profile.Profile = s.Profile;
            profile.CloseAction = s.CloseAction;
            profile.Theme = s.Theme;
            return profile;
        }

        return s;
    }

    // 启动早期没有窗体可用：先尝试直接用已保存的路径，不可行时再弹文件夹选择
    public static bool EnsureConfigFolder(AppSettings settings, string exeDir)
    {
        string folder = settings.ConfigFolder;
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) return true;

        using (var dlg = new FolderBrowserDialog())
        {
            dlg.Description = "请选择配置文件的保存路径（用于保存按键设置）";
            dlg.ShowNewFolderButton = true;
            dlg.SelectedPath = exeDir;
            while (true)
            {
                if (dlg.ShowDialog() != DialogResult.OK)
                {
                    MessageBox.Show("未选择配置文件保存路径，程序将退出。", "右键长按联动器",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
                string picked = dlg.SelectedPath;
                if (!string.IsNullOrEmpty(picked) && Directory.Exists(picked))
                {
                    settings.ConfigFolder = picked;
                    break;
                }
            }
        }
        try
        {
            ConfigStore.SaveProfile(settings, ConfigStore.ProfilePath(settings, exeDir));
            ConfigStore.SavePointer(settings, Path.Combine(exeDir, "config.ini"));
        }
        catch { }
        return true;
    }
}

class Program
{
    const string AppVersion = "2.0.0";

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string exeDir = Path.GetDirectoryName(Application.ExecutablePath);

        string runtime = null;
        try { runtime = CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch { }
        if (string.IsNullOrEmpty(runtime))
        {
            MessageBox.Show(
                "本程序需要 Microsoft Edge WebView2 运行时。\n\n" +
                "请前往微软官网下载并安装「WebView2 Runtime (Evergreen Bootstrapper)」后重新启动。",
                "右键长按联动器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        AppSettings settings = Bootstrap.Load(exeDir);
        if (!Bootstrap.EnsureConfigFolder(settings, exeDir)) return;

        var hook = new MouseHook();
        if (!hook.Install())
        {
            MessageBox.Show("无法安装鼠标钩子，程序将退出。", "右键长按联动器",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var rt = new AppRuntime();
        rt.Settings = settings;
        rt.ExeDir = exeDir;
        rt.Hook = hook;
        rt.Ctrl = new TriggerController(new SendInputKeySender(), settings.Keys, settings.LongPressMs, settings.Enabled);
        rt.Ctrl.RandomJitter = settings.RandomJitter;
        rt.WebDir = WebAssets.Extract(AppVersion);

        Application.Run(new MainForm(rt));
        hook.Dispose();
    }
}
