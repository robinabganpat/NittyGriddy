# Writes the notes for a GitHub release: the files with size, SHA-256 and VirusTotal result, and how to install.
#
#   pwsh tools/release-notes.ps1 -Version 2.0.0 -Dir dist -ScanResults vt.json -OutFile notes.md
#
# -ScanResults is the JSON written by tools/virustotal-scan.ps1. Without it, each file gets a plain report link.

param(
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$Dir,
    [string]$ScanResults,
    [Parameter(Mandatory)] [string]$OutFile
)

$ErrorActionPreference = 'Stop'
$repo = if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'robinabganpat/NittyGriddy' }

$scans = @{}
if ($ScanResults -and (Test-Path $ScanResults)) {
    foreach ($s in @(Get-Content $ScanResults -Raw | ConvertFrom-Json)) { $scans[$s.Sha256] = $s }
}

# The zip first: it is the recommended download
$files = @('portable.zip', 'setup.exe') |
    ForEach-Object { Join-Path $Dir "NittyGriddy-$Version-$_" } |
    Where-Object { Test-Path $_ } |
    ForEach-Object { Get-Item $_ }
if (-not $files) { throw "No release files for $Version in $Dir" }

$rows = @()
$details = @()
$zipClean = $false
foreach ($file in $files) {
    $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLower()
    $size = ($file.Length / 1MB).ToString('N1', [Globalization.CultureInfo]::InvariantCulture) + ' MB'
    $link = "https://www.virustotal.com/gui/file/$hash"
    $scan = $scans[$hash]

    $result = if ($scan -and $scan.Status -eq 'completed') {
        if ($scan.Flagged -eq 0) { "[clean, 0 of $($scan.Engines) engines]($link)" }
        else { "[$($scan.Flagged) of $($scan.Engines) engines flag it]($link)" }
    } else {
        "[report]($link)"
    }
    $rows += "| $($file.Name) | $size | ``$hash`` | $result |"

    if ($file.Name -like '*.zip' -and $scan -and $scan.Status -eq 'completed' -and $scan.Flagged -eq 0) { $zipClean = $true }
    if ($scan -and $scan.Flagged -gt 0) {
        $details += "- **$($file.Name)**: " + (($scan.FlaggedBy | ForEach-Object { "$($_.Engine) ($($_.Result))" }) -join ', ')
    }
}

$notes = @(
    "## NittyGriddy $Version"
    ''
    '**Download the portable zip** (recommended), unpack it to a folder of your choice and run `NittyGriddy.exe`. Or run the installer: it installs for your user account only, without administrator rights.'
    ''
    'Both need 64-bit Windows 10 or 11 and the [.NET 9 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/9.0).'
    ''
    '| File | Size | SHA-256 | VirusTotal |'
    '|---|---|---|---|'
    $rows
    ''
)
if ($details) {
    $notes += 'Flagged by:'
    $notes += ''
    $notes += $details
    $notes += ''
    # Only explained when it is the known pattern: the unsigned installer is flagged while the same program in the zip
    # is clean. Anything else is left for the reader to judge from the reports.
    if ($zipClean -and $details.Count -eq 1 -and $details[0] -like '*-setup.exe*') {
        $notes += @(
            'The zip, which contains exactly the same program, is clean. Detections of the installer alone come from engines that judge unsigned installers by machine learning; if your antivirus blocks it, use the zip.'
            ''
        )
    }
}
$notes += @(
    'The files were built from this tag by the [release workflow](https://github.com/' + $repo + '/actions/workflows/release.yml) and uploaded to VirusTotal by it. To check a download, compare its hash with the table above or with `SHA256SUMS.txt`:'
    ''
    '```powershell'
    "Get-FileHash .\NittyGriddy-$Version-portable.zip -Algorithm SHA256"
    '```'
    ''
    'Your settings in `%APPDATA%\NittyGriddy` are kept when you update.'
)

$OutFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile)
$parent = Split-Path $OutFile -Parent
if ($parent) { New-Item -ItemType Directory -Force $parent | Out-Null }
[IO.File]::WriteAllText($OutFile, ($notes -join "`n") + "`n", (New-Object Text.UTF8Encoding $false))
Write-Host "Release notes written to $OutFile"
