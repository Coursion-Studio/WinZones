# Packs an already-built WinZones.exe and WinZonesSetup.exe into dist\ and writes the Scoop manifest.
#   dist\WinZones-X.Y.Z-win.zip (+ .sha256)    portable zip; the in-app updater downloads this one
#   dist\WinZonesSetup-X.Y.Z.exe (+ .sha256)   per-user installer
#   packaging\scoop\winzones.json              Scoop manifest
# release.bat runs it locally; the Release workflow runs it after signing and commits the Scoop manifest,
# so the hashes always match the files that were really published.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$v = (Get-Item .\WinZones.exe).VersionInfo.ProductVersion
$sv = (Get-Item .\WinZonesSetup.exe).VersionInfo.ProductVersion
if ($v -ne $sv) { throw "WinZones.exe is $v but WinZonesSetup.exe is $sv. Run build-setup.bat again." }

New-Item -ItemType Directory -Force dist | Out-Null
function Write-Sha($file) {
    $hash = (Get-FileHash $file -Algorithm SHA256).Hash
    Set-Content -Encoding ascii "$file.sha256" "$hash  $(Split-Path $file -Leaf)"
    return $hash
}

$zip = "dist\WinZones-$v-win.zip"
Compress-Archive -Force -Path WinZones.exe, README.md, LICENSE -DestinationPath $zip
$zipHash = Write-Sha $zip
$setup = "dist\WinZonesSetup-$v.exe"
Copy-Item -Force WinZonesSetup.exe $setup
$setupHash = Write-Sha $setup

# ---------------------------------------------------------------- Scoop manifest
$base = "https://github.com/Coursion-Studio/WinZones/releases/download/v$v"
$enc = New-Object Text.UTF8Encoding $false
New-Item -ItemType Directory -Force packaging\scoop | Out-Null
[IO.File]::WriteAllText("$root\packaging\scoop\winzones.json", @"
{
    "version": "$v",
    "description": "Snap windows into zones with Shift + drag, and arrange every window with one key.",
    "homepage": "https://github.com/Coursion-Studio/WinZones",
    "license": "MIT",
    "url": "$base/WinZones-$v-win.zip",
    "hash": "$($zipHash.ToLowerInvariant())",
    "bin": "WinZones.exe",
    "shortcuts": [["WinZones.exe", "WinZones"]],
    "checkver": "github",
    "autoupdate": {
        "url": "https://github.com/Coursion-Studio/WinZones/releases/download/v`$version/WinZones-`$version-win.zip",
        "hash": { "url": "`$url.sha256" }
    }
}
"@, $enc)

Write-Host "Release files for $v in dist\:"
Get-ChildItem dist -Filter "*$v*" | ForEach-Object { Write-Host "  $($_.Name)" }
Write-Host "Scoop manifest: packaging\scoop\winzones.json"
