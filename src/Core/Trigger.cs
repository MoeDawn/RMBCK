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
