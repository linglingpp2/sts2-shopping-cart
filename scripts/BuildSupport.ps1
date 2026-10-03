# Shared validation for build.ps1 and test.ps1. No game path is guessed.
function Resolve-CartBuildContext {
    param(
        [string]$GameDir,
        [string]$GameDataDir,
        [string]$DotnetPath
    )

    if ([string]::IsNullOrWhiteSpace($GameDir)) { $GameDir = $env:STS2_GAME_DIR }
    if ([string]::IsNullOrWhiteSpace($GameDir)) {
        throw 'Specify -GameDir "path to Slay the Spire 2" or set STS2_GAME_DIR. No game files are bundled with this project.'
    }
    if (-not (Test-Path -LiteralPath $GameDir -PathType Container)) {
        throw "Game directory not found: $GameDir"
    }
    $resolvedGameDir = (Resolve-Path -LiteralPath $GameDir).ProviderPath
    if ([string]::IsNullOrWhiteSpace($GameDataDir)) { $GameDataDir = $env:STS2_GAME_DATA_DIR }
    if ([string]::IsNullOrWhiteSpace($GameDataDir)) {
        $GameDataDir = Join-Path $resolvedGameDir 'data_sts2_windows_x86_64'
    }
    if (-not (Test-Path -LiteralPath $GameDataDir -PathType Container)) {
        throw "Game DLL directory not found: $GameDataDir. Use -GameDataDir if your installation has a different managed DLL directory."
    }
    $resolvedGameDataDir = (Resolve-Path -LiteralPath $GameDataDir).ProviderPath
    foreach ($assembly in @('sts2.dll', 'GodotSharp.dll', '0Harmony.dll')) {
        $assemblyPath = Join-Path $resolvedGameDataDir $assembly
        if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
            throw "Game assembly not found: $assemblyPath. Check -GameDir and -GameDataDir."
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($DotnetPath)) {
        if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) {
            throw "dotnet executable not found: $DotnetPath"
        }
        $resolvedDotnetPath = (Resolve-Path -LiteralPath $DotnetPath).ProviderPath
        # An explicit path can point at a portable SDK installed outside this repository.
        $env:DOTNET_ROOT = Split-Path -Parent $resolvedDotnetPath
    } else {
        $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
        if (-not $dotnetCommand) {
            throw 'Install the .NET 9 SDK (or newer), or pass -DotnetPath "path to dotnet.exe" for an external portable SDK.'
        }
        $resolvedDotnetPath = $dotnetCommand.Source
    }
    $sdkList = @(& $resolvedDotnetPath --list-sdks)
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect the SDK at $resolvedDotnetPath." }
    $supportedSdks = @($sdkList | Where-Object { $_ -match '^([0-9]+)\.' -and [int]$Matches[1] -ge 9 })
    if ($supportedSdks.Count -eq 0) {
        throw "The .NET 9 SDK (or newer) is required. No compatible SDK was found at $resolvedDotnetPath."
    }
    [pscustomobject]@{
        GameDir = $resolvedGameDir
        GameDataDir = $resolvedGameDataDir
        DotnetPath = $resolvedDotnetPath
    }
}
