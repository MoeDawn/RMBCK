using System;
using System.Collections.Generic;
using System.IO;

class TestRunner
{
    static int _fail;
    static void Assert(bool cond, string msg)
    {
        if (cond) Console.WriteLine("  PASS  " + msg);
        else { _fail++; Console.WriteLine("  FAIL  " + msg); }
    }
    static void Section(string name) { Console.WriteLine("== " + name + " =="); }

    static int Main()
    {
        TestKeyNames();
        TestSendInput();
        TestConfigStore();
        TestTriggerController();
        TestTriggerDelayAndMods();
        TestWebAssets();
        TestBridge();
        if (_fail > 0) { Console.WriteLine(_fail + " 个测试失败"); return 1; }
        Console.WriteLine("全部通过"); return 0;
    }

    static void TestKeyNames()
    {
        Section("KeyNames 键位映射");
        Assert(KeyNames.Resolve("Q") == 0x51, "Q -> VK_Q");
        Assert(KeyNames.Resolve("a") == 0x41, "小写 a -> VK_A");
        Assert(KeyNames.Resolve("Space") == 0x20, "Space -> VK_SPACE");
        Assert(KeyNames.Resolve("F1") == 0x70, "F1 -> VK_F1");
        Assert(KeyNames.Resolve("F12") == 0x7B, "F12 -> VK_F12");
        Assert(KeyNames.Resolve("F24") == 0x87, "F24 -> VK_F24");
        Assert(KeyNames.Resolve("5") == 0x35, "5 -> VK_5");
        Assert(KeyNames.Resolve("Control") == 0x11, "Control -> VK_CONTROL");
        Assert(KeyNames.Resolve("Delete") == 0x2E, "Delete -> VK_DELETE");
        Assert(KeyNames.Resolve("`") == 0xC0, "` -> VK_OEM_3");
        Assert(KeyNames.Resolve("-") == 0xBD, "- -> VK_OEM_MINUS");
        Assert(KeyNames.Resolve("=") == 0xBB, "= -> VK_OEM_PLUS");
        Assert(KeyNames.Resolve("[") == 0xDB, "[ -> VK_OEM_4");
        Assert(KeyNames.Resolve("]") == 0xDD, "] -> VK_OEM_6");
        Assert(KeyNames.Resolve("\\") == 0xDC, "\\ -> VK_OEM_5");
        Assert(KeyNames.Resolve(";") == 0xBA, "; -> VK_OEM_1");
        Assert(KeyNames.Resolve("'") == 0xDE, "' -> VK_OEM_7");
        Assert(KeyNames.Resolve(",") == 0xBC, ", -> VK_OEM_COMMA");
        Assert(KeyNames.Resolve(".") == 0xBE, ". -> VK_OEM_PERIOD");
        Assert(KeyNames.Resolve("/") == 0xBF, "/ -> VK_OEM_2");
        Assert(KeyNames.Resolve("Win") == 0x5B, "Win -> VK_LWIN");
        Assert(KeyNames.Resolve("Menu") == 0x5D, "Menu -> VK_APPS");
        Assert(KeyNames.Resolve("PrintScreen") == 0x2C, "PrintScreen -> VK_SNAPSHOT");
        Assert(KeyNames.Resolve("ScrollLock") == 0x91, "ScrollLock -> VK_SCROLL");
        Assert(KeyNames.Resolve("Pause") == 0x13, "Pause -> VK_PAUSE");
        Assert(KeyNames.Resolve("Insert") == 0x2D, "Insert -> VK_INSERT");
        Assert(KeyNames.Resolve("Home") == 0x24, "Home -> VK_HOME");
        Assert(KeyNames.Resolve("End") == 0x23, "End -> VK_END");
        Assert(KeyNames.Resolve("PageUp") == 0x21, "PageUp -> VK_PRIOR");
        Assert(KeyNames.Resolve("PageDown") == 0x22, "PageDown -> VK_NEXT");
        Assert(KeyNames.Resolve("NumLock") == 0x90, "NumLock -> VK_NUMLOCK");
        Assert(KeyNames.Resolve("Num5") == 0x65, "Num5 -> VK_NUMPAD5");
        Assert(KeyNames.Resolve("Num+") == 0x6B, "Num+ -> VK_ADD");
        Assert(KeyNames.Resolve("Num/") == 0x6F, "Num/ -> VK_DIVIDE");
        Assert(KeyNames.NameOf(0x51) == "Q", "NameOf(0x51) -> Q");
        Assert(KeyNames.NameOf(0xC0) == "`", "NameOf(0xC0) -> `");
        Assert(KeyNames.NameOf(0x2E) == "Delete", "NameOf(0x2E) -> Delete");
        Assert(KeyNames.NameOf(0x65) == "Num5", "NameOf(0x65) -> Num5");
        Assert(KeyNames.NameOf(0x7B) == "F12", "NameOf(0x7B) -> F12");
        Assert(KeyNames.NameOf(0x91) == "ScrollLock", "NameOf(0x91) -> ScrollLock");
        bool threw = false;
        try { KeyNames.Resolve("xx"); } catch (ArgumentException) { threw = true; }
        Assert(threw, "未知键名抛 ArgumentException");
    }

