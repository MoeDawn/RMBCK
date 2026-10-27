using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

class HotkeyManager : IDisposable
{
    public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;   // 只作用于 RegisterHotKey，不写入用户可见的 Mods
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
        // MOD_NOREPEAT 只加在注册调用上：按住热键时系统不再自动重复，避免联动被连翻
        if (!RegisterHotKey(hwnd, id, mods | MOD_NOREPEAT, vk))
        {
            // 恢复旧热键；若旧组合也已被占用，Register 仍返回 false，调用方必须保留设置不变
            RegisterHotKey(hwnd, id, Mods | MOD_NOREPEAT, Vk);
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
