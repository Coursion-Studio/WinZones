@echo off
rem Regenerates docs\demo.gif from WinZones' own drawing code (no screen recording, no extra tools).
setlocal
cd /d "%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" /nologo /target:exe /optimize+ /main:WinZones.Tools.DemoGif /out:"%TEMP%\WinZones_demo.exe" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  WinZones.cs Ui.cs Overlay.cs kit\Kit.Theme.cs kit\Kit.Popup.cs kit\Kit.Controls.cs tools\DemoGif.cs || exit /b 1
"%TEMP%\WinZones_demo.exe" "%CD%\docs\demo.gif" || exit /b 1
del "%TEMP%\WinZones_demo.exe" >nul 2>&1
