using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

enum KeyMode { Hold, TapOnce, TapRepeat }

class KeyConfig
{
    public bool Enabled = true;
    public string Name = "";
    public int Vk;
    public KeyMode Mode = KeyMode.Hold;
    public int RepeatIntervalMs = 100;
    public int DelayMs = 0;          // 到达阈值后延迟该毫秒再触发本行
    public int Mods = 0;             // 1=Ctrl 2=Shift 4=Alt 8=Win
}

class AppSettings
{
    public const string DefaultProfileName = "默认";
    public const string ReservedProfileName = "config";   // 与指针文件 config.ini 同名，绝不能作为方案名

    public int LongPressMs = 300;
    public string ConfigFolder = "";
    public bool Enabled = true;
    public string TargetProcess = "";
    public string Profile = DefaultProfileName;
    public string CloseAction = "Ask";
    public bool BlockCtrl = true;
    public bool BlockShift = true;
    public bool BlockAlt = true;
    public bool BlockWin = true;
    public bool EggBoost = false;
    public bool RandomJitter = false;
    public bool SuppressToast = false;
    public bool SuppressEgg = false;
    public bool SuppressHud = false;
    public bool SuppressHotkeyToast = false;
    public bool HeaderAnim = true;
    public uint HotkeyMods = 0x0002;   // MOD_CONTROL
    public uint HotkeyVk = 0x7B;       // VK_F12
    public string Theme = "Night";     // Night / Day
    public List<KeyConfig> Keys = new List<KeyConfig>();
}

static class ConfigStore
{
    public static void SavePointer(AppSettings s, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[General]");
        sb.AppendLine("ConfigFolder=" + s.ConfigFolder);
        sb.AppendLine("Profile=" + s.Profile);
        sb.AppendLine("CloseAction=" + s.CloseAction);
        sb.AppendLine("Theme=" + s.Theme);
        WriteAll(path, sb.ToString());
    }

