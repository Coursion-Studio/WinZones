@echo off
rem Regenerates docs\demo.gif and the README screenshot cards (docs\shot-*.png)
rem from WinZones' own drawing code. No screen recording, no extra tools.
setlocal
cd /d "%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set "EXE=%TEMP%\WinZones_demo.exe"
"%CSC%" /nologo /target:exe /optimize+ /main:WinZones.Tools.DemoGif /out:"%EXE%" ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  WinZones.cs Ui.cs Overlay.cs Updater.cs kit\Kit.Theme.cs kit\Kit.Popup.cs kit\Kit.Controls.cs tools\DemoGif.cs tools\Showcase.cs || exit /b 1
"%EXE%" "%CD%\docs\demo.gif" || exit /b 1
"%EXE%" --stills "%CD%\docs" || exit /b 1
del "%EXE%" >nul 2>&1
