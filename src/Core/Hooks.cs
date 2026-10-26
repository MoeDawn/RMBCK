using System;
using System.Runtime.InteropServices;

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

class KeyboardHook : IDisposable
{
    public event Action<int> KeyDown;
    public event Action<int> KeyUp;

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    const int WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

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
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN || msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    KBDLLHOOKSTRUCT s = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                    var h = down ? KeyDown : KeyUp;
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
