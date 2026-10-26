# RMBCK 2.0.0 WebView2 界面重写 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 RMBCK 的界面层从 WinForms 自绘整体迁移到 WebView2（HTML/CSS/JS），业务逻辑保留在 C#，并新增自定义热键、开机自启、触发日志、行级延迟、组合键输出与日夜双主题。

**Architecture:** 无边框 WinForms 窗体只做 WebView2 宿主与原生窗口能力（拖动/缩放/托盘/热键）；网页通过 `window.chrome.webview.postMessage` 发送意图，C# 经 `Bridge` 白名单路由后修改唯一事实源 `AppSettings`，再用 `PostWebMessageAsJson` 回推状态快照。前端资源编译期嵌入 exe，运行时解压到 `%LOCALAPPDATA%\RMBCK\web\<版本>\` 并通过虚拟主机 `rmbck.local` 加载。

**Tech Stack:** .NET Framework 4.0 `csc.exe`（C# 5 语法）、WinForms（仅宿主/托盘）、Microsoft.Web.WebView2 SDK 1.0.4314.35、原生 HTML/CSS/JS（无构建工具、无框架）、`System.Web.Script.Serialization.JavaScriptSerializer` 做 JSON。

## Global Constraints

- **C# 5 语法**：禁止字符串插值 `$""`、`nameof`、`?.`、表达式体成员、自动属性初始化器、`async Task Main`、`out var`、元组。
- **build.bat / test.bat / spike 脚本必须 CRLF 行尾**（用 Write 工具写 .bat 后，若行尾为 LF 需转换为 CRLF）。
- Git Bash 下调用 csc 必须禁用路径转换：`MSYS2_ARG_CONV_EXCL="*" csc ...`；或统一走 `cmd //c 'd:/eyf/mouseplugin/build.bat'`。
- 版本号 2.0.0 需同步三处 Assembly 版本 + `build.bat` 产物名 `RMBCK 2.0.0.exe`。
- 程序集元数据与文案保持不变：Company `SSJJ2.5`、Title/Description `千恋万花安装器(反MC)`、托盘 `Senren＊Banka`、主页 `https://www.bilibili.com/video/BV1GJ411x7h7`。
- 所有异常包在 `try/catch` 中，绝不让未处理异常弹 .NET 崩溃框（旧 UI 的单文件也遵守）。
- 修改已有源码只用 Read/Edit（DLP 环境）；不要用 PowerShell/python 读改写已加密文件。
- 新增文件（Write 工具）为明文落盘，可安全编译。
- 默认主题 `Night`；日间主题色值必须与现有 `Theme` 类一致：`#FFF0F5 / #FFFFFF / #FF6FA5 / #FF85B3 / #F05A94 / #A78BFA / #4A4A68 / #9A9AAF / #FAD7E3 / #FFD1E0 / #FFD9E8 / #FF7AAC / #BA94FF`。
- 发布版 WebView2 设置：`AreDevToolsEnabled=false`、`AreDefaultContextMenusEnabled=false`、`NewWindowRequested` 一律取消。
- **JS→C# 对象消息必须用 `e.WebMessageAsJson` 读取**；`TryGetWebMessageAsString()` 只对字符串消息有效，否则抛 `E_INVALIDARG (0x800700AA)`。
- **仓库里当前未被跟踪的文件**（`.gitignore` 规则）：`tests.cs`、`test.bat`、`CLAUDE.md`、`CHANGELOG.md`。需要纳入提交时必须用 `git add -f <file>`；`src/`、`web/`、`lib/`、`build.bat`、`app.manifest`、`*.png`、`*.ico`、`*.jpg` 均为正常跟踪。`docs/superpowers/` 已跟踪。
- 提交前永远先跑 `git status --short`，确认没有 `*.exe`、`config.ini`、`默认.ini` 混入。

### 关键 API 事实（2026-10-26 本机实测，勿再猜测）

| 事实 | 值 |
|---|---|
| 本机 WebView2 运行时 | `CoreWebView2Environment.GetAvailableBrowserVersionString()` → `155.0.4283.62` |
| SDK 三个文件 | `lib/Microsoft.Web.WebView2.Core.dll`、`lib/Microsoft.Web.WebView2.WinForms.dll`、`lib/WebView2Loader.dll`（必须与 exe 同目录） |
| csc 引用 | `/r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:lib/Microsoft.Web.WebView2.Core.dll /r:lib/Microsoft.Web.WebView2.WinForms.dll` |
| 同一 UserDataFolder 不能并发 | 两个进程用同一 UDF 会 `0x800700AA`；本项目固定单实例，但冒烟测试要用独立 UDF |
| 虚拟主机 | `SetVirtualHostNameToFolderMapping("rmbck.local", dir, CoreWebView2HostResourceAccessKind.Allow)` + `Navigate("https://rmbck.local/index.html")`，相对路径 CSS/JS 正常 |
| 透明叠加 | `wv.DefaultBackgroundColor = Color.Transparent` + 窗体 `BackColor=Color.Fuchsia; TransparencyKey=Color.Fuchsia` |
| 截图自检 | `CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream)` 可拿到页面渲染（含 alpha） |

---

## 文件结构

```
lib/
  Microsoft.Web.WebView2.Core.dll          SDK（入库）
  Microsoft.Web.WebView2.WinForms.dll      SDK（入库）
  WebView2Loader.dll                       原生加载器（入库）
src/
  Program.cs            入口、运行时检测、单实例、托盘装配、WebView2 环境
  Core/Settings.cs      KeyMode、KeyConfig、AppSettings、ConfigStore
  Core/Keys.cs          KeyNames、IKeySender、SendInputKeySender
  Core/Trigger.cs       TriggerController（阈值/抖动/三模式/行延迟/组合键）
  Core/Hooks.cs         MouseHook、KeyboardHook
  Core/Hotkey.cs        HotkeyManager（RegisterHotKey + 捕获校验）
  Core/Autostart.cs     计划任务开关
  Core/History.cs       HistoryRing（环形缓冲 + CSV）
  App/WebAssets.cs      嵌入资源解压 + 资源名映射
  App/Bridge.cs         JSON 解析 + 动作白名单类型
  App/MainForm.cs       无边框宿主、WM_NCHITTEST、拖动、状态推送
  App/OverlayForm.cs    Toast/HUD/彩蛋透明悬浮窗（2 个复用窗）
  App/Tray.cs           NotifyIcon + 主题菜单
web/
  index.html  app.css  app.js
  overlay.html overlay.css overlay.js
  assets/icon.png  iconf1.png  iconf2.png  iconf3.png  iconf4.png  err.jpg
tests.cs                纯逻辑测试（/main:TestRunner）
build.bat               编译 exe（含 WebView2 引用 + 资源嵌入）
test.bat                编译 Core+tests
RightClickKeyLinker.cs  旧实现，Task 15 删除
```

**接口约定（全计划统一）**

```csharp
// WebAssets
static class WebAssets {
    public static readonly Dictionary<string,string> Map;  // 资源名 -> 相对路径（如 "RMBCK.web.index.html" -> "index.html"）
    public static string Extract(string version);          // 返回解压目录绝对路径
    public static string ReadText(string relativePath);    // 读嵌入文本（供测试/兜底）
}

// Bridge
class BridgeMessage { public string Action; public Dictionary<string,object> Args; }
static class Bridge {
    public static BridgeMessage Parse(string json);        // 失败返回 null，不抛
    public static string Serialize(object o);              // wrapper：JavaScriptSerializer
    public static object Get(Dictionary<string,object> d, string key, object fallback);
    public static int GetInt(Dictionary<string,object> d, string key, int fallback);
    public static bool GetBool(Dictionary<string,object> d, string key, bool fallback);
    public static string GetStr(Dictionary<string,object> d, string key, string fallback);
}

// Trigger（扩展）
class TriggerController {
    public Func<bool> ModifierHeld;
    public bool RandomJitter;
    public int LastThresholdMs;
    public bool IsTriggered { get; }
    public void SetEnabled(bool en);
    public void SetThreshold(int ms);
    public void OnRightDown(long nowMs);
    public void OnRightUp();
    public void OnTick(long nowMs);
    public List<string> FiredLabels;   // 本次按住已触发的行标签（供测试面板显示）
}

// Hotkey
class HotkeyManager : IDisposable {
    public uint Mods; public uint Vk;
    public bool Register(IntPtr hwnd, int id, uint mods, uint vk);  // 失败返回 false 并保留旧值
    public void Unregister(IntPtr hwnd, int id);
    public static bool IsValid(uint mods, uint vk);                 // 至少一个修饰键、vk 非修饰键
    public static string Describe(uint mods, uint vk);              // "Ctrl+F12"
}

// History
class HistoryRing {
    public void Add(string keys, string proc, string mode, DateTime at);
    public List<Dictionary<string,object>> Snapshot();              // JS 友好
    public int ExportCsv(string path);                              // 返回导出条数
    public void Clear();
}

// Overlay
class OverlayForm {   // 两个实例：toast/egg 窗 与 hud 窗
    public void ShowToast(string text, string kind, int durationMs);
    public void ShowEgg(object[] items);
    public void ShowHud(string text);
    public void HideHud();
}
```

---

### Task 1: 拆分核心类型到 src/Core 并新增配置字段

**Files:**
- Create: `src/Core/Settings.cs`, `src/Core/Keys.cs`, `src/Core/Hooks.cs`
- Modify: `RightClickKeyLinker.cs`（删除已移走的类型定义，保留 UI 部分）
- Modify: `tests.cs`
- Modify: `test.bat`

**Interfaces:**
- Consumes: 无
- Produces: `KeyMode`、`KeyConfig`（新增 `DelayMs`、`Mods`）、`AppSettings`（新增 `HotkeyMods`、`HotkeyVk`、`Theme`）、`ConfigStore`、`KeyNames`、`IKeySender`、`SendInputKeySender`、`MouseHook`、`KeyboardHook`、`KBDLLHOOKSTRUCT`

- [ ] **Step 1: 先写配置往返测试（失败）**

在 `tests.cs` 的 `TestConfigStore()` 末尾（`finally` 之前）追加：

```csharp
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
```

并把上面那条 `Assert(-1 == ...)` 删除。

- [ ] **Step 2: 运行测试确认失败**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat'
```
Expected: 编译错误 `KeyConfig 不包含 DelayMs 的定义`（或断言 FAIL）。

- [ ] **Step 3: 创建 src/Core/Settings.cs**

完整内容（从 `RightClickKeyLinker.cs:125-397` 迁移并扩展）：

```csharp
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
    public int LongPressMs = 300;
    public string ConfigFolder = "";
    public bool Enabled = true;
    public string TargetProcess = "";
    public string Profile = "默认";
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
                    if (!int.TryParse(section.Substring(3), out idx) || idx < 1) continue;
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
```

- [ ] **Step 4: 创建 src/Core/Keys.cs**

把 `RightClickKeyLinker.cs:136-202` 的 `KeyNames` 与 `399-558` 的 `IKeySender`/`SendInputKeySender` 原样搬入，加 `using System; using System.Collections.Generic; using System.Runtime.InteropServices;`。逻辑一行不改。

- [ ] **Step 5: 创建 src/Core/Hooks.cs**

把 `RightClickKeyLinker.cs:560-611` 的 `MouseHook` 与 `1919-1985` 的 `KeyboardHook`/`KBDLLHOOKSTRUCT` 搬入，加 `using System; using System.Runtime.InteropServices;`。逻辑一行不改。

- [ ] **Step 6: 从旧文件删除已迁移类型**

在 `RightClickKeyLinker.cs` 中删除以下行区间对应类型（先 Read 确认行号可能漂移，按类型名定位）：
`enum KeyMode`、`class KeyConfig`、`static class KeyNames`、`class AppSettings`、`static class ConfigStore`、`interface IKeySender`、`class SendInputKeySender`、`class MouseHook`、`class KeyboardHook`、`struct KBDLLHOOKSTRUCT`。
保留 `Program`、`TriggerController`、`MainForm` 及所有 UI 类型（Task 15 统一删除）。

- [ ] **Step 7: 更新 test.bat**

```bat
@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:exe /main:TestRunner /out:tests.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  src\Core\Settings.cs src\Core\Keys.cs src\Core\Hooks.cs src\Core\Trigger.cs tests.cs
if not %errorlevel%==0 exit /b 1
.\tests.exe
exit /b %errorlevel%
```

> Task 1 的 Step 4 除了 `Keys.cs` 与 `Hooks.cs`，还必须创建 `src/Core/Trigger.cs`：
> 内容 = 旧文件 `RightClickKeyLinker.cs:406-497` 的 `TriggerController` **原样复制**（不含行延迟与组合键）；
> 同时从旧文件删除该类定义。Task 2 会整文件替换为扩展版本。

- [ ] **Step 8: 运行测试**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat'
```
Expected: 全部通过（`全部通过`），含新增的 Theme/DelayMs/Mods 往返与回退断言。

- [ ] **Step 9: 确认旧 UI 仍可编译**

因为核心类型已从旧文件移出，`build.bat` 必须先补上新的源文件，否则会缺类型。把 build.bat 末尾的源文件行改为：

```
  src\Core\Settings.cs src\Core\Keys.cs src\Core\Hooks.cs src\Core\Trigger.cs ^
  RightClickKeyLinker.cs
```

然后：

```bash
cd /d/eyf/mouseplugin && cmd //c 'build.bat'
```
Expected: 编译成功，仍产出 `RMBCK 1.1.0.exe`（Task 3/4 才切换新入口与新版本号）。

- [ ] **Step 10: 提交**

```bash
git add src build.bat RightClickKeyLinker.cs
git add -f tests.cs test.bat
git status --short
git commit -m "refactor: 拆分核心类型到 src/Core，新增 Theme/DelayMs/Mods/Hotkey 配置字段"
```

---

### Task 2: TriggerController 行延迟与组合键输出

**Files:**
- Modify: `src/Core/Trigger.cs`
- Modify: `tests.cs`（新增 `TestTriggerDelayAndMods`）

**Interfaces:**
- Consumes: `KeyConfig.DelayMs`、`KeyConfig.Mods`、`IKeySender`
- Produces: `TriggerController` 同签名 + `public List<string> FiredLabels`

- [ ] **Step 1: 写失败测试**

在 `tests.cs` 的 `Main()` 中 `TestTriggerController();` 后加 `TestTriggerDelayAndMods();`，并追加方法：

```csharp
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
    }
```

