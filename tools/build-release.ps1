# Builds the release files for NittyGriddy: a portable zip and an installer, with their SHA-256 hashes.
#
#   powershell -ExecutionPolicy Bypass -File tools\build-release.ps1
#
# Output goes to artifacts\release\<version>\ (or -OutputDir):
#   NittyGriddy-<version>-setup.exe      installer (needs Inno Setup 6 to build; skipped with a warning if absent)
#   NittyGriddy-<version>-portable.zip   unpack and run
#   SHA256SUMS.txt                       hashes of both
#
# The version is taken from <Version> in App\App.csproj unless -Version is given.
# Inno Setup: winget install --id JRSoftware.InnoSetup -e
#
# Published releases are built by .github/workflows/release.yml, which runs this script; a local build is for testing.

param(
    [string]$Version,
    [switch]$SkipTests,
    # Where the release files go (default artifacts\release\<version>)
    [string]$OutputDir
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'App\App.csproj'

if (-not $Version) {
    $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$Version' is not of the form 1.2.3" }

$name = "NittyGriddy-$Version"
$stage = Join-Path $root "artifacts\release\$name"
$out = if ($OutputDir) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir) } else { Join-Path $root "artifacts\release\$Version" }
$zip = Join-Path $out "$name-portable.zip"
$setup = Join-Path $out "$name-setup.exe"

function Invoke-Checked([string]$what, [scriptblock]$command) {
    Write-Host "== $what"
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" }
}

if (-not $SkipTests) {
    Invoke-Checked 'Tests' { dotnet test (Join-Path $root 'NittyGriddy.sln') -c Release --nologo -v q }
}

# Framework-dependent build for 64-bit Windows: small, and uses the .NET 9 Desktop Runtime installed on the machine
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Invoke-Checked 'Publish' {
    dotnet publish $project -c Release -r win-x64 --self-contained false --nologo -v q `
        -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false -o $stage
}

New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE.txt')

Write-Host '== Portable zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    if (Test-Path $setup) { Remove-Item $setup -Force }
    Invoke-Checked 'Installer' {
        & $iscc /Qp "/DAppVersion=$Version" "/DSourceDir=$stage" "/DOutputDir=$out" (Join-Path $root 'installer\NittyGriddy.iss')
    }
} else {
    Write-Warning 'Inno Setup 6 was not found; the installer was not built. Install it with: winget install --id JRSoftware.InnoSetup -e'
}

Write-Host '== Hashes'
# The zip first: it is the recommended download
$files = @($zip, $setup) | Where-Object { Test-Path $_ } | ForEach-Object { Get-Item $_ }
$hashes = $files | ForEach-Object {
    [pscustomobject]@{
        Name = $_.Name
        # Invariant culture: the same output whatever the build machine's locale
        Size = ($_.Length / 1MB).ToString('N1', [Globalization.CultureInfo]::InvariantCulture) + ' MB'
        Hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
    }
}

# Same layout as sha256sum, so "sha256sum -c SHA256SUMS.txt" works
$sums = ($hashes | ForEach-Object { "$($_.Hash) *$($_.Name)" }) -join "`n"
[IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS.txt'), $sums + "`n", (New-Object Text.UTF8Encoding $false))

$hashes | Format-Table -AutoSize | Out-String | Write-Host
Write-Host "Done: $out"
