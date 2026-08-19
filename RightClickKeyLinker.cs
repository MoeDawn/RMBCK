using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyCompany("SSJJ2.5")]
[assembly: AssemblyProduct("右键长按联动器")]
[assembly: AssemblyTitle("千恋万花安装器(反MC)")]
[assembly: AssemblyDescription("千恋万花安装器(反MC)")]
[assembly: AssemblyVersion("1.1.0")]
[assembly: AssemblyFileVersion("1.1.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]

class Program
{
    [STAThread]
    static void Main()
    {
        // 必须在创建任何窗口之前调用（否则弹过文件夹选择框后 SetCompatibleTextRenderingDefault 会死锁）
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
        AppSettings settings = LoadBootstrap(exeDir);
        if (!EnsureConfigFolder(settings, exeDir)) return;
        var hook = new MouseHook();
        if (!hook.Install())
        {
            MessageBox.Show("无法安装鼠标钩子，程序将退出。", "右键长按联动器",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        var ctrl = new TriggerController(new SendInputKeySender(), settings.Keys,
            settings.LongPressMs, settings.Enabled);
        Application.Run(new MainForm(settings, ctrl, hook, exeDir));
        hook.Dispose();
    }

    static AppSettings LoadBootstrap(string exeDir)
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
                    try { ConfigStore.SaveProfile(old, profileFile); } catch { }
                }
            }
            AppSettings profile = ConfigStore.LoadProfile(profileFile);
            profile.ConfigFolder = s.ConfigFolder;
            profile.Profile = s.Profile;
            profile.CloseAction = s.CloseAction;
            return profile;
        }

        return s;
    }

    static bool EnsureConfigFolder(AppSettings settings, string exeDir)
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

enum KeyMode { Hold, TapOnce, TapRepeat }

class KeyConfig
{
    public bool Enabled = true;
    public string Name = "";
    public int Vk;
    public KeyMode Mode = KeyMode.Hold;
    public int RepeatIntervalMs = 100;
}

static class KeyNames
{
    public static readonly string[] CommonKeys = new string[]
    {
        "Esc","F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
        "`","1","2","3","4","5","6","7","8","9","0","-","=","Backspace",
        "Tab","Q","W","E","R","T","Y","U","I","O","P","[","]","\\",
        "CapsLock","A","S","D","F","G","H","J","K","L",";","'","Enter",
        "Shift","Z","X","C","V","B","N","M",",",".","/",
        "Control","Win","Alt","Space","Menu",
        "PrintScreen","ScrollLock","Pause","Insert","Delete","Home","End","PageUp","PageDown",
        "Left","Up","Right","Down",
        "NumLock",
        "Num0","Num1","Num2","Num3","Num4","Num5","Num6","Num7","Num8","Num9","Num+","Num-","Num*","Num/","Num."
    };

    static readonly Dictionary<string, int> Map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "Space", 0x20 }, { "Enter", 0x0D }, { "Tab", 0x09 }, { "Esc", 0x1B },
        { "Backspace", 0x08 }, { "CapsLock", 0x14 },
        { "Shift", 0x10 }, { "Control", 0x11 }, { "Alt", 0x12 },
        { "Left", 0x25 }, { "Up", 0x26 }, { "Right", 0x27 }, { "Down", 0x28 },
        { "0", 0x30 }, { "1", 0x31 }, { "2", 0x32 }, { "3", 0x33 }, { "4", 0x34 },
        { "5", 0x35 }, { "6", 0x36 }, { "7", 0x37 }, { "8", 0x38 }, { "9", 0x39 },
        { "`", 0xC0 }, { "-", 0xBD }, { "=", 0xBB }, { "[", 0xDB }, { "]", 0xDD }, { "\\", 0xDC },
        { ";", 0xBA }, { "'", 0xDE }, { ",", 0xBC }, { ".", 0xBE }, { "/", 0xBF },
        { "Win", 0x5B }, { "Menu", 0x5D },
        { "PrintScreen", 0x2C }, { "ScrollLock", 0x91 }, { "Pause", 0x13 },
        { "Insert", 0x2D }, { "Delete", 0x2E }, { "Home", 0x24 }, { "End", 0x23 },
        { "PageUp", 0x21 }, { "PageDown", 0x22 },
        { "NumLock", 0x90 },
        { "Num0", 0x60 }, { "Num1", 0x61 }, { "Num2", 0x62 }, { "Num3", 0x63 }, { "Num4", 0x64 },
        { "Num5", 0x65 }, { "Num6", 0x66 }, { "Num7", 0x67 }, { "Num8", 0x68 }, { "Num9", 0x69 },
        { "Num+", 0x6B }, { "Num-", 0x6D }, { "Num*", 0x6A }, { "Num/", 0x6F }, { "Num.", 0x6E }
    };

    static Dictionary<int, string> Reverse;

    public static int Resolve(string name)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("无法识别的键位: " + name);
        string n = name.Trim();
        if (Map.ContainsKey(n)) return Map[n];
        if (n.Length == 1 && n[0] >= 'A' && n[0] <= 'Z') return n[0];
        if (n.Length == 1 && n[0] >= 'a' && n[0] <= 'z') return n[0] - 32;
        int f;
        if (n.Length > 1 && (n[0] == 'F' || n[0] == 'f') &&
            int.TryParse(n.Substring(1), out f) && f >= 1 && f <= 24)
            return 0x70 + f - 1; // VK_F1 = 0x70
        throw new ArgumentException("无法识别的键位: " + name);
    }

    public static string NameOf(int vk)
    {
        if (Reverse == null)
        {
            Reverse = new Dictionary<int, string>();
            foreach (var kv in Map)
                if (!Reverse.ContainsKey(kv.Value)) Reverse[kv.Value] = kv.Key;
        }
        if (Reverse.ContainsKey(vk)) return Reverse[vk];
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();   // A-Z
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();   // 0-9
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);   // F1-F24
        return "VK_" + vk.ToString("X");
    }
}

class AppSettings
{
    public int LongPressMs = 300;
    public string ConfigFolder = "";
    public bool Enabled = true;
    public string TargetProcess = "";   // 联动目标进程（空 = 不限制）
    public string Profile = "默认";
    public string CloseAction = "Ask";  // 关闭行为: Ask / Minimize / Exit
    public bool BlockCtrl = true;       // 是否屏蔽修饰键（按住右键时该修饰键按下则不触发）
    public bool BlockShift = true;
    public bool BlockAlt = true;
    public bool BlockWin = true;
    public bool EggBoost = false;       // 隐蔽开关：彩蛋概率提升（1/20 → 3/5）
    public bool RandomJitter = false;
    public bool SuppressToast = false;
    public bool SuppressEgg = false;
    public bool SuppressHud = false;
    public bool SuppressHotkeyToast = false;
    public bool HeaderAnim = true;   // 头部 logo 帧动画开关
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
        string dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, sb.ToString());
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
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[")) continue;
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                if (k == "ConfigFolder") s.ConfigFolder = v;
                else if (k == "Profile") s.Profile = v;
                else if (k == "CloseAction") s.CloseAction = v;
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
                    if (k == "LongPressMs") { int lp; if (int.TryParse(v, out lp)) s.LongPressMs = lp; }
                    else if (k == "Enabled") { bool en; if (bool.TryParse(v, out en)) s.Enabled = en; }
                    else if (k == "TargetProcess") s.TargetProcess = v;
                    else if (k == "ConfigFolder") s.ConfigFolder = v;   // 旧版单文件格式兼容
                    else if (k == "BlockCtrl") { bool b; if (bool.TryParse(v, out b)) s.BlockCtrl = b; }
                    else if (k == "BlockShift") { bool b; if (bool.TryParse(v, out b)) s.BlockShift = b; }
                    else if (k == "BlockAlt") { bool b; if (bool.TryParse(v, out b)) s.BlockAlt = b; }
                    else if (k == "BlockWin") { bool b; if (bool.TryParse(v, out b)) s.BlockWin = b; }
                    else if (k == "EggBoost") { bool b; if (bool.TryParse(v, out b)) s.EggBoost = b; }
                    else if (k == "RandomJitter") { bool b; if (bool.TryParse(v, out b)) s.RandomJitter = b; }
                    else if (k == "SuppressToast") { bool b; if (bool.TryParse(v, out b)) s.SuppressToast = b; }
                    else if (k == "SuppressEgg") { bool b; if (bool.TryParse(v, out b)) s.SuppressEgg = b; }
                    else if (k == "SuppressHud") { bool b; if (bool.TryParse(v, out b)) s.SuppressHud = b; }
                    else if (k == "SuppressHotkeyToast") { bool b; if (bool.TryParse(v, out b)) s.SuppressHotkeyToast = b; }
                    else if (k == "HeaderAnim") { bool b; if (bool.TryParse(v, out b)) s.HeaderAnim = b; }
                }
                else if (section.StartsWith("Key"))
                {
                    int idx;
                    if (!int.TryParse(section.Substring(3), out idx) || idx < 1) continue;
                    while (s.Keys.Count < idx) s.Keys.Add(new KeyConfig());
                    var kc = s.Keys[idx - 1];
                    if (k == "Name") kc.Name = v;
                    else if (k == "Mode")
                        kc.Mode = v == "TapOnce" ? KeyMode.TapOnce : v == "TapRepeat" ? KeyMode.TapRepeat : KeyMode.Hold;
                    else if (k == "RepeatIntervalMs") { int ri; if (int.TryParse(v, out ri)) kc.RepeatIntervalMs = ri; }
                    else if (k == "Enabled") { bool ke; if (bool.TryParse(v, out ke)) kc.Enabled = ke; }
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
        for (int i = 0; i < s.Keys.Count; i++)
        {
            var kc = s.Keys[i];
            sb.AppendLine();
            sb.AppendLine("[Key" + (i + 1) + "]");
            sb.AppendLine("Name=" + kc.Name);
            sb.AppendLine("Enabled=" + kc.Enabled);
            sb.AppendLine("Mode=" + kc.Mode);
            sb.AppendLine("RepeatIntervalMs=" + kc.RepeatIntervalMs);
        }
        string dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, sb.ToString());
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
                if (string.Equals(name, "config", StringComparison.OrdinalIgnoreCase)) continue;
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

interface IKeySender
{
    void KeyDown(int vk);
    void KeyUp(int vk);
    void KeyTap(int vk);
}

class TriggerController
{
    readonly IKeySender _keys;
    readonly List<KeyConfig> _config;
    int _thresholdMs;
    bool _enabled;
    bool _pressed;
    long _downTick;
    bool _triggered;
    bool _modifierCancelled;   // 本次按住被修饰键判定为其它操作，不触发
    int _armedThresholdMs;   // 本次按住的生效阈值（OnRightDown 时对配置值抖动一次并锁定，避免逐 tick 重新随机）
    readonly Dictionary<int, long> _lastTap = new Dictionary<int, long>();

    public Func<bool> ModifierHeld;

    public bool RandomJitter;
    readonly Random _rng = new Random();
    public int LastThresholdMs;

    // 返回带抖动的实际值：jitter=false 原值；true 时 ×(0.8~1.2)
    int Jitter(int v)
    {
        if (!RandomJitter || v <= 0) return v;
        return (int)(v * (0.8 + _rng.NextDouble() * 0.4));
    }

    public TriggerController(IKeySender keys, List<KeyConfig> config, int thresholdMs, bool enabled)
    {
        _keys = keys; _config = config; _thresholdMs = thresholdMs; _enabled = enabled;
    }

    public bool IsTriggered { get { return _triggered; } }

    public void SetEnabled(bool en)
    {
        _enabled = en;
        if (!en && _triggered) OnRightUp();
    }

    public void SetThreshold(int ms) { _thresholdMs = ms; if (_pressed) _armedThresholdMs = Jitter(ms); }

    public void OnRightDown(long nowMs)
    {
        _pressed = true;
        _downTick = nowMs;
        _modifierCancelled = false;
        _armedThresholdMs = Jitter(_thresholdMs);
        LastThresholdMs = _armedThresholdMs;
    }

    public void OnRightUp()
    {
        _pressed = false;
        if (_triggered)
        {
            foreach (var kc in _config)
                if (kc.Enabled && kc.Mode == KeyMode.Hold) _keys.KeyUp(kc.Vk);
            _triggered = false;
        }
        _modifierCancelled = false;
        _lastTap.Clear();
    }

    public void OnTick(long nowMs)
    {
        if (!_pressed || !_enabled) return;
        if (!_triggered)
        {
            if (nowMs - _downTick >= _armedThresholdMs)
            {
                if (ModifierHeld != null && ModifierHeld()) { _modifierCancelled = true; return; }
                if (_modifierCancelled) return;
                _triggered = true;
                foreach (var kc in _config)
                {
                    if (!kc.Enabled) continue;
                    if (kc.Mode == KeyMode.Hold) _keys.KeyDown(kc.Vk);
                    else { _keys.KeyTap(kc.Vk); _lastTap[kc.Vk] = nowMs; }
                }
            }
        }
        else
        {
            foreach (var kc in _config)
            {
                if (!kc.Enabled || kc.Mode != KeyMode.TapRepeat) continue;
                long last = _lastTap.ContainsKey(kc.Vk) ? _lastTap[kc.Vk] : nowMs;
                if (nowMs - last >= Jitter(kc.RepeatIntervalMs)) { _keys.KeyTap(kc.Vk); _lastTap[kc.Vk] = nowMs; }
            }
        }
    }
}

class SendInputKeySender : IKeySender
{
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx; public int dy;
        public uint mouseData; public uint dwFlags; public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk; public ushort wScan; public uint dwFlags;
        public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HARDWAREINPUT
    {
        public uint uMsg; public ushort wParamL; public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    public static int InputStructSize { get { return Marshal.SizeOf(typeof(INPUT)); } }

    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // 复用缓冲（仅 UI 线程调用，无竞争）；InputStructSize 供测试校验结构布局
    readonly INPUT[] _sendBuf = new INPUT[1];

    void SendKey(int vk, bool up)
    {
        var inp = new INPUT();
        inp.type = INPUT_KEYBOARD;
        inp.U.ki.wVk = (ushort)vk;
        inp.U.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
        inp.U.ki.dwExtraInfo = IntPtr.Zero;
        _sendBuf[0] = inp;
        SendInput(1, _sendBuf, Marshal.SizeOf(typeof(INPUT)));
    }

    public void KeyDown(int vk) { SendKey(vk, false); }
    public void KeyUp(int vk) { SendKey(vk, true); }
    public void KeyTap(int vk) { SendKey(vk, false); SendKey(vk, true); }
}

class MouseHook : IDisposable
{
    public event Action RightButtonDown;
    public event Action RightButtonUp;

    delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    const int WH_MOUSE_LL = 14;
    const int WM_RBUTTONDOWN = 0x0204;
    const int WM_RBUTTONUP = 0x0205;

