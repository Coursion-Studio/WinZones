# Signs files with the Coursion code-signing certificate (one certificate for every Coursion Windows app).
#   powershell -File tools\sign.ps1 WinZones.exe [more files...]
#
# The certificate lives in the Windows certificate store on the maintainer's PC (cloud certificate;
# each signing is confirmed on the phone), so signing runs locally, never in CI.
#   CODESIGN_THUMBPRINT  SHA-1 thumbprint of the certificate (required to sign)
#   CODESIGN_TIMESTAMP   RFC 3161 timestamp server (default: http://time.certum.pl)
# Without CODESIGN_THUMBPRINT it prints a warning and exits 0, so unsigned builds keep working.
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Files)
$ErrorActionPreference = 'Stop'

if (-not $Files -or $Files.Count -eq 0) { throw "Usage: sign.ps1 <file> [file...]" }
$thumb = $env:CODESIGN_THUMBPRINT
if ([string]::IsNullOrWhiteSpace($thumb)) {
    Write-Warning "CODESIGN_THUMBPRINT is not set: NOT signing $($Files -join ', ')"
    exit 0
}
$thumb = ($thumb -replace '\s', '').ToUpperInvariant()
$ts = if ($env:CODESIGN_TIMESTAMP) { $env:CODESIGN_TIMESTAMP } else { 'http://time.certum.pl' }

# newest signtool.exe from the Windows SDK
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
            Sort-Object { [version]($_.Directory.Parent.Name) } -Descending | Select-Object -First 1
if (-not $signtool) { throw "signtool.exe not found. Install the Windows SDK: winget install Microsoft.WindowsSDK.10.0.26100" }

$cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $thumb } | Select-Object -First 1
if (-not $cert) { throw "No certificate with thumbprint $thumb in the Windows certificate store. Is the signing app (e.g. SimplySign Desktop) connected?" }

$paths = $Files | ForEach-Object { (Resolve-Path $_).Path }
Write-Host "Signing as '$($cert.GetNameInfo('SimpleName', $false))': $($paths | ForEach-Object { Split-Path $_ -Leaf })"
& $signtool.FullName sign /sha1 $thumb /fd SHA256 /tr $ts /td SHA256 /q @paths
if ($LASTEXITCODE -ne 0) { throw "signtool sign failed ($LASTEXITCODE)" }

foreach ($p in $paths) {
    & $signtool.FullName verify /pa /q $p
    if ($LASTEXITCODE -ne 0) { throw "signtool verify failed for $p" }
    $s = Get-AuthenticodeSignature $p
    if ($s.Status -ne 'Valid') { throw "$p signature is $($s.Status)" }
    Write-Host "  signed: $(Split-Path $p -Leaf)"
}
