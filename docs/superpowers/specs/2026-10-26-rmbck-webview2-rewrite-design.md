# RMBCK 2.0.0 — WebView2 界面重写设计

日期：2026-10-26
状态：待评审
目标版本：2.0.0

## 1. 背景与目标

当前 1.1.0 用 WinForms 自绘实现全部界面。在 E-SafeNet DLP 环境下，自绘控件依赖
`Invalidate(); Update();` 强制重绘来绕过 WM_PAINT 丢失，原生控件（按钮、输入框、
下拉框）仍有黑边和残缺渲染，维护成本高。

**目标**：界面层整体迁移到 WebView2（HTML + CSS + JS），业务逻辑保留在 C#，
通过 WebView2 消息通道双向通信。同时扩展功能、统一日间/夜间双主题。

**非目标**：不做安装包、不做自动更新、不做多语言、发布版不开放开发者工具、
不改动触发逻辑的既有行为语义（保持/单次/连发）。

**明确移除**：现有 WinForms 界面代码（`RightClickKeyLinker.cs` 中的 MainForm /
CloseDialog / PromptForm / FeedbackForm / ToastForm / HudForm / LayeredForm /
SecretDot / FlatButton / RoundedSlider / BorderBox / Theme / Ui 等）整体删除，
不保留回退开关；单文件结构拆分为 `src/` 下的多文件。

## 2. 已验证的技术前提（2026-10-26 本机实测）