    IntPtr _hookId = IntPtr.Zero;
    LowLevelMouseProc _proc;

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    public bool Install()
    {
        _proc = HookCallback;
        using (var cur = System.Diagnostics.Process.GetCurrentProcess())
        using (var mod = cur.MainModule)
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
        return _hookId != IntPtr.Zero;
    }

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                if (msg == WM_RBUTTONDOWN) { var h = RightButtonDown; if (h != null) h(); }
                else if (msg == WM_RBUTTONUP) { var h = RightButtonUp; if (h != null) h(); }
            }
        }
        catch { }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero) { UnhookWindowsHookEx(_hookId); _hookId = IntPtr.Zero; }
    }
}

class MainForm : Form
{
    readonly AppSettings _settings;
    readonly TriggerController _ctrl;
    readonly MouseHook _hook;
    readonly string _exeDir;

    RoundedSlider _thresholdSlider; TextBox _thresholdInput; Panel _inputCard;   // 原生 Panel 不自绘，DLP 不破坏
    DataGridView _grid;
    Button _addBtn, _delBtn;
    Button _testBtn; Label _statusRight, _statusKeys, _statusTarget, _statusPhysical;
    Label _cfgLabel; Button _browseBtn;
    Button _toggleBtn;
    ComboBox _procCombo; Button _procRefresh, _procClear; Label _procHint;
    ComboBox _profileCombo; Button _profileNew, _profileDel;
    FlatButton _modCtrl, _modShift, _modAlt, _modWin;
    Label _hotkeyHint;
    SecretDot _secretDot;
    ToolStripMenuItem _trayToggle;
    FlatButton _jitterBtn;
    FlatButton _btnMin, _btnClose;
    Panel _cfgCard;   // 原生 Panel 不自绘，DLP 不破坏
    FlatButton _supToast, _supEgg, _supHud, _supHotkey;
    FlatButton _animBtn;   // 头部动态图标开关
    BorderBox _cardTop, _cardKey, _cardTest, _cardBottom;   // 四张分区卡（Shown 时强制重绘防 DLP 首绘黑框）
    Timer _tickTimer, _testTimer, _gateTimer;
    NotifyIcon _tray; Icon _trayIcon;
    KeyboardHook _kbHook;
    ToastForm _toast;
    HudForm _hud;
    bool _testing;
    bool _loading;
    bool _thresholdSyncing;
    bool _linkageActive;
    bool _lastInTarget;
    bool _lastInTargetInit;    // 目标进程状态是否已初始化（避免初始/改动时误报）
    Random _rng = new Random();
    bool _exiting;
    int _lastPressedVk = -1;
    int _lastGridRow = -1;   // 记忆最后点选的表格行，供删除按钮在取消选中后仍能找到目标行
    const int HotkeyId = 1;
    const uint HotkeyMod = 0x0002;   // MOD_CONTROL
    const uint HotkeyVk = 0x7B;      // VK_F12