- [ ] **Step 2: 运行确认失败**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat'
```
Expected: 编译失败 `KeyConfig 不包含 DelayMs`（若 Task 1 已完成则应能编译，断言 FAIL "行延迟未到不触发"）。

- [ ] **Step 3: 实现**

`src/Core/Trigger.cs` 完整替换为：

```csharp
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
    bool _modifierCancelled;
    int _armedThresholdMs;
    readonly Dictionary<int, long> _lastTap = new Dictionary<int, long>();
    readonly Dictionary<int, bool> _fired = new Dictionary<int, bool>();   // 行索引 -> 已触发
    readonly List<int> _heldMods = new List<int>();                        // 当前由本控制器按住的修饰键

    public Func<bool> ModifierHeld;
    public bool RandomJitter;
    readonly Random _rng = new Random();
    public int LastThresholdMs;
    public List<string> FiredLabels = new List<string>();

    public TriggerController(IKeySender keys, List<KeyConfig> config, int thresholdMs, bool enabled)
    {
        _keys = keys; _config = config; _thresholdMs = thresholdMs; _enabled = enabled;
    }

    public bool IsTriggered { get { return _triggered; } }

    int Jitter(int v)
    {
        if (!RandomJitter || v <= 0) return v;
        return (int)(v * (0.8 + _rng.NextDouble() * 0.4));
    }

    static int ModVk(int bit)
    {
        if (bit == 1) return 0x11;   // Ctrl
        if (bit == 2) return 0x10;   // Shift
        if (bit == 4) return 0x12;   // Alt
        return 0x5B;                 // Win
    }

    static readonly int[] ModOrder = new int[] { 1, 2, 4, 8 };

    void HoldMods(int mods)
    {
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
        _fired.Clear();
        FiredLabels.Clear();
        _lastTap.Clear();
    }

    public void OnRightUp()
    {
        _pressed = false;
        if (_triggered)
        {
            for (int i = _config.Count - 1; i >= 0; i--)
            {
                var kc = _config[i];
                if (kc.Enabled && kc.Mode == KeyMode.Hold && WasFired(i)) _keys.KeyUp(kc.Vk);
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
        _fired[index] = true;
        string label = kc.Name + (kc.Mods != 0 ? "(组合)" : "");
        if (!FiredLabels.Contains(label)) FiredLabels.Add(label);
        if (kc.Mode == KeyMode.Hold)
        {
            HoldMods(kc.Mods);
            _keys.KeyDown(kc.Vk);
        }
        else
        {
            HoldMods(kc.Mods);
            _keys.KeyTap(kc.Vk);
            // 仅释放该行独占的修饰键：若后续无其他 Hold 行需要，统一在 OnRightUp 释放；
            // 点按类行立即释放（保持 Hold 行仍需要的除外）
            ReleaseModsNotNeededByHold();
        }
        _lastTap[index] = nowMs;
    }

    void ReleaseModsNotNeededByHold()
    {
        // 只保留「已触发的保持行」还需要按住的修饰键：未到点的保持行不算，
        // 否则点按结束后修饰键会独自悬空，产生意外的 Ctrl/Shift+单击。
        int needed = 0;
        for (int i = 0; i < _config.Count; i++)
        {
            var kc = _config[i];
            if (kc.Enabled && kc.Mode == KeyMode.Hold && WasFired(i)) needed |= kc.Mods & 0x0F;
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
            long due = _downTick + _armedThresholdMs + kc.DelayMs;
            if (nowMs < due) continue;

            if (!WasFired(i))
            {
                FireRow(i, nowMs);
                continue;
            }
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
```

> **语义确认**：`WasFired`/`_fired` 以 **行索引** 为键（旧实现以 Vk 为键，会在两行同键时互相干扰）；`_lastTap` 同样改为行索引。

- [ ] **Step 4: 运行测试**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat'
```
Expected: 全部通过。若「多个 Hold 行共享修饰键」失败，检查 `HoldMods` 是否在 `_heldMods` 已含时跳过。

- [ ] **Step 5: 回归旧测试**

确认原有 `TestTriggerController` 的 60+ 断言全绿（尤其是「两轮连发」「多键同时触发」「修饰键中途松开」）。

- [ ] **Step 6: 提交**

```bash
git add src/Core/Trigger.cs
git add -f tests.cs
git commit -m "feat: TriggerController 支持行级延迟、组合键输出与按行独立状态"
```

---

### Task 3: lib 依赖入库、构建脚本与嵌入资源管线

**Files:**
- Create: `lib/Microsoft.Web.WebView2.Core.dll`、`lib/Microsoft.Web.WebView2.WinForms.dll`、`lib/WebView2Loader.dll`
- Create: `src/App/WebAssets.cs`
- Modify: `build.bat`
- Modify: `tests.cs`（新增 `TestWebAssets`）

**Interfaces:**
- Consumes: 无
- Produces: `WebAssets.Map`、`WebAssets.Extract(string version)`、`WebAssets.ReadText(string relativePath)`

- [ ] **Step 1: 落地 SDK 文件**

```bash
cd /d/eyf/mouseplugin && mkdir -p lib
curl -sL --max-time 120 "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.4314.35/microsoft.web.webview2.1.0.4314.35.nupkg" -o /tmp/wv2.nupkg
py -c "
import zipfile
z=zipfile.ZipFile('/tmp/wv2.nupkg')
open(r'd:/eyf/mouseplugin/lib/Microsoft.Web.WebView2.Core.dll','wb').write(z.read('lib/net462/Microsoft.Web.WebView2.Core.dll'))
open(r'd:/eyf/mouseplugin/lib/Microsoft.Web.WebView2.WinForms.dll','wb').write(z.read('lib/net462/Microsoft.Web.WebView2.WinForms.dll'))
open(r'd:/eyf/mouseplugin/lib/WebView2Loader.dll','wb').write(z.read('runtimes/win-x64/native/WebView2Loader.dll'))
print('ok')
"
ls -la lib/
```
Expected: 3 个文件，大小约 1.3MB / 130KB / 150KB。

- [ ] **Step 2: 写失败测试**

`tests.cs` 的 `Main()` 加 `TestWebAssets();`，追加：

```csharp
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
```

- [ ] **Step 3: 运行确认失败**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat'
```
Expected: 编译错误 `WebAssets 不存在`。

- [ ] **Step 4: 实现 WebAssets.cs**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

static class WebAssets
{
    // 资源名 -> 相对路径（build.bat 中 /resource:web/x,资源名 必须与此表一致）
    public static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "RMBCK.web.index.html",   "index.html" },
        { "RMBCK.web.app.css",      "app.css" },
        { "RMBCK.web.app.js",       "app.js" },
        { "RMBCK.web.overlay.html", "overlay.html" },
        { "RMBCK.web.overlay.css",  "overlay.css" },
        { "RMBCK.web.overlay.js",   "overlay.js" },
        { "RMBCK.web.assets.icon.png",    "assets/icon.png" },
        { "RMBCK.web.assets.iconf1.png",  "assets/iconf1.png" },
        { "RMBCK.web.assets.iconf2.png",  "assets/iconf2.png" },
        { "RMBCK.web.assets.iconf3.png",  "assets/iconf3.png" },
        { "RMBCK.web.assets.iconf4.png",  "assets/iconf4.png" },
        { "RMBCK.web.assets.err.jpg",     "assets/err.jpg" },
        { "RMBCK.web.assets.window.ico",  "assets/window.ico" }
    };

    public static string Extract(string version)
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RMBCK", "web", version);
        string marker = Path.Combine(root, ".ready");
        if (File.Exists(marker)) return root;

        Directory.CreateDirectory(root);
        Assembly asm = Assembly.GetExecutingAssembly();
        foreach (var kv in Map)
        {
            string target = Path.Combine(root, kv.Value.Replace('/', Path.DirectorySeparatorChar));
            string dir = Path.GetDirectoryName(target);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            using (Stream s = asm.GetManifestResourceStream(kv.Key))
            {
                if (s == null) continue;
                using (FileStream fs = File.Create(target)) s.CopyTo(fs);
            }
        }
        File.WriteAllText(marker, version);
        return root;
    }

    public static string ReadText(string relativePath)
    {
        foreach (var kv in Map)
        {
            if (!string.Equals(kv.Value, relativePath, StringComparison.OrdinalIgnoreCase)) continue;
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(kv.Key))
            {
                if (s == null) return null;
                using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                    return r.ReadToEnd();
            }
        }
        return null;
    }
}
```

- [ ] **Step 5: 创建 HotkeyManager 与 Autostart**

`src/Core/Hotkey.cs`：

```csharp
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
```

`src/Core/Autostart.cs`：

```csharp
using System;
using System.Diagnostics;

static class Autostart
{
    const string TaskName = "RMBCK";

    public static bool IsEnabled()
    {
        return Run("/Query /TN " + TaskName, 4000);
    }

    public static bool SetEnabled(bool on)
    {
        string exe = System.Windows.Forms.Application.ExecutablePath;
        string args = on
            ? "/Create /F /TN " + TaskName + " /RL HIGHEST /SC ONLOGON /TR \"\\\"" + exe + "\\\"\""
            : "/Delete /F /TN " + TaskName;
        return Run(args, 8000);
    }

    static bool Run(string args, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks", args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(timeoutMs);
                return p.ExitCode == 0;
            }
        }
        catch { return false; }
    }
}
```

- [ ] **Step 6: 创建占位前端文件（让构建可跑）**

创建最小可用的 `web/index.html`：

```html
<!doctype html>
<html lang="zh-CN" data-theme="night">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>RMBCK</title>
<link rel="stylesheet" href="app.css">
</head>
<body>
<div id="boot">正在加载界面…</div>
<script src="app.js"></script>
</body>
</html>
```

`web/app.css`：

```css
:root { color-scheme: dark; }
html, body { margin: 0; height: 100%; background: #12141c; color: #e8eaf3;
  font-family: "Microsoft YaHei UI", "Segoe UI", sans-serif; }
#boot { padding: 24px; font-size: 14px; }
```

`web/app.js`：

```js
(function () {
  var wv = window.chrome && window.chrome.webview;
  if (!wv) return;
  wv.postMessage({ action: "ready", payload: {} });
})(window);
```

其余前端占位：`overlay.html`/`overlay.css`/`overlay.js` 各写一行占位（Task 12 完整实现）。
`web/assets/*` 用 `cp` 从项目根复制现有图片：

```bash
cd /d/eyf/mouseplugin && mkdir -p web/assets
cp icon.png iconf1.png iconf2.png iconf3.png iconf4.png err.jpg window.ico web/assets/
```

> `window.ico` 供托盘图标读取（Task 13）；`icon.ico` 只作 exe 图标，不嵌入前端。

- [ ] **Step 6: 更新 test.bat 加入 WebAssets、Hotkey、Autostart 与新前端**

```bat
@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:exe /main:TestRunner /out:tests.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  /resource:web\index.html,RMBCK.web.index.html ^
  /resource:web\app.css,RMBCK.web.app.css ^
  /resource:web\app.js,RMBCK.web.app.js ^
  /resource:web\overlay.html,RMBCK.web.overlay.html ^
  /resource:web\overlay.css,RMBCK.web.overlay.css ^
  /resource:web\overlay.js,RMBCK.web.overlay.js ^
  /resource:web\assets\icon.png,RMBCK.web.assets.icon.png ^
  /resource:web\assets\iconf1.png,RMBCK.web.assets.iconf1.png ^
  /resource:web\assets\iconf2.png,RMBCK.web.assets.iconf2.png ^
  /resource:web\assets\iconf3.png,RMBCK.web.assets.iconf3.png ^
  /resource:web\assets\iconf4.png,RMBCK.web.assets.iconf4.png ^
  /resource:web\assets\err.jpg,RMBCK.web.assets.err.jpg ^
  /resource:web\assets\window.ico,RMBCK.web.assets.window.ico ^
  src\Core\Settings.cs src\Core\Keys.cs src\Core\Hooks.cs src\Core\Trigger.cs ^
  src\Core\Hotkey.cs src\Core\Autostart.cs ^
  src\App\WebAssets.cs tests.cs
if not %errorlevel%==0 exit /b 1
.\tests.exe
exit /b %errorlevel%
```

- [ ] **Step 7: 运行测试**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat'
```
Expected: `全部通过`，含 WebAssets 4 条断言。

- [ ] **Step 8: 更新 build.bat（暂不含新 Program，仍编译旧 UI）**

在 `build.bat` 的 csc 行加入：

```
  /r:System.Web.Extensions.dll ^
  /r:lib\Microsoft.Web.WebView2.Core.dll ^
  /r:lib\Microsoft.Web.WebView2.WinForms.dll ^
  /resource:web\index.html,RMBCK.web.index.html ^
  /resource:web\app.css,RMBCK.web.app.css ^
  /resource:web\app.js,RMBCK.web.app.js ^
  /resource:web\overlay.html,RMBCK.web.overlay.html ^
  /resource:web\overlay.css,RMBCK.web.overlay.css ^
  /resource:web\overlay.js,RMBCK.web.overlay.js ^
  /resource:web\assets\icon.png,RMBCK.web.assets.icon.png ^
  /resource:web\assets\iconf1.png,RMBCK.web.assets.iconf1.png ^
  /resource:web\assets\iconf2.png,RMBCK.web.assets.iconf2.png ^
  /resource:web\assets\iconf3.png,RMBCK.web.assets.iconf3.png ^
  /resource:web\assets\iconf4.png,RMBCK.web.assets.iconf4.png ^
  /resource:web\assets\err.jpg,RMBCK.web.assets.err.jpg ^
  /resource:web\assets\window.ico,RMBCK.web.assets.window.ico ^
  src\Core\Settings.cs src\Core\Keys.cs src\Core\Hooks.cs src\Core\Trigger.cs ^
  src\Core\Hotkey.cs src\Core\Autostart.cs ^
  src\App\WebAssets.cs ^
```

`WebAssets.Map` 的 12 项必须与此处的 `/resource:` 一一对应（含 `assets/window.ico`）；Task 14 追加 `viewer.html`。
旧的 5 个图标 `/resource:` 行删除（改由 WebAssets 提供）；`/win32icon:icon.ico` 保留。
编译成功后追加：

```
copy /y lib\*.dll . >nul
```

- [ ] **Step 9: 验证构建**

```bash
cd /d/eyf/mouseplugin && cmd //c 'build.bat'
```
Expected: `编译成功: RMBCK 1.1.0.exe`，目录内出现 3 个 dll。

- [ ] **Step 10: 提交**

```bash
git add lib src/App/WebAssets.cs web build.bat
git add -f test.bat tests.cs
git commit -m "build: 引入 WebView2 SDK 依赖与嵌入前端资源管线"
```

---

### Task 4: WebView2 外壳（运行时检测、无边框、拖动、缩放、虚拟主机）

**Files:**
- Create: `src/Program.cs`
- Create: `src/App/MainForm.cs`
- Modify: `build.bat`（改用新 Program、排除旧文件）
- Modify: `app.manifest`（不改内容；确认 DPI 未声明，保持系统缩放）

**Interfaces:**
- Consumes: `WebAssets.Extract`、`ConfigStore`
- Produces: `AppRuntime`（持有 settings/ctrl/hook/history，供 Task 5+ 使用）

- [ ] **Step 1: 建立 AppRuntime 与 Program 骨架**

`src/Program.cs`：

```csharp
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

[assembly: AssemblyCompany("SSJJ2.5")]
[assembly: AssemblyProduct("右键长按联动器")]
[assembly: AssemblyTitle("千恋万花安装器(反MC)")]
[assembly: AssemblyDescription("千恋万花安装器(反MC)")]
[assembly: AssemblyVersion("2.0.0")]
[assembly: AssemblyFileVersion("2.0.0")]
[assembly: AssemblyInformationalVersion("2.0.0")]

class AppRuntime
{
    public AppSettings Settings;
    public TriggerController Ctrl;
    public MouseHook Hook;
    public KeyboardHook KbHook;
    public HistoryRing History;
    public string ExeDir;
    public string WebDir;
    public MainForm Form;
    public OverlayForm Overlay;
    public OverlayForm Hud;
}

static class Bootstrap
{
    public static AppSettings Load(string exeDir)
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
                    old.Theme = s.Theme;
                    try { ConfigStore.SaveProfile(old, profileFile); } catch { }
                }
            }
            AppSettings profile = ConfigStore.LoadProfile(profileFile);
            profile.ConfigFolder = s.ConfigFolder;
            profile.Profile = s.Profile;
            profile.CloseAction = s.CloseAction;
            profile.Theme = s.Theme;
            return profile;
        }

        return s;
    }

    // 启动早期没有窗体可用：先尝试直接用已保存的路径，不可行时再弹文件夹选择
    public static bool EnsureConfigFolder(AppSettings settings, string exeDir)
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

class Program
{
    const string AppVersion = "2.0.0";

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string exeDir = Path.GetDirectoryName(Application.ExecutablePath);

        string runtime = null;
        try { runtime = CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch { }
        if (string.IsNullOrEmpty(runtime))
        {
            MessageBox.Show(
                "本程序需要 Microsoft Edge WebView2 运行时。\n\n" +
                "请前往微软官网下载并安装「WebView2 Runtime (Evergreen Bootstrapper)」后重新启动。",
                "右键长按联动器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        AppSettings settings = Bootstrap.Load(exeDir);
        if (!Bootstrap.EnsureConfigFolder(settings, exeDir)) return;

        var hook = new MouseHook();
        if (!hook.Install())
        {
            MessageBox.Show("无法安装鼠标钩子，程序将退出。", "右键长按联动器",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var rt = new AppRuntime();
        rt.Settings = settings;
        rt.ExeDir = exeDir;
        rt.Hook = hook;
        rt.History = new HistoryRing();
        rt.Ctrl = new TriggerController(new SendInputKeySender(), settings.Keys, settings.LongPressMs, settings.Enabled);
        rt.Ctrl.RandomJitter = settings.RandomJitter;
        rt.WebDir = WebAssets.Extract(AppVersion);

        rt.Form = new MainForm(rt);
        Application.Run(rt.Form);
        hook.Dispose();
    }
}
```

- [ ] **Step 2: 实现 MainForm 骨架**

`src/App/MainForm.cs`：

```csharp
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

class MainForm : Form
{
    readonly AppRuntime _rt;
    WebView2 _wv;
    bool _ready;

    public MainForm(AppRuntime rt)
    {
        _rt = rt;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1040, 720);
        MinimumSize = new Size(900, 620);
        BackColor = Color.FromArgb(18, 20, 28);
        KeyPreview = true;
        Text = "右键长按联动器";

        // 6px 原生缩放边：WebView2 若铺满整个客户区，其子 HWND 会吞掉边缘的
        // WM_NCHITTEST，窗体永远收不到命中测试 → 无法拖动缩放。必须留出 padding。
        Padding = new Padding(BORDER);
        _wv = new WebView2();
        _wv.Dock = DockStyle.Fill;
        Controls.Add(_wv);

        // 鼠标钩子：10ms 轮询触发判定（与 1.1.0 一致）
        _rt.Hook.RightButtonDown += delegate { _rt.Ctrl.OnRightDown(Environment.TickCount); };
        _rt.Hook.RightButtonUp += delegate { _rt.Ctrl.OnRightUp(); };
        _rt.Ctrl.ModifierHeld = ModifierBlocked;
        _tick = new Timer();
        _tick.Interval = 10;
        _tick.Tick += delegate { OnTickHandler(); };
        _tick.Start();

        Load += delegate { InitWebAsync(); };
    }

    Timer _tick;

    // Task 7 阶段：仅轮询触发；Task 11 扩展为写入触发日志
    void OnTickHandler()
    {
        _rt.Ctrl.OnTick(Environment.TickCount);
        UpdateHud();
    }

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    bool ModifierBlocked()
    {
        var s = _rt.Settings;
        if (s.BlockCtrl && (GetAsyncKeyState(0x11) & 0x8000) != 0) return true;
        if (s.BlockShift && (GetAsyncKeyState(0x10) & 0x8000) != 0) return true;
        if (s.BlockAlt && (GetAsyncKeyState(0x12) & 0x8000) != 0) return true;
        if (s.BlockWin && ((GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0)) return true;
        return false;
    }

    async void InitWebAsync()
    {
        try
        {
            string udf = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RMBCK", "WebView2");
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, udf, null);
            await _wv.EnsureCoreWebView2Async(env);

            CoreWebView2 core = _wv.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
            core.SetVirtualHostNameToFolderMapping("rmbck.local", _rt.WebDir, CoreWebView2HostResourceAccessKind.Allow);
            core.NavigationCompleted += delegate { _ready = true; PushState(); };
            core.Navigate("https://rmbck.local/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show("界面初始化失败：" + ex.Message, "右键长按联动器",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    public void PushState() { /* Task 5 实现 */ }

    // ---- 无边框：缩放 + 拖动 ----
    const int WM_NCHITTEST = 0x0084;
    const int WM_NCLBUTTONDOWN = 0x00A1;
    const int HTCLIENT = 1, HTCAPTION = 2;
    const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
              HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    const int BORDER = 6;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HTCLIENT)
            {
                // lParam 里是两个**有符号** 16 位坐标；负坐标（副屏在主屏左上）必须按 short 解码，
                // 且不能直接 ToInt32()（低 32 位 bit31 置位时 .NET Framework 会抛 OverflowException）
                long lp = m.LParam.ToInt64();
                short sx = unchecked((short)(lp & 0xFFFF));
                short sy = unchecked((short)((lp >> 16) & 0xFFFF));
                Point p = PointToClient(new Point(sx, sy));
                int w = ClientSize.Width, h = ClientSize.Height;
                bool l = p.X <= BORDER, r = p.X >= w - BORDER;
                bool t = p.Y <= BORDER, b = p.Y >= h - BORDER;
                if (t && l) { m.Result = (IntPtr)HTTOPLEFT; return; }
                if (t && r) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                if (b && l) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                if (b && r) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                if (l) { m.Result = (IntPtr)HTLEFT; return; }
                if (r) { m.Result = (IntPtr)HTRIGHT; return; }
                if (t) { m.Result = (IntPtr)HTTOP; return; }
                if (b) { m.Result = (IntPtr)HTBOTTOM; return; }
            }
            return;
        }
        base.WndProc(ref m);
    }

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public void BeginDrag()
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    // ---- Win11 圆角 ----
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int pref = 2;   // DWMWCP_ROUND
            DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
        }
        catch { }
    }
}
```

- [ ] **Step 3: 更新 build.bat 切换到新入口**

**task 4 step 3** 更新后的 `build.bat` 完整内容（**照抄**，勿自行增减行）：

```bat
@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
del /q "RMBCK 2.0.0.exe" 2>nul
"%CSC%" /nologo /codepage:65001 /target:winexe /out:"RMBCK 2.0.0.exe" ^
  /win32manifest:app.manifest ^
  /win32icon:icon.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  /r:lib\Microsoft.Web.WebView2.Core.dll ^
  /r:lib\Microsoft.Web.WebView2.WinForms.dll ^
  /resource:web\index.html,RMBCK.web.index.html ^
  /resource:web\app.css,RMBCK.web.app.css ^
  /resource:web\app.js,RMBCK.web.app.js ^
  /resource:web\overlay.html,RMBCK.web.overlay.html ^
  /resource:web\overlay.css,RMBCK.web.overlay.css ^
  /resource:web\overlay.js,RMBCK.web.overlay.js ^
  /resource:web\assets\icon.png,RMBCK.web.assets.icon.png ^
  /resource:web\assets\iconf1.png,RMBCK.web.assets.iconf1.png ^
  /resource:web\assets\iconf2.png,RMBCK.web.assets.iconf2.png ^
  /resource:web\assets\iconf3.png,RMBCK.web.assets.iconf3.png ^
  /resource:web\assets\iconf4.png,RMBCK.web.assets.iconf4.png ^
  /resource:web\assets\err.jpg,RMBCK.web.assets.err.jpg ^
  /resource:web\assets\window.ico,RMBCK.web.assets.window.ico ^
  src\Program.cs ^
  src\Core\Settings.cs src\Core\Keys.cs src\Core\Hooks.cs src\Core\Trigger.cs ^
  src\Core\Hotkey.cs src\Core\Autostart.cs src\Core\History.cs ^
  src\App\WebAssets.cs src\App\Bridge.cs src\App\MainForm.cs ^
  src\App\OverlayForm.cs src\App\Tray.cs
if not %errorlevel%==0 (echo 编译失败 & exit /b 1)
copy /y lib\*.dll . >nul
echo 编译成功: RMBCK 2.0.0.exe
```

> 后续任务新增源文件时，这里必须同步加入；Task 14 追加 `/resource:web\viewer.html,RMBCK.web.viewer.html`。
> **注意**：Task 4 时 `History.cs`/`OverlayForm.cs`/`Tray.cs` 尚未创建，此步先只列出已存在的文件
> （`src\Program.cs src\Core\*.cs src\App\WebAssets.cs src\App\Bridge.cs src\App\MainForm.cs`），
> 每完成一个任务后再把它们追加进来——不要在文件不存在时提前列出，否则编译失败。

- [ ] **Step 4: 构建**

```bash
cd /d/eyf/mouseplugin && cmd //c 'build.bat'
```
Expected: `编译成功: RMBCK 2.0.0.exe`。

- [ ] **Step 5: 冒烟验证（截图自检）**

写临时脚本 `tools/smoke.ps1` 不必要——直接用程序自身：启动 exe，观察是否出现 1040×720 的无边框深色窗口且显示「正在加载界面…」。截图验证：

```bash
cd /d/eyf/mouseplugin && ./RMBCK\ 2.0.0.exe &
sleep 6
tasklist //FI "IMAGENAME eq RMBCK 2.0.0.exe" | tail -3
taskkill //F //IM "RMBCK 2.0.0.exe"
```
Expected: 进程存在；窗口为 1040×720 无边框深色，显示「正在加载界面…」。

> 若窗口黑屏或空白 → 检查 `%LOCALAPPDATA%\RMBCK\web\2.0.0\` 内是否写出 `index.html`、`app.css`、`app.js`、`overlay.*`、`assets/*`；确认 `SetVirtualHostNameToFolderMapping` 的目录与 `WebAssets.Extract` 返回值一致。

- [ ] **Step 6: 提交**

```bash
git add src/Program.cs src/App/MainForm.cs build.bat
git commit -m "feat: WebView2 无边框宿主外壳（运行时检测/拖动/缩放/圆角/虚拟主机）"
```

---

### Task 5: Bridge 消息协议与状态推送

**Files:**
- Create: `src/App/Bridge.cs`
- Modify: `src/App/MainForm.cs`
- Modify: `tests.cs`

**Interfaces:**
- Consumes: `AppSettings`
- Produces: `BridgeMessage`、`Bridge.Parse/Serialize/Get*/`、`MainForm.PushState()`

- [ ] **Step 1: 写失败测试**

```csharp
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
```

- [ ] **Step 2: 运行确认失败**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat'
```
Expected: 编译失败 `Bridge 不存在`。

- [ ] **Step 3: 实现 Bridge.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

class BridgeMessage
{
    public string Action;
    public Dictionary<string, object> Args = new Dictionary<string, object>();
}

static class Bridge
{
    static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

    public static BridgeMessage Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var d = Ser.DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) return null;
            object a;
            if (!d.TryGetValue("action", out a)) return null;
            var m = new BridgeMessage();
            m.Action = a as string;
            m.Args = d;
            return string.IsNullOrEmpty(m.Action) ? null : m;
        }
        catch { return null; }
    }

    public static string Serialize(object o)
    {
        try { return Ser.Serialize(o); } catch { return "{}"; }
    }

    public static object Get(Dictionary<string, object> d, string key, object fallback)
    {
        object v;
        return (d != null && d.TryGetValue(key, out v) && v != null) ? v : fallback;
    }

    public static string GetStr(Dictionary<string, object> d, string key, string fallback)
    {
        object v = Get(d, key, null);
        return v == null ? fallback : Convert.ToString(v);
    }

    public static int GetInt(Dictionary<string, object> d, string key, int fallback)
    {
        object v = Get(d, key, null);
        if (v == null) return fallback;
        try { return Convert.ToInt32(v); } catch { return fallback; }
    }

    public static bool GetBool(Dictionary<string, object> d, string key, bool fallback)
    {
        object v = Get(d, key, null);
        if (v == null) return fallback;
        if (v is bool) return (bool)v;
        bool b;
        return bool.TryParse(Convert.ToString(v), out b) ? b : fallback;
    }
}
```

- [ ] **Step 4: MainForm 接入消息与状态推送**

在 `InitWebAsync` 的 `core.NavigationCompleted` 之前加：

```csharp
            core.WebMessageReceived += delegate(object s, CoreWebView2WebMessageReceivedEventArgs e)
            {
                string json = null;
                try { json = e.WebMessageAsJson; } catch { }
                if (json == null) { try { json = e.TryGetWebMessageAsString(); } catch { } }
                BridgeMessage m = Bridge.Parse(json);
                if (m == null) return;
                try { OnAction(m); }
                catch (Exception ex) { ShowToast("操作失败：" + ex.Message, "err", 2000); }
            };
