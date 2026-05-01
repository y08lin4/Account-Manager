$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $root "dist"
$out = Join-Path $distRoot "portable"
$zip = Join-Path $distRoot "AccountManager_Portable.zip"

if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

Push-Location $root
try {
    dotnet publish .\AccountManager.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:DefineConstants=PORTABLE `
        -o $out
} finally {
    Pop-Location
}

if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $out "AccountManager.exe") -DestinationPath $zip -Force

Write-Host "Portable build:" (Join-Path $out "AccountManager.exe")
Write-Host "Portable zip:  " $zip
