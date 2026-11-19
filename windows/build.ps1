# Builds SnipFrame and packages it into a per-user setup executable.
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appProject = Join-Path $root "SnipFrame.Windows\SnipFrame.Windows.csproj"
$setupProject = Join-Path $root "SnipFrame.Setup\SnipFrame.Setup.csproj"
$dist = Join-Path $root "dist"
$appOut = Join-Path $dist "app"
$setupOut = Join-Path $dist "setup"
$payload = Join-Path $root "SnipFrame.Setup\Payload"
$payloadZip = Join-Path $payload "SnipFrame.zip"
$version = "1.1.0"

if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Force $appOut | Out-Null
New-Item -ItemType Directory -Force $payload | Out-Null

Write-Host "Publishing the application…"
dotnet publish $appProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o $appOut

# Turkish strings ship as a satellite assembly, which single-file publish leaves next to the exe.
$satellite = Join-Path $root "SnipFrame.Windows\bin\Release\net8.0-windows\win-x64\tr\SnipFrame.resources.dll"
if (Test-Path $satellite) {
    New-Item -ItemType Directory -Force (Join-Path $appOut "tr") | Out-Null
    Copy-Item $satellite (Join-Path $appOut "tr\SnipFrame.resources.dll")
}

Write-Host "Packing the installer payload…"
if (Test-Path $payloadZip) { Remove-Item $payloadZip }
Compress-Archive -Path (Join-Path $appOut "*") -DestinationPath $payloadZip -CompressionLevel Optimal

Write-Host "Publishing the setup…"
dotnet publish $setupProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o $setupOut

Copy-Item (Join-Path $setupOut "SnipFrameSetup.exe") (Join-Path $dist "SnipFrameSetup-$version.exe")

Get-ChildItem $dist -File | Select-Object Name, Length
Write-Host "Setup: $dist\SnipFrameSetup-$version.exe"