    static void TestSendInput()
    {
        Section("SendInput INPUT 结构");
        int exp = IntPtr.Size == 8 ? 40 : 28;
        Assert(SendInputKeySender.InputStructSize == exp, "INPUT struct size matches native (" + exp + ")");
    }

    static void TestConfigStore()
    {
        Section("ConfigStore INI 读写");
        string tmp = Path.Combine(Path.GetTempPath(), "rckl_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var d = ConfigStore.LoadProfile(Path.Combine(tmp, "默认.ini"));
            Assert(d.LongPressMs == 300, "缺失文件回退默认 LongPressMs=300");
            Assert(d.Keys.Count == 0, "缺失文件无按键");

            var s = new AppSettings();
            s.LongPressMs = 500; s.Enabled = false;
            s.Keys.Add(new KeyConfig { Enabled = true, Name = "Q", Vk = KeyNames.Resolve("Q"), Mode = KeyMode.Hold, RepeatIntervalMs = 100 });
            s.Keys.Add(new KeyConfig { Enabled = true, Name = "Space", Vk = KeyNames.Resolve("Space"), Mode = KeyMode.TapRepeat, RepeatIntervalMs = 80 });
            string p = Path.Combine(tmp, "默认.ini");
            ConfigStore.SaveProfile(s, p);
            var s2 = ConfigStore.LoadProfile(p);
            Assert(s2.LongPressMs == 500, "往返 LongPressMs");
            Assert(s2.Enabled == false, "往返 Enabled");
            Assert(s2.Keys.Count == 2, "往返按键数量=2");
            Assert(s2.Keys[0].Name == "Q" && s2.Keys[0].Mode == KeyMode.Hold && s2.Keys[0].Vk == 0x51, "往返 Key1 字段");
            Assert(s2.Keys[1].Mode == KeyMode.TapRepeat && s2.Keys[1].RepeatIntervalMs == 80, "往返 Key2 字段");

            var ptr = new AppSettings();
            ptr.ConfigFolder = tmp; ptr.Profile = "默认"; ptr.CloseAction = "Minimize";
            string pp = Path.Combine(tmp, "config.ini");
            ConfigStore.SavePointer(ptr, pp);
            var ptr2 = ConfigStore.LoadPointer(pp);
            Assert(ptr2.ConfigFolder == tmp, "指针文件往返 ConfigFolder");
            Assert(ptr2.Profile == "默认", "指针文件往返 Profile");
            Assert(ptr2.CloseAction == "Minimize", "指针文件往返 CloseAction");

            Assert(ConfigStore.ListProfiles(tmp).Contains("默认"), "ListProfiles 列出方案");
            ConfigStore.DeleteProfile(tmp, "默认");
            Assert(!File.Exists(p), "DeleteProfile 删除方案文件");

            File.WriteAllText(pp, "[General]\nLongPressMs=400\n[Key1]\nName=Q\n");
            Assert(ConfigStore.IsOldFormat(pp), "旧格式检测 IsOldFormat=true");
            File.WriteAllText(pp, "[General]\nConfigFolder=" + tmp + "\nProfile=默认\nCloseAction=Ask\n");
            Assert(!ConfigStore.IsOldFormat(pp), "新指针格式 IsOldFormat=false");

            File.WriteAllText(Path.Combine(tmp, "坏.ini"), "[[[\nnot-a-key=[[[");
            var s3 = ConfigStore.LoadProfile(Path.Combine(tmp, "坏.ini"));
            Assert(s3.LongPressMs == 300, "损坏文件回退默认且不抛异常");

            var s4 = new AppSettings();
            s4.HotkeyMods = 0x0002; s4.HotkeyVk = 0x7B; s4.Theme = "Day";
            s4.Keys.Add(new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.TapRepeat, RepeatIntervalMs = 80, DelayMs = 120, Mods = 6 });
            string p4 = Path.Combine(tmp, "新字段.ini");
            ConfigStore.SaveProfile(s4, p4);
            var s5 = ConfigStore.LoadProfile(p4);
            Assert(s5.Theme == "Day", "往返 Theme");
            Assert(s5.Keys.Count == 1 && s5.Keys[0].DelayMs == 120, "往返 DelayMs");
            Assert(s5.Keys[0].Mods == 6, "往返 Mods");
            var ptr3 = new AppSettings();
            ptr3.Theme = "Day";
            ConfigStore.SavePointer(ptr3, Path.Combine(tmp, "config.ini"));
            Assert(ConfigStore.LoadPointer(Path.Combine(tmp, "config.ini")).Theme == "Day", "指针文件往返 Theme");

            // 旧格式文件缺 DelayMs/Mods/Theme 时应回退默认值

            File.WriteAllText(Path.Combine(tmp, "旧键.ini"), "[General]\nLongPressMs=300\n[Key1]\nName=Q\nMode=Hold\n");
            var s6 = ConfigStore.LoadProfile(Path.Combine(tmp, "旧键.ini"));
            Assert(s6.Keys.Count == 1 && s6.Keys[0].DelayMs == 0 && s6.Keys[0].Mods == 0, "旧配置缺 DelayMs/Mods 回退为 0");
            Assert(s6.Theme == "Night", "旧配置缺 Theme 回退 Night");
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    static void TestTriggerController()
    {
        Section("TriggerController 状态机");

        var rec1 = new RecordingKeySender();
        var c1 = new TriggerController(rec1, new List<KeyConfig> { new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold } }, 300, true);
        c1.OnRightDown(0); c1.OnTick(299); c1.OnRightUp();
        Assert(rec1.Calls.Count == 0, "短按(未到阈值)不触发任何按键");

        var rec2 = new RecordingKeySender();
        var c2 = new TriggerController(rec2, new List<KeyConfig> { new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold } }, 300, true);
        c2.OnRightDown(0); c2.OnTick(300);
        Assert(rec2.Calls.Count == 1 && rec2.Calls[0] == "D81", "长按到阈值 -> KeyDown Q");
        Assert(c2.IsTriggered, "已触发状态为 true");
        c2.OnTick(500); Assert(rec2.Calls.Count == 1, "保持模式下按着不再重复下发");
        c2.OnRightUp();
        Assert(rec2.Calls.Count == 2 && rec2.Calls[1] == "U81", "松开右键 -> KeyUp Q");

