using System;
using System.Collections.Generic;

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
    readonly Dictionary<int, long> _lastTap = new Dictionary<int, long>();   // 行索引 -> 上次点按时刻
    readonly Dictionary<int, bool> _fired = new Dictionary<int, bool>();     // 行索引 -> 已触发
    readonly List<int> _heldMods = new List<int>();                          // 当前由本控制器按住的修饰键

    public Func<bool> ModifierHeld;

    public bool RandomJitter;
    readonly Random _rng = new Random();
    public int LastThresholdMs;
    public List<string> FiredLabels = new List<string>();

    // 返回带抖动的实际值：jitter=false 原值；true 时 ×(0.8~1.2)
    int Jitter(int v)
    {
        if (!RandomJitter || v <= 0) return v;
        return (int)(v * (0.8 + _rng.NextDouble() * 0.4));
    }

    // Mods 位布局：1=Ctrl(VK 0x11) 2=Shift(VK 0x10) 4=Alt(VK 0x12) 8=Win(VK 0x5B)；不是 Win32 MOD_* 布局
    static int ModVk(int bit)
    {
        if (bit == 1) return 0x11;   // Ctrl
        if (bit == 2) return 0x10;   // Shift
        if (bit == 4) return 0x12;   // Alt
        return 0x5B;                 // Win
    }

    static readonly int[] ModOrder = new int[] { 1, 2, 4, 8 };

    // 非法位一律忽略，避免配置脏数据误按其它键
    static int MaskMods(int mods) { return mods & 0x0F; }

    void HoldMods(int mods)
    {
        mods = MaskMods(mods);
        foreach (int bit in ModOrder)
            if ((mods & bit) != 0 && !_heldMods.Contains(ModVk(bit)))
            {
                _keys.KeyDown(ModVk(bit));
                _heldMods.Add(ModVk(bit));
            }
    }

    void ReleaseMods()
    {
        for (int i = _heldMods.Count - 1; i >= 0; i--) _keys.KeyUp(_heldMods[i]);
        _heldMods.Clear();
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
        // 丢失过一次 WM_RBUTTONUP 时不能直接开新一轮：先释放上一轮仍按住的键，
        // 否则 _fired 被清空后那些键会永久留在按下状态。
        if (_triggered) OnRightUp();
        _pressed = true;
        _downTick = nowMs;
        _modifierCancelled = false;
        _armedThresholdMs = Jitter(_thresholdMs);
        LastThresholdMs = _armedThresholdMs;
        _fired.Clear();
        FiredLabels.Clear();
        _lastTap.Clear();
    }

    public void OnRightUp()
    {
        _pressed = false;
        if (_triggered)
        {
            // 倒序释放各 Hold 行主键，最后统一释放共享修饰键，保证游戏先看到主键抬起
            for (int i = _config.Count - 1; i >= 0; i--)
            {
                var kc = _config[i];
                if (kc.Mode == KeyMode.Hold && WasFired(i)) _keys.KeyUp(kc.Vk);
            }
            ReleaseMods();
            _triggered = false;
        }
        _heldMods.Clear();
        _modifierCancelled = false;
        _lastTap.Clear();
        _fired.Clear();
        FiredLabels.Clear();
    }

    bool WasFired(int index)
    {
        return _fired.ContainsKey(index) && _fired[index];
    }

    void FireRow(int index, long nowMs)
    {
        var kc = _config[index];
        int mods = MaskMods(kc.Mods);
        _fired[index] = true;
        string label = kc.Name + (mods != 0 ? "(组合)" : "");
        if (!FiredLabels.Contains(label)) FiredLabels.Add(label);
        if (kc.Mode == KeyMode.Hold)
        {
            HoldMods(mods);
            _keys.KeyDown(kc.Vk);
        }
        else
        {
            HoldMods(mods);
            _keys.KeyTap(kc.Vk);
            // 仅释放该行独占的修饰键：若后续无其他 Hold 行需要，统一在 OnRightUp 释放；
            // 点按类行立即释放（保持 Hold 行仍需要的除外）
            ReleaseModsNotNeededByHold();
        }
        _lastTap[index] = nowMs;
    }

    void ReleaseModsNotNeededByHold()
    {
        int needed = 0;
        for (int j = 0; j < _config.Count; j++)
        {
            var kc = _config[j];
            // 只有已经真正触发的 Hold 行才需要保留修饰键；尚未到延迟/未触发的行不得扣住
            if (kc.Enabled && kc.Mode == KeyMode.Hold && WasFired(j)) needed |= MaskMods(kc.Mods);
        }
        for (int i = _heldMods.Count - 1; i >= 0; i--)
        {
            int vk = _heldMods[i];
            if ((needed & 1) != 0 && vk == 0x11) continue;
            if ((needed & 2) != 0 && vk == 0x10) continue;
            if ((needed & 4) != 0 && vk == 0x12) continue;
            if ((needed & 8) != 0 && vk == 0x5B) continue;
            _keys.KeyUp(vk);
            _heldMods.RemoveAt(i);
        }
    }

    public void OnTick(long nowMs)
    {
        if (!_pressed || !_enabled) return;
        if (!_triggered)
        {
            if (nowMs - _downTick < _armedThresholdMs) return;
            if (ModifierHeld != null && ModifierHeld()) { _modifierCancelled = true; return; }
            if (_modifierCancelled) return;
            _triggered = true;
        }
        for (int i = 0; i < _config.Count; i++)
        {
            var kc = _config[i];
            if (!kc.Enabled) continue;
            int delay = kc.DelayMs < 0 ? 0 : kc.DelayMs;   // 负数视为不延迟
            long due = _downTick + _armedThresholdMs + delay;
            if (nowMs < due) continue;
            if (!WasFired(i)) { FireRow(i, nowMs); continue; }
            if (kc.Mode != KeyMode.TapRepeat) continue;
            long last = _lastTap.ContainsKey(i) ? _lastTap[i] : nowMs;
            if (nowMs - last >= Jitter(kc.RepeatIntervalMs))
            {
                HoldMods(kc.Mods);
                _keys.KeyTap(kc.Vk);
                ReleaseModsNotNeededByHold();
                _lastTap[i] = nowMs;
            }
        }
    }
}
