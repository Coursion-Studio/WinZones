@echo off
rem Builds the Microsoft Store package: dist\store\WinZones-<version>.msix
rem Needs the Windows 10/11 SDK (makeappx, makepri). Identity comes from store\identity.txt.
setlocal EnableDelayedExpansion
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "FX=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "KITS=%ProgramFiles(x86)%\Windows Kits\10"
set "SDKBIN="
for /f "delims=" %%d in ('dir /b /ad /o-n "%KITS%\bin\10.*" 2^>nul') do if not defined SDKBIN if exist "%KITS%\bin\%%d\x64\makeappx.exe" set "SDKBIN=%KITS%\bin\%%d\x64"
set "WINMD="
for /f "delims=" %%d in ('dir /b /ad /o-n "%KITS%\UnionMetadata\10.*" 2^>nul') do if not defined WINMD if exist "%KITS%\UnionMetadata\%%d\Windows.winmd" set "WINMD=%KITS%\UnionMetadata\%%d\Windows.winmd"
if not defined SDKBIN ( echo Windows SDK not found. Install it: winget install Microsoft.WindowsSDK.10.0.26100 & exit /b 1 )
if not defined WINMD ( echo Windows.winmd not found in the Windows SDK. & exit /b 1 )

rem ---- identity
for /f "usebackq tokens=1,* delims==" %%a in ("store\identity.txt") do (
  set "k=%%a"
  if not "!k:~0,1!"=="#" set "%%a=%%b"
)
if "%Name%"=="" ( echo store\identity.txt is missing Name & exit /b 1 )

set "OUT=dist\store"
set "PKG=%OUT%\package"
if exist "%PKG%" rmdir /s /q "%PKG%"
mkdir "%PKG%\Assets" 2>nul

rem ---- 1) Store build of the app (STORE: no in-app updater, startup via StartupTask)
if not exist WinZones.ico call build.bat >nul || exit /b 1
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /define:STORE /out:"%PKG%\WinZones.exe" ^
  /win32icon:WinZones.ico /win32manifest:app.manifest /nowarn:162 ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  /r:"%WINMD%" /r:"%FX%\System.Runtime.dll" /r:"%FX%\System.Runtime.WindowsRuntime.dll" /r:"%FX%\System.Runtime.InteropServices.WindowsRuntime.dll" ^
  WinZones.cs Ui.cs Overlay.cs Updater.cs kit\Kit.Theme.cs kit\Kit.Popup.cs kit\Kit.Controls.cs store\StoreStartup.cs || goto :fail

rem ---- 2) version: x.y.z from the exe -> x.y.z.0 (the Store requires the 4th part to be 0)
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Get-Item '%PKG%\WinZones.exe').VersionInfo.ProductVersion"`) do set "VER=%%v.0"

rem ---- 3) logos + manifest + resources.pri
start "" /wait "%PKG%\WinZones.exe" --make-store-assets "%CD%\%PKG%\Assets" || goto :fail
powershell -NoProfile -Command "$m = Get-Content -Raw 'store\AppxManifest.template.xml'; $m = $m.Replace('$NAME$', $env:Name).Replace('$PUBLISHER$', $env:Publisher).Replace('$PUBLISHER_DISPLAY$', $env:PublisherDisplayName).Replace('$VERSION$', $env:VER); [IO.File]::WriteAllText('%CD%\%PKG%\AppxManifest.xml', $m, (New-Object Text.UTF8Encoding $false))" || goto :fail
"%SDKBIN%\makepri.exe" createconfig /cf "%OUT%\priconfig.xml" /dq en-US /o >nul || goto :fail
rem one combined resources.pri (no split resource packs: we ship a single .msix)
powershell -NoProfile -Command "$p = (Resolve-Path '%OUT%\priconfig.xml').Path; $x = [xml](Get-Content -Raw $p); foreach ($n in @($x.SelectNodes('//packaging'))) { [void]$n.ParentNode.RemoveChild($n) }; $x.Save($p)" || goto :fail
"%SDKBIN%\makepri.exe" new /pr "%PKG%" /cf "%OUT%\priconfig.xml" /mn "%PKG%\AppxManifest.xml" /of "%PKG%\resources.pri" /o >nul || goto :fail

rem ---- 4) pack (validates the manifest against the schema)
set "MSIX=%OUT%\WinZones-%VER%.msix"
"%SDKBIN%\makeappx.exe" pack /d "%PKG%" /p "%MSIX%" /o || goto :fail
echo.
echo Built %MSIX%  (identity: %Name%, version %VER%)
echo Upload it in Partner Center ^> your app ^> Packages. The Store signs it for you.
exit /b 0

:fail
echo.
echo STORE BUILD FAILED
exit /b 1
