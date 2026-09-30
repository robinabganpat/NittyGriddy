#Requires -Version 7
# Uploads release files to VirusTotal, waits for the scans to finish and writes the results as JSON.
#
#   $env:VT_API_KEY = '<key>'
#   pwsh tools/virustotal-scan.ps1 -Path dist\a.zip, dist\b.exe -OutFile vt.json
#
# The release workflow runs it with the VT_API_KEY repository secret. Without a key nothing is uploaded and every
# file is written as "not scanned", so a release still gets its hashes and report links.
#
# The public API allows 4 requests a minute: requests are spaced out, and a 429 answer is waited out and retried.
# A scan that fails or does not finish in time is a warning, not an error: the report link keeps working, and
# VirusTotal fills it in later.

param(
    [Parameter(Mandatory)] [string[]]$Path,
    [Parameter(Mandatory)] [string]$OutFile,
    [int]$TimeoutMinutes = 20,
    # For testing against a stand-in server
    [string]$ApiUrl = 'https://www.virustotal.com/api/v3',
    [int]$PollSeconds = 30
)

$ErrorActionPreference = 'Stop'
$api = $ApiUrl.TrimEnd('/')
$key = $env:VT_API_KEY

function Warn([string]$message) {
    # Shown as an annotation on the workflow run
    if ($env:GITHUB_ACTIONS) { Write-Host "::warning::$message" } else { Write-Warning $message }
}

function Invoke-Vt([string]$method, [string]$uri, [hashtable]$form) {
    for ($attempt = 1; ; $attempt++) {
        try {
            $request = @{ Method = $method; Uri = $uri; Headers = @{ 'x-apikey' = $key } }
            if ($form) { $request.Form = $form }
            return Invoke-RestMethod @request
        } catch {
            $status = [int]$_.Exception.Response.StatusCode
            if ($status -eq 429 -and $attempt -lt 6) {
                Write-Host '   rate limited, waiting a minute'
                Start-Sleep -Seconds 60
                continue
            }
            throw
        }
    }
}

$results = foreach ($file in $Path | ForEach-Object { Get-Item $_ }) {
    $hash = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLower()
    [pscustomobject]@{
        Name      = $file.Name
        File      = $file
        Sha256    = $hash
        Link      = "https://www.virustotal.com/gui/file/$hash"
        Status    = 'not scanned'
        Flagged   = $null
        Engines   = $null
        FlaggedBy = @()
        Analysis  = $null
    }
}

if (-not $key) {
    Warn 'VT_API_KEY is not set: the files were not uploaded to VirusTotal'
} else {
    foreach ($r in $results) {
        Write-Host "== Uploading $($r.Name)"
        try {
            $upload = Invoke-Vt Post "$api/files" @{ file = $r.File }
            $r.Analysis = $upload.data.id
            $r.Status = 'queued'
        } catch {
            $r.Status = 'failed'
            Warn "VirusTotal upload of $($r.Name) failed: $($_.Exception.Message)"
        }
        Start-Sleep -Seconds ([Math]::Min(15, $PollSeconds))
    }

    # A scan usually takes a few minutes; several dozen engines report one after another
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while ($pending = @($results | Where-Object { $_.Analysis -and $_.Status -ne 'completed' })) {
        if ((Get-Date) -gt $deadline) {
            $pending | ForEach-Object { Warn "VirusTotal did not finish scanning $($_.Name) within $TimeoutMinutes minutes" }
            break
        }
        Start-Sleep -Seconds $PollSeconds
        foreach ($r in $pending) {
            try {
                $analysis = (Invoke-Vt Get "$api/analyses/$($r.Analysis)").data.attributes
            } catch {
                Warn "Could not read the VirusTotal scan of $($r.Name): $($_.Exception.Message)"
                continue
            }
            $r.Status = $analysis.status
            if ($analysis.status -ne 'completed') { continue }

            $stats = $analysis.stats
            $r.Flagged = $stats.malicious + $stats.suspicious
            # Engines that gave a verdict; the ones that skip this file type or timed out are left out
            $r.Engines = $r.Flagged + $stats.undetected + $stats.harmless
            $r.FlaggedBy = @($analysis.results.PSObject.Properties |
                Where-Object { $_.Value.category -in 'malicious', 'suspicious' } |
                ForEach-Object { [pscustomobject]@{ Engine = $_.Name; Result = $_.Value.result } } |
                Sort-Object Engine)
            Write-Host "   $($r.Name): $($r.Flagged) of $($r.Engines) engines flag it"
        }
    }
}

$parent = Split-Path $OutFile -Parent
if ($parent) { New-Item -ItemType Directory -Force $parent | Out-Null }
$results | Select-Object Name, Sha256, Link, Status, Flagged, Engines, FlaggedBy |
    ConvertTo-Json -Depth 4 -AsArray |
    Set-Content -Encoding utf8NoBOM $OutFile
Write-Host "Results written to $OutFile"
