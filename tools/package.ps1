# Packs an already-built WinZones.exe and WinZonesSetup.exe into dist\ and writes the package-manager manifests.
#   dist\WinZones-X.Y.Z-win.zip (+ .sha256)    portable zip; the in-app updater downloads this one
#   dist\WinZonesSetup-X.Y.Z.exe (+ .sha256)   per-user installer (winget installs this one)
#   dist\winget\X.Y.Z\*.yaml                   winget manifests (for the first winget-pkgs submission)
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

# ---------------------------------------------------------------- package manifests
$base = "https://github.com/Coursion-Studio/WinZones/releases/download/v$v"
$id = "CoursionStudio.WinZones"
$wg = "dist\winget\$v"
New-Item -ItemType Directory -Force $wg | Out-Null
$enc = New-Object Text.UTF8Encoding $false

[IO.File]::WriteAllText("$root\$wg\$id.yaml", @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json
PackageIdentifier: $id
PackageVersion: $v
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@, $enc)

[IO.File]::WriteAllText("$root\$wg\$id.installer.yaml", @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json
PackageIdentifier: $id
PackageVersion: $v
MinimumOSVersion: 10.0.18362.0
InstallerType: exe
Scope: user
InstallModes:
- interactive
- silent
- silentWithProgress
InstallerSwitches:
  Silent: /S
  SilentWithProgress: /S
  InstallLocation: /dir="<INSTALLPATH>"
UpgradeBehavior: install
ProductCode: WinZones
AppsAndFeaturesEntries:
- DisplayName: WinZones
  Publisher: Coursion Studio
  ProductCode: WinZones
Installers:
- Architecture: neutral
  InstallerUrl: $base/WinZonesSetup-$v.exe
  InstallerSha256: $setupHash
ManifestType: installer
ManifestVersion: 1.6.0
"@, $enc)

[IO.File]::WriteAllText("$root\$wg\$id.locale.en-US.yaml", @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json
PackageIdentifier: $id
PackageVersion: $v
PackageLocale: en-US
Publisher: Coursion Studio
PublisherUrl: https://github.com/Coursion-Studio
PublisherSupportUrl: https://github.com/Coursion-Studio/WinZones/issues
PrivacyUrl: https://github.com/Coursion-Studio/WinZones/blob/main/PRIVACY.md
Author: kuraydev
PackageName: WinZones
PackageUrl: https://github.com/Coursion-Studio/WinZones
License: MIT
LicenseUrl: https://github.com/Coursion-Studio/WinZones/blob/main/LICENSE
ShortDescription: Snap windows into zones with Shift + drag, and arrange every window with one key.
Description: |-
  WinZones is a fast, free window manager for Windows 10 and 11.
  Hold Shift while dragging a window to snap it into a zone, press Win+Alt+A to tile every window by app type,
  save workspaces and bring them back with one key, give apps a fixed place, and let game mode keep it out of your way while you play.
Moniker: winzones
Tags:
- window-manager
- tiling
- zones
- snap
- workspaces
- ultrawide
- productivity
- fancyzones
ReleaseNotesUrl: https://github.com/Coursion-Studio/WinZones/releases/tag/v$v
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@, $enc)

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
Write-Host "Manifests: $wg, packaging\scoop\winzones.json"
