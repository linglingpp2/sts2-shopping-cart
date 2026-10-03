# Build a player download without installing or changing the game.
[CmdletBinding()]
param(
    [string]$GameDir = $env:STS2_GAME_DIR,
    [string]$GameDataDir,
    [string]$DotnetPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoDir = $PSScriptRoot
$manifestPath = Join-Path $repoDir 'ShoppingCartMod\ShoppingCart.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = [string]$manifest.version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw 'The mod manifest must contain a valid release version.'
}

$buildParameters = @{ SkipInstall = $true }
if ($GameDir) { $buildParameters.GameDir = $GameDir }
if ($GameDataDir) { $buildParameters.GameDataDir = $GameDataDir }
if ($DotnetPath) { $buildParameters.DotnetPath = $DotnetPath }
& (Join-Path $repoDir 'build.ps1') @buildParameters

$builtDll = Join-Path $repoDir 'ShoppingCartMod\bin\Release\ShoppingCart.dll'
if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) {
    throw "Build output is missing: $builtDll"
}
$dllVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($builtDll).ProductVersion
if (-not $dllVersion -or $dllVersion.Split('+')[0] -ne $version) {
    throw "DLL version '$dllVersion' does not match manifest version '$version'. Update the project version before packaging."
}
$distDir = Join-Path $repoDir 'dist'
New-Item -ItemType Directory -Path $distDir -Force | Out-Null
$stageDir = Join-Path $distDir ('package-' + [Guid]::NewGuid().ToString('N'))
$modDir = Join-Path $stageDir 'ShoppingCart'
New-Item -ItemType Directory -Path $modDir -Force | Out-Null
Copy-Item -LiteralPath $builtDll -Destination (Join-Path $modDir 'ShoppingCart.dll')
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $modDir 'ShoppingCart.json')
Copy-Item -LiteralPath (Join-Path $repoDir 'LICENSE') -Destination (Join-Path $stageDir 'LICENSE')
$playerReadme = @"
Shop Shopping Cart / 商店购物车 $version

Installation / 安装
Close the game. Copy the ShoppingCart folder into the game's mods folder.
Restart the game to load the mod. No BaseLib or extra PCK is required.
关闭游戏，将 ShoppingCart 文件夹复制到游戏目录的 mods 文件夹，再启动游戏。
无需 BaseLib 或额外 PCK。此压缩包不包含游戏文件或源码。

Features / 功能
Select cards, relics, potions and card removal, then check out from the cart.
A selected, initially affordable Membership Card is purchased first.
Cancelled card removal stops checkout; earlier purchases are not refunded.
卡牌、遗物、药水和删牌服务均可加入购物车后结算。
已选且当前金币足够购买的会员卡优先购买；取消删牌会停止后续结算。

Compatibility / 兼容性
Windows, Slay the Spire 2 0.111.0. Multiplayer and other platforms unverified.
测试环境：Windows、游戏 0.111.0；多人联机和其他平台尚未实测。

Source and support / 源码与反馈
https://github.com/linglingpp2/sts2-shopping-cart
License: MIT; see LICENSE. Copyright (c) 2026 lin.
"@
[IO.File]::WriteAllText((Join-Path $stageDir 'README.txt'), $playerReadme, [Text.UTF8Encoding]::new($false))

$packagePath = Join-Path $distDir "ShoppingCart-$version.zip"
$packageFiles = @(Join-Path $stageDir 'ShoppingCart'; Join-Path $stageDir 'LICENSE'; Join-Path $stageDir 'README.txt')
Compress-Archive -LiteralPath $packageFiles -DestinationPath $packagePath -Force
$digest = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLine = $digest + '  ' + [IO.Path]::GetFileName($packagePath) + "`n"
[IO.File]::WriteAllText(($packagePath + '.sha256'), $checksumLine, [Text.UTF8Encoding]::new($false))
Write-Output "Player package: $packagePath"
Write-Output "SHA256: $digest"
