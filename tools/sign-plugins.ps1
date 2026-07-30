# Régénère la liste des hash SHA-256 des plugins officiels.
# À relancer après toute modification d'un plugin.js dans plugins/.
#   .\tools\sign-plugins.ps1

$ErrorActionPreference = "Stop"
$root   = Split-Path $PSScriptRoot -Parent
$out    = Join-Path $root "src\AndroidMirror\Services\VerifiedPlugins.cs"
$files  = Get-ChildItem (Join-Path $root "plugins") -Recurse -Filter *.js | Sort-Object FullName

$entries = foreach ($f in $files) {
    $h = (Get-FileHash $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $rel = $f.FullName.Substring($root.Length + 1) -replace '\\', '/'
    '        "{0}", // {1}' -f $h, $rel
}

@"
// Généré par tools/sign-plugins.ps1 — ne pas éditer à la main.
using System.Collections.Generic;

namespace TouchMirror.Services;

public static class VerifiedPlugins
{
    public static readonly HashSet<string> Hashes = new(System.StringComparer.OrdinalIgnoreCase)
    {
$($entries -join "`n")
    };
}
"@ | Set-Content $out -Encoding utf8

Write-Output "VerifiedPlugins.cs régénéré ($($files.Count) plugin(s))."
