@echo off
rem Builds, tests, signs and packs a release into dist\ (zip, installer, checksums) and refreshes the Scoop manifest.
rem Signing uses the Coursion certificate (set CODESIGN_THUMBPRINT, see tools\sign.ps1); without it the files stay unsigned.
rem Then publish with: powershell -File tools\publish.ps1
setlocal
cd /d "%~dp0"
call "%~dp0build.bat" || exit /b 1
call "%~dp0tests\run-tests.bat" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\sign.ps1" WinZones.exe || exit /b 1
call "%~dp0build-setup.bat" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\sign.ps1" WinZonesSetup.exe || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\package.ps1" || exit /b 1
