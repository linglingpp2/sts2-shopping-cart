# 构建并安装购物车 mod。使用 -SkipInstall 仅构建，不修改游戏目录。
[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$GameDataDir,
    [string]$DotnetPath,
    [switch]$SkipInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
. (Join-Path $scriptDir 'scripts\BuildSupport.ps1')
$context = Resolve-CartBuildContext -GameDir $GameDir -GameDataDir $GameDataDir -DotnetPath $DotnetPath
$resolvedGameDir = $context.GameDir

$projectDir = Join-Path $scriptDir 'ShoppingCartMod'
$buildDir = Join-Path $projectDir 'bin\Release'
$projectPath = Join-Path $projectDir 'ShoppingCartMod.csproj'
& $context.DotnetPath build $projectPath -c Release --output $buildDir "-p:GameDir=$resolvedGameDir" "-p:GameDataDir=$($context.GameDataDir)"
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}
$builtDll = Join-Path $buildDir 'ShoppingCart.dll'
if ($SkipInstall) {
    Write-Output "Built: $builtDll"
    return
}

$modDir = Join-Path $resolvedGameDir 'mods\ShoppingCart'
$installedDll = Join-Path $modDir 'ShoppingCart.dll'
$installedManifest = Join-Path $modDir 'ShoppingCart.json'
if ((Test-Path -LiteralPath $installedDll) -or (Test-Path -LiteralPath $installedManifest)) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fffffff'
    $backupDir = Join-Path $scriptDir "backups\ShoppingCart\$stamp"
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    foreach ($installedFile in @($installedDll, $installedManifest)) {
        if (Test-Path -LiteralPath $installedFile -PathType Leaf) {
            Copy-Item -LiteralPath $installedFile -Destination $backupDir
        }
    }
    Write-Output "Previous mod backed up to: $backupDir"
}
New-Item -ItemType Directory -Path $modDir -Force | Out-Null
Copy-Item -LiteralPath $builtDll -Destination $installedDll -Force
Copy-Item -LiteralPath (Join-Path $projectDir 'ShoppingCart.json') -Destination $installedManifest -Force
Write-Output "Installed to: $modDir"
