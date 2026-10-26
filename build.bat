@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
del /q "RMBKeyLinker.exe" 2>nul
del /q "RMBCK 0.2.16.exe" 2>nul
rem 注意：exe 文件名里的版本号需与 RightClickKeyLinker.cs 的 AssemblyVersion 保持一致，升版本时同步修改
"%CSC%" /nologo /codepage:65001 /target:winexe /out:"RMBCK 1.1.0.exe" ^
  /win32manifest:app.manifest ^
  /win32icon:icon.ico ^
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
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  src\Core\Settings.cs src\Core\Keys.cs src\Core\Hooks.cs src\Core\Trigger.cs ^
  src\Core\Hotkey.cs src\Core\Autostart.cs ^
  src\App\WebAssets.cs ^
  RightClickKeyLinker.cs
if %errorlevel%==0 (echo 编译成功: RMBCK 1.1.0.exe & copy /y lib\*.dll . >nul) else (echo 编译失败 & exit /b 1)