    public static AppSettings LoadPointer(string path)
    {
        var s = new AppSettings();
        if (!File.Exists(path)) return s;
        try
        {
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("[")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                if (k == "ConfigFolder") s.ConfigFolder = v;
                else if (k == "Profile") s.Profile = v;
                else if (k == "CloseAction") s.CloseAction = v;
                else if (k == "Theme") s.Theme = v;
            }
        }
        catch { }
        return s;
    }

    public static AppSettings LoadProfile(string path)
    {
        var s = new AppSettings();
        if (!File.Exists(path)) return s;
        try
        {
            string section = "";
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                if (section == "General")
                {
                    int n; bool b;
                    if (k == "LongPressMs") { if (int.TryParse(v, out n)) s.LongPressMs = n; }
                    else if (k == "Enabled") { if (bool.TryParse(v, out b)) s.Enabled = b; }
                    else if (k == "TargetProcess") s.TargetProcess = v;
                    else if (k == "ConfigFolder") s.ConfigFolder = v;
                    else if (k == "BlockCtrl") { if (bool.TryParse(v, out b)) s.BlockCtrl = b; }
                    else if (k == "BlockShift") { if (bool.TryParse(v, out b)) s.BlockShift = b; }
                    else if (k == "BlockAlt") { if (bool.TryParse(v, out b)) s.BlockAlt = b; }
                    else if (k == "BlockWin") { if (bool.TryParse(v, out b)) s.BlockWin = b; }
                    else if (k == "EggBoost") { if (bool.TryParse(v, out b)) s.EggBoost = b; }
                    else if (k == "RandomJitter") { if (bool.TryParse(v, out b)) s.RandomJitter = b; }
                    else if (k == "SuppressToast") { if (bool.TryParse(v, out b)) s.SuppressToast = b; }
                    else if (k == "SuppressEgg") { if (bool.TryParse(v, out b)) s.SuppressEgg = b; }
                    else if (k == "SuppressHud") { if (bool.TryParse(v, out b)) s.SuppressHud = b; }
                    else if (k == "SuppressHotkeyToast") { if (bool.TryParse(v, out b)) s.SuppressHotkeyToast = b; }
                    else if (k == "HeaderAnim") { if (bool.TryParse(v, out b)) s.HeaderAnim = b; }
                    else if (k == "Theme") s.Theme = v;
                    else if (k == "HotkeyMods") { uint u; if (uint.TryParse(v, out u)) s.HotkeyMods = u; }
                    else if (k == "HotkeyVk") { uint u; if (uint.TryParse(v, out u)) s.HotkeyVk = u; }
                }
                else if (section.StartsWith("Key"))
                {
                    int idx;
                    // 上限防损坏配置伪造 [Key999999] 时分配上百万行
                    if (!int.TryParse(section.Substring(3), out idx) || idx < 1 || idx > 200) continue;
                    while (s.Keys.Count < idx) s.Keys.Add(new KeyConfig());
                    var kc = s.Keys[idx - 1];
                    int n; bool b;
                    if (k == "Name") kc.Name = v;
                    else if (k == "Mode") kc.Mode = v == "TapOnce" ? KeyMode.TapOnce : v == "TapRepeat" ? KeyMode.TapRepeat : KeyMode.Hold;
                    else if (k == "RepeatIntervalMs") { if (int.TryParse(v, out n)) kc.RepeatIntervalMs = n; }
                    else if (k == "Enabled") { if (bool.TryParse(v, out b)) kc.Enabled = b; }
                    else if (k == "DelayMs") { if (int.TryParse(v, out n)) kc.DelayMs = n; }
                    else if (k == "Mods") { if (int.TryParse(v, out n)) kc.Mods = n; }
                }
            }
            foreach (var kc in s.Keys)
                if (kc.Name.Length > 0)
                {
                    try { kc.Vk = KeyNames.Resolve(kc.Name); } catch { kc.Enabled = false; }
                }
        }
        catch { }
        return s;
    }

    public static void SaveProfile(AppSettings s, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[General]");
        sb.AppendLine("LongPressMs=" + s.LongPressMs);
        sb.AppendLine("Enabled=" + s.Enabled);
        sb.AppendLine("TargetProcess=" + s.TargetProcess);
        sb.AppendLine("BlockCtrl=" + s.BlockCtrl);
        sb.AppendLine("BlockShift=" + s.BlockShift);
        sb.AppendLine("BlockAlt=" + s.BlockAlt);
        sb.AppendLine("BlockWin=" + s.BlockWin);
        sb.AppendLine("EggBoost=" + s.EggBoost);
        sb.AppendLine("RandomJitter=" + s.RandomJitter);
        sb.AppendLine("SuppressToast=" + s.SuppressToast);
        sb.AppendLine("SuppressEgg=" + s.SuppressEgg);
        sb.AppendLine("SuppressHud=" + s.SuppressHud);
        sb.AppendLine("SuppressHotkeyToast=" + s.SuppressHotkeyToast);
        sb.AppendLine("HeaderAnim=" + s.HeaderAnim);
        sb.AppendLine("HotkeyMods=" + s.HotkeyMods);
        sb.AppendLine("HotkeyVk=" + s.HotkeyVk);
        sb.AppendLine("Theme=" + s.Theme);
        for (int i = 0; i < s.Keys.Count; i++)
        {
            var kc = s.Keys[i];
            sb.AppendLine();
            sb.AppendLine("[Key" + (i + 1) + "]");
            sb.AppendLine("Name=" + kc.Name);
            sb.AppendLine("Enabled=" + kc.Enabled);
            sb.AppendLine("Mode=" + kc.Mode);
            sb.AppendLine("RepeatIntervalMs=" + kc.RepeatIntervalMs);
            sb.AppendLine("DelayMs=" + kc.DelayMs);
            sb.AppendLine("Mods=" + kc.Mods);
        }
        WriteAll(path, sb.ToString());
    }

    static void WriteAll(string path, string text)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, text);
    }

    // 方案名 "config" 在 ConfigFolder==exeDir 时会与指针文件 config.ini 撞名；
    // 这里作为共享纯函数，供 UI / 启动恢复 / 测试共同校验。
    public static bool IsReservedProfileName(string name)
    {
        if (name == null) return false;
        return string.Equals(name.Trim(), AppSettings.ReservedProfileName, StringComparison.OrdinalIgnoreCase);
    }

    public static List<string> ListProfiles(string folder)
    {
        var list = new List<string>();
        try
        {
            if (!Directory.Exists(folder)) return list;
            foreach (string f in Directory.GetFiles(folder, "*.ini"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (IsReservedProfileName(name)) continue;
                if (name.Length > 0) list.Add(name);
            }
        }
        catch { }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public static void DeleteProfile(string folder, string name)
    {
        try { string p = Path.Combine(folder, name + ".ini"); if (File.Exists(p)) File.Delete(p); } catch { }
    }

    public static string ProfilePath(AppSettings s, string exeDir)
    {
        string dir = string.IsNullOrEmpty(s.ConfigFolder) ? exeDir : s.ConfigFolder;
        return Path.Combine(dir, s.Profile + ".ini");
    }

    public static bool IsOldFormat(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("[Key") || line.StartsWith("LongPressMs=")) return true;
            }
        }
        catch { }
        return false;
    }
}
