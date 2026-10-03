#!/usr/bin/env bash
# Run isolated regression tests without installing the mod or starting the game.
set -euo pipefail
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/scripts/build-support.sh"
cart_parse_arguments "$@"
cart_resolve_context
bash "$SCRIPT_DIR/build.sh" --game-dir "$CART_GAME_DIR" --game-data-dir "$CART_GAME_DATA_DIR" --dotnet-path "$CART_DOTNET" --no-install
"$CART_DOTNET" build "$SCRIPT_DIR/validation/CheckoutHarness/CheckoutHarness.csproj" -c Release \
    "-p:GameDir=$CART_GAME_DIR_BUILD" "-p:GameDataDir=$CART_GAME_DATA_DIR_BUILD"
export STS2_GAME_DATA_DIR="$CART_GAME_DATA_DIR_BUILD"
"$CART_DOTNET" "$SCRIPT_DIR/validation/CheckoutHarness/bin/Release/net9.0/CheckoutHarness.dll"
