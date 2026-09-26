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

$esc = {
    param($v)
    $s = "$v" -replace '\\', '\\' -replace '"', '\"'
    $s = [regex]::Replace($s, '[^\x00-\x7F]', { '\u{0:x4}' -f [int][char]$args[0].Value })
    [regex]::Replace($s, "['<>&+``]", { '\u{0:x4}' -f [int][char]$args[0].Value })
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('{')
$lines.Add('    "plugins": [')
$first = $true
foreach ($p in $plugins) {
    if (!$first) { $lines[$lines.Count - 1] += ',' }
    $first = $false
    $lines.Add('        {')
    $lines.Add('            "id": "' + (& $esc $p.id) + '",')
    $lines.Add('            "name": "' + (& $esc $p.name) + '",')
    $lines.Add('            "version": "' + (& $esc $p.version) + '",')
    $lines.Add('            "author": "' + (& $esc $p.author) + '",')
    $lines.Add('            "icon": "' + (& $esc $p.icon) + '",')
    $lines.Add('            "description": "' + (& $esc $p.description) + '",')
    $lines.Add('            "official": ' + $p.official.ToString().ToLowerInvariant() + ',')
    $lines.Add('            "featured": ' + $p.featured.ToString().ToLowerInvariant() + ',')
    $lines.Add('            "tags": [')
    $t = 0
    foreach ($tag in $p.tags) {
        $t++
        $comma = if ($t -lt $p.tags.Count) { ',' } else { '' }
        $lines.Add('                "' + (& $esc $tag) + '"' + $comma)
    }
    $lines.Add('            ],')
    $lines.Add('            "hash": "' + $p.hash + '"')
    $lines.Add('        }')
}
$lines.Add('    ]')
$lines.Add('}')
[System.IO.File]::WriteAllText("$root\marketplace\index.json", ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
$plugins | ForEach-Object { Write-Output ("  {0} v{1}  {2}..." -f $_.id, $_.version, $_.hash.Substring(0,16)) }
