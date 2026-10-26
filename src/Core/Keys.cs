using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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

interface IKeySender
{
    void KeyDown(int vk);
    void KeyUp(int vk);
    void KeyTap(int vk);
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