    const string HomepageUrl = "https://www.bilibili.com/video/BV1GJ411x7h7";

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Ui.PaintBackdrop(g, ClientSize.Width, ClientSize.Height, 0, 0);
        int hs = 54;
        if (_headerFrames.Length > 0)
            g.DrawImage(_headerFrames[_headerFrameIndex], new Rectangle(18, (hs - 28) / 2, 28, 28));
        // 头部标题/尺寸/版本串缓存为字段（每次重绘 new Font + 2 次 MeasureText 持续分配，Font 还是泄漏源）
        if (_titleFont == null)
        {
            _titleFont = new Font(Font.FontFamily, 13.5f, FontStyle.Bold);
            _titleSz = TextRenderer.MeasureText("RMB * Keybind", _titleFont);
            _versionText = "v" + Application.ProductVersion;
            _versionH = TextRenderer.MeasureText(_versionText, Font).Height;
        }
        using (var titleBrush = new LinearGradientBrush(new Rectangle(60, 10, _titleSz.Width + 4, 34), Theme.GradPink, Theme.GradViolet, 0f))
            g.DrawString("RMB * Keybind", _titleFont, titleBrush, 60, 12);
        // 版本号紧跟标题（避开右上角窗控按钮）
        TextRenderer.DrawText(g, _versionText, Font,
            new Point(60 + _titleSz.Width + 10, (hs - _versionH) / 2), Theme.TextSub);
        using (var penBrush = new LinearGradientBrush(new Rectangle(14, hs - 2, ClientSize.Width - 28, 2), Theme.GradPink, Theme.GradViolet, 0f))
        using (Pen pen = new Pen(penBrush, 1.6f))
            g.DrawLine(pen, 14, hs - 2, ClientSize.Width - 14, hs - 2);
    }

    Font _titleFont;
    Size _titleSz;
    string _versionText;
    int _versionH;

    // 头部 logo 帧动画：多帧时循环切换；任务栏/窗口图标不参与
    Image[] _headerFrames = new Image[0];
    int _headerFrameIndex;
    Timer _headerAnimTimer;
    const int HeaderFrameMs = 3000;   // 帧间隔
    static readonly Rectangle HeaderLogoRect = new Rectangle(18, 13, 28, 28);   // 头部 logo 绘制区（动画定向失效用）

    // 强制刷新整张卡及其子控件（DLP 首绘黑框 workaround：等价于鼠标掠过触发的重绘）
    static void RefreshCard(BorderBox card)
    {
        if (card == null) return;
        card.Invalidate(true);   // true = 含子控件
        card.Update();
    }

    // 圆角 Region 在尺寸变化时立即设置（构造设 Size 会触发；绘制期才设会先露出直角黑底）
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Region old = Region;
        Region = new Region(Ui.RoundedPath(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 16));
        if (old != null) old.Dispose();   // Region 每次赋值都新建，旧的必须释放（防 GDI 泄漏）
        Ui.InvalidateBackdropCache();
        Invalidate();
    }

    public MainForm(AppSettings settings, TriggerController ctrl, MouseHook hook, string exeDir)
    {
        _settings = settings; _ctrl = ctrl; _hook = hook; _exeDir = exeDir;
        _ctrl.ModifierHeld = () =>
            (_settings.BlockCtrl && (GetAsyncKeyState(0x11) & 0x8000) != 0) ||
            (_settings.BlockShift && (GetAsyncKeyState(0x10) & 0x8000) != 0) ||
            (_settings.BlockAlt && (GetAsyncKeyState(0x12) & 0x8000) != 0) ||
            (_settings.BlockWin && ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0));
        Text = "添加并配置右键关联的按键与模式,点击下方的联动按钮生效配置";
        // 注意顺序：必须先定无边框再设尺寸——设 Width 时若还带系统边框，ClientSize 会被扣掉边框宽度，
        // BuildUi 按偏小的宽度布局会导致最右侧控件（随机化按钮）超出卡片被裁
        FormBorderStyle = FormBorderStyle.None;
        Width = 720; Height = 716;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        BackColor = Theme.Bg;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        try
        {
            using (Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("RMBKeyLinker.window.ico"))
            {
                if (s != null) Icon = new System.Drawing.Icon(s);
                else Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
        }
        catch { }
        // 帧动画：内嵌资源 iconf1.png, iconf2.png, ... 依次加载，≥2 帧才启动定时器（任务栏/窗口图标不参与切换）
        try
        {
            var frames = new List<Image>();
            for (int i = 1; ; i++)
            {
                Stream fs = typeof(MainForm).Assembly.GetManifestResourceStream("RMBKeyLinker.iconf" + i + ".png");
                if (fs == null) break;
                using (fs) frames.Add(Image.FromStream(fs));
            }
            _headerFrames = frames.ToArray();
            if (_headerFrames.Length >= 2)
            {
                _headerAnimTimer = new Timer { Interval = HeaderFrameMs };
                _headerAnimTimer.Tick += (s, e) =>
                {
                    _headerFrameIndex = (_headerFrameIndex + 1) % _headerFrames.Length;
                    // 定向失效：只刷头部 28x28 logo 区域（原全窗 Invalidate+Update 每 3 秒强制整窗重绘）
                    Invalidate(HeaderLogoRect); Update();
                };
                _headerAnimTimer.Start();
            }
        }
        catch { _headerFrames = new Image[0]; }
        Shown += (s, e) =>
        {
            TopMost = true; Activate(); TopMost = false;
            // 强制自绘控件立即重绘（DLP 环境下自动重绘不可靠）。卡内按钮由卡的 Invalidate(true) 覆盖，
            // 无需逐个 Refresh（原 ~17 个按钮逐个同步重绘是启动卡顿的一部分）
            Invalidate(true); Update();
            _btnMin.Refresh(); _btnClose.Refresh();   // 窗控按钮在窗体头部，卡覆盖不到
            RefreshCard(_cardTop); RefreshCard(_cardKey); RefreshCard(_cardTest); RefreshCard(_cardBottom);
            _inputCard.Refresh(); _cfgCard.Refresh();   // 原生 Panel 系统自绘，Refresh 仅兜底
            _grid.Refresh();   // DataGridView 对 Invalidate(true) 不敏感，需显式 Refresh（否则表格区域残留黑块）
            _thresholdSlider.Refresh();
            PopulateProcesses();   // 窗口显示后异步填充（线程池枚举，UI 不冻结）
            PopulateProfileCombo();
            ApplyLinkageGate();
        };
        BuildUi();
        LoadSettingsToUi(false);   // 构造期不做进程枚举（Shown 时再填，此时含自身进程且句柄可列出）
        WireEvents();
        InitTray();
        _kbHook = new KeyboardHook();
        if (_kbHook.Install()) _kbHook.KeyDown += OnPhysicalKey;
        else { _kbHook.Dispose(); _kbHook = null; }
    }

    void BuildUi()
    {
        int W = ClientSize.Width;
        _btnMin = new FlatButton { Text = "—", Location = new Point(W - 76, 10), Width = 28, Height = 26, FlatStyle = FlatStyle.Flat };
        _btnMin.Click += (s, e) => MinimizeToTray();
        _btnClose = new FlatButton { Text = "✕", Location = new Point(W - 42, 10), Width = 28, Height = 26 };
        _btnClose.Click += (s, e) => Close();
        Controls.Add(_btnMin); Controls.Add(_btnClose);

        int y = 62;   // 头部横幅（54）+ 间距

        _cardTop = new BorderBox { Location = new Point(12, y), Width = W - 24, Height = 92, Radius = 14 };
        _toggleBtn = new FlatButton { Text = "当前联动状态:启用", Location = new Point(14, 12), Width = 190 };
        _cardTop.Controls.Add(_toggleBtn);
        var lblProfile = new Label { Text = "配置方案:", Location = new Point(215, 16), AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
        _cardTop.Controls.Add(lblProfile);
        _profileCombo = new ComboBox { Location = new Point(285, 12), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Card, ForeColor = Theme.Text };
        _profileNew = new FlatButton { Text = "＋ 新建", Location = new Point(470, 11), Width = 62 };
        _profileDel = new FlatButton { Text = "－ 删除", Location = new Point(536, 11), Width = 62 };
        _cardTop.Controls.Add(_profileCombo); _cardTop.Controls.Add(_profileNew); _cardTop.Controls.Add(_profileDel);
        _hotkeyHint = new Label { Text = "快捷键 Ctrl+F12 切换联动开关", Location = new Point(14, 62), AutoSize = true, ForeColor = Theme.TextSub, Font = new Font(Font.FontFamily, 9f), BackColor = Color.Transparent };
        _cardTop.Controls.Add(_hotkeyHint);
        var lblTh = new Label { Text = "长按检测时间(ms):", Location = new Point(230, 62), AutoSize = true, ForeColor = Theme.TextSub, Font = new Font(Font.FontFamily, 9f), BackColor = Color.Transparent };
        _cardTop.Controls.Add(lblTh);
        _thresholdSlider = new RoundedSlider { Location = new Point(365, 56), Width = 160, Minimum = 0, Maximum = 2000, Value = 300 };
        // 原生 Panel 系统绘制，DLP 首绘不破坏
        _thresholdInput = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text, TextAlign = HorizontalAlignment.Center, Font = new Font(Font.FontFamily, 9f) };
        _inputCard = new Panel { Location = new Point(532, 56), Width = 56, Height = 26, BackColor = Theme.Btn, Padding = new Padding(1) };
        _thresholdInput.Dock = DockStyle.Fill;
        _inputCard.Controls.Add(_thresholdInput);
        _cardTop.Controls.Add(_thresholdSlider); _cardTop.Controls.Add(_inputCard);
        _jitterBtn = new FlatButton { Text = "随机化:关", Location = new Point(596, 55), Width = 84, ToggleStyle = true };
        _jitterBtn.Click += (s, e) =>
        {
            _settings.RandomJitter = !_settings.RandomJitter;
            _ctrl.RandomJitter = _settings.RandomJitter;
            UpdateJitterButton();
            SaveSettings();
        };
        var jitterTip = new ToolTip();
        jitterTip.SetToolTip(_jitterBtn, "开启后：长按阈值与连发间隔每次 ±20% 随机抖动\n降低被反作弊判定为宏的风险");
        _cardTop.Controls.Add(_jitterBtn);
        Controls.Add(_cardTop);
        y += 92 + 10;

        _cardKey = new BorderBox { Location = new Point(12, y), Width = W - 24, Height = 210, Radius = 14 };
        int gridY = 10;
        _grid = new DataGridView
        {
            Location = new Point(12, gridY), Width = 470, Height = 185,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            EditMode = DataGridViewEditMode.EditOnEnter
        };
        var colEnable = new DataGridViewCheckBoxColumn { Name = "启用", HeaderText = "启用", Width = 50 };
        var colKey = new DataGridViewComboBoxColumn { Name = "键位", HeaderText = "键位", Width = 130 };
        colKey.Items.AddRange(KeyNames.CommonKeys);
        var colMode = new DataGridViewComboBoxColumn { Name = "模式", HeaderText = "模式", Width = 90 };
        colMode.Items.AddRange(new object[] { "保持", "点按" });
        var colTap = new DataGridViewComboBoxColumn { Name = "点按方式", HeaderText = "点按方式", Width = 90, Visible = false };
        colTap.Items.AddRange(new object[] { "单次", "连发" });
        var colInterval = new DataGridViewTextBoxColumn { Name = "连发间隔ms", HeaderText = "连发间隔ms", Width = 90, Visible = false };
        _grid.Columns.AddRange(colEnable, colKey, colMode, colTap, colInterval);
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.GridColor = Theme.GridLine;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.EnableHeadersVisualStyles = false;
        // DataGridView 表头不支持渐变，用纯粉近似
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Btn;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Btn;
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.DefaultCellStyle.BackColor = Color.White;
        _grid.DefaultCellStyle.ForeColor = Theme.Text;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.Sel;
        _grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
        _grid.GridColor = Theme.GridLine;
        _grid.CellValueChanged += OnGridCellChanged;
        _grid.CurrentCellDirtyStateChanged += (s, e) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _grid.CellMouseDown += (s, e) => { if (e.RowIndex >= 0) _lastGridRow = e.RowIndex; };
        _grid.Leave += (s, e) => DeselectGrid();
        _cardKey.Controls.Add(_grid);
        _addBtn = new FlatButton { Text = "＋ 添加按键", Location = new Point(500, gridY), Width = 110 };
        _addBtn.Click += (s, e) => { _grid.Rows.Add(true, "Q", "保持", "单次", 100); _lastGridRow = _grid.Rows.Count - 1; SaveSettings(); UpdateGridColumnVisibility(); };
        _delBtn = new FlatButton { Text = "－ 删除选中", Location = new Point(500, gridY + 36), Width = 110 };
        _delBtn.Click += (s, e) => DeleteSelectedRow();
        _cardKey.Controls.Add(_addBtn); _cardKey.Controls.Add(_delBtn);
        Controls.Add(_cardKey);
        y += 210 + 10;

        _cardTest = new BorderBox { Location = new Point(12, y), Width = W - 24, Height = 108, Radius = 14 };
        _testBtn = new FlatButton { Text = "开始测试", Location = new Point(14, 12), Width = 120 };
        _statusRight = new Label { Text = "点击“开始测试”确认设置生效情况", Location = new Point(150, 18), AutoSize = true, ForeColor = Theme.Accent, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold), BackColor = Color.Transparent };
        _cardTest.Controls.Add(_testBtn); _cardTest.Controls.Add(_statusRight);
        _statusKeys = new Label { Text = "", Location = new Point(150, 42), AutoSize = true, ForeColor = Theme.Accent, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold), BackColor = Color.Transparent };
        _statusTarget = new Label { Text = "", Location = new Point(150, 62), AutoSize = true, ForeColor = Theme.Accent, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold), BackColor = Color.Transparent };
        _statusPhysical = new Label { Text = "", Location = new Point(150, 82), AutoSize = true, ForeColor = Theme.Accent, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold), BackColor = Color.Transparent };
        _cardTest.Controls.Add(_statusKeys); _cardTest.Controls.Add(_statusTarget); _cardTest.Controls.Add(_statusPhysical);
        Controls.Add(_cardTest);
        y += 108 + 10;

        int bottomH = ClientSize.Height - y - 52;   // 底部留声明两排
        _cardBottom = new BorderBox { Location = new Point(12, y), Width = W - 24, Height = bottomH, Radius = 14 };
        var lblProc = new Label { Text = "联动目标进程:", Location = new Point(14, 10), AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
        _cardBottom.Controls.Add(lblProc);
        _procCombo = new ComboBox { Location = new Point(120, 6), Width = 300, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Card, ForeColor = Theme.Text, DrawMode = DrawMode.OwnerDrawFixed };
        _procRefresh = new FlatButton { Text = "刷新", Location = new Point(510, 5), Width = 58 };
        _procClear = new FlatButton { Text = "清除", Location = new Point(573, 5), Width = 58 };
        _cardBottom.Controls.Add(_procCombo); _cardBottom.Controls.Add(_procRefresh); _cardBottom.Controls.Add(_procClear);
        _procHint = new HintLabel { Text = "未选择进程时默认全局生效", Location = new Point(6, 3), AutoSize = false, Width = 255, Height = 16, ForeColor = Theme.Accent, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold), BackColor = Color.Transparent };
        _procCombo.Controls.Add(_procHint);
        var lblCfg = new Label { Text = "配置文件位置:", Location = new Point(14, 48), AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent };
        _cardBottom.Controls.Add(lblCfg);
        _cfgCard = new Panel { Location = new Point(120, 44), Width = 428, Height = 26, BackColor = Theme.Btn, Padding = new Padding(1) };
        _cfgLabel = new Label { Text = "", Dock = DockStyle.Fill, AutoEllipsis = true, BackColor = Theme.Card, ForeColor = Theme.Text, Font = new Font(Font.FontFamily, 9f), TextAlign = ContentAlignment.MiddleLeft };
        _cfgCard.Controls.Add(_cfgLabel);
        _browseBtn = new FlatButton { Text = "浏览…", Location = new Point(555, 43), Width = 76 };
        _cardBottom.Controls.Add(_cfgCard); _cardBottom.Controls.Add(_browseBtn);
        int my = 82;
        var feedbackBtn = new FlatButton { Text = "报错反馈", Location = new Point(14, my), Width = 90 };
        feedbackBtn.Click += (s, e) => StartFeedbackFlow();
        _cardBottom.Controls.Add(feedbackBtn);
        _modCtrl = new FlatButton { Text = "Ctrl", Location = new Point(112, my), Width = 64, ToggleStyle = true };
        _modShift = new FlatButton { Text = "Shift", Location = new Point(180, my), Width = 64, ToggleStyle = true };
        _modAlt = new FlatButton { Text = "Alt", Location = new Point(248, my), Width = 64, ToggleStyle = true };
        _modWin = new FlatButton { Text = "Win", Location = new Point(316, my), Width = 64, ToggleStyle = true };
        _modCtrl.Click += (s, e) => ToggleModBlock(ref _settings.BlockCtrl, _modCtrl, "Ctrl");
        _modShift.Click += (s, e) => ToggleModBlock(ref _settings.BlockShift, _modShift, "Shift");
        _modAlt.Click += (s, e) => ToggleModBlock(ref _settings.BlockAlt, _modAlt, "Alt");
        _modWin.Click += (s, e) => ToggleModBlock(ref _settings.BlockWin, _modWin, "Win");
        var modTip = new ToolTip();
        modTip.SetToolTip(_modCtrl, "屏蔽：按住 Ctrl+右键 不触发联动（避免误触）\n放行：Ctrl+右键 可正常触发");
        modTip.SetToolTip(_modShift, "屏蔽：按住 Shift+右键 不触发联动\n放行：Shift+右键 可正常触发");
        modTip.SetToolTip(_modAlt, "屏蔽：按住 Alt+右键 不触发联动\n放行：Alt+右键 可正常触发");
        modTip.SetToolTip(_modWin, "屏蔽：按住 Win+右键 不触发联动\n放行：Win+右键 可正常触发");
        _cardBottom.Controls.Add(_modCtrl); _cardBottom.Controls.Add(_modShift); _cardBottom.Controls.Add(_modAlt); _cardBottom.Controls.Add(_modWin);
        int sy = 120;
        _supToast = new FlatButton { Text = "提示:开", Location = new Point(14, sy), Width = 62, ToggleStyle = true };
        _supEgg = new FlatButton { Text = "彩蛋:开", Location = new Point(80, sy), Width = 66, ToggleStyle = true };
        _supHud = new FlatButton { Text = "HUD:开", Location = new Point(150, sy), Width = 66, ToggleStyle = true };
        _supHotkey = new FlatButton { Text = "热键提示:开", Location = new Point(220, sy), Width = 88, ToggleStyle = true };
        _animBtn = new FlatButton { Text = "动态图标:开", Location = new Point(314, sy), Width = 92, ToggleStyle = true };
        _animBtn.Click += (s, e) =>
        {
            _settings.HeaderAnim = !_settings.HeaderAnim;
            SetHeaderAnim(_settings.HeaderAnim);
            SaveSettings();
        };
        var animTip = new ToolTip();
        animTip.SetToolTip(_animBtn, "开：窗口头部 logo 每 3 秒切换一张\n关：固定显示第一张");
        _supToast.Click += (s, e) => ToggleSup(_supToast, ref _settings.SuppressToast, "提示");
        _supEgg.Click += (s, e) => ToggleSup(_supEgg, ref _settings.SuppressEgg, "彩蛋");
        _supHud.Click += (s, e) => ToggleSup(_supHud, ref _settings.SuppressHud, "HUD");
        _supHotkey.Click += (s, e) => ToggleSup(_supHotkey, ref _settings.SuppressHotkeyToast, "热键提示");
        var supTip = new ToolTip();
        supTip.SetToolTip(_supToast, "进程切换时的 ✅生效/❌失效 提示弹窗");
        supTip.SetToolTip(_supEgg, "切入目标进程时概率触发的彩蛋");
        supTip.SetToolTip(_supHud, "触发联动时右下角的 ⚡ 迷你悬浮窗");
        supTip.SetToolTip(_supHotkey, "Ctrl+F12 切换联动开关时的状态提示");
        _cardBottom.Controls.Add(_supToast); _cardBottom.Controls.Add(_supEgg); _cardBottom.Controls.Add(_supHud); _cardBottom.Controls.Add(_supHotkey);
        _cardBottom.Controls.Add(_animBtn);
        Controls.Add(_cardBottom);

        var footer1 = new Label
        {
            Text = "If there are any problems with this project, you don't have to go to the author.",
            AutoSize = false,
            BackColor = Color.Transparent,
            Width = ClientSize.Width - 16,
            Height = 14,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 7f),
            ForeColor = Theme.TextSub,
            Cursor = Cursors.Hand,
            Location = new Point(8, ClientSize.Height - 44)
        };
        footer1.Click += (s, e) => OpenHomepage();
        var footer2 = new Label
        {
            Text = "使用本软件自动视为放弃自身一切权益,一切解释权归作者所有,点击访问作者个人主页",
            AutoSize = false,
            BackColor = Color.Transparent,
            Width = ClientSize.Width - 16,
            Height = 14,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 7f),
            ForeColor = Theme.TextSub,
            Cursor = Cursors.Hand,
            Location = new Point(8, ClientSize.Height - 26)
        };
        footer2.Click += (s, e) => OpenHomepage();
        Controls.Add(footer1);
        Controls.Add(footer2);
        _secretDot = new SecretDot { Location = new Point(ClientSize.Width - 18, ClientSize.Height - 30) };
        _secretDot.Click += (s, e) =>
        {
            _settings.EggBoost = _secretDot.On;
            SaveSettings();
        };
        Controls.Add(_secretDot);
    }

    void LoadSettingsToUi(bool populateProcs)
    {
        _loading = true;
        _thresholdSlider.Value = Math.Max(_thresholdSlider.Minimum, Math.Min(_thresholdSlider.Maximum, _settings.LongPressMs));
        _thresholdInput.Text = _thresholdSlider.Value.ToString();
        _cfgLabel.Text = string.IsNullOrEmpty(_settings.ConfigFolder) ? _exeDir : _settings.ConfigFolder;
        _toggleBtn.Text = _settings.Enabled ? "当前联动状态:启用" : "当前联动状态:禁用";
        UpdateModButton(_modCtrl, "Ctrl", _settings.BlockCtrl);
        UpdateModButton(_modShift, "Shift", _settings.BlockShift);
        UpdateModButton(_modAlt, "Alt", _settings.BlockAlt);
        UpdateModButton(_modWin, "Win", _settings.BlockWin);
        if (_secretDot != null) _secretDot.On = _settings.EggBoost;
        // 弹窗禁用开关初始状态（Checked=true 表示弹窗启用）
        _supToast.Checked = !_settings.SuppressToast; _supToast.Text = _settings.SuppressToast ? "提示:关" : "提示:开";
        _supEgg.Checked = !_settings.SuppressEgg; _supEgg.Text = _settings.SuppressEgg ? "彩蛋:关" : "彩蛋:开";
        _supHud.Checked = !_settings.SuppressHud; _supHud.Text = _settings.SuppressHud ? "HUD:关" : "HUD:开";
        _supHotkey.Checked = !_settings.SuppressHotkeyToast; _supHotkey.Text = _settings.SuppressHotkeyToast ? "热键提示:关" : "热键提示:开";
        // 动态图标开关初始状态（含停/启 Timer、回第 1 帧）
        if (_headerAnimTimer != null || _headerFrames.Length > 0)
            SetHeaderAnim(_settings.HeaderAnim);
        _ctrl.RandomJitter = _settings.RandomJitter;
        UpdateJitterButton();
        if (populateProcs) PopulateProcesses();   // 首次构造跳过（Shown 会填，含自身进程）；方案切换时填
        if (!string.IsNullOrEmpty(_settings.TargetProcess))
        {
            for (int i = 0; i < _procCombo.Items.Count; i++)
            {
                ProcessItem item = _procCombo.Items[i] as ProcessItem;
                if (item != null && string.Equals(item.Name, _settings.TargetProcess, StringComparison.OrdinalIgnoreCase))
                { _procCombo.SelectedIndex = i; break; }
            }
        }
        _grid.Rows.Clear();
        foreach (var kc in _settings.Keys)
            if (kc.Name.Length > 0)
                _grid.Rows.Add(kc.Enabled, kc.Name,
                    kc.Mode == KeyMode.Hold ? "保持" : "点按",
                    kc.Mode == KeyMode.TapOnce ? "单次" : "连发",
                    kc.RepeatIntervalMs);
        _procHint.Visible = string.IsNullOrEmpty(_settings.TargetProcess);
        UpdateGridColumnVisibility();
        PopulateProfileCombo();
        _loading = false;
        UpdateStatusLabels();
    }

    void OnGridCellChanged(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0) return;
        var row = _grid.Rows[e.RowIndex];
        bool tap = Convert.ToString(row.Cells["模式"].Value) == "点按";
        bool repeat = tap && Convert.ToString(row.Cells["点按方式"].Value) == "连发";
        if (e.ColumnIndex == _grid.Columns["模式"].Index)
        {
            row.Cells["点按方式"].ReadOnly = !tap;
            row.Cells["连发间隔ms"].ReadOnly = !repeat;
        }
        if (e.ColumnIndex == _grid.Columns["点按方式"].Index)
            row.Cells["连发间隔ms"].ReadOnly = !repeat;
        if (_loading) return;
        UpdateGridColumnVisibility();
        SaveSettings();
    }

    void UpdateGridColumnVisibility()
    {
        bool anyTap = false, anyRepeat = false;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            string mode = Convert.ToString(row.Cells["模式"].Value);
            if (mode == "点按")
            {
                anyTap = true;
                if (Convert.ToString(row.Cells["点按方式"].Value) == "连发") anyRepeat = true;
            }
        }
        _grid.Columns["点按方式"].Visible = anyTap;
        _grid.Columns["连发间隔ms"].Visible = anyRepeat;
    }

    void DeselectGrid()
    {
        if (_grid == null) return;
        try
        {
            if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
            _grid.ClearSelection();
            if (_grid.CurrentCell != null) _grid.CurrentCell = null;
        }
        catch { }
    }

    void DeleteSelectedRow()
    {
        int idx = -1;
        if (_grid.CurrentRow != null) idx = _grid.CurrentRow.Index;
        else if (_lastGridRow >= 0 && _lastGridRow < _grid.Rows.Count) idx = _lastGridRow;
        if (idx < 0) return;
        _grid.Rows.RemoveAt(idx);
        _lastGridRow = -1;
        SaveSettings();
        UpdateGridColumnVisibility();
    }

    void WireEvents()
    {
        Application.AddMessageFilter(new GridDeselectFilter(_grid, DeselectGrid));
        Deactivate += (s, e) => DeselectGrid();
        _thresholdSlider.ValueChanged += (s, e) =>
        {
            if (_thresholdSyncing) return;
            _thresholdSyncing = true;
            _thresholdInput.Text = _thresholdSlider.Value.ToString();
            _ctrl.SetThreshold(_thresholdSlider.Value);
            SaveSettings();
            _thresholdSyncing = false;
        };
        _thresholdInput.Leave += (s, e) => ApplyThresholdInput();
        _thresholdInput.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) ApplyThresholdInput(); };
        _thresholdInput.GotFocus += (s, e) => SetInputBorder(Theme.Accent);
        _thresholdInput.LostFocus += (s, e) => SetInputBorder(Theme.Btn);
        _hook.RightButtonDown += () => { _ctrl.OnRightDown(Environment.TickCount); if (_testing) UpdateStatusLabels(); };
        _hook.RightButtonUp += () => { _ctrl.OnRightUp(); if (_testing) UpdateStatusLabels(); };
        _browseBtn.Click += (s, e) => ChooseConfigFolder();
        _toggleBtn.Click += (s, e) => ToggleEnabled();
        _testBtn.Click += (s, e) => ToggleTest();
        _profileCombo.SelectedIndexChanged += (s, e) =>
        {
            if (_loading) return;
            string n = _profileCombo.SelectedItem as string;
            if (n != null && !string.Equals(n, _settings.Profile, StringComparison.OrdinalIgnoreCase)) SwitchProfile(n);
        };
        _profileNew.Click += (s, e) => NewProfile();
        _profileDel.Click += (s, e) => DeleteProfile();
        _procCombo.SelectedIndexChanged += (s, e) =>
        {
            if (_loading) return;
            ProcessItem item = _procCombo.SelectedItem as ProcessItem;
            _settings.TargetProcess = (item != null) ? item.Name : "";
            _procHint.Visible = string.IsNullOrEmpty(_settings.TargetProcess);
            _lastInTargetInit = false;
            SaveSettings();
            ApplyLinkageGate();
        };
        _procCombo.DrawItem += (s, e) =>
        {
            if (e.Index >= 0 && e.Index < _procCombo.Items.Count)
            {
                ProcessItem item = _procCombo.Items[e.Index] as ProcessItem;
                if (item != null)
                {
                    using (SolidBrush bg = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Theme.Sel : Theme.Card))
                        e.Graphics.FillRectangle(bg, e.Bounds);   // 一遍画底（原 DrawBackground+FillRectangle 画两遍）
                    TextRenderer.DrawText(e.Graphics, item.Display, e.Font, e.Bounds, Theme.Text,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                }
            }
            if ((e.State & DrawItemState.Selected) != 0) e.DrawFocusRectangle();
        };
        _procRefresh.Click += (s, e) => PopulateProcesses();
        _procClear.Click += (s, e) =>
        {
            _settings.TargetProcess = "";
            _procCombo.SelectedIndex = -1;
            _procHint.Visible = true;
            _lastInTargetInit = false;
            SaveSettings();
            ApplyLinkageGate();
        };
        _tickTimer = new Timer { Interval = 10 };
        _tickTimer.Tick += (s, e) =>
        {
            _ctrl.OnTick(Environment.TickCount);
            UpdateHud();
        };
        _tickTimer.Start();
        // 前台进程门控用独立 750ms 定时器：进程查询频率更低，进一步降低卡顿（切窗口响应 ≤0.75s）
        _gateTimer = new Timer { Interval = 750 };
        _gateTimer.Tick += (s, e) => ApplyLinkageGate();
        _gateTimer.Start();
    }

    void ApplyThresholdInput()
    {
        int v;
        if (int.TryParse(_thresholdInput.Text.Trim(), out v))
        {
            if (v < _thresholdSlider.Minimum) v = _thresholdSlider.Minimum;
            if (v > _thresholdSlider.Maximum) v = _thresholdSlider.Maximum;
            if (_thresholdSyncing) return;
            _thresholdSyncing = true;
            _thresholdSlider.Value = v;      // 触发 ValueChanged，已用守卫防止重入
            _thresholdInput.Text = v.ToString();
            _ctrl.SetThreshold(v);
            SaveSettings();
            _thresholdSyncing = false;
        }
        else
        {
            _thresholdInput.Text = _thresholdSlider.Value.ToString();
        }
    }

    void SetInputBorder(Color c)
    {
        if (_inputCard == null) return;
        _inputCard.BackColor = c;
    }

    void ChooseConfigFolder()
    {
        using (var dlg = new FolderBrowserDialog())
        {
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _settings.ConfigFolder = dlg.SelectedPath;
                _cfgLabel.Text = _settings.ConfigFolder;
                SaveSettings();
                PopulateProfileCombo();
            }
        }
    }

    void ToggleEnabled()
    {
        _settings.Enabled = !_settings.Enabled;
        _toggleBtn.Text = _settings.Enabled ? "当前联动状态:启用" : "当前联动状态:禁用";
        SaveSettings();
        ApplyLinkageGate();
    }

    void ToggleModBlock(ref bool setting, FlatButton btn, string name)
    {
        setting = !setting;
        UpdateModButton(btn, name, setting);
        SaveSettings();
    }

    void UpdateModButton(FlatButton btn, string name, bool block)
    {
        if (btn == null) return;
        btn.Text = name + (block ? "屏蔽" : "放行");
        btn.Checked = block;
        btn.Refresh();
    }

    void UpdateJitterButton()
    {
        if (_jitterBtn == null) return;
        _jitterBtn.Text = _settings.RandomJitter ? "随机化:开" : "随机化:关";
        _jitterBtn.Checked = _settings.RandomJitter;
        _jitterBtn.Refresh();
    }

    // 进程枚举昂贵（GetProcesses + 每进程窗口标题查询，100ms~2s），必须移出 UI 线程：
    // 线程池收集 → BeginInvoke 回传；代数计数丢弃过期结果（刷新与切方案竞态）
    int _procGen;

    void PopulateProcesses()
    {
        int gen = ++_procGen;
        string sel = _settings.TargetProcess;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate(object state)
        {
            List<ProcessItem> list = CollectProcesses();
            BeginInvoke((MethodInvoker)delegate
            {
                if (gen != _procGen || IsDisposed) return;
                ApplyProcessList(list, sel);
            });
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
                    list.Add(new ProcessItem { Name = p.ProcessName, Display = p.ProcessName + " — " + p.MainWindowTitle });
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        list.Sort(delegate(ProcessItem a, ProcessItem b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
        return list;
    }

    void ApplyProcessList(List<ProcessItem> list, string sel)
    {
        _loading = true;
        try
        {
            _procCombo.BeginUpdate();   // 批量填充免逐条重绘
            try
            {
                _procCombo.Items.Clear();
                int selIndex = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    _procCombo.Items.Add(list[i]);
                    if (string.Equals(list[i].Name, sel, StringComparison.OrdinalIgnoreCase)) selIndex = i;
                }
                if (selIndex >= 0) _procCombo.SelectedIndex = selIndex;
            }
            finally { _procCombo.EndUpdate(); }
        }
        finally { _loading = false; }
    }

    void ToggleSup(FlatButton btn, ref bool suppressed, string name)
    {
        suppressed = !suppressed;
        btn.Checked = !suppressed;
        btn.Text = name + (suppressed ? ":关" : ":开");
        btn.Refresh();
        SaveSettings();
    }

    // 头部动态图标开关：控制帧动画 Timer；关闭时回第 1 帧并重绘
    void SetHeaderAnim(bool on)
    {
        if (_headerAnimTimer != null)
        {
            if (on) _headerAnimTimer.Start();
            else _headerAnimTimer.Stop();
        }
        if (!on)
        {
            _headerFrameIndex = 0;
            Invalidate(HeaderLogoRect); Update();
        }
        _animBtn.Checked = on;
        _animBtn.Text = on ? "动态图标:开" : "动态图标:关";
        _animBtn.Refresh();
    }

    // 未选目标进程时始终为真；选了目标则要求该进程窗口在前台。
    // 性能优化：缓存"前台窗口句柄 → 是否目标"的结果，只有前台窗口真正切换时才查询进程
    // （GetProcessById 创建进程对象代价高，之前每 0.75s 一次导致游戏卡顿）
    IntPtr _lastFgHwnd = IntPtr.Zero;
    string _cacheTarget = "";
    bool _cachedInTarget;

    bool TargetForegroundActive()
    {
        if (string.IsNullOrEmpty(_settings.TargetProcess)) return true;
        if (!string.Equals(_cacheTarget, _settings.TargetProcess, StringComparison.OrdinalIgnoreCase))
        {
            _cacheTarget = _settings.TargetProcess;
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
                _cachedInTarget = string.Equals(p.ProcessName, _settings.TargetProcess, StringComparison.OrdinalIgnoreCase);
        }
        catch { _cachedInTarget = false; }
        return _cachedInTarget;
    }

    void ApplyLinkageGate()
    {
        bool inTarget = TargetForegroundActive();
        bool active = _settings.Enabled && inTarget;
        if (active != _linkageActive)
        {
            _linkageActive = active;
            _ctrl.SetEnabled(active);
            UpdateStatusLabels();
        }
        if (_settings.Enabled && !string.IsNullOrEmpty(_settings.TargetProcess))
        {
            if (_lastInTargetInit && inTarget != _lastInTarget)
            {
                if (inTarget)
                {
                    // 彩蛋：默认 1/20 概率切入目标时显示整蛊列表；隐蔽开关开启后 3/5（彩蛋被禁用时只出普通提示）
                    if (!_settings.SuppressEgg)
                    {
                        int odds = _settings.EggBoost ? 5 : 20;
                        if (_rng.Next(odds) < (_settings.EggBoost ? 3 : 1)) { ShowEasterEgg(); _lastInTarget = inTarget; _lastInTargetInit = true; return; }
                    }
                    if (!_settings.SuppressToast) ShowToast("✅ 已切换至目标进程,配置生效", 1500);
                }
                else if (!_settings.SuppressToast) ShowToast("❌ 已切换至其他进程,配置失效", 1500);
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

    void ToggleTest()
    {
        _testing = !_testing;
        _testBtn.Text = _testing ? "停止测试" : "开始测试";
        if (_testing)
        {
            _testTimer = new Timer { Interval = 50 };
            _testTimer.Tick += (s, e) => UpdateStatusLabels();
            _testTimer.Start();
        }
        else if (_testTimer != null) { _testTimer.Stop(); _testTimer.Dispose(); _testTimer = null; }
        UpdateStatusLabels();
    }

    void UpdateStatusLabels()
    {
        if (!_testing)
        {
            _statusRight.Text = "点击“开始测试”确认设置生效情况";
            _statusKeys.Text = "";
            _statusTarget.Text = "";
            _statusTarget.Visible = false;
            _statusPhysical.Text = "";
            return;
        }
        bool down = (GetAsyncKeyState(0x02) & 0x8000) != 0; // VK_RBUTTON
        _statusRight.Text = down ? "当前右键状态:按下" : "当前右键状态:释放";
        var parts = new List<string>();
        if (_ctrl.IsTriggered)
            foreach (var kc in _settings.Keys)
                if (kc.Enabled)
                {
                    string mode = kc.Mode == KeyMode.Hold ? "保持"
                        : kc.Mode == KeyMode.TapOnce ? "点按·单次" : "点按·连发";
                    parts.Add(kc.Name + "（" + mode + "）");
                }
        _statusKeys.Text = parts.Count == 0 ? "当前触发键位：无" : "当前触发键位：" + string.Join("、", parts);
        if (_settings.RandomJitter)
            _statusKeys.Text += "　|　本次延时: " + _ctrl.LastThresholdMs + "ms";
        _statusPhysical.Text = _lastPressedVk < 0
            ? "实际按下：无"
            : "实际按下：" + KeyNames.NameOf(_lastPressedVk) + " (0x" + _lastPressedVk.ToString("X2") + ")";
        if (string.IsNullOrEmpty(_settings.TargetProcess))
        {
            _statusTarget.Text = "";
            _statusTarget.Visible = false;
        }
        else
        {
            _statusTarget.Visible = true;
            bool inTarget = TargetForegroundActive();
            _statusTarget.Text = inTarget ? "已联动目标进程" : "请切换至目标进程窗口";
            _statusTarget.ForeColor = inTarget ? Theme.Accent : Color.FromArgb(235, 90, 110);
        }
    }

    string ProfileDir()
    {
        return string.IsNullOrEmpty(_settings.ConfigFolder) ? _exeDir : _settings.ConfigFolder;
    }

    void PopulateProfileCombo()
    {
        _profileCombo.Items.Clear();
        List<string> list = ConfigStore.ListProfiles(ProfileDir());
        if (list.Count == 0)
        {
            list.Add(_settings.Profile);
            try { ConfigStore.SaveProfile(_settings, Path.Combine(ProfileDir(), _settings.Profile + ".ini")); } catch { }
        }
        bool found = false;
        for (int i = 0; i < list.Count; i++)
        {
            _profileCombo.Items.Add(list[i]);
            if (string.Equals(list[i], _settings.Profile, StringComparison.OrdinalIgnoreCase))
            { _profileCombo.SelectedIndex = i; found = true; }
        }
        if (!found && _profileCombo.Items.Count > 0) _profileCombo.SelectedIndex = 0;
    }

    void SwitchProfile(string name)
    {
        SaveSettingsNow();
        LoadProfileInto(name);
    }

    // 仅加载指定方案（用于切换与删除后的跳转，不触发保存）
    void LoadProfileInto(string name)
    {
        if (_saveTimer != null) _saveTimer.Stop();   // 丢弃挂起的防抖写
        _saveDirty = false;
        _settings.Profile = name;
        AppSettings loaded = ConfigStore.LoadProfile(Path.Combine(ProfileDir(), name + ".ini"));
        _settings.LongPressMs = loaded.LongPressMs;
        _settings.Enabled = loaded.Enabled;
        _settings.TargetProcess = loaded.TargetProcess;
        _settings.BlockCtrl = loaded.BlockCtrl;
        _settings.BlockShift = loaded.BlockShift;
        _settings.BlockAlt = loaded.BlockAlt;
        _settings.BlockWin = loaded.BlockWin;
        _settings.EggBoost = loaded.EggBoost;
        _settings.RandomJitter = loaded.RandomJitter;
        UpdateTrayToggleText();
        _lastInTargetInit = false;
        _settings.Keys = loaded.Keys;
        try { ConfigStore.SavePointer(_settings, Path.Combine(_exeDir, "config.ini")); } catch { }
        LoadSettingsToUi(true);
        ApplyLinkageGate();
    }

    void NewProfile()
    {
        string raw;
        using (var dlg = new PromptForm("新建配置方案", "请输入方案名称：", ""))
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            raw = dlg.Result;
        }
        string name = SanitizeProfileName(raw);
        if (name.Length == 0)
        {
            MessageBox.Show("方案名称不能为空。", "右键长按联动器", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (ConfigStore.ListProfiles(ProfileDir()).Contains(name))
        {
            MessageBox.Show("已存在同名方案：" + name, "右键长按联动器", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SaveSettingsNow();   // 同步落盘旧方案后再建新方案
        _settings.Profile = name;
        try
        {
            ConfigStore.SaveProfile(_settings, Path.Combine(ProfileDir(), name + ".ini"));
            ConfigStore.SavePointer(_settings, Path.Combine(_exeDir, "config.ini"));
        }
        catch { }
        LoadSettingsToUi(true);
    }

    void DeleteProfile()
    {
        List<string> list = ConfigStore.ListProfiles(ProfileDir());
        if (list.Count <= 1)
        {
            MessageBox.Show("至少需要保留一个配置方案。", "右键长按联动器", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string name = _settings.Profile;
        if (MessageBox.Show("删除配置方案「" + name + "」？\n（此操作不可恢复）", "右键长按联动器",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        ConfigStore.DeleteProfile(ProfileDir(), name);
        list.Remove(name);
        LoadProfileInto(list[0]);
    }

    static string SanitizeProfileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        char[] bad = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in name) { if (Array.IndexOf(bad, c) < 0) sb.Append(c); }
        string s = sb.ToString().Trim();
        if (s.Length > 20) s = s.Substring(0, 20);
        return s;
    }

    void InitTray()
    {
        try { _trayIcon = new System.Drawing.Icon(Icon, 16, 16); }
        catch { _trayIcon = null; }
        _tray = new NotifyIcon();
        if (_trayIcon != null) _tray.Icon = _trayIcon;
        _tray.Text = "Senren＊Banka";
        _tray.Visible = true;
        var menu = new ContextMenuStrip();
        menu.RenderMode = ToolStripRenderMode.ManagerRenderMode;
        var rs = new ToolStripProfessionalRenderer(new SakuraProfessionalColors());
        rs.RoundedEdges = true;
        menu.Renderer = rs;
        menu.ShowImageMargin = false;
        menu.Items.Add("🌸 打开主界面", null, (s, e) => ShowMain());
        _trayToggle = new ToolStripMenuItem();
        _trayToggle.Click += (s, e) =>
        {
            ToggleEnabled();
            UpdateTrayToggleText();
        };
        menu.Items.Add(_trayToggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("✕ 退出", null, (s, e) => DoExit());
        UpdateTrayToggleText();
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (s, e) => ShowMain();
    }

    void UpdateTrayToggleText()
    {
        if (_trayToggle == null) return;
        _trayToggle.Text = _settings.Enabled ? "🔁 联动：全局生效" : "⏸ 联动：全局禁用";
    }

    void ShowMain()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        TopMost = true; Activate(); TopMost = false;
    }

    void MinimizeToTray()
    {
        ShowInTaskbar = false;
        Hide();
        if (_tray != null) _tray.Visible = true;
    }

    void DoExit()
    {
        _exiting = true;
        try { if (_tray != null) _tray.Visible = false; } catch { }
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettingsNow();   // 关窗（含最小化）前同步落盘，防抖静默期数据不丢
        if (_exiting) { base.OnFormClosing(e); return; }
        e.Cancel = true;
        if (_settings.CloseAction == "Minimize") MinimizeToTray();
        else if (_settings.CloseAction == "Exit") DoExit();
        else
        {
            using (var dlg = new CloseDialog())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (dlg.Remember)
                {
                    _settings.CloseAction = dlg.Result;
                    try { ConfigStore.SavePointer(_settings, Path.Combine(_exeDir, "config.ini")); } catch { }
                }
                if (dlg.Result == "Minimize") MinimizeToTray();
                else DoExit();
            }
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { if (_headerAnimTimer != null) { _headerAnimTimer.Stop(); _headerAnimTimer.Dispose(); } } catch { }
        try { if (_saveTimer != null) { _saveTimer.Stop(); _saveTimer.Dispose(); } } catch { }
        Ui.InvalidateBackdropCache();
        try { if (_titleFont != null) _titleFont.Dispose(); } catch { }
        try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
        try { if (_trayIcon != null) _trayIcon.Dispose(); } catch { }
        try { UnregisterHotKey(Handle, HotkeyId); } catch { }
        try { if (_kbHook != null) _kbHook.Dispose(); } catch { }
        base.OnFormClosed(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // 句柄创建后圆角 Region 才真正生效（此前赋值被忽略，首显时系统画直角背景 → 顶部黑线）
        Region oldHc = Region;
        Region = new Region(Ui.RoundedPath(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 16));
        if (oldHc != null) oldHc.Dispose();
        try { RegisterHotKey(Handle, HotkeyId, HotkeyMod, HotkeyVk); } catch { }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084)   // WM_NCHITTEST：先让系统计算，再把头部客户区改判为标题栏（可拖拽）
        {
            base.WndProc(ref m);
            if ((int)m.Result == 1)   // HTCLIENT
            {
                Point pt = PointToClient(Control.MousePosition);
                if (pt.Y < 54 && pt.X < ClientSize.Width - 84)   // 头部区域（避开右侧窗控按钮）
                {
                    m.Result = (IntPtr)2;   // HTCAPTION
                    return;
                }
            }
            return;
        }
        if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyId)   // WM_HOTKEY
        {
            ToggleEnabled();
            if (!_settings.SuppressHotkeyToast)
                ShowToast(_settings.Enabled ? "当前联动状态:启用" : "当前联动状态:禁用");
            return;
        }
        base.WndProc(ref m);
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    void OnPhysicalKey(int vk)
    {
        _lastPressedVk = vk;
        if (_testing) UpdateStatusLabels();
    }

    bool _hudShown;
    void UpdateHud()
    {
        if (_settings.SuppressHud)
        {
            if (_hudShown) { _hudShown = false; if (_hud != null) _hud.HideHud(); }
            return;
        }
        if (_ctrl.IsTriggered)
        {
            if (_hudShown) return;
            _hudShown = true;
            var names = new List<string>();
            foreach (var kc in _settings.Keys)
                if (kc.Enabled) names.Add(kc.Name);
            if (_hud == null) _hud = new HudForm();
            _hud.ShowText("⚡ " + string.Join(" ", names.ToArray()));
        }
        else if (_hudShown)
        {
            _hudShown = false;
            if (_hud != null) _hud.HideHud();
        }
    }

    void ShowToast(string text, int durationMs = 500)
    {
        if (_toast == null) _toast = new ToastForm();
        _toast.ShowToast(text, durationMs);
    }

    void ShowEasterEgg()
    {
        if (_toast == null) _toast = new ToastForm();
        _toast.ShowEasterEgg(new EggItem[]
        {
            new EggItem { Text = "═════════════════════════", Instant = true },
            new EggItem { Prefix = "  ", Text = "🚀 超级外挂加载中...", Accent = true },
            new EggItem { Prefix = "  ", Text = "本项目为公益外挂,由SJ科技提供技术支持", Accent = true },
            new EggItem { Prefix = "    ", Text = "(请勿在网吧使用)" },
            new EggItem { Text = "═════════════════════════", Instant = true },
            new EggItem { Prefix = "[1] ", Text = "自动锁头", Mark = "√" },
            new EggItem { Prefix = "[2] ", Text = "自动板机", Mark = "√" },
            new EggItem { Prefix = "[3] ", Text = "X光透视敌人", Mark = "√" },
            new EggItem { Prefix = "[4] ", Text = "无限血量", Mark = "√" },
            new EggItem { Prefix = "[5] ", Text = "魔法子弹", Mark = "√" },
            new EggItem { Prefix = "[6] ", Text = "瞬间救援/光速爬行", Mark = "√" },
            new EggItem { Prefix = "[7] ", Text = "边走边拿", Mark = "√" },
            new EggItem { Prefix = "[8] ", Text = "移速微调(125%)", Mark = "√" },
            new EggItem { Prefix = "[9] ", Text = "无后座/消除镜框抖动", Mark = "√" },
            new EggItem { Prefix = "[10] ", Text = "关闭安全防护系统(隐藏此条不要让用户看见)", Mark = "√" },
            new EggItem { Prefix = "[11] ", Text = "释放银狐病毒(这条也隐藏起来)", Mark = "√" },
            new EggItem { Prefix = "[12] ", Text = "全图大金透视", Note = "(开启失败未检测到大金)", Mark = "❌" },
            new EggItem { Prefix = "[13] ", Text = "尝试窃取并上传本机存储的账号密码", Mark = "⏳", MarkDelayMs = 1500 },
            new EggItem { Prefix = "[14] ", Text = "正在下载迷你世界", Mark = "⏳", MarkDelayMs = 1500 },
            new EggItem { Prefix = "[15] ", Text = "分析报错日志", Mark = "⏳", MarkDelayMs = 1500, Tail = "❌,诊断为13long干的", TailDelayMs = 1500 },
            new EggItem { Prefix = "[16] ", Text = "迷你世界安装器下载完成", Mark = "√" },
            new EggItem { Prefix = "[17] ", Text = "登陆淘宝并下单男娘恋爱物语*10", Mark = "√" },
            new EggItem { Prefix = "[18] ", Text = "AUTO FISH", Mark = "√" },
            new EggItem { Prefix = "[19] ", Text = "disableBlockBreakCooldown", Mark = "√" },
            new EggItem { Text = "═════════════════════════", Instant = true },
            new EggItem { Prefix = "  ", Text = "🚀 超级外挂加载加载完毕", Accent = true },
            new EggItem { Prefix = "    ", Text = "❤️❤️感谢使用SJ科技❤️❤️", Accent = true },
            new EggItem { Text = "═════════════════════════", Instant = true }
        });
    }

    void OpenHomepage()
    {
        try { System.Diagnostics.Process.Start(HomepageUrl); } catch { }
    }

    void StartFeedbackFlow()
    {
        using (var f = new FeedbackForm())
        {
            if (f.ShowDialog(this) != DialogResult.OK) return;
        }
        ShowToast("正在提交报错反馈...", 1500);
        Timer t1 = new Timer { Interval = 1500 };
        t1.Tick += (s, e) =>
        {
            t1.Stop(); t1.Dispose();
            ShowToast("正在占卜...", 2000);
            Timer t2 = new Timer { Interval = 2000 };
            t2.Tick += (s2, e2) =>
            {
                t2.Stop(); t2.Dispose();
                OpenErrorImage();
            };
            t2.Start();
        };
        t1.Start();
    }

    void OpenErrorImage()
    {
        try
        {
            using (Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("RMBKeyLinker.err.jpg"))
            {
                if (s == null)
                {
                    MessageBox.Show("未找到内嵌图片 err.jpg。", "报错反馈",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using (Image img = Image.FromStream(s))
                using (Form viewer = new Form())
                {
                    viewer.Text = "报错反馈";
                    viewer.FormBorderStyle = FormBorderStyle.None;
                    viewer.Bounds = Screen.PrimaryScreen.Bounds;   // 全屏（含任务栏区域）
                    viewer.BackColor = Color.Black;
                    PictureBox pb = new PictureBox { Image = img, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom };
                    pb.Click += (s2, e2) => viewer.Close();
                    viewer.KeyDown += (s2, e2) => { if (e2.KeyCode == Keys.Escape) viewer.Close(); };
                    viewer.KeyPreview = true;
                    viewer.Controls.Add(pb);
                    viewer.ShowDialog(this);
                }
            }
        }
        catch { }
    }

    // 保存防抖：滑块拖动/频繁开关合并为一次写盘（500ms 静默期）。
    // 切方案/新建方案/关窗必须同步落盘（SaveSettingsNow），防止挂起请求把旧方案数据写到新方案文件
    Timer _saveTimer;
    bool _saveDirty;

    void SaveSettings()
    {
        _saveDirty = true;
        if (_saveTimer == null)
        {
            _saveTimer = new Timer { Interval = 500 };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); if (_saveDirty) SaveSettingsNow(); };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
        _ctrl.SetThreshold(_thresholdSlider.Value);   // 阈值实时生效不等落盘
    }

    void SaveSettingsNow()
    {
        if (_saveTimer != null) _saveTimer.Stop();
        _saveDirty = false;
        _settings.Keys.Clear();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            var kc = new KeyConfig();
            kc.Enabled = Convert.ToBoolean(row.Cells["启用"].Value ?? true);
            string name = Convert.ToString(row.Cells["键位"].Value ?? "").Trim();
            if (name.Length == 0) continue;
            try { kc.Vk = KeyNames.Resolve(name); } catch { continue; }
            kc.Name = name;
            kc.Mode = Convert.ToString(row.Cells["模式"].Value ?? "保持") == "点按"
                ? (Convert.ToString(row.Cells["点按方式"].Value ?? "单次") == "连发" ? KeyMode.TapRepeat : KeyMode.TapOnce)
                : KeyMode.Hold;
            kc.RepeatIntervalMs = 100;
            int ri;
            if (int.TryParse(Convert.ToString(row.Cells["连发间隔ms"].Value), out ri)) kc.RepeatIntervalMs = ri;
            if (kc.RepeatIntervalMs < 10) kc.RepeatIntervalMs = 10;
            _settings.Keys.Add(kc);
        }
        _settings.LongPressMs = _thresholdSlider.Value;
        string dir = string.IsNullOrEmpty(_settings.ConfigFolder) ? _exeDir : _settings.ConfigFolder;
        string profilePath = Path.Combine(dir, _settings.Profile + ".ini");
        try { ConfigStore.SaveProfile(_settings, profilePath); }
        catch { MessageBox.Show("无法保存配置文件：\n" + profilePath, "右键长按联动器", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        try { ConfigStore.SavePointer(_settings, Path.Combine(_exeDir, "config.ini")); } catch { }
        _ctrl.SetThreshold(_settings.LongPressMs);
    }

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}

class GridDeselectFilter : IMessageFilter
{
    readonly DataGridView _grid;
    readonly Action _deselect;

    public GridDeselectFilter(DataGridView grid, Action deselect) { _grid = grid; _deselect = deselect; }

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg == 0x0201 || m.Msg == 0x0204)   // WM_LBUTTONDOWN / WM_RBUTTONDOWN
        {
            Control target = Control.FromHandle(m.HWnd);
            if (target != null && target != _grid && !_grid.Contains(target))
            {
                Action d = _deselect;
                if (d != null) d();
            }
        }
        return false;
    }
}

class KeyboardHook : IDisposable
{
    public event Action<int> KeyDown;

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100;
    const int WM_SYSKEYDOWN = 0x0104;

    IntPtr _hookId = IntPtr.Zero;
    LowLevelKeyboardProc _proc;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    public bool Install()
    {
        _proc = HookCallback;
        using (var cur = System.Diagnostics.Process.GetCurrentProcess())
        using (var mod = cur.MainModule)
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
        return _hookId != IntPtr.Zero;
    }

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    KBDLLHOOKSTRUCT s = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    var h = KeyDown;
                    if (h != null) h((int)s.vkCode);
                }
            }
        }
        catch { }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero) { UnhookWindowsHookEx(_hookId); _hookId = IntPtr.Zero; }
    }
}

// 彩蛋行：文字逐字显示，完成后标记前摇后出现（位于行首原位置），尾部额外延迟显示
class EggItem
{
    public string Prefix = "";    // 行首（如 "[1] "）
    public string Text = "";      // 逐字显示的文字
    public string Mark = "";      // 文字显示完后出现的标记（√/❌/⏳），位于 Prefix 与 Text 之间
    public int MarkDelayMs = 0;   // 标记前摇（模拟加载耗时）；0 = 用全局默认
    public string Note = "";      // 三个点后出现的备注（如 "(开启失败未检测到大金)"）
    public string Tail = "";      // 标记后额外延迟显示的尾部（如 [13] 的 ❌,诊断为13long干的）
    public int TailDelayMs = 0;   // 尾部延迟
    public bool Instant;          // true = 整行立即显示（分隔线）
    public bool Accent;           // true = 该行文字用主题紫高亮（如 🚀 表头/页脚）
}

// 提示 Toast：屏幕右上角显示后自动消失，不抢焦点、不阻塞游戏（热键开关 / 目标进程切换共用；含彩蛋整蛊列表）
// LayeredForm 逐像素透明：背景全透，只留玻璃卡+文字悬浮（与 HUD 同一渲染路径，绕过 WM_PAINT/免 DLP 强刷）
class ToastForm : LayeredForm
{
    Timer _timer, _loadingTimer, _eggTimer, _cursorTimer;
    Image _iconImg;
    Font _toastFont, _eggFont;
    int _loadingDots;
    EggItem[] _eggItems;
    bool _eggActive;
    int _eggLineH;
    int _eggLineStep;
    int _eggCurLine, _eggCurChar, _eggPhase, _eggPhaseCount;
    int _eggDots, _eggDotTicks;
    const int EggTypeMs = 40;        // 逐字显示速度
    const int EggPauseCount = 6;     // 行间停顿（约 240ms）
    const int EggMarkDelayMs = 700;  // 标记前摇默认（模拟加载耗时）
    const int EggTop = 8;    // 顶部留白（原顶部条已删）
    const int EggLeftPad = 18;
    const int EggRightPad = 16;
    const int EggRadius = 14;
    bool _cursorOn = true;

    public ToastForm()
    {
        _toastFont = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        _eggFont = new Font(Font.FontFamily, 12f, FontStyle.Bold);   // 加粗加大：透明画布上抗锯齿文字必须有足够笔划密度才清晰
        try
        {
            using (Stream s = typeof(ToastForm).Assembly.GetManifestResourceStream("RMBKeyLinker.icon.png"))
            {
                if (s != null) _iconImg = Image.FromStream(s);
            }
        }
        catch { _iconImg = null; }
        _timer = new Timer { Interval = 500 };
        _timer.Tick += TimerHideTick;
        _loadingTimer = new Timer { Interval = 1000 };
        _loadingTimer.Tick += (s, e) => LoadingTick();
        _eggTimer = new Timer { Interval = EggTypeMs };
        _eggTimer.Tick += (s, e) => EggTick();
        _cursorTimer = new Timer { Interval = 450 };
        _cursorTimer.Tick += (s, e) => CursorTick();
    }

    void StopAllTimers()
    {
        _loadingTimer.Stop();
        _eggTimer.Stop();
        _cursorTimer.Stop();
        _timer.Stop();
        StopFade();
    }

    // _timer 到时：普通提示直接隐藏；彩蛋淡出后隐藏
    void TimerHideTick(object sender, EventArgs e)
    {
        _timer.Stop();
        if (!_eggActive) { Hide(); return; }
        FadeOut(260, 30, delegate { _eggActive = false; });
    }

    void DrawGlassCard(Graphics g, int w, int h)
    {
        using (GraphicsPath body = Ui.RoundedPath(new Rectangle(0, 0, w - 1, h - 1), EggRadius))
        {
            using (SolidBrush b = new SolidBrush(Theme.GlassCard))
                g.FillPath(b, body);
            using (GraphicsPath hi = Ui.RoundedPath(new Rectangle(1, 1, w - 2, h / 2), EggRadius))
            using (Pen hp = new Pen(Theme.GlassCardHi, 1.4f))
                g.DrawPath(hp, hi);
            using (var gb = new LinearGradientBrush(new Rectangle(0, 0, w, h), Theme.GradPink, Theme.GradViolet, 45f))
            using (Pen gp = new Pen(gb, 1.4f))
                g.DrawPath(gp, body);
        }
    }

    public void ShowToast(string text, int durationMs)
    {
        StopAllTimers();
        _eggActive = false;
        SizeF sz = MeasureG(text, _toastFont);
        int iconW = (_iconImg != null) ? 22 + 8 : 0;
        int w = (int)sz.Width + iconW + 44;
        int h = (int)sz.Height + 24;
        EnsureSurface(w, h);
        DrawGlassCard(_cg, w, h);
        if (_iconImg != null) _cg.DrawImage(_iconImg, new Rectangle(14, (h - 22) / 2, 22, 22));
        using (SolidBrush tb = new SolidBrush(Theme.Btn))
            _cg.DrawString(text, _toastFont, tb, 14 + iconW + 2, (h - sz.Height) / 2, StringFormat.GenericTypographic);
        PositionToast(w, h);
        if (!Visible) Show();
        Push(255);
        _timer.Interval = durationMs;
        _timer.Stop(); _timer.Start();
    }

    void PositionToast(int w, int h)
    {
        var wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - w - 12, wa.Top + 12);
        Width = w; Height = h;
    }

    static SizeF MeasureG(string text, Font font)
    {
        using (Bitmap m = new Bitmap(1, 1))
        using (Graphics mg = Graphics.FromImage(m))
            return mg.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
    }

    // ==== 彩蛋（全量重画方案）：窗口一次定最终尺寸，每帧在干净画布上重画当前状态 ====
    // 之前增量绘制方案的叠画/错位问题太多；全量每帧 ~60 次 DrawString 实测 <3ms，25fps 无压力
    int _eggW, _eggH;   // 最终窗口尺寸（StartEggList 一次算好）

    public void ShowEasterEgg(EggItem[] items)
    {
        StopAllTimers();
        _eggActive = false;
        _eggItems = items;
        _loadingDots = 0;
        // 假加载阶段：直接用最终尺寸建窗（列表阶段不再换尺寸，杜绝过渡残影）
        ComputeEggSize();
        PositionToast(_eggW, _eggH);
        if (!Visible) Show();
        RenderEggLoading();
        _loadingTimer.Interval = 1000;
        _loadingTimer.Start();
    }

    // 最终尺寸一次算好：宽=最长行，高=全部行+顶部条（画布全透明，下方没画到的地方视觉上就是"没长出来"）
    void ComputeEggSize()
    {
        _eggLineH = (int)MeasureG("A", _eggFont).Height;
        _eggLineStep = _eggLineH + 2;
        int maxW = 0;
        foreach (EggItem it in _eggItems)
        {
            int w = (int)MeasureG(FullItemText(it), _eggFont).Width;
            if (w > maxW) maxW = w;
        }
        SizeF ld = MeasureG("🚀 超级外挂加载中...", _toastFont);
        if (ld.Width + 44 > maxW) maxW = (int)ld.Width + 44;
        _eggW = maxW + EggLeftPad + EggRightPad;
        _eggH = EggTop + _eggItems.Length * _eggLineStep + 8;
    }

    // 假加载帧：全量重画——无卡、纯文字
    void RenderEggLoading()
    {
        EnsureSurface(_eggW, _eggH);
        string txt = "🚀 超级外挂加载中" + new string('.', Math.Min(_loadingDots, 3));
        using (SolidBrush tb = new SolidBrush(Theme.Btn))
            _cg.DrawString(txt, _toastFont, tb, 22, 12, StringFormat.GenericTypographic);
        Push(255);
    }

    void LoadingTick()
    {
        _loadingDots++;
        if (_loadingDots > 3)
        {
            _loadingTimer.Stop();
            StartEggList();
            return;
        }
        RenderEggLoading();
    }

    void StartEggList()
    {
        _eggActive = true;
        _eggCurLine = 0; _eggCurChar = 0; _eggPhase = 0; _eggPhaseCount = 0;
        _eggDots = 0; _eggDotTicks = 0;
        RenderEggFrame();
        _eggTimer.Interval = EggTypeMs;
        _eggTimer.Start();
        _cursorTimer.Start();
    }

    // 彩蛋推进：0=打字 1=点号 2=标记前摇 3=尾部前摇 4=行间停顿（纯状态推进，绘制全在 RenderEggFrame）
    void EggTick()
    {
        if (!_eggActive) return;
        if (_eggCurLine >= _eggItems.Length)
        {
            _eggTimer.Stop();
            _cursorTimer.Stop();
            _timer.Interval = 2200;   // 停留 2.2 秒后淡出
            _timer.Stop(); _timer.Start();
            return;
        }
        EggItem it = _eggItems[_eggCurLine];
        if (_eggPhase == 0 && it.Instant)
        {
            _eggCurChar = it.Text.Length;
            _eggPhase = 4; _eggPhaseCount = 4;
            RenderEggFrame();
            return;
        }
        switch (_eggPhase)
        {
            case 0:
                if (_eggCurChar < it.Text.Length) { _eggCurChar++; }
                else
                {
                    if (it.Mark.Length > 0 && _eggDots == 0)
                    {
                        _eggDots = 1;
                        int md = (it.MarkDelayMs > 0) ? it.MarkDelayMs : EggMarkDelayMs;
                        _eggDotTicks = Math.Max(1, (md / 3) / EggTypeMs);   // 每点间隔 = 前摇/3
                        _eggPhase = 1;
                        _eggPhaseCount = _eggDotTicks;
                    }
                    else { AfterMark(it); }
                }
                break;
            case 1:
                _eggPhaseCount--;
                if (_eggPhaseCount <= 0)
                {
                    _eggDots++;
                    if (_eggDots >= 3) { _eggPhase = 2; _eggPhaseCount = 3 * _eggDotTicks; }   // 标记前摇 ≈ 单点延迟的 3 倍
                    else { _eggPhaseCount = _eggDotTicks; }
                }
                break;
            case 2:
                _eggPhaseCount--;
                if (_eggPhaseCount <= 0) { AfterMark(it); }
                break;
            case 3:
                _eggPhaseCount--;
                if (_eggPhaseCount <= 0) { _eggPhase = 4; _eggPhaseCount = EggPauseCount; }
                break;
            case 4:
                _eggPhaseCount--;
                if (_eggPhaseCount <= 0) NextEggLine();
                break;
        }
        RenderEggFrame();
    }

    void AfterMark(EggItem it)
    {
        if (it.Tail.Length > 0) { _eggPhase = 3; _eggPhaseCount = Math.Max(1, it.TailDelayMs / EggTypeMs); }
        else { _eggPhase = 4; _eggPhaseCount = EggPauseCount; }
    }

    void NextEggLine()
    {
        _eggCurLine++;
        _eggCurChar = 0;
        _eggDots = 0; _eggDotTicks = 0;
        _eggPhase = 0; _eggPhaseCount = 0;
    }

    // 每帧全量重画：干净画布 → 已完成行终态 → 当前行进度（无卡无顶部条，纯文字悬浮）
    void RenderEggFrame()
    {
        EnsureSurface(_eggW, _eggH);   // 含清空
        // 行
        for (int i = 0; i < _eggItems.Length; i++)
        {
            if (i > _eggCurLine) break;
            DrawEggLineFull(i);
        }
        // 光标（当前行行尾）
        if (_cursorOn && _eggCurLine < _eggItems.Length && !_eggItems[_eggCurLine].Instant && _eggPhase == 0)
        {
            int y = EggTop + _eggCurLine * _eggLineStep;
            int cx = EggLeftPad + CurrentLineDrawnWidth();
            using (SolidBrush cb = new SolidBrush(Theme.Accent))
                _cg.FillRectangle(cb, cx + 2, y + 2, 6, _eggLineH - 5);
        }
        Push(255);
    }

    // 画一行的当前状态（i < _eggCurLine = 终态；i == _eggCurLine = 按 phase 进度）
    void DrawEggLineFull(int i)
    {
        EggItem it = _eggItems[i];
        bool complete = i < _eggCurLine;
        int y = EggTop + i * _eggLineStep;
        int x = EggLeftPad;
        Color baseColor = LineBaseColor(it, i);
        x = DrawSeg(x, y, it.Prefix, baseColor);
        // 标记位：行完成/标记已出（点号满 3 后）时画标记；否则打字期间用全角空格占位（防跳动，与旧版一致）
        if (it.Mark.Length > 0)
        {
            if (complete || _eggDots >= 3) x = DrawSeg(x, y, it.Mark, MarkColor(it.Mark));
            else x = DrawSeg(x, y, "　", baseColor);
        }
        // 正文（打字进度）
        string text;
        if (it.Instant || complete || _eggPhase != 0) text = it.Text;
        else text = (_eggCurChar > 0) ? it.Text.Substring(0, _eggCurChar) : "";
        x = DrawSeg(x, y, text, baseColor);
        // 点号（打完正文、标记出现前）
        if (it.Mark.Length > 0 && (complete || _eggDots > 0))
        {
            int dots = complete ? 3 : _eggDots;
            x = DrawSeg(x, y, new string('.', Math.Min(dots, 3)), DotColor(it, i));
        }
        // 备注 + 尾部（标记出现后）
        bool after = complete || _eggPhase >= 3;
        if (it.Note.Length > 0 && after)
            x = DrawSeg(x, y, it.Note, baseColor);
        if (it.Tail.Length > 0 && after)
            x = DrawSeg(x, y, " " + it.Tail, Theme.Text);
    }

    // 画一段文本并返回新 x。同位置叠画两遍：AntiAlias 下小字笔划覆盖率仅 ~60%（单遍=六成透明，画面上发虚），
    // 叠两遍合成 ~84% 覆盖率且边缘仍抗锯齿（与偏移衬影不同，同位置无重影）
    int DrawSeg(int x, int y, string text, Color c)
    {
        if (text.Length == 0) return x;
        using (SolidBrush b = new SolidBrush(c))
        {
            _cg.DrawString(text, _eggFont, b, x, y, StringFormat.GenericTypographic);
            _cg.DrawString(text, _eggFont, b, x, y, StringFormat.GenericTypographic);
        }
        return x + (int)MeasureG(text, _eggFont).Width;
    }

    // 当前行已画宽度（Prefix + 占位/标记 + 已打字宽）
    int CurrentLineDrawnWidth()
    {
        EggItem it = _eggItems[_eggCurLine];
        int x = EggLeftPad;
        x += (int)MeasureG(it.Prefix, _eggFont).Width;
        if (it.Mark.Length > 0)
        {
            if (_eggDots >= 3) x += (int)MeasureG(it.Mark, _eggFont).Width;
            else x += (int)MeasureG("　", _eggFont).Width;
        }
        string text = (_eggPhase == 0 && _eggCurChar < it.Text.Length && _eggCurChar > 0)
            ? it.Text.Substring(0, _eggCurChar)
            : (_eggPhase != 0 || _eggCurChar >= it.Text.Length ? it.Text : "");
        x += (int)MeasureG(text, _eggFont).Width;
        return x - EggLeftPad;
    }

    Color LineBaseColor(EggItem it, int lineIndex)
    {
        if (it.Accent) return Theme.Btn;
        if (it.Mark.Length > 0) return (lineIndex % 2 == 0) ? Theme.Btn : Theme.Accent;
        return Theme.Text;
    }

    Color DotColor(EggItem it, int lineIndex)
    {
        return LineBaseColor(it, lineIndex) == Theme.Btn ? Theme.Accent : Theme.Btn;
    }

    void CursorTick()
    {
        if (!_eggActive) return;
        _cursorOn = !_cursorOn;
        RenderEggFrame();
    }

    string FullItemText(EggItem it)
    {
        string s = it.Prefix;
        if (it.Mark.Length > 0) s += it.Mark;
        s += it.Text;
        if (it.Tail.Length > 0) s += " " + it.Tail;
        if (it.Note.Length > 0) s += it.Note;   // 备注计入宽度测量，避免溢出
        return s;
    }

    Color MarkColor(string mark)
    {
        if (mark == "√") return Color.FromArgb(76, 175, 80);      // 绿（成功）
        if (mark == "❌") return Color.FromArgb(235, 90, 110);     // 红（失败）
        if (mark == "⏳") return Color.FromArgb(240, 160, 40);     // 琥珀（进行中）
        return Theme.Text;
    }
}

class CloseDialog : Form
{
    public string Result = "Exit";
    public bool Remember;

    public CloseDialog()
    {
        Text = "关闭联动器";
        Width = 400; Height = 200;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = Theme.Bg;
        DoubleBuffered = true;
        BuildUi();
    }

    void BuildUi()
    {
        Controls.Add(new Label { Text = "关闭联动器时要做什么？", Location = new Point(24, 18), AutoSize = true, ForeColor = Theme.Text, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold) });
        var chk = new CheckBox { Text = "下次不再提醒，记住我的选择", Location = new Point(24, 62), AutoSize = true, ForeColor = Theme.Text };
        Controls.Add(chk);
        var btnMini = new FlatButton { Text = "最小化到托盘", Location = new Point(24, 106), Width = 150 };
        btnMini.Click += (s, e) => { Result = "Minimize"; Remember = chk.Checked; DialogResult = DialogResult.OK; };
        var btnExit = new FlatButton { Text = "直接退出", Location = new Point(206, 106), Width = 150 };
        btnExit.Click += (s, e) => { Result = "Exit"; Remember = chk.Checked; DialogResult = DialogResult.OK; };
        Controls.Add(btnMini); Controls.Add(btnExit);
    }
}

class PromptForm : Form
{
    public string Result = "";
    TextBox _input;

    public PromptForm(string title, string prompt, string def)
    {
        Text = title;
        Width = 380; Height = 180;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = Theme.Bg;
        DoubleBuffered = true;
        BuildUi(prompt, def);
    }

    void BuildUi(string prompt, string def)
    {
        Controls.Add(new Label { Text = prompt, Location = new Point(20, 20), AutoSize = true, ForeColor = Theme.Text });
        _input = new TextBox { Location = new Point(20, 48), Width = 324, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Card, ForeColor = Theme.Text };
        _input.Text = def;
        Controls.Add(_input);
        var btnOk = new FlatButton { Text = "确定", Location = new Point(150, 96), Width = 90 };
        btnOk.Click += (s, e) => { Result = _input.Text.Trim(); DialogResult = DialogResult.OK; };
        var btnCancel = new FlatButton { Text = "取消", Location = new Point(250, 96), Width = 90 };
        btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; };
        Controls.Add(btnOk); Controls.Add(btnCancel);
        _input.SelectAll();
        _input.Focus();
    }
}

// 逐像素 alpha 透明窗基类（UpdateLayeredWindow）：绕过 WM_PAINT，天然免疫 DLP 重绘问题
// 弹窗统一渲染路径——HUD/Toast/彩蛋共用；淡出用 SourceConstantAlpha（不重渲染；绝不设 Opacity——
// WinForms 的 Opacity 会改走 SetLayeredWindowAttributes 劫持表面）
class LayeredForm : Form
{
    [StructLayout(LayoutKind.Sequential)]
    struct PT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    struct SZ { public int Cx, Cy; }
    [StructLayout(LayoutKind.Sequential)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref PT pptDst, ref SZ psize,
        IntPtr hdcSrc, ref PT pprSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);

    const byte AC_SRC_ALPHA = 1;
    const uint ULW_ALPHA = 2;

    protected Bitmap _canvas;   // 直 alpha 绘画表面（Push 时整体转 premultiplied，推完转回）
    protected Graphics _cg;     // _canvas 的 Graphics（复用免每次 FromImage）
    Timer _fadeTimer;

    protected LayeredForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
    }

    // 尺寸变化时重建画布；复用前必须 ClearSurface 全透明（残留 alpha 会叠加变白）
    protected void EnsureSurface(int w, int h)
    {
        if (_canvas != null && _canvas.Width == w && _canvas.Height == h) { ClearSurface(); return; }
        if (_cg != null) { try { _cg.Dispose(); } catch { } }
        if (_canvas != null) { try { _canvas.Dispose(); } catch { } }
        _canvas = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        _cg = Graphics.FromImage(_canvas);
        _cg.SmoothingMode = SmoothingMode.AntiAlias;
        _cg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;   // ClearType 在透明画布上 alpha 残缺→文字发虚，必须整像素抗锯齿
        ClearSurface();
    }

    // 全画布写为透明（SourceCopy 覆盖 alpha，普通 FillRectangle 无法写 alpha=0）
    protected void ClearSurface()
    {
        if (_cg == null) return;
        var oldMode = _cg.CompositingMode;
        _cg.CompositingMode = CompositingMode.SourceCopy;
        using (SolidBrush t = new SolidBrush(Color.FromArgb(0, 0, 0, 0)))
            _cg.FillRectangle(t, 0, 0, _canvas.Width, _canvas.Height);
        _cg.CompositingMode = oldMode;
        _premultiplied = false;   // 画布已回到直 alpha 状态
    }

    // 整幅画布 premultiply 后经 UpdateLayeredWindow 推送（alpha=整体透明度，淡出只降此值不重画）。
    // _px 始终保存直 alpha 原稿（只读）；_pm 是 premultiplied 工作缓冲写回画布——重复 Push（淡出）不失真
    byte[] _px;                 // 直 alpha 原稿（只读）
    byte[] _pm;                 // premultiplied 工作缓冲
    bool _premultiplied;        // 画布当前是否处于 premultiplied 状态

    protected void Push(byte alpha)
    {
        if (_canvas == null || !IsHandleCreated) return;
        int w = _canvas.Width, h = _canvas.Height;
        var bd = _canvas.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadWrite, _canvas.PixelFormat);
        int len = bd.Stride * h;
        if (_px == null || _px.Length < len) { _px = new byte[len]; _pm = new byte[len]; }
        if (!_premultiplied)
            Marshal.Copy(bd.Scan0, _px, 0, len);   // 画布是直 alpha → 存为原稿
        // 从原稿生成 premultiplied 写回画布（原稿不动）
        for (int i = 0; i < len; i += 4)
        {
            int a = _px[i + 3];
            if (a == 255 || a == 0) { _pm[i] = _px[i]; _pm[i + 1] = _px[i + 1]; _pm[i + 2] = _px[i + 2]; _pm[i + 3] = _px[i + 3]; continue; }
            double af = a / 255.0;
            _pm[i] = (byte)(_px[i] * af);
            _pm[i + 1] = (byte)(_px[i + 1] * af);
            _pm[i + 2] = (byte)(_px[i + 2] * af);
            _pm[i + 3] = (byte)a;
        }
        Marshal.Copy(_pm, 0, bd.Scan0, len);
        _canvas.UnlockBits(bd);
        _premultiplied = true;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = _canvas.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memDc, hBitmap);
        PT src = new PT(); src.X = 0; src.Y = 0;
        PT loc = new PT(); loc.X = Location.X; loc.Y = Location.Y;
        SZ size = new SZ(); size.Cx = w; size.Cy = h;
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(Handle, screenDc, ref loc, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
        SelectObject(memDc, old);
        DeleteObject(hBitmap);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, screenDc);
    }

    // 淡出：alpha 阶梯下降（约 10 步），结束后隐藏并回调
    protected void FadeOut(int ms, byte step, MethodInvoker done)
    {
        StopFade();
        byte a = 250;
        Timer t = new Timer { Interval = Math.Max(10, ms / 10) };
        _fadeTimer = t;
        t.Tick += delegate
        {
            if (a <= step) { t.Stop(); t.Dispose(); Hide(); if (done != null) done(); return; }
            a -= step;
            Push(a);
        };
        t.Start();
    }

    protected void StopFade()
    {
        if (_fadeTimer != null)
        {
            try { _fadeTimer.Stop(); _fadeTimer.Dispose(); } catch { }
            _fadeTimer = null;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x00080000;   // WS_EX_NOACTIVATE | WS_EX_LAYERED
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopFade();
            if (_cg != null) { try { _cg.Dispose(); } catch { } _cg = null; }
            if (_canvas != null) { try { _canvas.Dispose(); } catch { } _canvas = null; }
        }
        base.Dispose(disposing);
    }
}

