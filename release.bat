@echo off
rem Builds, tests and packs a release into dist\ (zip, installer, checksums) and refreshes the Scoop manifest.
rem The GitHub "Release" workflow does the same on a clean machine (and signs the files) when you push a vX.Y.Z tag.
setlocal
cd /d "%~dp0"
call "%~dp0build.bat" || exit /b 1
call "%~dp0tests\run-tests.bat" || exit /b 1
call "%~dp0build-setup.bat" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\package.ps1" || exit /b 1
