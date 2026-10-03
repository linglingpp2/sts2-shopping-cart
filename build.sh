#!/usr/bin/env bash
# Usage: ./build.sh --game-dir "path to Slay the Spire 2" [--no-install]
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/scripts/build-support.sh"
cart_parse_arguments "$@"
cart_resolve_context

PROJECT_DIR="$SCRIPT_DIR/ShoppingCartMod"
BUILD_DIR="$PROJECT_DIR/bin/Release"
"$CART_DOTNET" build "$PROJECT_DIR/ShoppingCartMod.csproj" -c Release \
    --output "$BUILD_DIR" "-p:GameDir=$CART_GAME_DIR_BUILD" "-p:GameDataDir=$CART_GAME_DATA_DIR_BUILD"
if [[ "$CART_INSTALL" == false ]]; then
    printf 'Built: %s/ShoppingCart.dll\n' "$BUILD_DIR"
    exit 0
fi

MOD_DIR="$CART_GAME_DIR/mods/ShoppingCart"
if [[ -f "$MOD_DIR/ShoppingCart.dll" || -f "$MOD_DIR/ShoppingCart.json" ]]; then
    BACKUP_DIR="$SCRIPT_DIR/backups/ShoppingCart/$(date +%Y%m%d-%H%M%S)-$$"
    mkdir -p -- "$BACKUP_DIR"
    for NAME in ShoppingCart.dll ShoppingCart.json; do
        if [[ -f "$MOD_DIR/$NAME" ]]; then
            cp -- "$MOD_DIR/$NAME" "$BACKUP_DIR/$NAME"
        fi
    done
    printf 'Previous mod backed up to: %s\n' "$BACKUP_DIR"
fi
mkdir -p -- "$MOD_DIR"
cp -- "$BUILD_DIR/ShoppingCart.dll" "$MOD_DIR/ShoppingCart.dll"
cp -- "$PROJECT_DIR/ShoppingCart.json" "$MOD_DIR/ShoppingCart.json"
printf 'Installed to: %s\n' "$MOD_DIR"