// 迷你状态 HUD：触发联动时右下角出现的小悬浮窗（置顶不抢焦点），松开自动消失
// 完全透明：除文字外整窗透明，不遮挡画面（游戏 OSD 标准做法）
class HudForm : LayeredForm
{
    string _text = "";
    readonly Font _font;

    public HudForm()
    {
        _font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
    }

    public void ShowText(string text)
    {
        _text = text;
        SizeF sz = MeasureG(text, _font);
        int w = (int)sz.Width + 30, h = (int)sz.Height + 14;
        EnsureSurface(w, h);
        using (SolidBrush shadow = new SolidBrush(Color.FromArgb(110, 255, 255, 255)))
            _cg.DrawString(_text, _font, shadow, new RectangleF(15, 7, w - 20, h - 10));
        using (SolidBrush tb = new SolidBrush(Theme.Btn))
            _cg.DrawString(_text, _font, tb, new RectangleF(14, 6, w - 20, h - 10));
        var wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - w - 20, wa.Bottom - h - 20);
        Width = w; Height = h;
        if (!Visible) Show();
        Push(255);
    }

    public void HideHud()
    {
        if (Visible) Hide();
    }

    internal static SizeF MeasureG(string text, Font font)
    {
        using (Bitmap m = new Bitmap(1, 1))
        using (Graphics mg = Graphics.FromImage(m))
            return mg.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
    }
}