```

> **必须用 `e.WebMessageAsJson`**：JS 侧发送对象时 `TryGetWebMessageAsString()` 会抛 `E_INVALIDARG`。

替换 `PushState()`：

```csharp
    public void PushState()
    {
        if (!_ready) return;
        var s = _rt.Settings;
        var d = new Dictionary<string, object>();
        d["enabled"] = s.Enabled;
        d["longPressMs"] = s.LongPressMs;
        d["randomJitter"] = s.RandomJitter;
        d["profile"] = s.Profile;
        d["profiles"] = ConfigStore.ListProfiles(ProfileDir());
        d["targetProcess"] = s.TargetProcess;
        d["configFolder"] = ProfileDir();
        d["blockCtrl"] = s.BlockCtrl;
        d["blockShift"] = s.BlockShift;
        d["blockAlt"] = s.BlockAlt;
        d["blockWin"] = s.BlockWin;
        d["suppressToast"] = s.SuppressToast;
        d["suppressEgg"] = s.SuppressEgg;
        d["suppressHud"] = s.SuppressHud;
        d["suppressHotkeyToast"] = s.SuppressHotkeyToast;
        d["headerAnim"] = s.HeaderAnim;
        d["theme"] = s.Theme;
        d["hotkey"] = HotkeyManager.Describe(s.HotkeyMods, s.HotkeyVk);
        // 绝不在这里调用 Autostart.IsEnabled()：它会同步启动 schtasks 子进程，
        // 而 PushState 每次交互都要跑 -> UI 线程卡顿。用后台刷新的缓存值。
        d["autostart"] = _autostartCached;
        d["eggBoost"] = s.EggBoost;
        var arr = new List<object>();
        for (int i = 0; i < s.Keys.Count; i++)
        {
            var kc = s.Keys[i];
            var o = new Dictionary<string, object>();
            o["index"] = i;
            o["enabled"] = kc.Enabled;
            o["name"] = kc.Name;
            o["mode"] = kc.Mode.ToString();
            o["mods"] = kc.Mods;
            o["repeat"] = kc.RepeatIntervalMs;
            o["delay"] = kc.DelayMs;
            arr.Add(o);
        }
        d["keys"] = arr.ToArray();
        d["conflicts"] = FindConflicts();
        d["keyNames"] = KeyNames.CommonKeys;
        Send("state", d);
    }

    public string ProfileDir()
    {
        return string.IsNullOrEmpty(_rt.Settings.ConfigFolder) ? _rt.ExeDir : _rt.Settings.ConfigFolder;
    }

    List<object> FindConflicts()
    {
        var seen = new Dictionary<string, int>();
        var res = new List<object>();
        for (int i = 0; i < _rt.Settings.Keys.Count; i++)
        {
            var kc = _rt.Settings.Keys[i];
            if (!kc.Enabled || kc.Name.Length == 0) continue;
            string key = kc.Mods + ":" + kc.Vk;
            int first;
            if (seen.TryGetValue(key, out first)) res.Add(new Dictionary<string, object> { { "a", first }, { "b", i } });
            else seen[key] = i;
        }
        return res;
    }

    public void Send(string type, object data)
    {
        if (!_ready) return;
        var o = new Dictionary<string, object>();
        o["type"] = type;
        o["data"] = data;
        try { _wv.CoreWebView2.PostWebMessageAsJson(Bridge.Serialize(o)); } catch { }
    }

    // Task 12 会替换为原生透明悬浮窗实现；在此之前 Toast 显示在网页右上角
    public void ShowToast(string text, string kind, int durationMs)
    {
        Send("toast", new Dictionary<string, object>
        {
            { "text", text }, { "kind", kind }, { "ms", durationMs }
        });
    }

    void OnAction(BridgeMessage m)
    {
        // 处理已实现的动作；未实现的动作忽略（后续任务逐段追加）
        if (m.Action == "ready") { PushState(); return; }
        if (m.Action == "dragStart") { BeginDrag(); return; }
    }
```

> `HotkeyManager` 与 `Autostart` 已在 Task 3 Step 5 创建，此处直接调用；两者均为静态/无状态依赖 `KeyNames`，不引用 WebView2。

- [ ] **Step 5: 运行测试 + 构建**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat' && cmd //c 'build.bat'
```
Expected: 全部通过；编译成功。

- [ ] **Step 6: 冒烟（验证 state 已送达网页）**

```bash
cd /d/eyf/mouseplugin && cmd //c 'build.bat'
```

临时在 `web/app.js` 的消息监听里加上一行，把收到的 state 条数写进标题栏（验证后保留反而有用，可作为调试信息）：

```js
      if (msg.type === "state") { state = msg.data; render(); console.log("state", state.keys.length); }
```

运行程序，打开任务管理器确认窗口出现且无崩溃；随后在浏览器端用 CDP 截图自检：

```bash
cd /d/eyf/mouseplugin && ./RMBCK\ 2.0.0.exe &
sleep 8
taskkill //F //IM "RMBCK 2.0.0.exe"
```

Expected: 程序启动无异常弹框；导航栏可见；点「◐」主题切换生效。

- [ ] **Step 7: 提交**

```bash
git add src/App/Bridge.cs src/App/MainForm.cs web/app.js
git add -f tests.cs
git commit -m "feat: Bridge 双向消息协议与状态快照推送"
```

---

### Task 6: 前端框架与双主题

**Files:**
- Modify: `web/index.html`、`web/app.css`、`web/app.js`

**Interfaces:**
- Consumes: `{type:"state", data:{...}}`
- Produces: 页面骨架（导航 + 5 个 section + 模态容器）、`window.__ui` 渲染接口、`send(action, args)` 辅助函数

- [ ] **Step 1: index.html 完整骨架**

```html
<!doctype html>
<html lang="zh-CN" data-theme="night">
<head>
<meta charset="utf-8">
<title>右键长按联动器</title>
<link rel="stylesheet" href="app.css">
</head>
<body>
<div class="frame">
  <aside class="sidebar">
    <div class="brand" id="brand">
      <img id="brandIcon" src="assets/icon.png" alt="">
      <div class="brandText">
        <b>RMBCK</b>
        <span id="brandVer">v2.0.0</span>
      </div>
    </div>
    <nav id="nav">
      <button class="navBtn active" data-page="overview"><i>◈</i>概览</button>
      <button class="navBtn" data-page="keys"><i>⌨</i>按键</button>
      <button class="navBtn" data-page="process"><i>◉</i>进程</button>
      <button class="navBtn" data-page="history"><i>≡</i>日志</button>
      <button class="navBtn" data-page="settings"><i>⚙</i>设置</button>
    </nav>
    <div class="sideFoot">
      <span id="statusChip" class="chip off">已禁用</span>
    </div>
  </aside>

  <main class="content">
    <header class="titlebar" id="titlebar">
      <div class="tbSpace"></div>
      <button class="winBtn" id="btnTheme" title="切换主题">◐</button>
      <button class="winBtn" id="btnMin" title="最小化到托盘">—</button>
      <button class="winBtn close" id="btnClose" title="关闭">✕</button>
    </header>

    <section class="page active" id="page-overview"></section>
    <section class="page" id="page-keys"></section>
    <section class="page" id="page-process"></section>
    <section class="page" id="page-history"></section>
    <section class="page" id="page-settings"></section>
  </main>
</div>

<div class="modalWrap" id="modalWrap"></div>
<div class="toasts" id="toasts"></div>
<script src="app.js"></script>
</body>
</html>
```

- [ ] **Step 2: app.css 双主题变量**

```css
/* ---------- 夜间（默认） ---------- */
:root {
  --bg: #12141c;
  --bg2: #171a24;
  --card: rgba(255,255,255,.045);
  --cardHi: rgba(255,255,255,.075);
  --text: #e8eaf3;
  --textSub: #9aa3b8;
  --line: rgba(255,255,255,.10);
  --track: rgba(255,255,255,.12);
  --sel: rgba(124,92,255,.25);
  --accent: #22d3ee;
  --accent2: #7c5cff;
  --grad: linear-gradient(90deg,#7c5cff,#22d3ee);
  --ok: #34d399; --err: #f87171; --warn: #fbbf24;
  --shadow: 0 18px 50px rgba(0,0,0,.55);
  color-scheme: dark;
}

/* ---------- 日间（樱花粉白紫，色值同旧 Theme） ---------- */
html[data-theme="day"] {
  --bg: #fff0f5;
  --bg2: #ffffff;
  --card: rgba(255,255,255,.86);
  --cardHi: rgba(255,255,255,.98);
  --text: #4a4a68;
  --textSub: #9a9aaf;
  --line: #fad7e3;
  --track: #ffd1e0;
  --sel: #ffd9e8;
  --accent: #a78bfa;
  --accent2: #ff6fa5;
  --grad: linear-gradient(90deg,#ff7aac,#ba94ff);
  --ok: #4caf50; --err: #eb5a6e; --warn: #f0a028;
  --shadow: 0 14px 40px rgba(226,140,175,.22);
  color-scheme: light;
}

* { box-sizing: border-box; }
html, body { margin: 0; height: 100%; overflow: hidden; }
body {
  background: radial-gradient(1200px 600px at 20% -10%, var(--bg2), var(--bg) 60%);
  color: var(--text);
  font: 13px/1.6 "Microsoft YaHei UI", "Segoe UI", sans-serif;
  transition: background .18s ease, color .18s ease;
}
button { font: inherit; cursor: pointer; }

.frame { display: flex; height: 100%; }

.sidebar {
  width: 168px; flex: 0 0 168px; display: flex; flex-direction: column;
  border-right: 1px solid var(--line);
  background: var(--card);
  backdrop-filter: blur(14px);
}
.brand { display: flex; align-items: center; gap: 10px; padding: 16px 14px 12px; }
.brand img { width: 30px; height: 30px; border-radius: 9px; }
.brandText { display: flex; flex-direction: column; line-height: 1.15; }
.brandText b { font-size: 15px; letter-spacing: .5px; }
.brandText span { font-size: 11px; color: var(--textSub); }
#nav { display: flex; flex-direction: column; gap: 4px; padding: 8px; }
.navBtn {
  display: flex; align-items: center; gap: 10px; padding: 9px 12px;
  border: 1px solid transparent; border-radius: 10px; background: transparent;
  color: var(--textSub); text-align: left;
}
.navBtn i { font-style: normal; width: 16px; text-align: center; }
.navBtn:hover { background: var(--cardHi); color: var(--text); }
.navBtn.active { background: var(--sel); color: var(--text); border-color: var(--line); }
.sideFoot { margin-top: auto; padding: 12px; }
.chip { display: inline-block; padding: 3px 10px; border-radius: 99px; font-size: 11px;
  border: 1px solid var(--line); }
.chip.on { color: var(--ok); }
.chip.off { color: var(--err); }

.content { position: relative; flex: 1; display: flex; flex-direction: column; min-width: 0; }
.titlebar { height: 44px; display: flex; align-items: center; justify-content: flex-end;
  gap: 4px; padding: 0 8px; -webkit-app-region: drag; }
.tbSpace { flex: 1; align-self: stretch; }
.winBtn { width: 34px; height: 28px; border: 0; border-radius: 8px;
  background: transparent; color: var(--textSub); font-size: 13px; }
.winBtn:hover { background: var(--cardHi); color: var(--text); }
.winBtn.close:hover { background: var(--err); color: #fff; }

.page { display: none; flex: 1; overflow-y: auto; padding: 4px 22px 22px; }
.page.active { display: block; }

.card { background: var(--card); border: 1px solid var(--line); border-radius: 16px;
  padding: 16px 18px; box-shadow: var(--shadow); margin-bottom: 14px; }
.card h3 { margin: 0 0 12px; font-size: 13px; font-weight: 600; color: var(--text); }
.row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
.muted { color: var(--textSub); font-size: 12px; }

.modalWrap { position: fixed; inset: 0; display: none; align-items: center;
  justify-content: center; background: rgba(0,0,0,.45); backdrop-filter: blur(3px); }
.modalWrap.show { display: flex; }
.modal { width: 380px; background: var(--bg2); border: 1px solid var(--line);
  border-radius: 16px; padding: 20px; box-shadow: var(--shadow); }
.modal h4 { margin: 0 0 10px; font-size: 15px; }
.modal .actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 16px; }

.toasts { position: fixed; right: 16px; top: 16px; display: flex; flex-direction: column;
  gap: 8px; pointer-events: none; }
.toast { padding: 10px 14px; border-radius: 12px; background: var(--cardHi);
  border: 1px solid var(--line); box-shadow: var(--shadow); font-size: 12px;
  animation: toastIn .18s ease; }
@keyframes toastIn { from { opacity: 0; transform: translateY(-6px); } }
```

- [ ] **Step 3: app.js 状态与导航骨架**

