# Run isolated checkout regression tests. This does not install the mod or start the game.
[CmdletBinding()]
param(
    [string]$GameDir,
    [string]$GameDataDir,
    [string]$DotnetPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\BuildSupport.ps1')
$context = Resolve-CartBuildContext -GameDir $GameDir -GameDataDir $GameDataDir -DotnetPath $DotnetPath
& (Join-Path $PSScriptRoot 'build.ps1') -GameDir $context.GameDir -GameDataDir $context.GameDataDir -DotnetPath $context.DotnetPath -SkipInstall
$harness = Join-Path $PSScriptRoot 'validation\CheckoutHarness\CheckoutHarness.csproj'
& $context.DotnetPath build $harness -c Release "-p:GameDir=$($context.GameDir)" "-p:GameDataDir=$($context.GameDataDir)"
if ($LASTEXITCODE -ne 0) { throw "Test build failed with exit code $LASTEXITCODE." }

$previousDataDir = $env:STS2_GAME_DATA_DIR
try {
    $env:STS2_GAME_DATA_DIR = $context.GameDataDir
    & $context.DotnetPath (Join-Path $PSScriptRoot 'validation\CheckoutHarness\bin\Release\net9.0\CheckoutHarness.dll')
    if ($LASTEXITCODE -ne 0) { throw "Checkout regression tests failed with exit code $LASTEXITCODE." }
} finally {
    if ($null -eq $previousDataDir) {
        Remove-Item Env:STS2_GAME_DATA_DIR -ErrorAction SilentlyContinue
    } else {
        $env:STS2_GAME_DATA_DIR = $previousDataDir
    }
}