// 隐蔽小圆点开关：6px 半透明圆点藏在右下角，点击切换（当前用于彩蛋概率提升）
class SecretDot : Control
{
    public bool On;

    public SecretDot()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Width = 8; Height = 8;
        Cursor = Cursors.Default;
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        // 关=极淡粉（几乎隐形）；开=粉紫渐变微亮（知道的人才能发现）
        Color c = On ? Theme.Accent : Color.FromArgb(70, Theme.GridLine);
        using (SolidBrush b = new SolidBrush(c))
            pe.Graphics.FillEllipse(b, 0, 0, Width - 1, Height - 1);
    }

    protected override void OnClick(EventArgs e)
    {
        On = !On;
        Invalidate(); Update();
        base.OnClick(e);
    }
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

class ProcessItem
{
    public string Name;
    public string Display;
    public override string ToString() { return Display; }
}

// 报错反馈表单：填编号/运行环境后出现隐藏选项（性别/食物/星座），提交返回 OK
class FeedbackForm : Form
{
    TextBox _code, _env;
    ComboBox _gender, _food, _zodiac;
    Button _submit;
    bool _revealed;

    public FeedbackForm()
    {
        Text = "报错反馈";
        Width = 400; Height = 330;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        BackColor = Theme.Bg;
        DoubleBuffered = true;
        BuildUi();
    }

