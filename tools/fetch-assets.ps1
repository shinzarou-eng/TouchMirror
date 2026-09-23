$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$tmp = Join-Path $env:TEMP ('tm-assets-' + [guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    $ptDir = Join-Path $assets 'platform-tools'
    if (-not (Test-Path (Join-Path $ptDir 'adb.exe'))) {
        $ptZip = Join-Path $tmp 'platform-tools.zip'
        Invoke-WebRequest 'https://dl.google.com/android/repository/platform-tools-latest-windows.zip' -OutFile $ptZip
        Expand-Archive $ptZip (Join-Path $tmp 'pt')
        New-Item -ItemType Directory -Force $ptDir | Out-Null
        'adb.exe', 'AdbWinApi.dll', 'AdbWinUsbApi.dll' | ForEach-Object {
            Copy-Item (Join-Path $tmp "pt/platform-tools/$_") $ptDir
        }
    }
    $ffZip = Join-Path $assets 'ffmpeg.zip'
    if (-not (Test-Path $ffZip)) {
        $srcZip = Join-Path $tmp 'ffmpeg-src.zip'
        Invoke-WebRequest 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.1/ffmpeg-9.0.1-full_build-shared.zip' -OutFile $srcZip
        Expand-Archive $srcZip (Join-Path $tmp 'ff')
        $bin = Get-ChildItem (Join-Path $tmp 'ff') -Recurse -Directory -Filter 'bin' | Select-Object -First 1
        $stage = Join-Path $tmp 'stage'
        New-Item -ItemType Directory -Force (Join-Path $stage 'licenses') | Out-Null
        Get-ChildItem $bin.FullName -Filter '*.dll' |
            Where-Object { $_.Name -match '^(av|sw)' } |
            Copy-Item -Destination $stage
        Copy-Item (Join-Path $root 'licenses/FFmpeg.txt') (Join-Path $stage 'licenses/FFmpeg.txt')
        Compress-Archive (Join-Path $stage '*') $ffZip
    }
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
