@echo off
rem Builds WinZonesSetup.exe: a per-user installer with WinZones.exe embedded. Run build.bat first.
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist WinZones.exe ( echo WinZones.exe not found. Run build.bat first. & exit /b 1 )

for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item 'WinZones.exe').VersionInfo.ProductVersion"`) do set "VER=%%v"
if "%VER%"=="" ( echo Could not read the WinZones.exe version. & exit /b 1 )

if not exist obj mkdir obj
> obj\SetupVersion.cs echo [assembly: System.Reflection.AssemblyVersion("%VER%.0")]
>>obj\SetupVersion.cs echo [assembly: System.Reflection.AssemblyFileVersion("%VER%.0")]
>>obj\SetupVersion.cs echo [assembly: System.Reflection.AssemblyInformationalVersion("%VER%")]
>>obj\SetupVersion.cs echo namespace WinZonesSetup { internal static class Build { public const string Version = "%VER%"; } }

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /out:WinZonesSetup.exe ^
  /win32icon:WinZones.ico /win32manifest:app.manifest ^
  /resource:WinZones.exe,payload.WinZones.exe /resource:LICENSE,payload.LICENSE.txt /resource:WinZones.ico,setup.icon.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  setup\Setup.cs obj\SetupVersion.cs kit\Kit.Theme.cs kit\Kit.Popup.cs kit\Kit.Controls.cs || goto :fail
echo Built WinZonesSetup.exe (WinZones %VER%)
exit /b 0

:fail
echo.
echo SETUP BUILD FAILED
exit /b 1
