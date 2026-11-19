# Builds a self-contained single-file Windows executable for SnipFrame.
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "SnipFrame.Windows\SnipFrame.Windows.csproj"
$out = Join-Path $root "dist"
$bin = Join-Path $root "SnipFrame.Windows\bin\Release\net8.0-windows\win-x64\tr"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o $out

# Turkish strings ship as a satellite assembly, which single-file publish leaves next to the exe.
$satellite = Join-Path $bin "SnipFrame.resources.dll"
if (Test-Path $satellite) {
    New-Item -ItemType Directory -Force (Join-Path $out "tr") | Out-Null
    Copy-Item $satellite (Join-Path $out "tr\SnipFrame.resources.dll")
}

Get-ChildItem $out | Select-Object Name, Length
Write-Host "Run: $out\SnipFrame.exe"
