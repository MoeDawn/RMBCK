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
}

class RecordingKeySender : IKeySender
{
    public readonly List<string> Calls = new List<string>();
    public void KeyDown(int vk) { Calls.Add("D" + vk); }
    public void KeyUp(int vk) { Calls.Add("U" + vk); }
    public void KeyTap(int vk) { Calls.Add("T" + vk); }
}