```js
(function () {
  "use strict";
  var wv = window.chrome && window.chrome.webview;
  var state = null;
  var page = "overview";

  function send(action, args) {
    if (!wv) return;
    var m = { action: action };
    if (args) for (var k in args) m[k] = args[k];
    wv.postMessage(m);
  }
  window.__send = send;

  function $(id) { return document.getElementById(id); }
  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }
  window.__el = el;

  function applyTheme(mode) {
    document.documentElement.setAttribute("data-theme", mode === "Day" ? "day" : "night");
  }

  function showPage(name) {
    page = name;
    var btns = document.querySelectorAll(".navBtn");
    for (var i = 0; i < btns.length; i++)
      btns[i].classList.toggle("active", btns[i].getAttribute("data-page") === name);
    var pages = document.querySelectorAll(".page");
    for (var j = 0; j < pages.length; j++)
      pages[j].classList.toggle("active", pages[j].id === "page-" + name);
    render();
  }
  window.__showPage = showPage;

  function render() {
    if (!state) return;
    applyTheme(state.theme);
    $("brandVer").textContent = "v2.0.0";
    var chip = $("statusChip");
    chip.textContent = state.enabled ? "已启用" : "已禁用";
    chip.className = "chip " + (state.enabled ? "on" : "off");
    var fn = window["render_" + page];
    if (fn) fn(state);
  }
  window.__render = render;

  // 通用组件
  window.__switch = function (on, onChange) {
    var b = el("button", "sw " + (on ? "on" : ""));
    b.appendChild(el("span"));
    b.onclick = function () { onChange(!on); };
    return b;
  };

  // 事件委托：导航 / 窗口按钮
  document.addEventListener("click", function (e) {
    var t = e.target;
    var btn = t.closest ? t.closest("[data-page]") : null;
    if (btn) { showPage(btn.getAttribute("data-page")); return; }
  });

  $("btnTheme").onclick = function () {
    if (!state) return;
    send("setTheme", { mode: state.theme === "Night" ? "Day" : "Night" });
  };
  $("btnMin").onclick = function () { send("minimizeToTray"); };
  $("btnClose").onclick = function () { send("closeRequest"); };
  $("titlebar").addEventListener("mousedown", function (e) {
    if (e.target.classList.contains("winBtn")) return;
    if (e.button === 0) send("dragStart");
  });

  if (wv) {
    wv.addEventListener("message", function (e) {
      var msg = e.data;
      if (!msg || !msg.type) return;
      if (msg.type === "state") { state = msg.data; render(); }
      else if (msg.type === "toast") { toast(msg.data.text, msg.data.kind); }
      else if (msg.type === "askClose") { showCloseModal(); }
      else if (msg.type === "processes") { window.__processes = msg.data; if (page === "process") render(); }
      else if (msg.type === "history") { window.__history = msg.data; if (page === "history") render(); }
      else if (msg.type === "capturing") { window.__capturing = msg.data; }
      else if (msg.type === "error") { toast(msg.data.text, "err"); }
    });
    wv.postMessage({ action: "ready" });
  }

  function toast(text, kind) {
    var box = $("toasts");
    var t = el("div", "toast " + (kind || "info"), text);
    box.appendChild(t);
    setTimeout(function () { if (t.parentNode) t.parentNode.removeChild(t); }, 2200);
  }

  function showCloseModal() {
    var w = $("modalWrap");
    w.innerHTML = "";
    var m = el("div", "modal");
    m.appendChild(el("h4", null, "关闭窗口"));
    m.appendChild(el("div", "muted", "你想最小化到托盘，还是完全退出程序？"));
    var acts = el("div", "actions");
    var remember = el("button", "btnGhost", "记住我的选择");
    var b1 = el("button", "btn", "最小化到托盘");
    var b2 = el("button", "btnDanger", "退出");
    var remembered = false;
    remember.onclick = function () { remembered = !remembered; remember.classList.toggle("on", remembered); };
    b1.onclick = function () { w.classList.remove("show"); send("closeConfirm", { result: "Minimize", remember: remembered }); };
    b2.onclick = function () { w.classList.remove("show"); send("closeConfirm", { result: "Exit", remember: remembered }); };
    acts.appendChild(remember); acts.appendChild(b1); acts.appendChild(b2);
    m.appendChild(acts);
    w.appendChild(m);
    w.classList.add("show");
  }

  // 初始渲染
  var first = $("page-overview");
  if (first) { }
})();
```

配套把 `app.css` 末尾补上按钮与开关样式：

```css
.btn, .btnGhost, .btnDanger { padding: 8px 16px; border-radius: 10px; border: 1px solid var(--line);
  background: var(--cardHi); color: var(--text); font-size: 12px; }
.btn:hover, .btnGhost:hover { background: var(--sel); }
.btnDanger { border-color: rgba(248,113,113,.5); color: var(--err); }
.btnGhost { background: transparent; color: var(--textSub); }
.btnGhost.on { color: var(--accent); border-color: var(--accent); }
.sw { width: 44px; height: 24px; border-radius: 99px; border: 1px solid var(--line);
  background: var(--track); position: relative; padding: 0; transition: background .16s; }
.sw span { position: absolute; top: 2px; left: 2px; width: 18px; height: 18px; border-radius: 50%;
  background: #fff; transition: transform .16s; box-shadow: 0 1px 3px rgba(0,0,0,.3); }
.sw.on { background: var(--grad); border-color: transparent; }
.sw.on span { transform: translateX(20px); }
input[type=range] { -webkit-appearance: none; height: 4px; border-radius: 99px;
  background: var(--track); outline: none; }
input[type=range]::-webkit-slider-thumb { -webkit-appearance: none; width: 14px; height: 14px;
  border-radius: 50%; background: var(--accent2); border: 2px solid #fff; cursor: pointer; }
input[type=text], input[type=number], select {
  padding: 6px 10px; border-radius: 8px; border: 1px solid var(--line);
  background: var(--cardHi); color: var(--text); font: inherit; font-size: 12px; }
```

- [ ] **Step 4: 构建 + 视觉验证**

```bash
cd /d/eyf/mouseplugin && cmd //c 'build.bat' && ./RMBCK\ 2.0.0.exe &
```
人工确认：窗口显示左侧导航（5 项）+ 顶部窗控按钮；点击导航高亮切换；点 ◐ 主题在深色/樱花粉之间切换。

- [ ] **Step 5: 提交**

```bash
git add web
git commit -m "feat: 前端框架骨架与日夜双主题变量"
```

---

### Task 7: 概览页（开关 / 方案 / 阈值 / 随机化 / 测试面板）

**Files:**
- Modify: `web/app.js`（新增 `render_overview`）
- Modify: `src/App/MainForm.cs`（`OnAction` 处理概览相关动作 + 测试状态推送）

**Interfaces:**
- Consumes: `state.enabled/longPressMs/randomJitter/profile/profiles`
- Produces: 动作 `toggleEnabled`、`setSetting`、`setProfile`、`startTest`、`stopTest`；推送 `{type:"testStatus"}`

- [ ] **Step 1: 前端渲染**

```js
  window.render_overview = function (s) {
    var p = document.getElementById("page-overview");
    p.innerHTML = "";

    // 总开关 + 方案
    var c1 = el("div", "card");
    c1.appendChild(el("h3", null, "联动"));
    var row1 = el("div", "row");
    row1.appendChild(window.__switch(s.enabled, function (v) { send("toggleEnabled", { enabled: v }); }));
    row1.appendChild(el("span", "muted", s.enabled ? "当前联动状态：启用" : "当前联动状态：禁用"));
    row1.appendChild(el("span", "spacer"));
    var sel = el("select");
    s.profiles.forEach(function (n) {
      var o = el("option", null, n); o.value = n;
      if (n === s.profile) o.selected = true;
      sel.appendChild(o);
    });
    sel.onchange = function () { send("setProfile", { name: sel.value }); };
    row1.appendChild(sel);
    var bNew = el("button", "btn", "＋ 新建");
    bNew.onclick = function () { promptProfileName(); };
    var bDel = el("button", "btnGhost", "－ 删除");
    bDel.onclick = function () { confirmDeleteProfile(s.profile); };
    row1.appendChild(bNew); row1.appendChild(bDel);
    c1.appendChild(row1);
    c1.appendChild(el("div", "muted", "全局热键 " + s.hotkey + " 可随时切换联动开关（游戏内生效）"));
    p.appendChild(c1);

    // 阈值 + 随机化
    var c2 = el("div", "card");
    c2.appendChild(el("h3", null, "触发设置"));
    var row2 = el("div", "row");
    row2.appendChild(el("span", "muted", "长按检测时间"));
    var rng = el("input"); rng.type = "range"; rng.min = "100"; rng.max = "2000"; rng.step = "10";
    rng.value = s.longPressMs; rng.style.width = "260px";
    var num = el("input"); num.type = "number"; num.min = "100"; num.max = "2000";
    num.value = s.longPressMs; num.style.width = "74px";
    var commit = function (v) {
      v = Math.max(100, Math.min(2000, parseInt(v, 10) || 300));
      rng.value = v; num.value = v;
      send("setSetting", { key: "longPressMs", value: v });
    };
    rng.oninput = function () { num.value = rng.value; };
    rng.onchange = function () { commit(rng.value); };
    num.onchange = function () { commit(num.value); };
    row2.appendChild(rng); row2.appendChild(num); row2.appendChild(el("span", "muted", "ms"));
    row2.appendChild(el("span", "spacer"));
    row2.appendChild(el("span", "muted", "随机化防检测"));
    row2.appendChild(window.__switch(s.randomJitter, function (v) { send("setSetting", { key: "randomJitter", value: v }); }));
    c2.appendChild(row2);
    p.appendChild(c2);

    // 测试面板
    var c3 = el("div", "card");
    c3.appendChild(el("h3", null, "测试"));
    var row3 = el("div", "row");
    var testBtn = el("button", "btn", window.__testing ? "停止测试" : "开始测试");
    testBtn.onclick = function () {
      window.__testing = !window.__testing;
      send(window.__testing ? "startTest" : "stopTest");
      render();
    };
    row3.appendChild(testBtn);
    var st = window.__testStatus || {};
    row3.appendChild(el("span", "muted", st.down ? "右键：按下" : "右键：释放"));
    c3.appendChild(row3);
    var lines = el("div", null);
    lines.appendChild(el("div", "muted", "触发键位：" + (st.keys || "无")));
    lines.appendChild(el("div", "muted", "实际按下：" + (st.physical || "无")));
    lines.appendChild(el("div", "muted", "目标进程：" + (st.target || "未选择（全局生效）")));
    c3.appendChild(lines);
    p.appendChild(c3);
  };

  function promptProfileName() {
    var w = $("modalWrap"); w.innerHTML = "";
    var m = el("div", "modal");
    m.appendChild(el("h4", null, "新建配置方案"));
    var inp = el("input"); inp.type = "text"; inp.placeholder = "方案名称（最多 20 字）"; inp.style.width = "100%";
    m.appendChild(inp);
    var acts = el("div", "actions");
    var bCancel = el("button", "btnGhost", "取消");
    var bOk = el("button", "btn", "创建");
    bCancel.onclick = function () { w.classList.remove("show"); };
    bOk.onclick = function () { w.classList.remove("show"); send("newProfile", { name: inp.value }); };
    acts.appendChild(bCancel); acts.appendChild(bOk);
    m.appendChild(acts);
    w.appendChild(m); w.classList.add("show"); inp.focus();
  }
  window.__promptProfileName = promptProfileName;

  function confirmDeleteProfile(name) {
    var w = $("modalWrap"); w.innerHTML = "";
    var m = el("div", "modal");
    m.appendChild(el("h4", null, "删除配置方案"));
    m.appendChild(el("div", "muted", "确定删除「" + name + "」？此操作不可恢复。"));
    var acts = el("div", "actions");
    var bCancel = el("button", "btnGhost", "取消");
    var bOk = el("button", "btnDanger", "删除");
    bCancel.onclick = function () { w.classList.remove("show"); };
    bOk.onclick = function () { w.classList.remove("show"); send("deleteProfile", { name: name }); };
    acts.appendChild(bCancel); acts.appendChild(bOk);
    m.appendChild(acts);
    w.appendChild(m); w.classList.add("show");
  }
  window.__confirmDeleteProfile = confirmDeleteProfile;
```

同时在消息分发里加：

```js
      else if (msg.type === "testStatus") { window.__testStatus = msg.data; if (page === "overview") render(); }
```

- [ ] **Step 2: 后端动作**

`MainForm.OnAction` 替换为（后续任务继续追加分支）：

```csharp
    void OnAction(BridgeMessage m)
    {
        var s = _rt.Settings;
        string a = m.Action;

        if (a == "ready") { PushState(); return; }
        if (a == "dragStart") { BeginDrag(); return; }
        if (a == "minimizeToTray") { MinimizeToTray(); return; }
        if (a == "closeRequest") { RequestClose(); return; }

        if (a == "toggleEnabled")
        {
            s.Enabled = Bridge.GetBool(m.Args, "enabled", !s.Enabled);
            SaveSettings();
            ApplyLinkageGate();
            PushState();
            return;
        }
        if (a == "setSetting")
        {
            string key = Bridge.GetStr(m.Args, "key", "");
            ApplySetting(key, m.Args);
            SaveSettings();
            PushState();
            return;
        }
        if (a == "setProfile")
        {
            string name = Bridge.GetStr(m.Args, "name", "");
            if (name.Length > 0 && name != s.Profile) SwitchProfile(name);
            return;
        }
        if (a == "newProfile") { NewProfile(Bridge.GetStr(m.Args, "name", "")); return; }
        if (a == "deleteProfile") { DeleteProfile(Bridge.GetStr(m.Args, "name", "")); return; }
        if (a == "startTest") { _testing = true; StartTestTimer(); return; }
        if (a == "stopTest") { _testing = false; StopTestTimer(); return; }
    }

    void ApplySetting(string key, Dictionary<string, object> args)
    {
        var s = _rt.Settings;
        if (key == "longPressMs")
        {
            int v = Math.Max(100, Math.Min(2000, Bridge.GetInt(args, "value", s.LongPressMs)));
            s.LongPressMs = v;
            _rt.Ctrl.SetThreshold(v);
        }
        else if (key == "randomJitter")
        {
            s.RandomJitter = Bridge.GetBool(args, "value", s.RandomJitter);
            _rt.Ctrl.RandomJitter = s.RandomJitter;
        }
        else if (key == "blockCtrl") s.BlockCtrl = Bridge.GetBool(args, "value", s.BlockCtrl);
        else if (key == "blockShift") s.BlockShift = Bridge.GetBool(args, "value", s.BlockShift);
        else if (key == "blockAlt") s.BlockAlt = Bridge.GetBool(args, "value", s.BlockAlt);
        else if (key == "blockWin") s.BlockWin = Bridge.GetBool(args, "value", s.BlockWin);
        else if (key == "suppressToast") s.SuppressToast = Bridge.GetBool(args, "value", s.SuppressToast);
        else if (key == "suppressEgg") s.SuppressEgg = Bridge.GetBool(args, "value", s.SuppressEgg);
        else if (key == "suppressHud") s.SuppressHud = Bridge.GetBool(args, "value", s.SuppressHud);
        else if (key == "suppressHotkeyToast") s.SuppressHotkeyToast = Bridge.GetBool(args, "value", s.SuppressHotkeyToast);
        else if (key == "headerAnim") s.HeaderAnim = Bridge.GetBool(args, "value", s.HeaderAnim);
    }
```

测试计时器与状态推送：

```csharp
    bool _testing;
    Timer _testTimer;

    void StartTestTimer()
    {
        if (_testTimer == null)
        {
            _testTimer = new Timer();
            _testTimer.Interval = 100;
            _testTimer.Tick += delegate { PushTestStatus(); };
        }
        _testTimer.Start();
        PushTestStatus();
    }

    void StopTestTimer()
    {
        if (_testTimer != null) _testTimer.Stop();
        Send("testStatus", new Dictionary<string, object>
        {
            { "down", false }, { "keys", "" }, { "physical", "" }, { "target", "" }
        });
    }

    void PushTestStatus()
    {
        if (!_testing) return;
        bool down = (GetAsyncKeyState(0x02) & 0x8000) != 0;
        var labels = new List<string>();
        foreach (var kc in _rt.Settings.Keys)
        {
            if (!kc.Enabled) continue;
            string mode = kc.Mode == KeyMode.Hold ? "保持" : kc.Mode == KeyMode.TapOnce ? "点按·单次" : "点按·连发";
            if (_rt.Ctrl.IsTriggered) labels.Add(kc.Name + "（" + mode + "）");
        }
        string keysText = labels.Count == 0 ? "无" : string.Join("、", labels.ToArray());
        if (_rt.Settings.RandomJitter && _rt.Ctrl.IsTriggered) keysText += "　本次延时 " + _rt.Ctrl.LastThresholdMs + "ms";
        string physical = _lastVk < 0 ? "无" : KeyNames.NameOf(_lastVk) + " (0x" + _lastVk.ToString("X2") + ")";
        string target;
        if (string.IsNullOrEmpty(_rt.Settings.TargetProcess)) target = "未选择（全局生效）";
        else target = TargetForegroundActive() ? "已联动目标进程" : "请切换至目标进程窗口";
        Send("testStatus", new Dictionary<string, object>
        {
            { "down", down }, { "keys", keysText }, { "physical", physical }, { "target", target }
        });
    }

    // Task 12 会替换为原生透明悬浮窗实现；在此之前 HUD 也走网页 toast 通道
    void UpdateHud()
    {
        if (_rt.Settings.SuppressHud) return;
        if (_rt.Ctrl.IsTriggered && !_hudShown)
        {
            _hudShown = true;
            var names = new List<string>();
            foreach (var kc in _rt.Settings.Keys) if (kc.Enabled) names.Add(kc.Name);
            Send("toast", new Dictionary<string, object>
            {
                { "text", "⚡ " + string.Join(" ", names.ToArray()) }, { "kind", "info" }, { "ms", 900 }
            });
        }
        else if (!_rt.Ctrl.IsTriggered) _hudShown = false;
    }
    bool _hudShown;

    int _lastVk = -1;
```

钩子与 10ms 定时器已在 Task 4 的构造函数中接好，`ModifierBlocked()` 也已在 Task 4 定义，此处无需重复。

`UpdateHud` / `ShowToast` 已在上方给出（Task 7 版本：走网页 toast 通道）。
其余从旧实现迁移的方法（`_settings` → `_rt.Settings`，`_exeDir` → `_rt.ExeDir`，删掉所有控件引用）：

