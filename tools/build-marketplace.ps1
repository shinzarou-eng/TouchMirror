$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$sha  = [System.Security.Cryptography.SHA256]::Create()
$plugins = @()

Get-ChildItem "$root\marketplace" -Directory | Sort-Object Name | ForEach-Object {
    $js = Join-Path $_.FullName 'plugin.js'
    $mf = Join-Path $_.FullName 'plugin.json'
    if (!(Test-Path $js) -or !(Test-Path $mf)) { Write-Warning "incomplet: $($_.Name)"; return }
    $manifest = Get-Content $mf -Raw -Encoding UTF8 | ConvertFrom-Json
    $bytes = [System.IO.File]::ReadAllBytes($js) + [System.IO.File]::ReadAllBytes($mf)
    $hash  = [BitConverter]::ToString($sha.ComputeHash([byte[]]$bytes)).Replace('-', '').ToLowerInvariant()
    $plugins += [ordered]@{
        id          = $_.Name
        name        = "$($manifest.name)"
        version     = "$($manifest.version)"
        author      = "$($manifest.author)"
        icon        = "$($manifest.icon)"
        description = "$($manifest.description)"
        official    = $true
        featured    = [bool]$manifest.featured
        tags        = @($manifest.tags)
        hash        = $hash
    }
}

$json = @{ plugins = $plugins } | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText("$root\marketplace\index.json", $json, [System.Text.UTF8Encoding]::new($false))
$plugins | ForEach-Object { Write-Output ("  {0} v{1}  {2}..." -f $_.id, $_.version, $_.hash.Substring(0,16)) }