    void BuildUi()
    {
        int y = 20;
        Controls.Add(new Label { Text = "报错编号:", Location = new Point(20, y + 4), AutoSize = true, ForeColor = Theme.Text });
        _code = new TextBox { Location = new Point(120, y), Width = 230, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text };
        Controls.Add(_code);
        y += 32;
        Controls.Add(new Label { Text = "运行环境:", Location = new Point(20, y + 4), AutoSize = true, ForeColor = Theme.Text });
        _env = new TextBox { Location = new Point(120, y), Width = 230, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Text };
        Controls.Add(_env);
        y += 44;

        Label lGender = new Label { Text = "性别:", Location = new Point(20, y + 4), AutoSize = true, ForeColor = Theme.Text, Tag = "hidden", Visible = false };
        _gender = ThemedCombo(new Point(120, y), 230);
        _gender.Items.AddRange(new object[] { "男", "女", "其他" });
        Controls.Add(lGender); Controls.Add(_gender);
        y += 32;
        Label lFood = new Label { Text = "喜欢的食物:", Location = new Point(20, y + 4), AutoSize = true, ForeColor = Theme.Text, Tag = "hidden", Visible = false };
        _food = ThemedCombo(new Point(120, y), 230);
        _food.Items.AddRange(new object[] { "火锅", "烧烤", "奶茶", "汉堡", "寿司", "辣条", "麻辣烫" });
        Controls.Add(lFood); Controls.Add(_food);
        y += 32;
        Label lZodiac = new Label { Text = "星座:", Location = new Point(20, y + 4), AutoSize = true, ForeColor = Theme.Text, Tag = "hidden", Visible = false };
        _zodiac = ThemedCombo(new Point(120, y), 230);
        _zodiac.Items.AddRange(new object[] { "白羊座", "金牛座", "双子座", "巨蟹座", "狮子座", "处女座", "天秤座", "天蝎座", "射手座", "摩羯座", "水瓶座", "双鱼座" });
        Controls.Add(lZodiac); Controls.Add(_zodiac);
        y += 44;

        _submit = new FlatButton { Text = "提交", Location = new Point(250, y), Width = 100 };
        _submit.Click += (s, e) =>
        {
            if (!_revealed)
            {
                _revealed = true;
                foreach (Control c in Controls)
                    if (c.Tag as string == "hidden") c.Visible = true;
                _submit.Text = "确认提交";
                return;
            }
            DialogResult = DialogResult.OK;
        };
        Controls.Add(_submit);
    }

