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
  src\App\WebAssets.cs src\App\Bridge.cs tests.cs
if not %errorlevel%==0 exit /b 1
.\tests.exe
exit /b %errorlevel%
