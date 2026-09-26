$ErrorActionPreference = "Stop"
$root   = Split-Path $PSScriptRoot -Parent
$out    = Join-Path $root "src\AndroidMirror\Services\VerifiedPlugins.cs"
$files  = @("plugins", "marketplace") | ForEach-Object { Join-Path $root $_ } |
    Where-Object { Test-Path $_ } |
    ForEach-Object { Get-ChildItem $_ -Recurse -Filter *.js } |
    Sort-Object FullName

$sha = [System.Security.Cryptography.SHA256]::Create()
$entries = foreach ($f in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $manifest = Join-Path $f.DirectoryName "plugin.json"
    if (Test-Path $manifest) { $bytes += [System.IO.File]::ReadAllBytes($manifest) }
    $h = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace("-", "").ToLowerInvariant()
    $rel = $f.FullName.Substring($root.Length + 1) -replace '\\', '/'
    '        "{0}",' -f $h
}

$content = @"
using System.Collections.Generic;

namespace TouchMirror.Services;

public static class VerifiedPlugins
{
    public static readonly HashSet<string> Hashes = new(System.StringComparer.OrdinalIgnoreCase)
    {
$($entries -join "`n")
    };
}
"@
[System.IO.File]::WriteAllText($out, $content + "`r`n", [System.Text.UTF8Encoding]::new($false))

Write-Output "VerifiedPlugins.cs régénéré ($($files.Count) plugin(s))."
