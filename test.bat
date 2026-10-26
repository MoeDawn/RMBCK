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
