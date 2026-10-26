using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

class HotkeyManager : IDisposable
{
    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008;
    public uint Mods;
    public uint Vk;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public HotkeyManager(uint mods, uint vk) { Mods = mods; Vk = vk; }

    public bool Register(IntPtr hwnd, int id, uint mods, uint vk)
    {
        UnregisterHotKey(hwnd, id);
        if (!RegisterHotKey(hwnd, id, mods, vk))
        {
            RegisterHotKey(hwnd, id, Mods, Vk);   // 注册失败时恢复原热键
            return false;
        }
        Mods = mods; Vk = vk;
        return true;
    }

    public void Unregister(IntPtr hwnd, int id) { UnregisterHotKey(hwnd, id); }

    public static bool IsValid(uint mods, uint vk)
    {
        if (mods == 0 || vk == 0) return false;
        if (vk == 0x11 || vk == 0x10 || vk == 0x12 || vk == 0x5B || vk == 0x5C) return false;
        if (vk >= 0xA0 && vk <= 0xA5) return false;
        return true;
    }

    public static string Describe(uint mods, uint vk)
    {
        var parts = new List<string>();
        if ((mods & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mods & MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyNames.NameOf((int)vk));
        return string.Join("+", parts.ToArray());
    }

    public void Dispose() { }
}