| 验证项 | 结论 |
|---|---|
| csc.exe (.NET 4, C# 5) 直接引用 WebView2 SDK | 编译通过 |
| 本机 WebView2 运行时 | 已安装，`BrowserVersionString = 155.0.4283.62` |
| DLP 环境下网页渲染（中文/渐变/圆角/字体） | 正常，截图确认无黑边 |
| 透明悬浮窗（`DefaultBackgroundColor=Transparent` + 分层窗体） | 四周像素 alpha≈0 |
| `SetVirtualHostNameToFolderMapping` + 相对路径 CSS/JS | 正常加载 |
| JS `postMessage` ⇄ C# `WebMessageReceived` / `ExecuteScriptAsync` | 往返正常 |

SDK 包：`microsoft.web.webview2` 1.0.4314.35，占用 3 个文件：
`Microsoft.Web.WebView2.Core.dll`、`Microsoft.Web.WebView2.WinForms.dll`、
`WebView2Loader.dll`（约 1.5MB，随仓库提交到 `lib/`）。

## 3. 架构

```
RMBCK 2.0.0.exe  (C# 外壳：全部状态与逻辑)
├─ Program            启动、WebView2 运行时检测、UAC(manifest)、托盘
├─ Core
│   ├─ Settings       AppSettings / KeyConfig / ConfigStore (INI)
│   ├─ Keys           KeyNames、IKeySender、SendInputKeySender
│   ├─ Trigger        TriggerController（阈值、抖动、三种模式、行延迟、组合键）
│   ├─ Hooks          MouseHook (WH_MOUSE_LL)、KeyboardHook（捕获用）
│   ├─ Hotkey         RegisterHotKey 封装 + 捕获校验
│   ├─ Autostart      计划任务 schtasks 包装
│   └─ History        环形缓冲 + CSV 导出
├─ App
│   ├─ Bridge         JSON 动作白名单路由；单向事件推送
│   ├─ MainForm       无边框窗体 + WebView2 铺满 + 6px 原生缩放边
│   ├─ OverlayForm    Toast / HUD / 彩蛋（透明悬浮窗）
│   └─ Tray           NotifyIcon（WinForms 唯一保留的原生 UI）
└─ web/               
    ├─ index.html  app.css  app.js
    ├─ overlay.html overlay.css overlay.js
    └─ assets/    icon.png iconf1..4.png err.jpg
```

**职责边界**：网页不持有真实状态，只做「渲染快照 + 发送意图」。C# 是唯一事实源，
每次变更后推送完整或增量状态。这样界面刷新、页面重载、主题切换都不会丢数据。

**线程模型**：鼠标/键盘钩子、消息路由、定时器全部在 UI 线程；进程枚举沿用
1.1.0 的线程池异步 + 代数计数丢弃过期结果。

## 4. 通信协议

### JS → C#（`window.chrome.webview.postMessage(JSON 字符串)`）

```
{"action":"setSetting","key":"longPressMs","value":450}
{"action":"addKey"}                            → 追加一行默认按键
{"action":"removeKey","index":2}
{"action":"setKey","index":0,"field":"mode","value":"TapRepeat"}
{"action":"captureKey","target":"row","index":0}
{"action":"captureKey","target":"hotkey"}
{"action":"toggleEnabled"}
{"action":"switchProfile","name":"战斗"}
{"action":"newProfile","name":"..."}
{"action":"deleteProfile","name":"..."}
{"action":"refreshProcesses"}
{"action":"selectProcess","name":"ssjj_battle.exe"}   // "" = 清除
{"action":"chooseFolder","which":"config"}
{"action":"setTheme","mode":"Day|Night"}
{"action":"setAutostart","on":true}
{"action":"startTest"} / {"action":"stopTest"}
{"action":"exportHistory"}
{"action":"closeConfirm","result":"Minimize|Exit","remember":true|false}
{"action":"cancelCloseConfirm"}
{"action":"dragStart"}
{"action":"openHomepage"} / {"action":"submitFeedback"}
{"action":"minimizeToTray"} / {"action":"exitApp"}
```

### C# → JS（`ExecuteScriptAsync("window.__native(<json>)")`）

```
{"type":"state","data":{ ...完整快照... }}
{"type":"testStatus","data":{"down":true,"triggered":["Q·保持"],"physical":"Q (0x51)","inTarget":true}}
{"type":"processes","data":[{"name":"a.exe","title":"窗口标题"}]}
{"type":"history","data":[{"time":"14:32:05","keys":"Ctrl+Q","proc":"a.exe","mode":"保持"}]}
{"type":"toast","data":{"text":"...","kind":"ok|err|info","ms":1500}}
{"type":"overlay","data":{"kind":"hud","text":"⚡ Q E"}}
{"type":"askClose"}
{"type":"capturing","active":true,"target":"hotkey"}  // 捕获态视觉反馈
{"type":"error","data":{"text":"..."}}
```

**安全**：`action` 走白名单 switch，未知动作丢弃；JSON 由 `JavaScriptSerializer`
生成，不做字符串拼接；WebView2 只允许导航 `https://rmbck.local/`；发布版关闭
`AreDefaultContextMenusEnabled` 与 `AreDevToolsEnabled`；禁止新窗口（`NewWindowRequested` 取消）。

## 5. 界面结构

无边框窗口，默认 1040×720，最小 900×620，可缩放。左侧图标导航（168px）+ 右侧内容区。

| 页面 | 内容 |
|---|---|
| 概览 | 联动总开关（大号）、方案下拉 + 新建/删除、长按阈值滑块与数字双向联动、随机化开关、实时状态徽章、测试面板（右键按下/释放、当前触发键位、本次延时、实际按下键、目标进程门控状态） |
| 按键 | 卡片行：启用 / 键位（可点开按键捕获或下拉选择）/ 模式 / 行延迟 / 连发间隔 / 删除；「添加按键」；重复键位告警条 |
| 进程 | 搜索框 + 进程列表（名称+标题）、刷新、清除、门控说明 |
| 日志 | 触发历史列表（时间/键位/进程/模式）、清空、导出 CSV |
| 设置 | 全局热键捕获框、开机自启、Ctrl/Shift/Alt/Win 屏蔽、提示/彩蛋/HUD/热键提示/动态图标开关、主题（日/夜）、配置路径+浏览、报错反馈、关于与页脚 |

**网页内模态框**（替换原 WinForms 弹窗）：关闭确认（最小化/退出/记住）、新建方案、
修改方案名、删除确认、报错反馈表单。全部 HTML/CSS，随主题换肤。

**透明悬浮窗**（独立小窗，首次使用时创建后复用）：
- Toast 400×100，右上角滑入滑出
- HUD 280×64，右下角，⚡ + 触发键，微脉冲
- 彩蛋 480×自适应高度，逐行亮起 + 打字机 + 渐隐，CSS 动画实现；文案与现有版本逐字一致

**窗口交互**：
- 拖拽：标题栏 `mousedown` → 发 `dragStart` → C# `ReleaseCapture()` +
  `SendMessage(WM_NCLBUTTONDOWN, HTCAPTION)`
- 缩放：`WM_NCHITTEST` 在四周 6px 返回对应 HT* 命中码。**WebView2 必须用
  `Padding = 6px` 内缩**，否则其子 HWND 吞掉边缘命中测试，缩放完全失效；
  `lParam` 坐标按**有符号 short** 解码（副屏负坐标），且不可直接 `ToInt32()`
  （bit31 置位时 .NET 4 抛 `OverflowException`）
- 圆角：Win11 用 DWM 系统圆角；Win10 用窗体 Region
- 最小化按钮 → 收到托盘（沿用 1.1.0 行为，任务栏隐藏）

## 6. 主题系统

单一布局，CSS 变量换肤，切换带 180ms 过渡。

### 日间模式（现有樱花粉白紫平移）

```
--bg #FFF0F5   --card #FFFFFF        --text #4A4A68   --text-sub #9A9AAF
--btn #FF6FA5  --btn-hover #FF85B3   --btn-press #F05A94
--accent #A78BFA                     --line #FAD7E3   --track #FFD1E0  --sel #FFD9E8
--grad-1 #FF7AAC  --grad-2 #BA94FF   --glass rgba(255,255,255,.84)
--ok #4CAF50  --err #EB5A6E  --warn #F0A028
背景：粉底 + 粉/紫柔光斑；卡片：半透明白玻璃 + 高光描边
```

### 夜间模式（现代深色紫青）

```
--bg #12141C   --card #191D29 / rgba(255,255,255,.04)   --text #E8EAF3  --text-sub #9AA3B8
--btn linear-gradient(90deg,#7C5CFF,#22D3EE)            --accent #22D3EE
--line rgba(255,255,255,.10)  --track rgba(255,255,255,.12)  --sel rgba(124,92,255,.25)
--ok #34D399  --err #F87171  --warn #FBBF24
背景：深色径向渐变；卡片：半透明面板 + 1px 内高光 + 深阴影
```

**默认夜间**（本次重写的主视觉），可在设置页一键切换，随 `config.ini` 全局持久化
（`Theme=Night|Day`），不随方案切换。

## 7. 新功能

1. **自定义全局热键**：点击捕获框 → `KeyboardHook` 抓组合键 → 校验（至少一个修饰键，
   禁止纯修饰键）→ `RegisterHotKey`。注册失败（被占用）时 Toast 报错并保留原热键。
   默认仍为 Ctrl+F12。
2. **开机自启**：程序需管理员权限，采用计划任务
   （`schtasks /Create /TN RMBCK /RL HIGHEST /SC ONLOGON /TR "<exe>"`），避免登录时 UAC
   弹窗；关闭时 `/Delete`。设置页注明"将创建计划任务 RMBCK"。
3. **触发日志**：内存环形缓冲（最近 1000 条），记录时间 / 键位组合 / 前台进程 / 模式。
   仅会话内保留，重启清空；可导出 CSV 到用户选择的位置。
4. **每行独立延迟**：`KeyConfig.DelayMs`——到达阈值后延迟该毫秒再触发本行，可做
   A 先 B 后的组合节奏；连发间隔本就按行（`RepeatIntervalMs`），移到界面直接编辑。
5. **组合键输出**：`KeyConfig.Mods` 位掩码（Ctrl=1/Shift=2/Alt=4/Win=8）。输出顺序
   修饰键 down → 键操作 → 修饰键 up；保持模式下修饰键随之保持、松开右键一并释放。
   捕获键位时自动识别修饰键。

   **修饰键生命周期（2026-10-26 定）**：修饰键只在「**已触发**的保持行」需要时才按住。
   若同一修饰键同时被「点按行」和「尚未到点的保持行」声明，点按行结束后立刻释放该
   修饰键，等到保持行真正触发时再按下——避免出现「修饰键独自悬空、无主键」的意外输入。

6. **重复键位检测**：同方案内相同（Mods+Vk）重复时页顶告警 + 行高亮，不阻止保存。

## 8. 配置格式

INI 保持人类可读，向后兼容 1.1.0：

- `[General]` 新增：`HotkeyMods`、`HotkeyVk`、`Autostart`（读取时以计划任务实际状态为准）
- `[KeyN]` 新增：`DelayMs`、`Mods`
- `config.ini` 指针文件新增：`Theme`
- 旧配置缺失字段取默认值；未知字段忽略；不删除既有字段
- 版本号：`AssemblyVersion`/`AssemblyFileVersion`/`AssemblyInformationalVersion` → 2.0.0，
  产物 `RMBCK 2.0.0.exe`
- 程序集元数据与托盘提示文案保持不变：Company `SSJJ2.5`、Title/Description
  `千恋万花安装器(反MC)`、托盘 `Senren＊Banka`
- 现有图片资源（`icon.ico`、`icon.png`、`iconf1~4.png`、`err.jpg`）迁移到 `web/assets/`
  并在构建时嵌入；`icon.ico` 继续用作 exe 图标

## 9. 构建与资源

```
build.bat
  csc /target:winexe /out:"RMBCK 2.0.0.exe"
      /win32manifest:app.manifest /win32icon:icon.ico
      /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll
      /r:System.Drawing.dll /r:System.Web.Extensions.dll
      /r:lib/Microsoft.Web.WebView2.Core.dll
      /r:lib/Microsoft.Web.WebView2.WinForms.dll
      /resource:web/index.html,RMBCK.web.index.html  ... (web/ 全量嵌入)
      src/Program.cs src/Core/*.cs src/App/*.cs
  copy lib\*.dll 到输出目录（含 WebView2Loader.dll）
```

- 运行时把嵌入资源解压到 `%LOCALAPPDATA%\RMBCK\web\<版本>\`，按版本目录管理，
  升级时清理旧目录
- `SetVirtualHostNameToFolderMapping("rmbck.local", 解压目录, Allow)`，
  导航 `https://rmbck.local/index.html`
- `WebView2Loader.dll` 必须与 exe 同目录
- UserDataFolder 固定为 `%LOCALAPPDATA%\RMBCK\WebView2`，避免污染 exe 目录
- `test.bat` 只编译 Core + tests，不引用 WebView2（测试可无界面运行）

## 10. 测试策略

- 保留 `tests.cs` 全部现有断言（KeyNames、SendInput 结构、ConfigStore、TriggerController）
- 新增：新 INI 字段往返、行延迟时序、组合键发送顺序、重复键位检测、Bridge 动作解析
  （以假 sender 注入，不启动 WebView2）
- 构建后启动真实程序，用 CDP `CapturePreviewAsync` 截图自查日/夜两套主题与各页面，
  截图有效性先确认非全黑；最终视觉以用户实机运行为准
- 交互项（拖拽、缩放、托盘、热键、自启、按键注入）需用户实机确认

## 11. 风险

| 风险 | 应对 |
|---|---|
| 目标机无 WebView2 运行时 | 启动时检测环境，失败弹原生 MessageBox：说明 + 「前往微软下载」+「退出」 |
| 内存占用上升 | 悬浮窗共享同一运行时；不使用时常驻一个浏览器进程组（可接受） |
| 无边框缩放/拖动在个别 DPI 下偏移 | 6px 原生边 + JS 拖拽；人工验收 |
| 计划任务需提权 | 程序本身已 requireAdministrator |
| 首次解压资源耗时 | 版本目录已存在则跳过；单次 <50ms |

## 12. 交付顺序（供计划阶段参考）

1. 骨架：目录拆分 + WebView2 宿主 + 消息通道 + 运行时检测
2. 核心页：概览（开关/方案/阈值/随机化）
3. 按键页（含捕获、延迟、组合键、冲突检测）
4. 测试面板 + 进程页
5. 设置页（热键/自启/屏蔽/弹窗开关/主题/路径）
6. 悬浮窗三件套 + 彩蛋 + 报错反馈
7. 托盘/关闭流程/日志导出
8. 双主题精修 + 构建脚本 + 测试补齐
