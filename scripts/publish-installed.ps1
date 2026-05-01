$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $root "dist"
$out = Join-Path $distRoot "installed\app"
$setupOut = Join-Path $distRoot "installed"

if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
New-Item -ItemType Directory -Force $setupOut | Out-Null

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
        -p:DefineConstants=INSTALLED `
        -o $out
} finally {
    Pop-Location
}

$isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$isccPath = if ($isccCommand) { $isccCommand.Source } else { $null }
if (-not $isccPath) {
    $isccPath = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if ($isccPath) {
    & $isccPath (Join-Path $root "installer\AccountManager.iss")
    Write-Host "Installer: " (Join-Path $setupOut "AccountManager_Setup.exe")
} else {
    Write-Host "Installed app published:" (Join-Path $out "AccountManager.exe")
    Write-Host "未检测到 Inno Setup，已跳过安装包生成。安装 Inno Setup 6 后重新运行本脚本即可输出 AccountManager_Setup.exe。"
}