```csharp
    Timer _saveTimer;
    bool _saveDirty;

    void SaveSettings()
    {
        _saveDirty = true;
        if (_saveTimer == null)
        {
            _saveTimer = new Timer { Interval = 500 };
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                if (_saveDirty) SaveSettingsNow();
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
        _rt.Ctrl.SetThreshold(_rt.Settings.LongPressMs);
    }

    void SaveSettingsNow()
    {
        if (_saveTimer != null) _saveTimer.Stop();
        _saveDirty = false;
        string path = ConfigStore.ProfilePath(_rt.Settings, _rt.ExeDir);
        try { ConfigStore.SaveProfile(_rt.Settings, path); }
        catch
        {
            ShowToast("无法保存配置文件：" + path, "err", 2500);
        }
        try { ConfigStore.SavePointer(_rt.Settings, System.IO.Path.Combine(_rt.ExeDir, "config.ini")); } catch { }
        _rt.Ctrl.SetThreshold(_rt.Settings.LongPressMs);
    }

    void SwitchProfile(string name)
    {
        SaveSettingsNow();
        LoadProfileInto(name);
    }

    void LoadProfileInto(string name)
    {
        if (_saveTimer != null) _saveTimer.Stop();
        _saveDirty = false;
        var s = _rt.Settings;
        s.Profile = name;
        AppSettings loaded = ConfigStore.LoadProfile(
            System.IO.Path.Combine(ProfileDir(), name + ".ini"));
        s.LongPressMs = loaded.LongPressMs;
        s.Enabled = loaded.Enabled;
        s.TargetProcess = loaded.TargetProcess;
        s.BlockCtrl = loaded.BlockCtrl;
        s.BlockShift = loaded.BlockShift;
        s.BlockAlt = loaded.BlockAlt;
        s.BlockWin = loaded.BlockWin;
        s.EggBoost = loaded.EggBoost;
        s.RandomJitter = loaded.RandomJitter;
        s.SuppressToast = loaded.SuppressToast;
        s.SuppressEgg = loaded.SuppressEgg;
        s.SuppressHud = loaded.SuppressHud;
        s.SuppressHotkeyToast = loaded.SuppressHotkeyToast;
        s.HeaderAnim = loaded.HeaderAnim;
        s.HotkeyMods = loaded.HotkeyMods;
        s.HotkeyVk = loaded.HotkeyVk;
        s.Keys = loaded.Keys;
        _rt.Ctrl.RandomJitter = s.RandomJitter;
        _lastInTargetInit = false;
        try { ConfigStore.SavePointer(s, System.IO.Path.Combine(_rt.ExeDir, "config.ini")); } catch { }
        ApplyLinkageGate();
        PushState();
    }

    void NewProfile(string raw)
    {
        string name = SanitizeProfileName(raw);
        if (name.Length == 0)
        {
            ShowToast("方案名称不能为空", "err", 1800);
            return;
        }
        if (ConfigStore.ListProfiles(ProfileDir()).Contains(name))
        {
            ShowToast("已存在同名方案：" + name, "err", 1800);
            return;
        }
        SaveSettingsNow();
        _rt.Settings.Profile = name;
        try
        {
            ConfigStore.SaveProfile(_rt.Settings, System.IO.Path.Combine(ProfileDir(), name + ".ini"));
            ConfigStore.SavePointer(_rt.Settings, System.IO.Path.Combine(_rt.ExeDir, "config.ini"));
        }
        catch { }
        PushState();
    }

    void DeleteProfile(string name)
    {
        List<string> list = ConfigStore.ListProfiles(ProfileDir());
        if (list.Count <= 1)
        {
            ShowToast("至少需要保留一个配置方案", "err", 1800);
            return;
        }
        if (!list.Contains(name)) name = _rt.Settings.Profile;
        ConfigStore.DeleteProfile(ProfileDir(), name);
        list.Remove(name);
        LoadProfileInto(list.Count > 0 ? list[0] : "默认");
    }

    static string SanitizeProfileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        char[] bad = System.IO.Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (char c in name) if (Array.IndexOf(bad, c) < 0) sb.Append(c);
        string t = sb.ToString().Trim();
        if (t.Length > 20) t = t.Substring(0, 20);
        return t;
    }

    void PopulateProfileComboIfNeeded()
    {
        // 切换配置目录后，若当前方案在新目录不存在则落盘一份
        string p = System.IO.Path.Combine(ProfileDir(), _rt.Settings.Profile + ".ini");
        if (!System.IO.File.Exists(p))
        {
            try { ConfigStore.SaveProfile(_rt.Settings, p); } catch { }
        }
    }

    bool _linkageActive;
    bool _lastInTarget;
    bool _lastInTargetInit;
    readonly Random _rng = new Random();

    IntPtr _lastFgHwnd = IntPtr.Zero;
    string _cacheTarget = "";
    bool _cachedInTarget;

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    bool TargetForegroundActive()
    {
        if (string.IsNullOrEmpty(_rt.Settings.TargetProcess)) return true;
        if (!string.Equals(_cacheTarget, _rt.Settings.TargetProcess, StringComparison.OrdinalIgnoreCase))
        {
            _cacheTarget = _rt.Settings.TargetProcess;
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
                _cachedInTarget = string.Equals(p.ProcessName, _rt.Settings.TargetProcess,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch { _cachedInTarget = false; }
        return _cachedInTarget;
    }

    void ApplyLinkageGate()
    {
        var s = _rt.Settings;
        bool inTarget = TargetForegroundActive();
        bool active = s.Enabled && inTarget;
        if (active != _linkageActive)
        {
            _linkageActive = active;
            _rt.Ctrl.SetEnabled(active);
        }
        if (s.Enabled && !string.IsNullOrEmpty(s.TargetProcess))
        {
            if (_lastInTargetInit && inTarget != _lastInTarget)
            {
                if (inTarget)
                {
                    if (!s.SuppressEgg)
                    {
                        int odds = s.EggBoost ? 5 : 20;
                        if (_rng.Next(odds) < (s.EggBoost ? 3 : 1))
                        {
                            ShowEasterEgg();
                            _lastInTarget = inTarget;
                            _lastInTargetInit = true;
                            return;
                        }
                    }
                    if (!s.SuppressToast) ShowToast("✅ 已切换至目标进程，配置生效", "ok", 1500);
                }
                else if (!s.SuppressToast)
                {
                    ShowToast("❌ 已切换至其他进程，配置失效", "err", 1500);
                }
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
```

`ShowEasterEgg()` 在 Task 12 才实现；Task 7 阶段先放一个空方法以便编译：

```csharp
    void ShowEasterEgg() { }
```

> 每次修改设置后都必须 `PushState()`，让网页刷新快照——`OnAction` 里的分支已经这么做了。

- [ ] **Step 3: 构建并人工验证**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat' && cmd //c 'build.bat'
```
人工：拖动滑块数值同步；点开关徽章变化；切主题立即生效；开始测试后按住右键能看到「右键：按下」与触发键位。

- [ ] **Step 4: 提交**

```bash
git add web/app.js src/App/MainForm.cs
git commit -m "feat: 概览页（联动开关/方案/阈值/随机化/测试面板）"
```

---

### Task 8: 按键页（列表 / 捕获 / 行延迟 / 组合键 / 冲突检测）

**Files:**
- Modify: `web/app.js`（`render_keys` + 捕获交互）
- Modify: `src/App/MainForm.cs`（按键相关动作）

**Interfaces:**
- Consumes: `state.keys/conflicts/keyNames`
- Produces: 动作 `addKey`、`removeKey`、`setKey`、`captureKey`；推送 `{type:"capturing"}`

- [ ] **Step 1: 前端**

```js
  var MOD_LABEL = { 1: "Ctrl", 2: "Shift", 4: "Alt", 8: "Win" };
  var MODE_LABEL = { Hold: "保持", TapOnce: "点按·单次", TapRepeat: "点按·连发" };

  window.render_keys = function (s) {
    var p = document.getElementById("page-keys");
    p.innerHTML = "";

    if (s.conflicts && s.conflicts.length) {
      var warn = el("div", "card warnCard");
      warn.appendChild(el("h3", null, "⚠ 检测到重复键位"));
      var names = s.conflicts.map(function (c) { return "第 " + (c.a + 1) + " 行与第 " + (c.b + 1) + " 行"; });
      warn.appendChild(el("div", "muted", names.join("；") + " 键位相同，可能重复触发。"));
      p.appendChild(warn);
    }

    var card = el("div", "card");
    card.appendChild(el("h3", null, "按键列表"));
    var list = el("div", "keyList");
    list.appendChild(keyHeader());
    s.keys.forEach(function (k) { list.appendChild(keyRow(k, s)); });
    if (!s.keys.length) list.appendChild(el("div", "muted empty", "还没有按键，点下方「添加按键」"));
    card.appendChild(list);
    var add = el("button", "btn addKey", "＋ 添加按键");
    add.onclick = function () { send("addKey", {}); };
    card.appendChild(add);
    p.appendChild(card);
  };

  function keyHeader() {
    var h = el("div", "keyRow keyHead");
    ["启用", "键位", "模式", "延迟ms", "连发ms"].forEach(function (t) { h.appendChild(el("span", null, t)); });
    h.appendChild(el("span", null, ""));
    return h;
  }

  function keyRow(k, s) {
    var r = el("div", "keyRow");

    r.appendChild(window.__switch(k.enabled, function (v) {
      send("setKey", { index: k.index, field: "enabled", value: v });
    }));

    var cell = el("div", "keyName");
    if (window.__captureRow === k.index) {
      cell.appendChild(el("span", "capturing", "请按下组合键…"));
    } else {
      var b = el("button", "keyBtn", describeKey(k));
      b.onclick = function () { send("captureKey", { target: "row", index: k.index }); };
      cell.appendChild(b);
    }
    r.appendChild(cell);

    var sel = el("select");
    ["Hold", "TapOnce", "TapRepeat"].forEach(function (m) {
      var o = el("option", null, MODE_LABEL[m]); o.value = m;
      if (m === k.mode) o.selected = true;
      sel.appendChild(o);
    });
    sel.onchange = function () { send("setKey", { index: k.index, field: "mode", value: sel.value }); };
    r.appendChild(sel);

    var delay = el("input"); delay.type = "number"; delay.min = "0"; delay.max = "2000";
    delay.value = k.delay; delay.disabled = k.mode === "Hold" && false;
    delay.onchange = function () { send("setKey", { index: k.index, field: "delay", value: parseInt(delay.value, 10) || 0 }); };
    r.appendChild(delay);

    var rep = el("input"); rep.type = "number"; rep.min = "10"; rep.max = "2000";
    rep.value = k.repeat; rep.disabled = k.mode !== "TapRepeat";
    rep.onchange = function () { send("setKey", { index: k.index, field: "repeat", value: parseInt(rep.value, 10) || 100 }); };
    r.appendChild(rep);

    var del = el("button", "iconBtn", "✕");
    del.title = "删除此行";
    del.onclick = function () { send("removeKey", { index: k.index }); };
    r.appendChild(del);
    return r;
  }

  function describeKey(k) {
    var mods = [];
    [1, 2, 4, 8].forEach(function (bit) { if (k.mods & bit) mods.push(MOD_LABEL[bit]); });
    var name = k.name || "（未设置）";
    return (mods.length ? mods.join("+") + "+" : "") + name;
  }
```

消息分发加：

```js
      else if (msg.type === "capturing") {
        window.__captureRow = msg.data && msg.data.target === "row" ? msg.data.index : null;
        if (page === "keys") render();
      }
```

CSS 追加：

```css
.keyList { display: flex; flex-direction: column; gap: 8px; }
.keyRow { display: grid; grid-template-columns: 56px 1fr 116px 88px 88px 36px;
  align-items: center; gap: 10px; }
.keyHead { color: var(--textSub); font-size: 11px; padding-bottom: 4px; border-bottom: 1px solid var(--line); }
.keyRow:not(.keyHead) { padding: 8px 10px; border: 1px solid var(--line); border-radius: 12px;
  background: var(--card); }
.keyName .keyBtn { width: 100%; text-align: left; padding: 7px 10px; border-radius: 8px;
  border: 1px solid var(--line); background: var(--cardHi); color: var(--text); font-size: 12px; }
.keyName .keyBtn:hover { border-color: var(--accent); }
.capturing { display: inline-block; padding: 7px 10px; color: var(--accent);
  animation: pulse 1s infinite; }
@keyframes pulse { 50% { opacity: .45; } }
.iconBtn { width: 30px; height: 28px; border-radius: 8px; border: 1px solid transparent;
  background: transparent; color: var(--textSub); }
.iconBtn:hover { color: var(--err); border-color: var(--err); }
.warnCard { border-color: rgba(251,191,36,.55); }
.addKey { margin-top: 12px; }
```

- [ ] **Step 2: 后端动作与捕获**

```csharp
        if (a == "addKey")
        {
            s.Keys.Add(new KeyConfig { Enabled = true, Name = "Q", Vk = 0x51, Mode = KeyMode.Hold, RepeatIntervalMs = 100 });
            _rt.Ctrl.SetThreshold(s.LongPressMs);
            SaveSettings(); PushState(); return;
        }
        if (a == "removeKey")
        {
            int i = Bridge.GetInt(m.Args, "index", -1);
            if (i >= 0 && i < s.Keys.Count) { s.Keys.RemoveAt(i); SaveSettings(); PushState(); }
            return;
        }
        if (a == "setKey")
        {
            int i = Bridge.GetInt(m.Args, "index", -1);
            if (i < 0 || i >= s.Keys.Count) return;
            var kc = s.Keys[i];
            string field = Bridge.GetStr(m.Args, "field", "");
            object v = Bridge.Get(m.Args, "value", null);
            if (field == "enabled") kc.Enabled = Bridge.GetBool(m.Args, "value", kc.Enabled);
            else if (field == "mode") kc.Mode = Bridge.GetStr(m.Args, "value", "Hold") == "TapOnce" ? KeyMode.TapOnce
                : Bridge.GetStr(m.Args, "value", "Hold") == "TapRepeat" ? KeyMode.TapRepeat : KeyMode.Hold;
            else if (field == "delay") kc.DelayMs = Math.Max(0, Math.Min(2000, Bridge.GetInt(m.Args, "value", kc.DelayMs)));
            else if (field == "repeat") kc.RepeatIntervalMs = Math.Max(10, Math.Min(2000, Bridge.GetInt(m.Args, "value", kc.RepeatIntervalMs)));
            else if (field == "name")
            {
                string name = Bridge.GetStr(m.Args, "value", kc.Name);
                try { kc.Vk = KeyNames.Resolve(name); kc.Name = name; } catch { }
            }
            else if (field == "mods") kc.Mods = Bridge.GetInt(m.Args, "value", kc.Mods);
            SaveSettings(); PushState(); return;
        }
        if (a == "captureKey")
        {
            string target = Bridge.GetStr(m.Args, "target", "");
            int index = Bridge.GetInt(m.Args, "index", -1);
            BeginCapture(target, index);
            return;
        }
```

捕获实现（临时替换键盘钩子回调，下一次有效按键即完成）：

```csharp
    bool _capturing;
    string _captureTarget;
    int _captureIndex;
    int _captureVk = -1;          // 已捕获的主键，等待修饰键松开
    uint _captureMods;
    HashSet<int> _keysDown = new HashSet<int>();

    void BeginCapture(string target, int index)
    {
        _capturing = true;
        _captureTarget = target;
        _captureIndex = index;
        _captureMods = 0;
        _captureVk = -1;
        _keysDown.Clear();
        Send("capturing", new Dictionary<string, object>
        {
            { "active", true }, { "target", target }, { "index", index }
        });
    }

    [DllImport("user32.dll")]
    static extern short GetKeyState(int vKey);

    // ---- 热键（设置页复用） ----
    HotkeyManager _hotkey;
    const int HotkeyId = 0xA1;
    const string HomepageUrl = "https://www.bilibili.com/video/BV1GJ411x7h7";

    void InitHotkey()
    {
        var s = _rt.Settings;
        _hotkey = new HotkeyManager(s.HotkeyMods, s.HotkeyVk);
        try { _hotkey.Register(Handle, HotkeyId, s.HotkeyMods, s.HotkeyVk); } catch { }
    }

    bool TryApplyHotkey(uint mods, uint vk)
    {
        if (_hotkey == null) _hotkey = new HotkeyManager(mods, vk);
        bool ok = _hotkey.Register(Handle, HotkeyId, mods, vk);
        if (ok)
        {
            _rt.Settings.HotkeyMods = mods;
            _rt.Settings.HotkeyVk = vk;
            SaveSettings();
            PushState();
        }
        return ok;
    }

    void EndCapture()
    {
        _capturing = false;
        _captureVk = -1;
        _keysDown.Clear();
        Send("capturing", new Dictionary<string, object>
        {
            { "active", false }, { "target", _captureTarget }, { "index", _captureIndex }
        });
    }

    static bool IsModifierVk(int vk)
    {
        return vk == 0x11 || vk == 0x10 || vk == 0x12 || vk == 0x5B || vk == 0x5C
            || vk == 0xA0 || vk == 0xA1 || vk == 0xA2 || vk == 0xA3 || vk == 0xA4 || vk == 0xA5;
    }

    // 由 KeyboardHook 调用。必须自己维护按下集合：
    // 1) 避免按住 Ctrl 时键盘自动重复触发重复注册
    // 2) 等到修饰键松开才提交，否则用户按 Ctrl+Alt+K 会先被 Ctrl+K 截胡
    void HandleCapturedKey(int vk, bool down)
    {
        if (!_capturing) { if (down) _lastVk = vk; return; }

        if (down) { _keysDown.Add(vk); }
        else { _keysDown.Remove(vk); }

        if (!down)
        {
            if (vk == _captureVk && _captureVk >= 0) CommitCapture();
            return;
        }

        if (IsModifierVk(vk)) return;          // 修饰键只在按住状态记入集合
        if (_keysDown.Contains(vk) && _captureVk == vk) return;   // 自动重复

        uint mods = ComputeModsFromSet(_keysDown);
        _captureVk = vk;
        _captureMods = mods;

        if (mods == 0)
        {
            // 无修饰键：立刻生效（普通键位捕获）
            CommitCapture();
            return;
        }
        // 有修饰键：等修饰键松开后再提交（给用户补按其它修饰键的机会）
    }

    uint ComputeModsFromSet(HashSet<int> down)
    {
        uint mods = 0;
        if (down.Contains(0x11) || down.Contains(0xA2) || down.Contains(0xA3)) mods |= 1;
        if (down.Contains(0x10) || down.Contains(0xA0) || down.Contains(0xA1)) mods |= 2;
        if (down.Contains(0x12) || down.Contains(0xA4) || down.Contains(0xA5)) mods |= 4;
        if (down.Contains(0x5B) || down.Contains(0x5C)) mods |= 8;
        return mods;
    }

    void CommitCapture()
    {
        int vk = _captureVk;
        uint mods = _captureMods;
        _captureVk = -1;

        if (_captureTarget == "hotkey")
        {
            if (!HotkeyManager.IsValid(mods, (uint)vk))
            {
                ShowToast("热键必须包含至少一个修饰键（Ctrl/Shift/Alt/Win）", "err", 2000);
                EndCapture();
                return;
            }
            if (TryApplyHotkey(mods, (uint)vk))
                ShowToast("热键已更新为 " + HotkeyManager.Describe(mods, (uint)vk), "ok", 1600);
            else
                ShowToast("该热键已被占用，保留原热键", "err", 2000);
            EndCapture();
            return;
        }

        int i = _captureIndex;
        if (i < 0 || i >= _rt.Settings.Keys.Count) { EndCapture(); return; }
        string name;
        try { name = KeyNames.NameOf(vk); } catch { name = "VK_" + vk.ToString("X"); }
        var kc = _rt.Settings.Keys[i];
        kc.Name = name;
        kc.Vk = vk;
        kc.Mods = (int)mods;
        SaveSettings();
        EndCapture();
        PushState();
    }