        var rec3 = new RecordingKeySender();
        var c3 = new TriggerController(rec3, new List<KeyConfig> { new KeyConfig { Enabled = true, Name = "E", Vk = 0x45, Mode = KeyMode.TapOnce } }, 300, true);
        c3.OnRightDown(0); c3.OnTick(300);
        Assert(rec3.Calls.Count == 1 && rec3.Calls[0] == "T69", "阈值到达 -> 点按一次(T)");
        c3.OnTick(310); Assert(rec3.Calls.Count == 1, "单次模式不再重复点按");
        c3.OnRightUp();

        var rec4 = new RecordingKeySender();
        var c4 = new TriggerController(rec4, new List<KeyConfig> { new KeyConfig { Enabled = true, Name = "Space", Vk = 0x20, Mode = KeyMode.TapRepeat, RepeatIntervalMs = 100 } }, 300, true);
        c4.OnRightDown(0); c4.OnTick(300);
        Assert(rec4.Calls.Count == 1, "阈值到达 -> 第一次点按");
        c4.OnTick(399); Assert(rec4.Calls.Count == 1, "间隔未到(99ms<100ms)不重复");
        c4.OnTick(400); Assert(rec4.Calls.Count == 2, "400ms 到达 -> 第二次点按");
        c4.OnTick(450); Assert(rec4.Calls.Count == 2, "450ms 间隔未到不重复");
        c4.OnRightUp(); Assert(rec4.Calls.Count == 2, "松开后连发停止");