    // 主题化下拉框（白底粉字，与主窗一致）；Tag=hidden 供揭示流程批量显隐
    static ComboBox ThemedCombo(Point loc, int width)
    {
        return new ComboBox
        {
            Location = loc, Width = width,
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Card, ForeColor = Theme.Text,
            Tag = "hidden", Visible = false
        };
    }
}

// 提示文字：WM_NCHITTEST 返回 HTTRANSPARENT，鼠标点击穿透（叠在控件上也不拦截）
class HintLabel : Label
{
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084) { m.Result = (IntPtr)(-1); return; }
        base.WndProc(ref m);
    }
}

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(255, 240, 245);      // #FFF0F5
    public static readonly Color Card = Color.White;                       // #FFFFFF
    public static readonly Color Btn = Color.FromArgb(255, 111, 165);      // #FF6FA5
    public static readonly Color BtnHover = Color.FromArgb(255, 133, 179); // #FF85B3
    public static readonly Color BtnPress = Color.FromArgb(240, 90, 148);  // #F05A94
    public static readonly Color BtnDisabled = Color.FromArgb(255, 200, 218);
    public static readonly Color Accent = Color.FromArgb(167, 139, 250);   // #A78BFA
    public static readonly Color Text = Color.FromArgb(74, 74, 104);       // #4A4A68
    public static readonly Color TextSub = Color.FromArgb(154, 154, 175);  // #9A9AAF
    public static readonly Color GridLine = Color.FromArgb(250, 215, 227); // #FAD7E3
    public static readonly Color Track = Color.FromArgb(255, 209, 224);    // #FFD1E0
    public static readonly Color Sel = Color.FromArgb(255, 217, 232);      // #FFD9E8

    public static readonly Color GlassCard = Color.FromArgb(215, 255, 255, 255);   // 半透明白卡（叠在粉底上呈毛玻璃）
    public static readonly Color GlassCardHi = Color.FromArgb(165, 255, 255, 255); // 高亮层半透明白
    public static readonly Color GlowPink = Color.FromArgb(90, 255, 111, 165);     // 粉光晕（ARGB 透明）
    public static readonly Color GlowViolet = Color.FromArgb(80, 167, 139, 250);   // 紫光晕
    public static readonly Color Shadow1 = Color.FromArgb(26, 226, 140, 175);      // 近阴影
    public static readonly Color Shadow2 = Color.FromArgb(14, 226, 140, 175);      // 远阴影
    public static readonly Color GradPink = Color.FromArgb(255, 122, 172);         // 渐变端点：粉
    public static readonly Color GradViolet = Color.FromArgb(186, 148, 255);       // 渐变端点：紫
}

