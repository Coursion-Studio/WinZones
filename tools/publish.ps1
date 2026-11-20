# Publishes the release that release.bat built into dist\ as a GitHub release, and commits the Scoop manifest.
#   powershell -File tools\publish.ps1 -DryRun          show what would happen, change nothing
#   powershell -File tools\publish.ps1                  publish (refuses unsigned files)
#   powershell -File tools\publish.ps1 -AllowUnsigned   publish even if the files aren't signed
# Needs git and the GitHub CLI (gh), signed in with push rights to Coursion-Studio/WinZones.
param([switch]$DryRun, [switch]$AllowUnsigned)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$repo = 'Coursion-Studio/WinZones'

# ---------------------------------------------------------------- checks
$v = (Get-Item .\WinZones.exe).VersionInfo.ProductVersion
$tag = "v$v"
$files = @("dist\WinZonesSetup-$v.exe", "dist\WinZonesSetup-$v.exe.sha256", "dist\WinZones-$v-win.zip", "dist\WinZones-$v-win.zip.sha256")
foreach ($f in $files) { if (-not (Test-Path $f)) { throw "$f is missing. Run release.bat first." } }
foreach ($f in $files | Where-Object { $_ -like '*.sha256' }) {
    $target = $f.Substring(0, $f.Length - 7)
    $want = (Get-Content $f -Raw).Trim().Split(' ')[0]
    if ((Get-FileHash $target -Algorithm SHA256).Hash -ne $want) { throw "$target doesn't match $f. Run release.bat again." }
}

# the files inside the zip and the installer must be signed (unless allowed)
$tmp = Join-Path $env:TEMP "wz-publish-$v"
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
Expand-Archive "dist\WinZones-$v-win.zip" $tmp
$unsigned = @("$tmp\WinZones.exe", "dist\WinZonesSetup-$v.exe") | Where-Object { (Get-AuthenticodeSignature $_).Status -ne 'Valid' } | ForEach-Object { Split-Path $_ -Leaf }
Remove-Item $tmp -Recurse -Force
if ($unsigned) {
    if (-not $AllowUnsigned) { throw "Not signed: $($unsigned -join ', '). Set CODESIGN_THUMBPRINT and run release.bat again, or pass -AllowUnsigned." }
    Write-Warning "Publishing UNSIGNED files: $($unsigned -join ', ')"
}

$section = Select-String -Path CHANGELOG.md -Pattern "^## $([regex]::Escape($v))" | Select-Object -First 1
if (-not $section) { throw "CHANGELOG.md has no '## $v' section." }
if (git tag --list $tag) { throw "Tag $tag already exists." }
if (gh release view $tag -R $repo 2>$null) { throw "Release $tag already exists on GitHub." }
$dirty = git status --porcelain | Where-Object { $_ -notmatch 'packaging/scoop/winzones.json' }
if ($dirty) { throw "Commit or stash your changes first:`n$($dirty -join "`n")" }
if ((git rev-parse --abbrev-ref HEAD) -ne 'main') { throw "Publish from the main branch." }

# ---------------------------------------------------------------- release notes from CHANGELOG.md
$lines = Get-Content CHANGELOG.md -Encoding UTF8
$notes = @()
for ($i = $section.LineNumber; $i -lt $lines.Count -and $lines[$i] -notmatch '^## '; $i++) { $notes += $lines[$i] }
$notes += ''
$notes += '### Install'
$notes += "- **Installer:** download ``WinZonesSetup-$v.exe`` and run it (no admin rights needed)."
$notes += "- **Scoop:** ``scoop install https://raw.githubusercontent.com/$repo/main/packaging/scoop/winzones.json``"
$notes += "- **Portable:** download ``WinZones-$v-win.zip``, unzip it anywhere and run ``WinZones.exe``."
$notes += ''
$notes += 'Already using WinZones? It updates itself: right-click the tray icon and choose **Update**.'
$notesFile = Join-Path $env:TEMP "wz-notes-$v.md"
[IO.File]::WriteAllText($notesFile, ($notes -join "`n").Trim(), (New-Object Text.UTF8Encoding $false))

if ($DryRun) {
    Write-Host "DRY RUN for WinZones $v (nothing changed)" -ForegroundColor Cyan
    Write-Host "Would commit packaging/scoop/winzones.json, push main, then create release $tag with:"
    $files | ForEach-Object { Write-Host "  $_" }
    Write-Host "Signed: $(if ($unsigned) { 'NO (' + ($unsigned -join ', ') + ')' } else { 'yes' })"
    Write-Host "`n--- release notes ---"; Get-Content $notesFile
    exit 0
}

# ---------------------------------------------------------------- publish
git add -f packaging/scoop/winzones.json
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) { git commit -q -m "Scoop manifest for $v"; if ($LASTEXITCODE -ne 0) { throw "git commit failed" } }
git push -q origin main
if ($LASTEXITCODE -ne 0) { throw "git push failed" }
gh release create $tag @files -R $repo --target main --title "WinZones $v" --notes-file $notesFile --latest
if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
git fetch -q --tags
Write-Host "Published https://github.com/$repo/releases/tag/$tag" -ForegroundColor Green