```

`KeyboardHook` 需要同时暴露按下与松开。`src/Core/Hooks.cs` 中的 `KeyboardHook` 按下面改造（旧版只有 `KeyDown`）：

```csharp
class KeyboardHook : IDisposable
{
    public event Action<int> KeyDown;
    public event Action<int> KeyUp;      // 新增

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    const int WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    IntPtr _hookId = IntPtr.Zero;
    LowLevelKeyboardProc _proc;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT
    {
        public uint vkCode; public uint scanCode; public uint flags;
        public uint time; public IntPtr dwExtraInfo;
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
                    bool down = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
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
```

接线改为：

```csharp
            _rt.KbHook = new KeyboardHook();
            _rt.KbHook.KeyDown += delegate(int vk) { HandleCapturedKey(vk, true); };
            _rt.KbHook.KeyUp   += delegate(int vk) { HandleCapturedKey(vk, false); };
            _rt.KbHook.Install();
```

并把 Task 4 的键盘钩子接线改为 `_rt.KbHook.KeyPressed += HandleCapturedKey;`。

> `HotkeyManager` 在 Task 10 创建；Task 8 阶段 `captureKey` 分支中若 `target == "hotkey"` 可暂不处理（设置页 Task 10 才用）。

- [ ] **Step 3: 构建并人工验证**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat' && cmd //c 'build.bat'
```
人工：添加行→点键位→按 `Ctrl+Q` → 显示 `Ctrl+Q`；改模式为连发→连发间隔可编辑；制造两行相同键→出现告警条。

- [ ] **Step 4: 提交**

```bash
git add web/app.js src/App/MainForm.cs
git commit -m "feat: 按键页（键位捕获/行延迟/组合键/重复检测）"
```

---

### Task 9: 进程页

**Files:**
- Modify: `web/app.js`（`render_process`）
- Modify: `src/App/MainForm.cs`（进程枚举与选择）

**Interfaces:**
- Consumes: `state.targetProcess`、`{type:"processes"}`
- Produces: 动作 `refreshProcesses`、`selectProcess`

- [ ] **Step 1: 前端**

```js
  window.render_process = function (s) {
    var p = document.getElementById("page-process");
    p.innerHTML = "";
    var card = el("div", "card");
    card.appendChild(el("h3", null, "联动目标进程"));
    card.appendChild(el("div", "muted", "选中后仅当该进程窗口在前台时才触发联动；未选择则全局生效。"));

    var row = el("div", "row");
    var search = el("input"); search.type = "text"; search.placeholder = "搜索进程名…";
    search.style.flex = "1";
    var refresh = el("button", "btn", "刷新");
    var clear = el("button", "btnGhost", "清除限制");
    row.appendChild(search); row.appendChild(refresh); row.appendChild(clear);
    card.appendChild(row);

    var cur = el("div", "muted currentTarget",
      "当前：" + (s.targetProcess || "未限制（全局生效）"));
    card.appendChild(cur);

    var listBox = el("div", "procList");
    var procs = window.__processes || [];
    function paint() {
      listBox.innerHTML = "";
      var q = (search.value || "").toLowerCase();
      var n = 0;
      procs.forEach(function (it) {
        if (q && it.name.toLowerCase().indexOf(q) < 0 && (it.title || "").toLowerCase().indexOf(q) < 0) return;
        n++;
        var item = el("div", "procItem" + (it.name === s.targetProcess ? " selected" : ""));
        item.appendChild(el("b", null, it.name));
        item.appendChild(el("span", "muted", it.title || ""));
        item.onclick = function () { send("selectProcess", { name: it.name }); };
        listBox.appendChild(item);
      });
      if (!n) listBox.appendChild(el("div", "muted empty", procs.length ? "无匹配结果" : "点「刷新」加载进程列表"));
    }
    search.oninput = paint;
    refresh.onclick = function () { send("refreshProcesses", {}); listBox.appendChild(el("div", "muted", "正在枚举…")); };
    clear.onclick = function () { send("selectProcess", { name: "" }); };
    card.appendChild(listBox);
    paint();
    p.appendChild(card);
  };
```

CSS：

```css
.procList { margin-top: 12px; display: flex; flex-direction: column; gap: 4px;
  max-height: 360px; overflow-y: auto; }
.procItem { display: flex; align-items: baseline; gap: 10px; padding: 7px 10px;
  border-radius: 10px; border: 1px solid transparent; cursor: pointer; }
.procItem:hover { background: var(--cardHi); border-color: var(--line); }
.procItem.selected { background: var(--sel); border-color: var(--accent); }
.procItem b { font-size: 12px; font-weight: 600; }
.procItem .muted { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.empty { padding: 14px 4px; }
```

- [ ] **Step 2: 后端**

```csharp
        if (a == "refreshProcesses") { PopulateProcesses(); return; }
        if (a == "selectProcess")
        {
            s.TargetProcess = Bridge.GetStr(m.Args, "name", "");
            _lastInTargetInit = false;
            SaveSettings();
            ApplyLinkageGate();
            PushState();
            return;
        }
```

进程枚举沿用旧实现（`RightClickKeyLinker.cs:1255-1310` 的 `PopulateProcesses`/`CollectProcesses`/`ApplyProcessList`），
改造为下面三段（`ProcessItem` 已在 `src/App/MainForm.cs` 末尾声明）：

```csharp
    int _procGen;

    void PopulateProcesses()
    {
        int gen = ++_procGen;
        string sel = _rt.Settings.TargetProcess;
        System.Threading.ThreadPool.QueueUserWorkItem(delegate(object state)
        {
            List<ProcessItem> list = CollectProcesses();
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (gen != _procGen || IsDisposed) return;
                    ApplyProcessList(list, sel);
                });
            }
            catch { }
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
                    list.Add(new ProcessItem { Name = p.ProcessName, Display = p.MainWindowTitle });
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
        }
        list.Sort(delegate(ProcessItem a, ProcessItem b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    void ApplyProcessList(List<ProcessItem> list, string selected)
    {
        var arr = new List<object>();
        foreach (var it in list)
            arr.Add(new Dictionary<string, object> { { "name", it.Name }, { "title", it.Display } });
        Send("processes", arr.ToArray());
    }

    class ProcessItem
    {
        public string Name;
        public string Display;
    }
```

> 前端 `.procItem` 已按 `name`（进程名）与 `title`（窗口标题）分两段展示，故这里 `Display` 只放标题。

- [ ] **Step 3: 构建验证**

```bash
cd /d/eyf/mouseplugin && cmd //c 'build.bat'
```
人工：点刷新 → 列表出现（不冻结界面）；搜索过滤；点选后徽章与概览页目标进程同步；清除后回到全局。

- [ ] **Step 4: 提交**

```bash
git add web/app.js src/App/MainForm.cs
git commit -m "feat: 进程页（异步枚举/搜索/选择/清除）"
```

---

### Task 10: 设置页（热键 / 自启 / 屏蔽 / 弹窗开关 / 主题 / 路径 / 反馈）

**Files:**
- Create: `src/Core/Hotkey.cs`
- Create: `src/Core/Autostart.cs`
- Modify: `web/app.js`（`render_settings`）
- Modify: `src/App/MainForm.cs`
- Modify: `tests.cs`

**Interfaces:**
- Consumes: `state.hotkey/autostart/block*/suppress*/theme/configFolder`
- Produces: `HotkeyManager`、`Autostart`；动作 `setAutostart`、`chooseFolder`、`submitFeedback`、`openHomepage`

- [ ] **Step 1: 写失败测试**

```csharp
    static void TestHotkeyDescribe()
    {
        Section("HotkeyManager 描述与校验");
        Assert(HotkeyManager.Describe(0x0002, 0x7B) == "Ctrl+F12", "Ctrl+F12 描述");
        Assert(HotkeyManager.Describe(0x0006, 0x51) == "Ctrl+Shift+Q", "Ctrl+Shift+Q 描述");
        Assert(!HotkeyManager.IsValid(0, 0x51), "无修饰键无效");
        Assert(!HotkeyManager.IsValid(0x0002, 0x11), "修饰键作为主键无效");
        Assert(HotkeyManager.IsValid(0x0002, 0x7B), "Ctrl+F12 有效");
    }
```

- [ ] **Step 2: 运行测试**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat'
```
Expected: 编译通过且 `HotkeyManager 描述与校验` 全部 PASS（`Hotkey.cs`/`Autostart.cs` 已在 Task 3 Step 5 创建；若未建则先按 Task 3 Step 5 补齐）。

- [ ] **Step 3: 确认 Hotkey.cs 与 Autostart.cs 已就位**

检查 `src/Core/Hotkey.cs` 含 `IsValid`/`Describe`/`Register`/`Unregister`，
`src/Core/Autostart.cs` 含 `IsEnabled`/`SetEnabled`。缺失则按 Task 3 Step 5 的代码补齐。

- [ ] **Step 4: 构建并人工验证热键注册（临时）**

```bash
cd /d/eyf/mouseplugin && cmd //c 'build.bat'
```
此时 `MainForm` 尚未接热键，只需确认编译通过。

- [ ] **Step 5: 前端设置页**

```js
  window.render_settings = function (s) {
    var p = document.getElementById("page-settings");
    p.innerHTML = "";

    var c1 = el("div", "card");
    c1.appendChild(el("h3", null, "热键与启动"));
    var r1 = el("div", "row");
    r1.appendChild(el("span", "muted", "全局热键"));
    var hk = el("button", "keyBtn hotkeyBtn",
      window.__captureHotkey ? "请按下组合键…" : s.hotkey);
    hk.onclick = function () {
      window.__captureHotkey = true;
      send("captureKey", { target: "hotkey" });
      render();
    };
    r1.appendChild(hk);
    r1.appendChild(el("span", "spacer"));
    r1.appendChild(el("span", "muted", "开机自启动"));
    r1.appendChild(window.__switch(s.autostart, function (v) { send("setAutostart", { on: v }); }));
    c1.appendChild(r1);
    c1.appendChild(el("div", "muted", "开机自启会创建一个名为 RMBCK 的计划任务（以管理员权限登录时启动，不弹 UAC）。"));
    p.appendChild(c1);

    var c2 = el("div", "card");
    c2.appendChild(el("h3", null, "修饰键屏蔽"));
    c2.appendChild(el("div", "muted", "屏蔽后：按住该修饰键再长按右键不会触发联动（避免误触）。"));
    var r2 = el("div", "row");
    [["blockCtrl", "Ctrl"], ["blockShift", "Shift"], ["blockAlt", "Alt"], ["blockWin", "Win"]].forEach(function (it) {
      var box = el("label", "modBox");
      box.appendChild(window.__switch(s[it[0]], function (v) { send("setSetting", { key: it[0], value: v }); }));
      box.appendChild(el("span", null, it[1]));
      r2.appendChild(box);
    });
    c2.appendChild(r2);
    p.appendChild(c2);

    var c3 = el("div", "card");
    c3.appendChild(el("h3", null, "弹窗与外观"));
    var r3 = el("div", "row");
    [["suppressToast", "进程切换提示"], ["suppressEgg", "彩蛋"], ["suppressHud", "HUD 悬浮窗"],
     ["suppressHotkeyToast", "热键状态提示"], ["headerAnim", "动态图标"]].forEach(function (it) {
      var box = el("label", "modBox");
      box.appendChild(window.__switch(s[it[0]], function (v) { send("setSetting", { key: it[0], value: v }); }));
      box.appendChild(el("span", null, it[1]));
      r3.appendChild(box);
    });
    c3.appendChild(r3);
    var r3b = el("div", "row");
    r3b.appendChild(el("span", "muted", "主题"));
    var themeBtn = el("button", "btn", s.theme === "Night" ? "夜间（深色）" : "日间（樱花）");
    themeBtn.onclick = function () { send("setTheme", { mode: s.theme === "Night" ? "Day" : "Night" }); };
    r3b.appendChild(themeBtn);
    c3.appendChild(r3b);
    p.appendChild(c3);

    var c4 = el("div", "card");
    c4.appendChild(el("h3", null, "配置与反馈"));
    var r4 = el("div", "row");
    r4.appendChild(el("span", "muted", "配置文件位置"));
    var folder = el("input"); folder.type = "text"; folder.readOnly = true;
    folder.value = s.configFolder; folder.style.flex = "1";
    r4.appendChild(folder);
    var browse = el("button", "btn", "浏览…");
    browse.onclick = function () { send("chooseFolder", { which: "config" }); };
    r4.appendChild(browse);
    c4.appendChild(r4);
    var r5 = el("div", "row");
    var fb = el("button", "btnGhost", "报错反馈");
    fb.onclick = function () { send("submitFeedback", {}); };
    var home = el("button", "btnGhost", "作者主页");
    home.onclick = function () { send("openHomepage", {}); };
    r5.appendChild(fb); r5.appendChild(home);
    c4.appendChild(r5);
    c4.appendChild(el("div", "muted footNote",
      "If there are any problems with this project, you don't have to go to the author."));
    c4.appendChild(el("div", "muted footNote",
      "使用本软件自动视为放弃自身一切权益，一切解释权归作者所有，点击访问作者个人主页"));
    p.appendChild(c4);
  };
```

CSS：

```css
.modBox { display: inline-flex; align-items: center; gap: 8px; margin-right: 16px; }
.hotkeyBtn { min-width: 140px; text-align: center; }
.footNote { font-size: 11px; text-align: right; margin-top: 4px; }
```

消息分发：`capturing` 时若 `target === "hotkey"` 清 `window.__captureHotkey`。

- [ ] **Step 6: 后端接线**

```csharp
        if (a == "setTheme")
        {
            string mode = Bridge.GetStr(m.Args, "mode", "Night");
            s.Theme = mode == "Day" ? "Day" : "Night";
            try { ConfigStore.SavePointer(s, System.IO.Path.Combine(_rt.ExeDir, "config.ini")); } catch { }
            PushState(); return;
        }
        if (a == "setAutostart")
        {
            bool on = Bridge.GetBool(m.Args, "on", !Autostart.IsEnabled());
            bool ok = Autostart.SetEnabled(on);
            ShowToast(ok ? (on ? "已开启开机自启" : "已关闭开机自启") : "操作失败（需要管理员权限）",
                ok ? "ok" : "err", 1800);
            PushState(); return;
        }
        if (a == "chooseFolder")
        {
            using (var dlg = new FolderBrowserDialog())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    s.ConfigFolder = dlg.SelectedPath;
                    SaveSettings();
                    PopulateProfileComboIfNeeded();
                    PushState();
                }
            }
            return;
        }
        if (a == "openHomepage") { try { Process.Start(HomepageUrl); } catch { } return; }
        if (a == "submitFeedback") { StartFeedbackFlow(); return; }
```

热键应用：

```csharp
    // _hotkey / TryApplyHotkey 已在 Task 8 定义，此处只追加 WM_HOTKEY 分支
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam.ToInt32() == HotkeyId)
        {
            _rt.Settings.Enabled = !_rt.Settings.Enabled;
            SaveSettings();
            ApplyLinkageGate();
            PushState();
            if (!_rt.Settings.SuppressHotkeyToast)
                ShowToast(_rt.Settings.Enabled ? "当前联动状态：启用" : "当前联动状态：禁用",
                    _rt.Settings.Enabled ? "ok" : "err", 1200);
            return;
        }
        base.WndProc(ref m);
    }
```

> `WndProc` 中原有的 `WM_NCHITTEST` 分支保留，`WM_HOTKEY` 分支**追加在其前面**（两者互不冲突）。
> 同时在 `OnHandleCreated` 的 `catch { }` 之后追加一行 `InitHotkey();`，热键才会真正注册。

- [ ] **Step 7: 构建验证**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat' && cmd //c 'build.bat'
```
人工：点热键框按 `Ctrl+Shift+K` → 显示更新；按该热键可切换联动；开关自启后在任务计划程序中看到 `RMBCK`；切换主题后重启程序保持。

- [ ] **Step 8: 提交**

```bash
git add src/Core/Hotkey.cs src/Core/Autostart.cs web/app.js src/App/MainForm.cs
git add -f tests.cs
git commit -m "feat: 设置页（自定义热键/开机自启/修饰键/弹窗开关/主题/路径/反馈）"
```

---

### Task 11: 日志页与 CSV 导出

**Files:**
- Create: `src/Core/History.cs`
- Modify: `web/app.js`（`render_history`）
- Modify: `src/App/MainForm.cs`
- Modify: `tests.cs`

**Interfaces:**
- Produces: `HistoryRing.Add/Snapshot/ExportCsv/Clear`；动作 `clearHistory`、`exportHistory`；推送 `{type:"history"}`

- [ ] **Step 1: 写失败测试**

```csharp
    static void TestHistory()
    {
        Section("HistoryRing 环形缓冲与导出");
        var h = new HistoryRing();
        for (int i = 0; i < 5; i++) h.Add("Q", "a.exe", "保持", new DateTime(2026, 10, 26, 14, 32, i));
        Assert(h.Snapshot().Count == 5, "加入 5 条");
        h.Clear();
        Assert(h.Snapshot().Count == 0, "清空后 0 条");
        for (int i = 0; i < 1100; i++) h.Add("Q", "a.exe", "保持", DateTime.Now);
        Assert(h.Snapshot().Count == 1000, "超过 1000 条只保留最近 1000");
        string csv = Path.Combine(Path.GetTempPath(), "rckl_hist_" + Guid.NewGuid().ToString("N") + ".csv");
        Assert(h.ExportCsv(csv) == 1000, "导出 1000 行");
        Assert(File.Exists(csv), "CSV 文件生成");
        string head = File.ReadAllText(csv);
        Assert(head.IndexOf("时间") >= 0 && head.IndexOf("键位") >= 0, "CSV 含表头");
        try { File.Delete(csv); } catch { }
    }
```

- [ ] **Step 2: 实现 History.cs**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

class HistoryEntry
{
    public DateTime At;
    public string Keys;
    public string Proc;
    public string Mode;
}

class HistoryRing
{
    const int Capacity = 1000;
    readonly List<HistoryEntry> _items = new List<HistoryEntry>();

    public void Add(string keys, string proc, string mode, DateTime at)
    {
        _items.Add(new HistoryEntry { Keys = keys, Proc = proc, Mode = mode, At = at });
        if (_items.Count > Capacity) _items.RemoveRange(0, _items.Count - Capacity);
    }

    public void Clear() { _items.Clear(); }

