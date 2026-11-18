@echo off
rem Builds WinZones and packs a release zip into dist\ (exe + README + LICENSE).
setlocal
cd /d "%~dp0"
call "%~dp0build.bat" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$v = (Get-Item .\WinZones.exe).VersionInfo.ProductVersion;" ^
  "New-Item -ItemType Directory -Force dist | Out-Null;" ^
  "$zip = \"dist\WinZones-$v-win.zip\";" ^
  "Compress-Archive -Force -Path WinZones.exe, README.md, LICENSE -DestinationPath $zip;" ^
  "$hash = (Get-FileHash $zip -Algorithm SHA256).Hash;" ^
  "Set-Content -Encoding ascii \"$zip.sha256\" \"$hash  $(Split-Path $zip -Leaf)\";" ^
  "Write-Host \"Release: $zip\"; Write-Host \"SHA256:  $hash\"" || exit /b 1
