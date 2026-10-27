using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

[assembly: AssemblyCompany("SSJJ2.5")]
[assembly: AssemblyProduct("右键长按联动器")]
[assembly: AssemblyTitle("千恋万花安装器(反MC)")]
[assembly: AssemblyDescription("千恋万花安装器(反MC)")]
[assembly: AssemblyVersion("2.1.0")]
[assembly: AssemblyFileVersion("2.1.0")]
[assembly: AssemblyInformationalVersion("2.1.0")]

class AppRuntime
{
    public AppSettings Settings;
    public TriggerController Ctrl;
    public MouseHook Hook;
    public KeyboardHook KbHook;
    public HistoryRing History;
    public string ExeDir;
    public string WebDir;
    // 全进程唯一的 WebView2 环境：主窗口与所有悬浮窗共用，避免重复启动浏览器进程（约 2.9s）
    public CoreWebView2Environment WebEnv;
}

static class Bootstrap
{
    // 与 MainForm.SanitizeProfileName 等价：保留合法字符、去首尾空白、限长，
    // 并拒绝 . / .. / config（config 会与指针 config.ini 撞名）。
    static string SanitizeProfileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        char[] bad = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (char c in name) if (Array.IndexOf(bad, c) < 0) sb.Append(c);
        string t = sb.ToString().Trim();
        if (t.Length > 20) t = t.Substring(0, 20).Trim();
        if (t.Length == 0 || t == "." || t == ".." || ConfigStore.IsReservedProfileName(t)) return "";
        return t;
    }

    static string ResolveExistingProfile(string folder, string name)
    {
        foreach (string n in ConfigStore.ListProfiles(folder))
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return n;
        return null;
    }

    public static AppSettings Load(string exeDir)
    {
        string ptrPath = Path.Combine(exeDir, "config.ini");
        AppSettings s = ConfigStore.LoadPointer(ptrPath);

        if (ConfigStore.IsOldFormat(ptrPath))
        {
            AppSettings old = ConfigStore.LoadProfile(ptrPath);
            s = old;
            s.Profile = AppSettings.DefaultProfileName;
            s.CloseAction = "Ask";
            string dir = string.IsNullOrEmpty(s.ConfigFolder) ? exeDir : s.ConfigFolder;
            try
            {
                ConfigStore.SaveProfile(s, Path.Combine(dir, s.Profile + ".ini"));
                ConfigStore.SavePointer(s, ptrPath);
            }
            catch { }
        }

        // Profile 来自可手改的指针文件：先清洗，绝不允许 ../ 或 config 逃逸/撞名
        string safeProfile = SanitizeProfileName(s.Profile);
        s.Profile = safeProfile.Length > 0 ? safeProfile : AppSettings.DefaultProfileName;

        if (!string.IsNullOrEmpty(s.ConfigFolder) && Directory.Exists(s.ConfigFolder))
        {
            // 目标方案不存在时，仅在旧版 config.ini 可迁移时创建它；否则回退默认方案
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
            string resolved = ResolveExistingProfile(s.ConfigFolder, s.Profile);
            if (resolved == null)
            {
                s.Profile = AppSettings.DefaultProfileName;
                resolved = ResolveExistingProfile(s.ConfigFolder, s.Profile);
                if (resolved != null) s.Profile = resolved;
                profileFile = Path.Combine(s.ConfigFolder, s.Profile + ".ini");
            }
            else
            {
                s.Profile = resolved;   // 使用目录中的真实大小写，避免切换后写出另一份文件
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
    const string AppVersion = "2.1.0";

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
        rt.History = new HistoryRing();

        try
        {
            try
            {
                rt.WebDir = WebAssets.Extract(AppVersion);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法解压界面资源：" + ex.Message, "右键长按联动器",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 环境只创建一次：之后主窗口与 Toast/HUD 悬浮窗都用它创建控制器。
            // 第二个控制器复用已启动的浏览器进程只需几十毫秒；重复建环境则要重新启动浏览器。
            try
            {
                string udf = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RMBCK", "WebView2");
                rt.WebEnv = CoreWebView2Environment.CreateAsync(null, udf, null).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法初始化 WebView2 环境：" + ex.Message, "右键长按联动器",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.Run(new MainForm(rt));
        }
        finally
        {
            hook.Dispose();
        }
    }
}
