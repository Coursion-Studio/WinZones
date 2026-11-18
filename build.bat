@echo off
rem Builds WinZones.exe with the C# compiler that ships with Windows (.NET Framework 4.x). Nothing to install.
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find csc.exe from .NET Framework 4.
  exit /b 1
)
set "REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll"
set "SRC=WinZones.cs Ui.cs Overlay.cs"

rem 1) generate the app icon from the artwork in Ui.cs (only if it does not exist yet)
if not exist WinZones.ico (
  echo Generating WinZones.ico ...
  "%CSC%" /nologo /target:winexe /out:"%TEMP%\WinZones_iconmaker.exe" %REFS% %SRC% || goto :fail
  start "" /wait "%TEMP%\WinZones_iconmaker.exe" --make-icon "%CD%\WinZones.ico"
  del "%TEMP%\WinZones_iconmaker.exe" >nul 2>&1
  if not exist WinZones.ico goto :fail
)

rem 2) build the app with icon + manifest (DPI awareness, Windows 10/11)
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /out:WinZones.exe ^
  /win32icon:WinZones.ico /win32manifest:app.manifest %REFS% %SRC% || goto :fail
echo Built WinZones.exe
exit /b 0

:fail
echo.
echo BUILD FAILED
exit /b 1
