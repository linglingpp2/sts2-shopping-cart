#!/usr/bin/env bash
# Shared argument parsing and validation; no installation path is guessed.
cart_parse_arguments() {
    CART_GAME_DIR="${STS2_GAME_DIR:-}"
    CART_GAME_DATA_DIR="${STS2_GAME_DATA_DIR:-}"
    CART_DOTNET=""
    CART_INSTALL=true
    while (($#)); do
        case "$1" in
            --game-dir|--game-data-dir|--dotnet-path)
                if (($# < 2)) || [[ -z "$2" ]]; then
                    printf 'Missing value for %s\n' "$1" >&2
                    return 1
                fi
                case "$1" in
                    --game-dir) CART_GAME_DIR="$2" ;;
                    --game-data-dir) CART_GAME_DATA_DIR="$2" ;;
                    --dotnet-path) CART_DOTNET="$2" ;;
                esac
                shift 2
                ;;
            --no-install) CART_INSTALL=false; shift ;;
            --*) printf 'Unknown argument: %s\n' "$1" >&2; return 1 ;;
            *) CART_GAME_DIR="$1"; shift ;;
        esac
    done
}

cart_resolve_context() {
    if [[ -z "$CART_GAME_DIR" ]]; then
        printf 'Specify --game-dir "path to Slay the Spire 2" or set STS2_GAME_DIR.\n' >&2
        return 1
    fi
    if command -v cygpath >/dev/null 2>&1; then
        CART_GAME_DIR="$(cygpath -a -u "$CART_GAME_DIR")"
        if [[ -n "$CART_GAME_DATA_DIR" ]]; then CART_GAME_DATA_DIR="$(cygpath -a -u "$CART_GAME_DATA_DIR")"; fi
        if [[ -n "$CART_DOTNET" ]]; then CART_DOTNET="$(cygpath -a -u "$CART_DOTNET")"; fi
    fi
    if [[ ! -d "$CART_GAME_DIR" ]]; then
        printf 'Game directory not found: %s\n' "$CART_GAME_DIR" >&2
        return 1
    fi
    CART_GAME_DIR="$(cd -- "$CART_GAME_DIR" && pwd)"
    CART_GAME_DATA_DIR="${CART_GAME_DATA_DIR:-$CART_GAME_DIR/data_sts2_windows_x86_64}"
    for ASSEMBLY in sts2.dll GodotSharp.dll 0Harmony.dll; do
        if [[ ! -f "$CART_GAME_DATA_DIR/$ASSEMBLY" ]]; then
            printf 'Game assembly not found: %s/%s. Check --game-dir or use --game-data-dir.\n' "$CART_GAME_DATA_DIR" "$ASSEMBLY" >&2
            return 1
        fi
    done
    CART_GAME_DATA_DIR="$(cd -- "$CART_GAME_DATA_DIR" && pwd)"
    if [[ -n "$CART_DOTNET" ]]; then
        if [[ ! -f "$CART_DOTNET" ]]; then
            printf 'dotnet executable not found: %s\n' "$CART_DOTNET" >&2
            return 1
        fi
        CART_DOTNET="$(cd -- "$(dirname -- "$CART_DOTNET")" && pwd)/$(basename -- "$CART_DOTNET")"
        export DOTNET_ROOT="$(dirname -- "$CART_DOTNET")"
    else
        CART_DOTNET="$(command -v dotnet || true)"
    fi
    if [[ -z "$CART_DOTNET" ]]; then
        printf 'Install the .NET 9 SDK (or newer), or pass --dotnet-path for an external portable SDK.\n' >&2
        return 1
    fi
    if ! "$CART_DOTNET" --list-sdks | awk -F. '/^[0-9]+\./ && $1 >= 9 { found = 1 } END { exit !found }'; then
        printf 'No .NET 9 SDK (or newer) was found at: %s\n' "$CART_DOTNET" >&2
        return 1
    fi
    CART_GAME_DIR_BUILD="$CART_GAME_DIR"
    CART_GAME_DATA_DIR_BUILD="$CART_GAME_DATA_DIR"
    if command -v cygpath >/dev/null 2>&1; then
        CART_GAME_DIR_BUILD="$(cygpath -w "$CART_GAME_DIR")"
        CART_GAME_DATA_DIR_BUILD="$(cygpath -w "$CART_GAME_DATA_DIR")"
    fi
}