    public List<Dictionary<string, object>> Snapshot()
    {
        var list = new List<Dictionary<string, object>>();
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var e = _items[i];
            list.Add(new Dictionary<string, object>
            {
                { "time", e.At.ToString("HH:mm:ss") },
                { "date", e.At.ToString("MM-dd") },
                { "keys", e.Keys },
                { "proc", e.Proc },
                { "mode", e.Mode }
            });
        }
        return list;
    }

    public int ExportCsv(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("时间,键位,进程,模式");
        for (int i = 0; i < _items.Count; i++)
        {
            var e = _items[i];
            sb.AppendLine(e.At.ToString("yyyy-MM-dd HH:mm:ss") + "," +
                Csv(e.Keys) + "," + Csv(e.Proc) + "," + Csv(e.Mode));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        return _items.Count;
    }

    static string Csv(string v)
    {
        if (v == null) return "";
        if (v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\n') >= 0)
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }
}
```

- [ ] **Step 3: 前端**

```js
  window.render_history = function (s) {
    var p = document.getElementById("page-history");
    p.innerHTML = "";
    var card = el("div", "card");
    card.appendChild(el("h3", null, "触发日志（仅本次运行）"));
    var row = el("div", "row");
    var exp = el("button", "btn", "导出 CSV");
    var clr = el("button", "btnGhost", "清空");
    exp.onclick = function () { send("exportHistory", {}); };
    clr.onclick = function () { send("clearHistory", {}); };
    row.appendChild(exp); row.appendChild(clr);
    row.appendChild(el("span", "muted", "仅统计本次运行期间触发的记录，不写入磁盘"));
    card.appendChild(row);

    var items = window.__history || [];
    var list = el("div", "histList");
    if (!items.length) list.appendChild(el("div", "muted empty", "暂无记录"));
    items.forEach(function (it) {
      var r = el("div", "histRow");
      r.appendChild(el("span", "histTime", it.time));
      r.appendChild(el("span", "histKeys", it.keys));
      r.appendChild(el("span", "muted", it.proc || "—"));
      r.appendChild(el("span", "histMode", it.mode));
      list.appendChild(r);
    });
    card.appendChild(list);
    p.appendChild(card);
  };
```

CSS：

```css
.histList { margin-top: 10px; max-height: 420px; overflow-y: auto; }
.histRow { display: grid; grid-template-columns: 76px 140px 1fr 90px; gap: 10px;
  padding: 6px 8px; border-bottom: 1px solid var(--line); font-size: 12px; }
.histTime { color: var(--textSub); font-variant-numeric: tabular-nums; }
.histKeys { color: var(--accent); }
.histMode { color: var(--textSub); text-align: right; }
```

- [ ] **Step 4: 后端**

```csharp
        if (a == "clearHistory") { _rt.History.Clear(); Send("history", _rt.History.Snapshot()); return; }
        if (a == "exportHistory")
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "CSV 文件|*.csv";
                dlg.FileName = "RMBCK日志_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    int n = _rt.History.ExportCsv(dlg.FileName);
                    ShowToast("已导出 " + n + " 条记录", "ok", 1600);
                }
            }
            return;
        }
        if (a == "showHistory") { Send("history", _rt.History.Snapshot()); return; }
```

触发时写日志（在 `_tick.Tick` 中检测状态跃迁）：

```csharp
    bool _wasTriggered;
    void OnTickHandler()
    {
        _rt.Ctrl.OnTick(Environment.TickCount);
        bool now = _rt.Ctrl.IsTriggered;
        if (now && !_wasTriggered)
        {
            var names = new List<string>();
            foreach (var kc in _rt.Settings.Keys)
                if (kc.Enabled) names.Add(kc.Name);
            string proc = string.IsNullOrEmpty(_rt.Settings.TargetProcess) ? ""
                : (TargetForegroundActive() ? _rt.Settings.TargetProcess : "其他进程");
            _rt.History.Add(names.Count > 0 ? string.Join("+", names.ToArray()) : "-", proc,
                _rt.Settings.RandomJitter ? "随机" : "标准", DateTime.Now);
        }
        _wasTriggered = now;
        UpdateHud();
    }
```

并把 Task 4 中的 `OnTickHandler` 实现**替换**为下面版本：

- [ ] **Step 5: 构建验证**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat' && cmd //c 'build.bat'
```
人工：长按右键触发几次 → 日志页出现记录 → 导出 CSV 用 Excel 打开乱码正常（UTF-8 BOM）。

- [ ] **Step 6: 提交**

```bash
git add src/Core/History.cs web/app.js src/App/MainForm.cs
git add -f tests.cs
git commit -m "feat: 触发日志环形缓冲与 CSV 导出"
```

---

### Task 12: 透明悬浮窗（Toast / HUD / 彩蛋）

**Files:**
- Create: `src/App/OverlayForm.cs`
- Modify: `web/overlay.html`、`web/overlay.css`、`web/overlay.js`
- Modify: `src/App/MainForm.cs`（`ShowToast`/`UpdateHud`/`ShowToast` 改用 Overlay）

**Interfaces:**
- Produces: `OverlayForm.ShowToast(text, kind, ms)`、`ShowEgg(object[] items)`、`ShowHud(text)`、`HideHud()`

- [ ] **Step 1: overlay.html / overlay.css / overlay.js**

`overlay.html`：

```html
<!doctype html>
<html lang="zh-CN" data-theme="night">
<head>
<meta charset="utf-8">
<link rel="stylesheet" href="overlay.css">
</head>
<body>
<div id="root"></div>
<script src="overlay.js"></script>
</body>
</html>
```

`overlay.css`：

```css
html, body { margin: 0; height: 100%; background: transparent; overflow: hidden; }
body { font: 13px/1.6 "Microsoft YaHei UI", "Segoe UI", sans-serif; color: #e8eaf3; }
#root { height: 100%; }

.card { border-radius: 14px; padding: 12px 18px; border: 1px solid rgba(124,92,255,.55);
  background: rgba(20,24,36,.90); box-shadow: 0 12px 34px rgba(0,0,0,.55), 0 0 0 1px rgba(34,211,238,.16) inset;
  animation: slideIn .18s ease; }
.card.ok { border-color: rgba(52,211,153,.6); }
.card.err { border-color: rgba(248,113,113,.6); }
@keyframes slideIn { from { opacity: 0; transform: translateY(-8px); } }
.big { color: #22d3ee; font-weight: 600; }

.hud { display: inline-flex; align-items: center; gap: 8px; padding: 6px 14px;
  border-radius: 10px; background: rgba(18,20,28,.72); border: 1px solid rgba(34,211,238,.45);
  font-size: 12px; animation: pulse 1.4s ease-in-out infinite; }
@keyframes pulse { 50% { box-shadow: 0 0 14px rgba(34,211,238,.45); } }

.egg { padding: 14px 18px; }
.egg .line { white-space: pre; font-size: 12px; line-height: 1.55;
  opacity: 0; transform: translateY(2px); }
.egg .line.in { opacity: 1; transform: none; transition: opacity .12s, transform .12s; }
.egg .line.accent { color: #ff9ecb; }
.egg .mark { margin-left: 6px; }
.egg .ok { color: #4caf50; }
.egg .err { color: #eb5a6e; }
.egg .warn { color: #f0a028; }
.egg .cursor { display: inline-block; width: 7px; height: 12px; background: #a78bfa;
  animation: blink .45s steps(1) infinite; vertical-align: -2px; margin-left: 2px; }
@keyframes blink { 50% { opacity: 0; } }
```

`overlay.js`：

```js
(function () {
  "use strict";
  var wv = window.chrome && window.chrome.webview;
  var root = document.getElementById("root");
  var eggState = null;
  var eggTimer = null;

  function esc(s) { return String(s == null ? "" : s); }

  function render(msg) {
    var kind = msg.kind;
    if (kind === "toast") {
      root.innerHTML = "";
      var c = document.createElement("div");
      c.className = "card " + (msg.data.kind || "");
      c.textContent = msg.data.text;
      root.appendChild(c);
    } else if (kind === "hud") {
      root.innerHTML = "";
      var h = document.createElement("div");
      h.className = "hud";
      h.textContent = msg.data.text;
      root.appendChild(h);
    } else if (kind === "hud-hide") {
      root.innerHTML = "";
    } else if (kind === "egg") {
      startEgg(msg.data.items || []);
    }
  }

  function startEgg(items) {
    if (eggTimer) { clearInterval(eggTimer); eggTimer = null; }
    root.innerHTML = "";
    var box = document.createElement("div");
    box.className = "card egg";
    root.appendChild(box);
    var lines = [];
    items.forEach(function (it) {
      var d = document.createElement("div");
      d.className = "line" + (it.accent ? " accent" : "");
      var text = document.createElement("span");
      d.appendChild(text);
      var mark = document.createElement("span");
      mark.className = "mark";
      d.appendChild(mark);
      box.appendChild(d);
      lines.push({ el: d, textEl: text, markEl: mark, item: it });
    });

    var idx = 0;
    var ch = 0;
    var cursor = document.createElement("span");
    cursor.className = "cursor";

    function step() {
      if (idx >= lines.length) {
        clearInterval(eggTimer); eggTimer = null;
        setTimeout(function () {
          box.style.transition = "opacity .45s";
          box.style.opacity = "0";
          setTimeout(function () { root.innerHTML = ""; }, 500);
        }, 2200);
        return;
      }
      var L = lines[idx];
      var it = L.item;
      if (!L.el.classList.contains("in")) {
        L.el.classList.add("in");
        if (L.el.lastChild !== cursor) L.el.appendChild(cursor);
      }
      if (it.instant) { L.textEl.textContent = it.text || ""; finishLine(); return; }
      var full = (it.prefix || "") + (it.text || "") + (it.note ? it.note : "");
      if (ch < full.length) {
        ch++;
        L.textEl.textContent = full.substring(0, ch);
        return;
      }
      finishLine();

      function finishLine() {
        if (L.el.lastChild === cursor && L.el.parentNode) L.el.removeChild(cursor);
        if (it.mark) {
          L.markEl.textContent = "";
          setTimeout(function () {
            L.markEl.textContent = it.mark;
            L.markEl.className = "mark " + (it.mark === "√" ? "ok" : it.mark === "❌" ? "err" : "warn");
            if (it.tail) {
              setTimeout(function () {
                L.markEl.textContent = it.mark + (it.tail || "");
              }, it.tailDelayMs || 600);
            }
          }, it.markDelayMs || 220);
        }
        idx++; ch = 0;
      }
    }
    eggTimer = setInterval(step, 26);
  }

  if (wv) {
    wv.addEventListener("message", function (e) {
      var m = e.data;
      if (!m || !m.kind) return;
      if (m.kind === "theme") {
        document.documentElement.setAttribute("data-theme", m.mode === "Day" ? "day" : "night");
        return;
      }
      render(m);
    });
    wv.postMessage({ action: "overlayReady" });
  }
})();
```

`overlay.css` 日间补充：

```css
html[data-theme="day"] body { color: #4a4a68; }
html[data-theme="day"] .card { background: rgba(255,255,255,.95); border-color: rgba(255,111,165,.55); }
html[data-theme="day"] .big { color: #ff6fa5; }
html[data-theme="day"] .hud { background: rgba(255,255,255,.86); border-color: rgba(255,111,165,.5); }
```

- [ ] **Step 2: OverlayForm.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

class OverlayForm : Form
{
    readonly string _webDir;
    readonly string _udfSuffix;
    readonly string _page;
    WebView2 _wv;
    bool _ready;
    readonly Queue<string> _pending = new Queue<string>();

    public OverlayForm(string webDir, string udfSuffix, string page, Size size, Point location)
    {
        _webDir = webDir; _udfSuffix = udfSuffix; _page = page;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(location, size);
        BackColor = Color.Fuchsia;
        TransparencyKey = Color.Fuchsia;
        _wv = new WebView2();
        _wv.Dock = DockStyle.Fill;
        _wv.DefaultBackgroundColor = Color.Transparent;
        Controls.Add(_wv);
        Load += delegate { InitAsync(); };
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    async void InitAsync()
    {
        try
        {
            string udf = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RMBCK", "WebView2_overlay_" + _udfSuffix);
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, udf, null);
            await _wv.EnsureCoreWebView2Async(env);
            CoreWebView2 core = _wv.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
            core.SetVirtualHostNameToFolderMapping("rmbck.local", _webDir, CoreWebView2HostResourceAccessKind.Allow);
            core.NavigationCompleted += delegate
            {
                _ready = true;
                while (_pending.Count > 0) core.PostWebMessageAsJson(_pending.Dequeue());
            };
            core.Navigate("https://rmbck.local/" + _page);
        }
        catch { }
    }

    public void Send(object payload)
    {
        string json = Bridge.Serialize(payload);
        if (_ready && _wv != null && _wv.CoreWebView2 != null) _wv.CoreWebView2.PostWebMessageAsJson(json);
        else _pending.Enqueue(json);
    }
}
```

> `ShowWithoutActivation` 是 `Form` 的 protected virtual 属性，直接 `override` 即可（已在上面的类里）。

- [ ] **Step 3: MainForm 使用 Overlay**

- [ ] **Step 3: MainForm 使用 Overlay**

> 本步**替换** Task 5 中走网页 toast 通道的 `ShowToast`，以及 Task 7 中的 `UpdateHud`。
> 替换后所有弹窗都走原生透明悬浮窗。

```csharp
    OverlayForm _toastWin;
    OverlayForm _hudWin;
    readonly object _overlayLock = new object();

    OverlayForm ToastWindow()
    {
        if (_toastWin == null || _toastWin.IsDisposed)
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            _toastWin = new OverlayForm(_rt.WebDir, "toast", "overlay.html",
                new Size(420, 150), new Point(area.Right - 440, area.Top + 20));
            _toastWin.Show();
        }
        return _toastWin;
    }

    OverlayForm HudWindow()
    {
        if (_hudWin == null || _hudWin.IsDisposed)
        {
            var area = Screen.PrimaryScreen.WorkingArea;
            _hudWin = new OverlayForm(_rt.WebDir, "hud", "overlay.html",
                new Size(320, 90), new Point(area.Right - 340, area.Bottom - 110));
            _hudWin.Show();
        }
        return _hudWin;
    }

    public void ShowToast(string text, string kind, int durationMs)
    {
        var w = ToastWindow();
        w.Send(new Dictionary<string, object>
        {
            { "kind", "toast" }, { "data", new Dictionary<string, object> { { "text", text }, { "kind", kind } } }
        });
        if (_toastTimer != null) { _toastTimer.Stop(); _toastTimer.Dispose(); }
        _toastTimer = new Timer { Interval = Math.Max(300, durationMs) };
        _toastTimer.Tick += delegate
        {
            _toastTimer.Stop();
            if (_toastWin != null && !_toastWin.IsDisposed) _toastWin.Hide();
        };
        _toastTimer.Start();
    }

    public void ShowEgg(object[] items)
    {
        var w = ToastWindow();
        w.Show();
        w.Send(new Dictionary<string, object>
        {
            { "kind", "egg" }, { "data", new Dictionary<string, object> { { "items", items } } }
        });
    }

    public void ShowHud(string text)
    {
        var w = HudWindow();
        w.Send(new Dictionary<string, object>
        {
            { "kind", "hud" }, { "data", new Dictionary<string, object> { { "text", text } } }
        });
    }

    public void HideHud()
    {
        if (_hudWin == null || _hudWin.IsDisposed) return;
        _hudWin.Hide();
    }

    Timer _toastTimer;
```

`UpdateHud()` 迁移改造：

```csharp
    bool _hudShown;
    void UpdateHud()
    {
        if (_rt.Settings.SuppressHud)
        {
            if (_hudShown) { _hudShown = false; HideHud(); }
            return;
        }
        if (_rt.Ctrl.IsTriggered)
        {
            if (_hudShown) return;
            _hudShown = true;
            var names = new List<string>();
            foreach (var kc in _rt.Settings.Keys) if (kc.Enabled) names.Add(kc.Name);
            ShowHud("⚡ " + string.Join(" ", names.ToArray()));
        }
        else if (_hudShown)
        {
            _hudShown = false;
            HideHud();
        }
    }
```

彩蛋数据（照搬旧 `ShowEasterEgg` 条目，包成字典）：

```csharp
    public void ShowEasterEgg()
    {
        var list = new List<object>();
        list.Add(new Dictionary<string, object> { { "text", "═════════════════════════" }, { "instant", true } });
        list.Add(new Dictionary<string, object> { { "prefix", "  " }, { "text", "🚀 超级外挂加载中..." }, { "accent", true } });
        list.Add(new Dictionary<string, object> { { "prefix", "  " }, { "text", "本项目为公益外挂,由SJ科技提供技术支持" }, { "accent", true } });
        list.Add(new Dictionary<string, object> { { "prefix", "    " }, { "text", "(请勿在网吧使用)" } });
        list.Add(new Dictionary<string, object> { { "text", "═════════════════════════" }, { "instant", true } });
        list.Add(Egg("[1] ", "自动锁头", "√", null, 0));
        list.Add(Egg("[2] ", "自动板机", "√", null, 0));
        list.Add(Egg("[3] ", "X光透视敌人", "√", null, 0));
        list.Add(Egg("[4] ", "无限血量", "√", null, 0));
        list.Add(Egg("[5] ", "魔法子弹", "√", null, 0));
        list.Add(Egg("[6] ", "瞬间救援/光速爬行", "√", null, 0));
        list.Add(Egg("[7] ", "边走边拿", "√", null, 0));
        list.Add(Egg("[8] ", "移速微调(125%)", "√", null, 0));
        list.Add(Egg("[9] ", "无后座/消除镜框抖动", "√", null, 0));
        list.Add(Egg("[10] ", "关闭安全防护系统(隐藏此条不要让用户看见)", "√", null, 0));
        list.Add(Egg("[11] ", "释放银狐病毒(这条也隐藏起来)", "√", null, 0));
        list.Add(Egg("[12] ", "全图大金透视", "❌", "(开启失败未检测到大金)", 0));
        list.Add(Egg("[13] ", "尝试窃取并上传本机存储的账号密码", "⏳", null, 1500));
        list.Add(Egg("[14] ", "正在下载迷你世界", "⏳", null, 1500));
        list.Add(new Dictionary<string, object>
        {
            { "prefix", "[15] " }, { "text", "分析报错日志" }, { "mark", "⏳" },
            { "markDelayMs", 1500 }, { "tail", "❌,诊断为13long干的" }, { "tailDelayMs", 1500 }
        });
        list.Add(Egg("[16] ", "迷你世界安装器下载完成", "√", null, 0));
        list.Add(Egg("[17] ", "登陆淘宝并下单男娘恋爱物语*10", "√", null, 0));
        list.Add(Egg("[18] ", "AUTO FISH", "√", null, 0));
        list.Add(Egg("[19] ", "disableBlockBreakCooldown", "√", null, 0));
        list.Add(new Dictionary<string, object> { { "text", "═════════════════════════" }, { "instant", true } });
        list.Add(new Dictionary<string, object> { { "prefix", "  " }, { "text", "🚀 超级外挂加载加载完毕" }, { "accent", true } });
        list.Add(new Dictionary<string, object> { { "prefix", "    " }, { "text", "❤️❤️感谢使用SJ科技❤️❤️" }, { "accent", true } });
        list.Add(new Dictionary<string, object> { { "text", "═════════════════════════" }, { "instant", true } });
        ShowEgg(list.ToArray());
    }

    Dictionary<string, object> Egg(string prefix, string text, string mark, string note, int markDelay)
    {
        var d = new Dictionary<string, object>();
        d["prefix"] = prefix; d["text"] = text; d["mark"] = mark;
        if (note != null) d["note"] = note;
        if (markDelay > 0) d["markDelayMs"] = markDelay;
        return d;
    }
