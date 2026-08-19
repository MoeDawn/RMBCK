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
  /resource:err.jpg,RMBKeyLinker.err.jpg ^
  /resource:window.ico,RMBKeyLinker.window.ico ^
  /resource:icon.png,RMBKeyLinker.icon.png ^
  /resource:iconf1.png,RMBKeyLinker.iconf1.png ^
  /resource:iconf2.png,RMBKeyLinker.iconf2.png ^
  /resource:iconf3.png,RMBKeyLinker.iconf3.png ^
  /resource:iconf4.png,RMBKeyLinker.iconf4.png ^
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  RightClickKeyLinker.cs
if %errorlevel%==0 (echo 编译成功: RMBCK 1.1.0.exe) else (echo 编译失败 & exit /b 1)
