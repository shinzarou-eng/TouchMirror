# TouchMirror — watchdog de reconnexion
# Surveille l'API locale et reconnecte tout appareil qui repasse "prêt".
# Usage :
#   .\watchdog.ps1                          # tous les appareils prêts
#   .\watchdog.ps1 -Serials RFGL22M2JQM     # seulement certains
#   .\watchdog.ps1 -IntervalSec 5 -RetrySec 45
# Port et token lus automatiquement dans les réglages TouchMirror
# (écrasables avec -Port / -Token). Ctrl+C pour arrêter.

param(
    [int]$Port,
    [string]$Token,
    [string[]]$Serials,
    [int]$IntervalSec = 3,
    [int]$RetrySec = 30
)

$ErrorActionPreference = "Stop"

# L'app injecte TOUCHMIRROR_API_URL / TOUCHMIRROR_API_TOKEN quand le
# plugin est lancé depuis les réglages ; sinon on lit les réglages.
if (-not $Port -and $env:TOUCHMIRROR_API_URL) {
    $Port = ([uri]$env:TOUCHMIRROR_API_URL).Port
}
if (-not $Token -and $env:TOUCHMIRROR_API_TOKEN) {
    $Token = $env:TOUCHMIRROR_API_TOKEN
}
$settingsPath = "$env:LOCALAPPDATA\TouchMirror\settings.json"
if ((-not $Port -or -not $Token) -and (Test-Path $settingsPath)) {
    $cfg = Get-Content $settingsPath -Raw | ConvertFrom-Json
    if (-not $Port)  { $Port  = $cfg.LocalApiPort }
    if (-not $Token) { $Token = $cfg.LocalApiToken }
}
if (-not $Port)  { $Port = 47613 }
if (-not $Token) {
    Write-Output "Token introuvable — active l'API dans Réglages → API LOCALE."
    exit 1
}

$base = "http://127.0.0.1:$Port/api"
$hdr  = @{ Authorization = "Bearer $Token" }
$retryAfter = @{}
$prevLive = @()

Write-Output "Watchdog TouchMirror → $base  (Ctrl+C pour quitter)"

while ($true) {
    try {
        $devices = @((Invoke-RestMethod "$base/devices" -Headers $hdr -TimeoutSec 5).data)
        $mirrors = @((Invoke-RestMethod "$base/mirrors" -Headers $hdr -TimeoutSec 5).data)
        $live    = @($mirrors | ForEach-Object { $_.serial })

        # signale les tuiles qui viennent de lâcher
        foreach ($gone in ($prevLive | Where-Object { $_ -notin $live })) {
            Write-Output "✗ $gone déconnecté"
        }
        $prevLive = $live

        foreach ($d in $devices) {
            if (-not $d.ready) { continue }
            if ($Serials -and $Serials.Count -gt 0 -and $d.serial -notin $Serials) { continue }
            if ($d.serial -in $live) { continue }
            if ($retryAfter[$d.serial] -and (Get-Date) -lt $retryAfter[$d.serial]) { continue }

            Write-Output "→ $($d.name) prêt — connexion…"
            try {
                $serial = [uri]::EscapeDataString($d.serial)
                $r = Invoke-RestMethod -Method Post "$base/devices/$serial/connect" -Headers $hdr -TimeoutSec 60
                Write-Output "  $($r.message)"
                $retryAfter.Remove($d.serial)
            }
            catch {
                Write-Output "  échec — nouvel essai dans ${RetrySec}s"
                $retryAfter[$d.serial] = (Get-Date).AddSeconds($RetrySec)
            }
        }
    }
    catch {
        Write-Output "API injoignable — TouchMirror lancé ? API activée ?"
    }
    Start-Sleep $IntervalSec
}
