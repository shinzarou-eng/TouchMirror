$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$sdk = $env:ANDROID_HOME
if (-not $sdk) { $sdk = "$env:LOCALAPPDATA\Android\Sdk" }
"sdk.dir=$($sdk -replace '\\', '/')" | Out-File -Encoding ascii local.properties

if (-not $env:JAVA_HOME) {
    $jbr = 'C:\Program Files\Android\Android Studio\jbr'
    if (Test-Path $jbr) { $env:JAVA_HOME = $jbr }
}

& .\gradlew.bat :server:assembleRelease --console=plain
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$apk = 'server\build\outputs\apk\release\server-release-unsigned.apk'
$dest = "$root\..\assets\touchmirror-engine.jar"
Copy-Item $apk $dest -Force
Write-Host "OK -> $dest ($([math]::Round((Get-Item $dest).Length/1KB)) Ko)"