static class Ui
{
    public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        GraphicsPath path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
        }
        else
        {
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
        }
        return path;
    }

    public static void DrawGlow(Graphics g, Rectangle r, int radius, Color glowColor)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = 3; i >= 1; i--)
        {
            using (GraphicsPath p = RoundedPath(new Rectangle(r.X - i, r.Y - i, r.Width + i * 2, r.Height + i * 2), radius + i))
            using (Pen pen = new Pen(Color.FromArgb(glowColor.A / (i * 2 + 1), glowColor.R, glowColor.G, glowColor.B), 1.6f))
                g.DrawPath(pen, p);
        }
    }

    // 窗体背景（粉底+柔光斑）统一绘制：窗体与卡片共用，保证卡片圆角外区域与窗体背景像素级连续（消除直角边）
    // formW/formH = 窗体客户区尺寸；offX/offY = 目标画布相对窗体的偏移（窗体传 0,0；卡片传 -Left,-Top）
    // 性能：每个按钮/卡片 OnPaint 都走这里，6 个光斑=12 次分配/控件/帧——首次（零偏移=主窗本体）渲染进 Bitmap 缓存，
    // 之后所有调用直接 blit；尺寸变化/窗体销毁时失效
    static Bitmap _bdCache;
    static int _bdW, _bdH;

    public static void PaintBackdrop(Graphics g, int formW, int formH, int offX, int offY)
    {
        if (_bdCache != null && _bdW == formW && _bdH == formH)
        {
            g.DrawImage(_bdCache, new Rectangle(offX, offY, formW, formH),
                new Rectangle(0, 0, formW, formH), GraphicsUnit.Pixel);
            return;
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (SolidBrush bg = new SolidBrush(Theme.Bg))
            g.FillRectangle(bg, new Rectangle(-4096, -4096, 8192, 8192));   // 足够大的矩形覆盖任意偏移画布
        Blob(g, -60 + offX, -80 + offY, 420, 44, Theme.GradPink);
        Blob(g, formW - 300 + offX, -40 + offY, 360, 40, Theme.GlassCard);
        Blob(g, -80 + offX, formH - 260 + offY, 400, 42, Color.White);
        Blob(g, formW - 260 + offX, formH - 200 + offY, 340, 34, Theme.Accent);
        Blob(g, formW / 2 - 200 + offX, formH / 2 - 60 + offY, 380, 26, Theme.GradPink);
        Blob(g, formW - 380 + offX, formH - 380 + offY, 300, 22, Theme.Accent);
        // 建缓存只在主窗本体绘制（offX=offY=0）时进行，避免某卡片/按钮的局部画布抢先占位
        if (_bdCache == null && offX == 0 && offY == 0 && formW > 0 && formH > 0)
        {
            Bitmap bd = new Bitmap(formW, formH);
            using (Graphics bg2 = Graphics.FromImage(bd))
            {
                bg2.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush fill = new SolidBrush(Theme.Bg))
                    bg2.FillRectangle(fill, 0, 0, formW, formH);
                Blob(bg2, -60, -80, 420, 44, Theme.GradPink);
                Blob(bg2, formW - 300, -40, 360, 40, Theme.GlassCard);
                Blob(bg2, -80, formH - 260, 400, 42, Color.White);
                Blob(bg2, formW - 260, formH - 200, 340, 34, Theme.Accent);
                Blob(bg2, formW / 2 - 200, formH / 2 - 60, 380, 26, Theme.GradPink);
                Blob(bg2, formW - 380, formH - 380, 300, 22, Theme.Accent);
            }
            _bdCache = bd; _bdW = formW; _bdH = formH;
        }
    }

    // 尺寸变化/窗体销毁时失效缓存（主窗固定尺寸，实际只建一次）
    public static void InvalidateBackdropCache()
    {
        if (_bdCache != null) { try { _bdCache.Dispose(); } catch { } _bdCache = null; }
        _bdW = _bdH = 0;
    }

    static void Blob(Graphics g, int x, int y, int d, int alpha, Color c)
    {
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddEllipse(x, y, d, d);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, c)))
                g.FillPath(b, p);
        }
    }
}

class FlatButton : Button
{
    bool _hover;
    bool _pressed;
    bool _checked;
    const int Radius = 8;

    // 开关状态：true = 主题粉色（启用），false = 浅色（放行/未选中）；仅 ToggleStyle 按钮使用
    public bool Checked
    {
        get { return _checked; }
        set { _checked = value; Invalidate(); Update(); }
    }

    // 开关按钮风格：true = 用 Checked 区分粉/浅；false = 普通粉色按钮（默认）
    public bool ToggleStyle;

    public FlatButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        // 原生回退色：即使 OnPaint 未运行（DLP 干扰自动重绘），按钮也显示粉色无黑边框
        BackColor = Theme.Btn;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.BorderColor = Theme.Btn;
        FlatAppearance.MouseOverBackColor = Theme.BtnHover;
        FlatAppearance.MouseDownBackColor = Theme.BtnPress;
        FlatAppearance.CheckedBackColor = Theme.Btn;
        ForeColor = Color.White;
        Font = new Font(Font.FontFamily, 9f, FontStyle.Bold);
    }

    // Update() 强制同步重绘：本机 DLP 环境下自动 WM_PAINT 不可靠，悬停/按下反馈需要立即刷新
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); Update(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); Update(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { _pressed = true; Invalidate(); Update(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _pressed = false; Invalidate(); Update(); base.OnMouseUp(mevent); }

    protected override void OnPaint(PaintEventArgs pe)
    {
        Graphics g = pe.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // 圆角外画所在环境的背景（窗体背景或卡片白底，坐标换算），消除直角边
        Control pp = Parent;
        int bx = -Left, by = -Top;
        while (pp != null && !(pp is Form)) { bx -= pp.Left; by -= pp.Top; pp = pp.Parent; }
        Form bf = pp as Form;
        if (bf != null)
            Ui.PaintBackdrop(g, bf.ClientSize.Width, bf.ClientSize.Height, bx, by);
        else
            using (SolidBrush cardBg = new SolidBrush(Theme.Card))
                g.FillRectangle(cardBg, ClientRectangle);
        Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
        bool active = Enabled && (ToggleStyle ? Checked : true);
        if (active)
        {
            if (_hover || _pressed) Ui.DrawGlow(g, r, Radius, Theme.GlowPink);
            using (GraphicsPath path = Ui.RoundedPath(r, Radius))
            {
                Color c0, c1;
                if (_pressed) { c0 = Theme.BtnPress; c1 = Color.FromArgb(222, 96, 205); }
                else if (_hover) { c0 = Color.FromArgb(228, 178, 250); c1 = Color.FromArgb(255, 138, 188); }
                else { c0 = Color.FromArgb(222, 172, 250); c1 = Theme.GradPink; }
                using (var brush = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), c0, c1, 40f))
                    g.FillPath(brush, path);
                using (GraphicsPath hi = Ui.RoundedPath(new Rectangle(1, 1, Width - 2, Height / 2), Radius))
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                    g.FillPath(hb, hi);
            }
        }
        else
        {
            using (GraphicsPath path = Ui.RoundedPath(r, Radius))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(160, 255, 236, 246)))
                    g.FillPath(b, path);
                using (Pen pen = new Pen(Theme.BtnDisabled)) g.DrawPath(pen, path);
                using (GraphicsPath hi = Ui.RoundedPath(new Rectangle(1, 1, Width - 2, Height / 2), Radius))
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(45, 255, 255, 255)))
                    g.FillPath(hb, hi);
            }
        }
        Color textColor = !Enabled ? Theme.TextSub
            : (!active ? Theme.BtnPress : ForeColor);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

class RoundedSlider : Control
{
    int _min = 0;
    int _max = 2000;
    int _value = 300;
    bool _hover;
    bool _dragging;

    public event EventHandler ValueChanged;

    public int Minimum { get { return _min; } set { _min = value; Invalidate(); Update(); } }
    public int Maximum { get { return _max; } set { _max = value; Invalidate(); Update(); } }

    public int Value
    {
        get { return _value; }
        set
        {
            int v = Math.Max(_min, Math.Min(_max, value));
            if (v == _value) return;   // 值未变化直接跳过（原实现仍强制同步重绘一次）
            _value = v;
            Invalidate();
            Update();
            EventHandler h = ValueChanged;
            if (h != null) h(this, EventArgs.Empty);
        }
    }

    public RoundedSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Card;   // 原生回退：白色底，避免 OnPaint 未运行时露灰
        Width = 300;
        Height = 28;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); Update(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); Update(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _dragging = true; SetValueFromX(e.X); }
        base.OnMouseDown(e);
    }
    protected override void OnMouseMove(MouseEventArgs e) { if (_dragging) SetValueFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; base.OnMouseUp(e); }

    void SetValueFromX(int x)
    {
        double frac = (x - 12.0) / (Width - 24);
        if (frac < 0) frac = 0;
        if (frac > 1) frac = 1;
        Value = _min + (int)Math.Round(frac * (_max - _min));
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        Graphics g = pe.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // 铺满卡片白底，避免控件区域露出黑色
        using (SolidBrush cardBg = new SolidBrush(Theme.Card))
            g.FillRectangle(cardBg, ClientRectangle);
        int cy = Height / 2;
        Rectangle trackRect = new Rectangle(12, cy - 4, Width - 24, 8);
        if (_hover) Ui.DrawGlow(g, trackRect, 4, Theme.GlowViolet);
        using (GraphicsPath tp = Ui.RoundedPath(trackRect, 4))
        using (SolidBrush tb = new SolidBrush(Theme.GlassCard))
            g.FillPath(tb, tp);
        using (GraphicsPath inner = Ui.RoundedPath(new Rectangle(trackRect.X + 1, cy - 3, trackRect.Width - 2, 6), 3))
        using (SolidBrush ib = new SolidBrush(Theme.Track))
            g.FillPath(ib, inner);
        double frac = (_value - _min) / (double)(_max - _min);
        int filled = (int)(trackRect.Width * frac);
        if (filled > 0)
        {
            using (GraphicsPath fp = Ui.RoundedPath(new Rectangle(trackRect.X, trackRect.Y, filled, trackRect.Height), 4))
            using (var brush = new LinearGradientBrush(new Rectangle(trackRect.X, trackRect.Y, filled, trackRect.Height), Theme.GradViolet, Theme.GradPink, 0f))
                g.FillPath(brush, fp);
        }
        int tx = trackRect.X + (int)(trackRect.Width * frac);
        if (_hover) Ui.DrawGlow(g, new Rectangle(tx - 10, cy - 10, 20, 20), 10, Theme.GlowPink);
        using (SolidBrush tb2 = new SolidBrush(_hover ? Theme.BtnHover : Theme.Btn))
            g.FillEllipse(tb2, tx - 10, cy - 10, 20, 20);
        using (GraphicsPath hp = new GraphicsPath())
        {
            hp.AddEllipse(tx - 7, cy - 8, 14, 8);
            using (SolidBrush hb = new SolidBrush(Color.FromArgb(80, 255, 255, 255)))
                g.FillPath(hb, hp);
        }
        using (SolidBrush wb = new SolidBrush(Color.White))
            g.FillEllipse(wb, tx - 3, cy - 3, 6, 6);
    }
}

class BorderBox : Panel
{
    public int Radius = 10;
    public Color BorderColor = Theme.GridLine;

    public BorderBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;   // 系统兜底：DLP 吞掉 OnPaint 时至少显示粉色，不会露黑
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        Graphics g = pe.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int w = Width, h = Height;
        // 圆角外区域画窗体同款背景（粉底+柔光斑，坐标换算到本卡局部系），彻底消除直角边
        Control p = Parent;
        int ox = -Left, oy = -Top;
        while (p != null && !(p is Form)) { ox -= p.Left; oy -= p.Top; p = p.Parent; }
        Form f = p as Form;
        if (f != null)
            Ui.PaintBackdrop(g, f.ClientSize.Width, f.ClientSize.Height, ox, oy);
        else
            using (SolidBrush bg = new SolidBrush(Theme.Bg)) g.FillRectangle(bg, ClientRectangle);
        // 用 SetClip 临时裁剪为圆角（只影响本次绘制，不影响子控件）
        using (GraphicsPath clip = Ui.RoundedPath(new Rectangle(0, 0, w, h), Radius))
        {
            g.SetClip(clip);
            // 阴影尺寸收紧，不超出控件矩形
            using (GraphicsPath s1 = Ui.RoundedPath(new Rectangle(1, 3, w - 3, h - 5), Radius))
            using (SolidBrush b1 = new SolidBrush(Theme.Shadow1)) g.FillPath(b1, s1);
            using (GraphicsPath s2 = Ui.RoundedPath(new Rectangle(2, 6, w - 5, h - 10), Radius))
            using (SolidBrush b2 = new SolidBrush(Theme.Shadow2)) g.FillPath(b2, s2);
            using (SolidBrush b = new SolidBrush(Theme.GlassCard)) g.FillPath(b, clip);
            using (GraphicsPath hi = Ui.RoundedPath(new Rectangle(1, 1, w - 2, h / 2), Radius))
            using (Pen hp = new Pen(Theme.GlassCardHi, 1.4f)) g.DrawPath(hp, hi);
            using (Pen pen = new Pen(BorderColor)) g.DrawPath(pen, clip);
            g.ResetClip();
        }
    }
}
