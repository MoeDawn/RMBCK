这些规则极其重要，你的所有回答必须以此为基准,你在每次输出前，先确保完全理解我的需求，达到 95% 的置信度。如果信息缺失或描述不清，每轮提出一个必要澄清问题，根据我的回答持续递进提问，直到信息完备后再输出结果。如果你调用了skill,要告诉我使用的skill的名字和用途
# RMBCK 右键长按联动器

C# WinForms 宿主 + WebView2 前端，用 Windows 自带 csc.exe 编译，WebView2 SDK 随包（`lib/`）。
长按右键超过阈值自动触发配置的键盘按键（保持/点按单次/点按连发），
支持随机抖动防检测、目标进程门控、全局热键、开机自启、触发日志。

## 关键约束

- **C# 5 语法**（csc 是 .NET 4 内置编译器，不支持现代语法）
- **build.bat / test.bat 必须保持 CRLF 行尾**（cmd 无法正确解析 LF）；不要用 py/bash 重写 .bat（DLP 会重新加密），需要修改时使用 Edit 工具
- Git Bash 下运行: `cmd //c 'd:/eyf/mouseplugin/build.bat'`
- 前端改动在 `web/`，改完必须重新执行 `build.bat`（资源编译期嵌入 exe，缓存键是内容哈希）
- JS→C# 对象消息用 `e.WebMessageAsJson` 读取（`TryGetWebMessageAsString` 对对象消息抛 E_INVALIDARG）
- 悬浮窗是 WebView2 透明窗（`DefaultBackgroundColor=Transparent` + `TransparencyKey`），不可改回 GDI 自绘；`CoreWebView2Environment` 全进程只创建一次并共享
- 进程枚举、计划任务查询/切换必须放线程池，完成后 `BeginInvoke` 回 UI 线程
- 版本号更新需同步: `src/Program.cs` 的三个 Assembly*Version + `build.bat` 的 exe 文件名
- 只有大功能和底层优化才升版本号，UI 微调/修复/彩蛋不升版本（记 CHANGELOG 即可）
- 旧版 WinForms 自绘控件已全部删除，不再需要给每个控件 `Invalidate(); Update();` 强制重绘；仅主窗口启动骨架与悬浮窗首显保留一次强制重绘，用于绕过 DLP 丢失 WM_PAINT

## 文件结构

| 文件 | 说明 |
|------|------|
| `src/Program.cs` | 入口：WebView2 运行时检测、配置引导、唯一 WebView2 环境、钩子安装 |
| `src/Core/Settings.cs` | 设置模型 + INI 读写（`config.ini` 指针 + `<方案名>.ini`） |
| `src/Core/Keys.cs` | 键名映射、SendInput 按键注入 |
| `src/Core/Hooks.cs` | 鼠标/键盘低级钩子 |
| `src/Core/Trigger.cs` | 长按触发状态机（保持/单次/连发、行级延迟、组合键） |
| `src/Core/Hotkey.cs` | 全局热键注册、描述与校验 |
| `src/Core/Autostart.cs` | 计划任务开机自启 |
| `src/Core/History.cs` | 触发日志环形缓冲（1000 条）+ CSV 导出 |
| `src/App/WebAssets.cs` | `web/` 资源嵌入、解压、内容哈希缓存 |
| `src/App/Bridge.cs` | 网页消息 JSON 解析/序列化 |
| `src/App/MainForm.cs` | 主窗口、无边框拖动/缩放、托盘、动作路由与状态推送 |
| `src/App/OverlayForm.cs` | 透明悬浮窗（Toast / HUD / 彩蛋） |
| `src/App/Tray.cs` | 托盘图标与右键菜单 |
| `web/` | 前端资源（`index.html`/`app.css`/`app.js`/`overlay.*`/`viewer.html`/`assets/`） |
| `lib/` | WebView2 SDK 三文件（随包跟踪） |
| `tests.cs` | 纯逻辑测试（无框架，`/main:TestRunner`） |
| `build.bat` | 编译 → `RMBCK 2.0.0.exe`，嵌入 `web/` 资源并把 `lib/` DLL 复制到根目录 |
| `test.bat` | 编译并运行纯逻辑测试（不含 MainForm/OverlayForm） |
| `CHANGELOG.md` | 完整版本历史 |
| `app.manifest` | UAC 提权 + Common-Controls v6 |
| `config.ini` | 运行时指针文件（gitignored） |
