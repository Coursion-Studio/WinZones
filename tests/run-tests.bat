@echo off
rem Builds and runs the WinZones unit tests. Builds WinZones.exe first if it is missing.
setlocal
cd /d "%~dp0.."
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist WinZones.exe call build.bat || exit /b 1
if not exist obj\tests mkdir obj\tests
copy /y WinZones.exe obj\tests\WinZones.exe >nul
"%CSC%" /nologo /target:exe /out:obj\tests\WinZones.Tests.exe /r:obj\tests\WinZones.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll tests\Tests.cs || exit /b 1
obj\tests\WinZones.Tests.exe