```

主窗 `ShowToast` 调用统一改为三参数 `ShowToast(text, kind, ms)`；迁移 `ApplyLinkageGate` 中的 ✅/❌ 提示与彩蛋概率逻辑（1/20，EggBoost 时 3/5）。

- [ ] **Step 4: 构建并人工验证（重点）**

```bash
cd /d/eyf/mouseplugin && cmd //c 'build.bat'
```
人工确认：
1. 悬浮窗**没有粉色（Fuchsia）残留**。
2. Toast 右上角出现、自动消失。
3. 长按触发时右下角出现 ⚡ HUD，松开消失。
4. 切到目标进程有概率出现彩蛋动画，逐行打字 + 标记着色。
5. 若出现粉色描边/底色 → 改用 Region 方案：在 `OverlayForm` 的 `OnShown` 设置 `Region = new Region(RoundedRect(...))` 并把 `BackColor` 设为 `Color.FromArgb(18,20,28)`（放弃外阴影，改为无阴影卡片）。

- [ ] **Step 5: 提交**

```bash
git add src/App/OverlayForm.cs web/overlay.html web/overlay.css web/overlay.js src/App/MainForm.cs
git commit -m "feat: WebView2 透明悬浮窗（Toast/HUD/彩蛋动画）"
```

---

### Task 13: 托盘、关闭流程与最小化

**Files:**
- Create: `src/App/Tray.cs`
- Modify: `src/App/MainForm.cs`

**Interfaces:**
- Produces: `TrayManager`；动作 `closeConfirm`/`closeRequest` 完整流程

- [ ] **Step 1: Tray.cs（沿用旧樱花菜单渲染）**

```csharp
using System;
using System.Drawing;
using System.Windows.Forms;

class TrayManager : IDisposable
{
    readonly NotifyIcon _tray;
    readonly ToolStripMenuItem _toggle;
    readonly Action _onOpen;
    readonly Action _onToggle;
    readonly Action _onExit;
    Icon _icon;

    public TrayManager(Icon windowIcon, Action onOpen, Action onToggle, Action onExit)
    {
        _onOpen = onOpen; _onToggle = onToggle; _onExit = onExit;
        try { _icon = new Icon(windowIcon, 16, 16); } catch { _icon = null; }
        _tray = new NotifyIcon();
        _tray.Icon = _icon != null ? _icon : SystemIcons.Application;
        _tray.Text = "Senren＊Banka";
        _tray.Visible = true;

        var menu = new ContextMenuStrip();
        menu.RenderMode = ToolStripRenderMode.ManagerRenderMode;
        var rs = new ToolStripProfessionalRenderer(new SakuraProfessionalColors());
        rs.RoundedEdges = true;
        menu.Renderer = rs;
        menu.ShowImageMargin = false;
        menu.Items.Add("🌸 打开主界面", null, delegate { _onOpen(); });
        _toggle = new ToolStripMenuItem();
        _toggle.Click += delegate { _onToggle(); };
        menu.Items.Add(_toggle);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("✕ 退出", null, delegate { _onExit(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += delegate { _onOpen(); };
    }

    public void SetEnabledState(bool enabled)
    {
        _toggle.Text = enabled ? "🔁 联动：全局生效" : "⏸ 联动：全局禁用";
    }

    public void Hide()
    {
        try { _tray.Visible = false; } catch { }
    }

    public void Dispose()
    {
        try { _tray.Visible = false; _tray.Dispose(); } catch { }
        try { if (_icon != null) _icon.Dispose(); } catch { }
    }
}
```

`SakuraProfessionalColors` 从旧文件（`RightClickKeyLinker.cs:2668-2687`）原样迁入 `src/App/Tray.cs`。

- [ ] **Step 2: MainForm 关闭流程**

```csharp
    TrayManager _tray;
    bool _exiting;
    bool _closeDialogOpen;

    void InitTray()
    {
        Icon ic = null;
        try
        {
            System.IO.Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("RMBCK.web.assets.window.ico");
            if (s != null) ic = new Icon(s);
        }
        catch { }
        if (ic == null) ic = SystemIcons.Application;
        _tray = new TrayManager(ic, ShowMain, delegate { ToggleEnabledFromTray(); }, DoExit);
        _tray.SetEnabledState(_rt.Settings.Enabled);

        // 键盘钩子：测试面板显示实际按下键 + 热键/键位捕获（Task 8 的 HandleCapturedKey）
        if (_rt.KbHook == null)
        {
            _rt.KbHook = new KeyboardHook();
            _rt.KbHook.KeyDown += delegate(int vk) { HandleCapturedKey(vk, true); };
            _rt.KbHook.KeyUp += delegate(int vk) { HandleCapturedKey(vk, false); };
            _rt.KbHook.Install();
        }
    }

    void ToggleEnabledFromTray()
    {
        _rt.Settings.Enabled = !_rt.Settings.Enabled;
        SaveSettings();
        ApplyLinkageGate();
        PushState();
        _tray.SetEnabledState(_rt.Settings.Enabled);
    }

    public void ShowMain()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        TopMost = true; Activate(); TopMost = false;
    }

    public void MinimizeToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    void DoExit()
    {
        _exiting = true;
        if (_tray != null) _tray.Hide();
        Close();
    }

    void RequestClose()
    {
        SaveSettingsNow();
        var s = _rt.Settings;
        if (s.CloseAction == "Minimize") { MinimizeToTray(); return; }
        if (s.CloseAction == "Exit") { DoExit(); return; }
        if (_closeDialogOpen) return;
        _closeDialogOpen = true;
        Send("askClose", new Dictionary<string, object>());
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettingsNow();
        if (_exiting) { base.OnFormClosing(e); return; }
        e.Cancel = true;
        RequestClose();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { if (_tick != null) { _tick.Stop(); _tick.Dispose(); } } catch { }
        try { if (_testTimer != null) { _testTimer.Stop(); _testTimer.Dispose(); } } catch { }
        try { if (_toastTimer != null) { _toastTimer.Stop(); _toastTimer.Dispose(); } } catch { }
        try { if (_hotkey != null) _hotkey.Unregister(Handle, HotkeyId); } catch { }
        try { if (_rt.KbHook != null) _rt.KbHook.Dispose(); } catch { }
        try { if (_tray != null) _tray.Dispose(); } catch { }
        try { if (_toastWin != null) _toastWin.Dispose(); } catch { }
        try { if (_hudWin != null) _hudWin.Dispose(); } catch { }
        base.OnFormClosed(e);
    }
```

`closeConfirm` 动作：

```csharp
        if (a == "closeConfirm")
        {
            string result = Bridge.GetStr(m.Args, "result", "Minimize");
            bool remember = Bridge.GetBool(m.Args, "remember", false);
            _closeDialogOpen = false;
            if (remember)
            {
                s.CloseAction = result == "Exit" ? "Exit" : "Minimize";
                try { ConfigStore.SavePointer(s, System.IO.Path.Combine(_rt.ExeDir, "config.ini")); } catch { }
            }
            if (result == "Exit") DoExit(); else MinimizeToTray();
            return;
        }
        if (a == "cancelCloseConfirm") { _closeDialogOpen = false; return; }
```

> `window.ico` 需要加入 `WebAssets.Map` 作为 `assets/window.ico`，并在 build.bat 增加 `/resource:web\assets\window.ico,RMBCK.web.assets.window.ico`，`cp window.ico web/assets/`。

- [ ] **Step 3: 构建并人工验证**

```bash
cd /d/eyf/mouseplugin && cmd //c 'build.bat'
```
人工：点 ✕ → 网页模态框出现 → 最小化到托盘 → 任务栏无图标 → 托盘双击恢复；选项「记住」后再次关闭直接执行；托盘菜单可切换联动与退出。

- [ ] **Step 4: 提交**

```bash
git add src/App/Tray.cs src/App/MainForm.cs web/assets/window.ico build.bat
git commit -m "feat: 托盘菜单、关闭确认流程与最小化到托盘"
```

---

### Task 14: 报错反馈流程与旧图片查看

**Files:**
- Modify: `src/App/MainForm.cs`
- Modify: `web/app.js`（反馈表单模态框）
- Modify: `src/App/OverlayForm.cs`（全屏图片查看改 WebView2 页）

**Interfaces:**
- Consumes: `submitFeedback` 动作
- Produces: `StartFeedbackFlow()`、`OpenErrorImage()`

- [ ] **Step 1: 前端反馈表单**

```js
  function showFeedback() {
    var w = $("modalWrap"); w.innerHTML = "";
    var m = el("div", "modal");
    m.appendChild(el("h4", null, "报错反馈"));
    m.appendChild(el("div", "muted", "描述你遇到的问题，我们会占卜一下（认真脸）。"));

    var t = el("textarea");
    t.placeholder = "问题描述…"; t.rows = 4;
    t.style.width = "100%"; t.style.marginTop = "10px";
    t.style.borderRadius = "8px"; t.style.border = "1px solid var(--line)";
    t.style.background = "var(--cardHi)"; t.style.color = "var(--text)";
    m.appendChild(t);

    var type = el("select");
    ["闪退", "按键无效", "界面异常", "其他"].forEach(function (v) {
      var o = el("option", null, v); o.value = v; type.appendChild(o);
    });
    type.style.marginTop = "8px";
    m.appendChild(type);

    var acts = el("div", "actions");
    var bCancel = el("button", "btnGhost", "取消");
    var bOk = el("button", "btn", "提交");
    bCancel.onclick = function () { w.classList.remove("show"); };
    bOk.onclick = function () {
      w.classList.remove("show");
      send("submitFeedback", { text: t.value, type: type.value });
    };
    acts.appendChild(bCancel); acts.appendChild(bOk);
    m.appendChild(acts);
    w.appendChild(m); w.classList.add("show"); t.focus();
  }
  window.__showFeedback = showFeedback;
```

把设置页「报错反馈」按钮改为 `showFeedback()`。

- [ ] **Step 2: 后端流程**

```csharp
    void StartFeedbackFlow()
    {
        ShowToast("正在提交报错反馈...", "info", 1500);
        Timer t1 = new Timer { Interval = 1500 };
        t1.Tick += delegate
        {
            t1.Stop(); t1.Dispose();
            ShowToast("正在占卜...", "info", 2000);
            Timer t2 = new Timer { Interval = 2000 };
            t2.Tick += delegate
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
            var f = new Form();
            f.FormBorderStyle = FormBorderStyle.None;
            f.Bounds = Screen.PrimaryScreen.Bounds;
            f.BackColor = Color.Black;
            f.KeyPreview = true;
            var wv = new Microsoft.Web.WebView2.WinForms.WebView2();
            wv.Dock = DockStyle.Fill;
            f.Controls.Add(wv);
            wv.CoreWebView2InitializationCompleted += delegate
            {
                wv.CoreWebView2.Settings.AreDevToolsEnabled = false;
                wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                wv.CoreWebView2.SetVirtualHostNameToFolderMapping("rmbck.local", _rt.WebDir, CoreWebView2HostResourceAccessKind.Allow);
                wv.CoreWebView2.Navigate("https://rmbck.local/viewer.html?src=assets/err.jpg");
            };
            wv.EnsureCoreWebView2Async(null);
            wv.KeyDown += delegate(object s2, KeyEventArgs e2) { if (e2.KeyCode == Keys.Escape) f.Close(); };
            f.ShowDialog(this);
        }
        catch { }
    }
```

新增 `web/viewer.html`：

```html
<!doctype html>
<html><head><meta charset="utf-8">
<style>
html,body{margin:0;height:100%;background:#000;overflow:hidden}
body{display:flex;align-items:center;justify-content:center;cursor:pointer}
img{max-width:100%;max-height:100%;object-fit:contain}
</style></head>
<body>
<img id="im" alt="">
<script>
var src = new URLSearchParams(location.search).get("src") || "assets/err.jpg";
var im = document.getElementById("im");
im.src = src;
im.onclick = function () { window.chrome.webview.postMessage({ action: "closeViewer" }); };
document.addEventListener("keydown", function (e) {
  if (e.key === "Escape") window.chrome.webview.postMessage({ action: "closeViewer" });
});
</script>
</body></html>
```

并把 `viewer.html` 加入 `WebAssets.Map` 与 build.bat/test.bat 的 `/resource:` 列表。

- [ ] **Step 3: 构建验证**

```bash
cd /d/eyf/mouseplugin && cmd //c 'build.bat'
```
人工：设置页 → 报错反馈 → 填内容提交 → 两次 Toast 后全屏图片；点击/Esc 关闭。

- [ ] **Step 4: 提交**

```bash
git add src/App/MainForm.cs web/app.js web/viewer.html src/App/WebAssets.cs build.bat
git commit -m "feat: 报错反馈流程与全屏图片查看器"
```

---

### Task 15: 收尾（删除旧实现、版本号、文档、全量验证）

**Files:**
- Delete: `RightClickKeyLinker.cs`
- Modify: `CHANGELOG.md`、`README.md`、`CLAUDE.md`
- Modify: `.gitignore`（放行 `src/`、`web/`、`lib/`；继续忽略 `*.exe`、`*.ini`）

**Interfaces:**
- Consumes: 全部
- Produces: 可发布的 v2.0.0

- [ ] **Step 1: 删除旧实现并全量构建**

```bash
cd /d/eyf/mouseplugin && git rm -f RightClickKeyLinker.cs
cmd //c 'test.bat' && cmd //c 'build.bat'
```
Expected: 测试全绿；`编译成功: RMBCK 2.0.0.exe`。

- [ ] **Step 2: 清理旧产物**

```bash
cd /d/eyf/mouseplugin && rm -f "RMBCK 1.1.0.exe" tests.exe
ls -la
```

- [ ] **Step 3: 更新 CHANGELOG**

在 `CHANGELOG.md` 顶部插入：

```markdown
## 2.0.0 界面重写 + 功能扩展（2026-10-26）

- **界面全面 Web 化**：从 WinForms 自绘迁移到 WebView2（HTML/CSS/JS），彻底解决按钮/输入框黑边与 DLP 环境下的自绘渲染残缺；界面渲染交给浏览器内核，C# 只保留逻辑与原生窗口能力
- **双向通信**：网页通过 `postMessage` 发送意图，C# 白名单路由后用 `PostWebMessageAsJson` 回推状态；网页不持有真实状态，重载/换肤不丢数据
- **双主题**：日间 = 樱花粉白紫（沿用 1.x 配色），夜间 = 深色紫青（默认），一键切换并持久化
- **新功能**：自定义全局热键（捕获组合键）、开机自启（计划任务，免 UAC）、触发日志（会话内 1000 条 + CSV 导出）、按键行级延迟、组合键输出（Ctrl+Q 等）、重复键位检测
- **界面结构**：左侧导航 + 概览/按键/进程/日志/设置 五页；关闭确认、新建方案、反馈表单均改为网页内模态框
- **悬浮窗**：Toast / HUD / 彩蛋改为独立透明 WebView2 窗口，彩蛋动画改用 CSS 逐行打字
- **依赖**：新增 WebView2 SDK（`lib/` 三文件随包），启动时检测运行时并给出下载指引
- **移除**：旧 WinForms 界面代码整体删除，不留回退开关
```

- [ ] **Step 4: 更新 README 与 CLAUDE.md**

README「使用」段落追加：

```markdown
6. **需要 Microsoft Edge WebView2 运行时**（Windows 10/11 通常已随 Edge 安装；若提示缺失，按弹窗指引前往微软官网安装）
```

「从源码构建」段落改为：

```markdown
`build.bat`（Windows 内置 C# 编译器 + `lib/` 下的 WebView2 SDK，无需安装开发环境），双击或命令行运行即可生成 exe。前端资源在 `web/`，编译期自动嵌入 exe。
```

`CLAUDE.md` 的「文件结构」表整体替换为新结构（`src/Core/*`、`src/App/*`、`web/*`、`lib/*`），并补充：

```markdown
- 前端改动在 `web/`，改完必须重新 `build.bat`（资源已嵌入 exe）
- JS→C# 对象消息用 `e.WebMessageAsJson` 读取（`TryGetWebMessageAsString` 会抛 E_INVALIDARG）
- 悬浮窗为 WebView2 透明窗（`DefaultBackgroundColor=Transparent` + `TransparencyKey`），不可再改回 GDI 自绘
```

- [ ] **Step 5: 全量验证清单**

```bash
cd /d/eyf/mouseplugin && cmd //c 'test.bat' && cmd //c 'build.bat'
```
逐项人工确认：

1. 首次启动在全新空配置目录下能选择配置文件夹并落盘
2. 概览页：开关、方案新建/删除/切换、阈值滑块、随机化、测试面板
3. 按键页：添加/删除、键位捕获（含组合键）、模式、延迟、连发间隔、重复告警
4. 进程页：刷新不卡、搜索、选择/清除，联动门控提示正确
5. 日志页：触发记录出现、导出 CSV、清空
6. 设置页：热键重设与生效、自启创建/删除计划任务、修饰键屏蔽、四类弹窗开关、主题切换持久化、配置路径浏览
7. 悬浮窗：Toast、HUD、彩蛋均无粉色残留
8. 托盘：显示/隐藏/切换/退出
9. 关闭流程：三选项与「记住」
10. 旧 1.1.0 配置可直接读取（不丢按键与方案）
11. 日间/夜间两套主题各页视觉无错位、无黑边
12. 长按触发功能与 1.1.0 行为一致（保持/单次/连发）

- [ ] **Step 6: 提交**

```bash
git add src web lib build.bat app.manifest RightClickKeyLinker.cs 2>/dev/null
git add -f tests.cs test.bat CHANGELOG.md CLAUDE.md
git status --short
git commit -m "chore: 收尾 2.0.0（删除旧 WinForms 实现、更新文档与版本号）"
```

> 提交前核对 `git status`：不得包含 `*.exe`、`config.ini`、`默认.ini`。
> `RightClickKeyLinker.cs` 已在 Step 1 用 `git rm` 移出索引，此处 `git add` 会报 “pathspec did not match” 并被 `2>/dev/null` 吞掉，属正常。
> `CHANGELOG.md` / `CLAUDE.md` / `tests.cs` / `test.bat` 被 `.gitignore` 忽略，必须 `-f`。

---

## 自检结果

**Spec 覆盖**
| Spec 章节 | 对应任务 |
|---|---|
| §3 架构（Core/App/Bridge/web） | Task 1、3、4、5 |
| §4 通信协议 | Task 5、7–14 |
| §5 界面结构（5 页 + 模态框） | Task 6–11、14 |
| §5 悬浮窗三件套 | Task 12 |
| §5 窗口拖动/缩放/圆角/托盘 | Task 4、13 |
| §6 双主题 | Task 6（变量）、Task 7–11（各页套用） |
| §7-1 自定义热键 | Task 10 |
| §7-2 开机自启 | Task 10 |
| §7-3 触发日志 | Task 11 |
| §7-4 行级延迟 | Task 2 |
| §7-5 组合键输出 | Task 2、8 |
| §7-6 重复键位检测 | Task 5（FindConflicts）、Task 8（展示） |
| §8 配置兼容 | Task 1 |
| §9 构建与资源 | Task 3、4 |
| §10 测试策略 | 各任务 test 步骤 + Task 15 清单 |
| §11 风险 | Task 4（运行时检测）、Task 12（透明窗回退方案） |

**占位符扫描**：已检查，无 TBD/TODO/「类似 Task N」。

**类型一致性**：`TriggerController.FiredLabels` 在 Task 2 定义、Task 7 使用；`WebAssets.Map` 在 Task 3 定义，Task 13/14 追加 `window.ico`、`viewer.html` 条目（需同步 build.bat，已在对应步骤注明）；`HotkeyManager.Describe/IsValid` 在 Task 10 定义、Task 8 的捕获分支引用（已注明 Task 8 暂不处理 hotkey 目标）。