        var rec5 = new RecordingKeySender();
        var c5 = new TriggerController(rec5, new List<KeyConfig> {
            new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold },
            new KeyConfig { Enabled = true, Name = "Space", Vk = 0x20, Mode = KeyMode.TapOnce } }, 300, true);
        c5.OnRightDown(0); c5.OnTick(300);
        Assert(rec5.Calls.Contains("D81") && rec5.Calls.Contains("T32"), "多键同时触发: Q 按下 + Space 点按");
        c5.OnRightUp();
        Assert(rec5.Calls.Contains("U81"), "多键: 松开时 Q 松开");

        var rec6 = new RecordingKeySender();
        var c6 = new TriggerController(rec6, new List<KeyConfig> { new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold } }, 300, true);
        c6.SetEnabled(false);
        c6.OnRightDown(0); c6.OnTick(300); c6.OnRightUp();
        Assert(rec6.Calls.Count == 0, "停用状态下不触发任何按键");

        var rec7 = new RecordingKeySender();
        var c7 = new TriggerController(rec7, new List<KeyConfig> { new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold } }, 300, true);
        c7.OnRightDown(0); c7.OnTick(300);
        Assert(rec7.Calls.Count == 1 && rec7.Calls[0] == "D81", "触发中(已按下Q)");
        c7.SetEnabled(false);
        Assert(rec7.Calls.Count == 2 && rec7.Calls[1] == "U81", "触发中停用 -> 立即 KeyUp Q");
        c7.OnTick(310);
        Assert(rec7.Calls.Count == 2, "停用后不再下发按键");

        var rec8 = new RecordingKeySender();
        var c8 = new TriggerController(rec8, new List<KeyConfig> { new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold } }, 500, true);
        c8.SetThreshold(100);
        c8.OnRightDown(0); c8.OnTick(99);
        Assert(rec8.Calls.Count == 0, "SetThreshold(100) 后 99ms 未触发");
        c8.OnTick(100);
        Assert(rec8.Calls.Count == 1 && rec8.Calls[0] == "D81", "SetThreshold(100) 后 100ms 触发");
        c8.OnRightUp();

        var rec9 = new RecordingKeySender();
        var c9 = new TriggerController(rec9, new List<KeyConfig> { new KeyConfig { Enabled = true, Name = "Space", Vk = 0x20, Mode = KeyMode.TapRepeat, RepeatIntervalMs = 100 } }, 300, true);
        c9.OnRightDown(0); c9.OnTick(300);
        Assert(rec9.Calls.Count == 1, "第一轮 300ms 触发点按");
        c9.OnTick(400);
        Assert(rec9.Calls.Count == 2, "第一轮 400ms 连发");
        c9.OnRightUp();
        c9.OnRightDown(500); c9.OnTick(800);
        Assert(rec9.Calls.Count == 3, "第二轮 800ms 重新触发点按");
        c9.OnTick(899);
        Assert(rec9.Calls.Count == 3, "第二轮 899ms 间隔未到不连发(_lastTap 已重置)");
        c9.OnTick(900);
        Assert(rec9.Calls.Count == 4, "第二轮 900ms 按新节奏连发");
        c9.OnRightUp();

        var rec10 = new RecordingKeySender();
        var c10 = new TriggerController(rec10, new List<KeyConfig> {
            new KeyConfig { Enabled = true, Name = "Space", Vk = 0x20, Mode = KeyMode.TapRepeat, RepeatIntervalMs = 100 },
            new KeyConfig { Enabled = true, Name = "Enter", Vk = 0x0D, Mode = KeyMode.TapRepeat, RepeatIntervalMs = 200 } }, 300, true);
        c10.OnRightDown(0); c10.OnTick(300);
        Assert(rec10.Calls.Count == 2, "两个连发键同时首次点按");
        c10.OnTick(400);
        Assert(rec10.Calls.Count == 3, "400ms 仅 100ms 间隔键连发");
        c10.OnTick(500);
        Assert(rec10.Calls.Count == 5, "500ms 两个连发键均按各自间隔重复");
        c10.OnRightUp();

        var rec11 = new RecordingKeySender();
        var c11 = new TriggerController(rec11, new List<KeyConfig> { new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold } }, 300, true);
        c11.ModifierHeld = delegate() { return true; };
        c11.OnRightDown(0); c11.OnTick(300); c11.OnTick(500);
        Assert(rec11.Calls.Count == 0, "修饰键按住(如 Ctrl+右键)不触发联动");
        c11.OnRightUp();
        Assert(rec11.Calls.Count == 0, "修饰键场景松开右键不补触发");

        var rec12 = new RecordingKeySender();
        var c12 = new TriggerController(rec12, new List<KeyConfig> { new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold } }, 300, true);
        c12.ModifierHeld = delegate() { return false; };
        c12.OnRightDown(0); c12.OnTick(300);
        Assert(rec12.Calls.Count == 1 && rec12.Calls[0] == "D81", "无修饰键时正常触发");
        c12.OnRightUp();

        bool mod = true;
        var rec13 = new RecordingKeySender();
        var c13 = new TriggerController(rec13, new List<KeyConfig> { new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold } }, 300, true);
        c13.ModifierHeld = delegate() { return mod; };
        c13.OnRightDown(0); c13.OnTick(300);
        mod = false;
        c13.OnTick(600);
        Assert(rec13.Calls.Count == 0, "修饰键中途松开，本次按住仍不触发");
        c13.OnRightUp();
    }

    static void TestTriggerDelayAndMods()
    {
        Section("TriggerController 行延迟 / 组合键");

        // 行延迟：阈值 300 + DelayMs 200 -> 500ms 才 KeyDown
        var r1 = new RecordingKeySender();
        var d1 = new TriggerController(r1, new List<KeyConfig> {
            new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold, DelayMs = 200 } }, 300, true);
        d1.OnRightDown(0); d1.OnTick(300);
        Assert(r1.Calls.Count == 0, "行延迟未到不触发");
        d1.OnTick(499);
        Assert(r1.Calls.Count == 0, "行延迟 499ms 仍未触发");
        d1.OnTick(500);
        Assert(r1.Calls.Count == 1 && r1.Calls[0] == "D81", "行延迟到点触发 KeyDown");
        d1.OnRightUp();
        Assert(r1.Calls.Count == 2 && r1.Calls[1] == "U81", "行延迟保持模式松开 KeyUp");

        // 两行不同延迟：先到先发
        var r2 = new RecordingKeySender();
        var d2 = new TriggerController(r2, new List<KeyConfig> {
            new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.TapOnce, DelayMs = 0 },
            new KeyConfig { Enabled = true, Name = "E", Vk = 0x45, Mode = KeyMode.TapOnce, DelayMs = 300 } }, 300, true);
        d2.OnRightDown(0); d2.OnTick(300);
        Assert(r2.Calls.Count == 1 && r2.Calls[0] == "T81", "0 延迟行先触发");
        d2.OnTick(600);
        Assert(r2.Calls.Count == 2 && r2.Calls[1] == "T69", "300 延迟行后触发");
        d2.OnRightUp();

        // 组合键：Ctrl+Q 的 TapOnce 顺序 = Ctrl 下 -> Q 下 -> Q 上 -> Ctrl 上
        var r3 = new RecordingKeySender();
        var d3 = new TriggerController(r3, new List<KeyConfig> {
            new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.TapOnce, Mods = 1 } }, 300, true);
        d3.OnRightDown(0); d3.OnTick(300);
        Assert(string.Join(",", r3.Calls.ToArray()) == "D17,T81,U17", "Ctrl+Q 点按顺序正确");
        d3.OnRightUp();

        // 组合键 Hold：修饰键与主键同持同放
        var r4 = new RecordingKeySender();
        var d4 = new TriggerController(r4, new List<KeyConfig> {
            new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold, Mods = 2 } }, 300, true);
        d4.OnRightDown(0); d4.OnTick(300);
        Assert(string.Join(",", r4.Calls.ToArray()) == "D16,D81", "Shift+Q 保持：先修饰键后主键");
        d4.OnRightUp();
        Assert(string.Join(",", r4.Calls.ToArray()) == "D16,D81,U81,U16", "Shift+Q 保持：先放主键再放修饰键");

        // 多个 Hold 行共享修饰键：只按一次
        var r5 = new RecordingKeySender();
        var d5 = new TriggerController(r5, new List<KeyConfig> {
            new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold, Mods = 1 },
            new KeyConfig { Enabled = true, Name = "E", Vk = 0x45, Mode = KeyMode.Hold, Mods = 1 } }, 300, true);
        d5.OnRightDown(0); d5.OnTick(300);
        Assert(r5.Calls.FindAll(delegate(string c) { return c == "D17"; }).Count == 1, "共享修饰键只按下一次");
        d5.OnRightUp();
        Assert(r5.Calls.FindAll(delegate(string c) { return c == "U17"; }).Count == 1, "共享修饰键只释放一次");

        // FiredLabels 供测试面板
        var r6 = new RecordingKeySender();
        var d6 = new TriggerController(r6, new List<KeyConfig> {
            new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold } }, 300, true);
        Assert(d6.FiredLabels != null && d6.FiredLabels.Count == 0, "未触发时 FiredLabels 为空");
        d6.OnRightDown(0); d6.OnTick(300);
        Assert(d6.FiredLabels.Count == 1 && d6.FiredLabels[0].IndexOf("Q") >= 0, "触发后 FiredLabels 含 Q");
        d6.OnRightUp();
        Assert(d6.FiredLabels.Count == 0, "松开后 FiredLabels 清空");

        // 仅已触发的 Hold 行才保留修饰键（未到延迟的 Hold 行不得扣住修饰键）
        var r7 = new RecordingKeySender();
        var d7 = new TriggerController(r7, new List<KeyConfig> {
            new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.TapOnce, DelayMs = 0, Mods = 1 },
            new KeyConfig { Enabled = true, Name = "X", Vk = 0x58, Mode = KeyMode.Hold, DelayMs = 2000, Mods = 1 } }, 300, true);
        d7.OnRightDown(0); d7.OnTick(300);
        Assert(string.Join(",", r7.Calls.ToArray()) == "D17,T81,U17", "TapOnce 点按后修饰键立即释放，未被未到点的 Hold 行扣住");
        d7.OnTick(2300);
        Assert(string.Join(",", r7.Calls.ToArray()) == "D17,T81,U17,D17,D88", "延迟 Hold 行到点后才按修饰键与主键");
        d7.OnRightUp();
        Assert(string.Join(",", r7.Calls.ToArray()) == "D17,T81,U17,D17,D88,U88,U17", "松开时先放 Hold 主键再放修饰键");

        // 同一 Vk 两行不同模式：按行索引独立（TapOnce 只点一次，TapRepeat 持续连发）
        var r8 = new RecordingKeySender();
        var d8 = new TriggerController(r8, new List<KeyConfig> {
            new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.TapOnce, DelayMs = 0 },
            new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.TapRepeat, RepeatIntervalMs = 100 } }, 300, true);
        d8.OnRightDown(0); d8.OnTick(300);
        Assert(r8.Calls.Count == 2, "同键不同模式：两行各自首次触发");
        d8.OnTick(400);
        Assert(r8.Calls.Count == 3, "同键不同模式：TapRepeat 行 400ms 连发");
        d8.OnTick(450);
        Assert(r8.Calls.Count == 3, "同键不同模式：450ms 间隔未到不再发");
        d8.OnTick(500);
        Assert(r8.Calls.Count == 4, "同键不同模式：TapRepeat 行 500ms 再连发");
        d8.OnRightUp();

        // 负数 DelayMs 视为 0
        var r9 = new RecordingKeySender();
        var d9 = new TriggerController(r9, new List<KeyConfig> {
            new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold, DelayMs = -500 } }, 300, true);
        d9.OnRightDown(0); d9.OnTick(299);
        Assert(r9.Calls.Count == 0, "负数 DelayMs 不导致提前触发");
        d9.OnTick(300);
        Assert(r9.Calls.Count == 1 && r9.Calls[0] == "D81", "负数 DelayMs 视为 0，阈值当拍触发");
        d9.OnRightUp();
    }

    static void TestWebAssets()
    {
        Section("WebAssets 资源映射");
        Assert(WebAssets.Map.Count >= 10, "资源映射至少 10 项");
        Assert(WebAssets.Map.ContainsKey("RMBCK.web.index.html"), "含 index.html 映射");
        Assert(WebAssets.Map["RMBCK.web.index.html"] == "index.html", "index.html 相对路径正确");
        Assert(WebAssets.Map["RMBCK.web.assets.icon.png"] == "assets/icon.png", "assets 子目录映射正确");
        string txt = WebAssets.ReadText("index.html");
        Assert(txt != null && txt.IndexOf("<html") >= 0, "ReadText 读取 index.html");
    }

    static void TestBridge()
    {
        Section("Bridge 消息解析");
        BridgeMessage m = Bridge.Parse("{\"action\":\"setSetting\",\"key\":\"longPressMs\",\"value\":450}");
        Assert(m != null && m.Action == "setSetting", "解析 action");
        Assert(Bridge.GetStr(m.Args, "key", "") == "longPressMs", "解析字符串参数");
        Assert(Bridge.GetInt(m.Args, "value", -1) == 450, "解析整数参数");

        BridgeMessage m2 = Bridge.Parse("{\"action\":\"setTheme\",\"mode\":\"Day\"}");
        Assert(m2 != null && Bridge.GetStr(m2.Args, "mode", "") == "Day", "解析 setTheme");

        Assert(Bridge.Parse("not json") == null, "非法 JSON 返回 null 不抛");
        Assert(Bridge.Parse("") == null, "空串返回 null");
        Assert(Bridge.Parse("{\"x\":1}") == null, "缺 action 返回 null");

        BridgeMessage m3 = Bridge.Parse("{\"action\":\"selectProcess\",\"name\":\"a.exe\"}");
        Assert(Bridge.GetStr(m3.Args, "name", "") == "a.exe", "解析进程名");

        string json = Bridge.Serialize(new Dictionary<string, object> { { "ok", true }, { "n", 3 } });
        Assert(json.IndexOf("\"ok\":true") >= 0 && json.IndexOf("\"n\":3") >= 0, "序列化对象");
    }
}

class RecordingKeySender : IKeySender
{
    public readonly List<string> Calls = new List<string>();
    public void KeyDown(int vk) { Calls.Add("D" + vk); }
    public void KeyUp(int vk) { Calls.Add("U" + vk); }
    public void KeyTap(int vk) { Calls.Add("T" + vk); }
}
