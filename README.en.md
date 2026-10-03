# Shop Shopping Cart

A shopping-cart mod for Slay the Spire 2: choose shop items first, review the estimated cost and remaining gold, then check out together.

[中文说明](README.md) · **Version 1.0.3** · [Download](https://github.com/linglingpp2/sts2-shopping-cart/releases/latest) · [Changelog](CHANGELOG.md) · [Report an issue](https://github.com/linglingpp2/sts2-shopping-cart/issues)

## Features and controls

- Click a card, relic, potion, or card-removal service to add it to the cart. Click again to remove it. Selected entries show a green check mark.
- The draggable panel on the right shows the planned purchase order, estimated total, and remaining gold, with an insufficient-gold indication.
- **Membership Card priority:** if the card is already selected and your starting gold can pay for it, the cart buys it first and estimates the discount on later purchases. All other items keep their selection order. The cart never automatically adds an unselected Membership Card. If it is initially unaffordable, the original order is retained.
- **Card removal:** the service is included in the total, including applicable Membership Card discounts. Its turn opens the game's card-selection screen; confirming removal resumes checkout. Cancelling does not charge the removal fee and stops subsequent purchases. Earlier purchases are not refunded.
- Checkout purchases entries sequentially through the game's own purchase flows. A failed purchase stops checkout and leaves unpurchased entries in the cart. Other shop purchases and local map interaction are paused during checkout.
- Turn off cart selection in the panel to restore immediate purchases.
- Click the shopping-cart icon in the upper right to show or hide the panel. Right-click it to restore the default panel position. The icon displays the selected item count.

Closing the shop inventory clears the cart. The panel position and click mode are saved in `shopping_cart.cfg` in the game's user directory.

Costs are estimates. Membership Card discounts and some base-game relic price and refund effects are supported; other relics or mods can still change a purchase's outcome. **The game's own purchase flow determines final prices, effects, and payment.** Checkout keeps the order fixed at its start; refunds received during checkout do not reorder it.

## Installation and updates

1. Download and extract `ShoppingCart-1.0.3.zip`.
2. Exit the game, then copy the package's entire `ShoppingCart` folder into the game's `mods` folder. Overwrite the matching files when updating.
3. Start the game and open a shop's inventory to use the panel. Restart the game after replacing the DLL.

```text
<game directory>/mods/ShoppingCart/
├── ShoppingCart.dll
└── ShoppingCart.json
```

The transparent cart icon is embedded in the DLL. No extra PCK file or BaseLib installation is required. The package's `LICENSE` and `README.txt` are documentation and do not need to be copied into the game directory.

No Steam Workshop item has been published yet. When testing a future subscription version, first move the manual installation with the same mod ID outside the game's `mods` directory to ensure the subscribed build is being tested.

## Compatibility and validation

- Built and validated on **Windows with game version 0.111.0**. The manifest's minimum game version is 0.111.0.
- 50 automated regression checks cover purchase order, Membership Card discounts, refunds, card-removal confirmation and cancellation, failed purchases, concurrent requests, exception recovery, and closing the shop. They use real game assemblies but replace UI and some synchronization boundaries, so they do not prove actual gameplay behavior.
- The panel also passed an independent engine UI check. Full gameplay, physical controller devices, and multiplayer still need testing. Other operating systems and game versions have not been verified.

Game updates may change the internal purchase or UI interfaces. Include your game version, mod version, other enabled mods, reproduction steps, and relevant logs when [reporting an issue](https://github.com/linglingpp2/sts2-shopping-cart/issues).

## Building from source

Install the **.NET 9 SDK** and the Windows game. Builds reference `sts2.dll`, `GodotSharp.dll`, and `0Harmony.dll` from the game's `data_sts2_windows_x86_64` directory. Neither the repository nor the release package includes these assemblies or a bundled SDK.

Run these PowerShell commands from the repository root, replacing `<game directory>` with your installation path:

```powershell
# Build without modifying the game installation
.\build.ps1 -GameDir '<game directory>' -SkipInstall

# Run regression checks
.\test.ps1 -GameDir '<game directory>'

# Build the release ZIP and SHA-256 checksum without installing the mod
.\package.ps1 -GameDir '<game directory>'
```

If `dotnet` is not on PATH, add `-DotnetPath '<path to dotnet.exe>'` to these commands. You can also set `STS2_GAME_DIR` and omit `-GameDir`. Use `-GameDataDir '<game assembly directory>'` for a different assembly layout.

Build output is in `ShoppingCartMod/bin/Release/`; release output is in `dist/`. Running `build.ps1` without `-SkipInstall` installs the mod and first backs up any existing DLL and manifest under the local `backups/` directory. Exit the game before replacing installed files.

On Windows Git Bash, `./build.sh '<game directory>' --no-install` is also available. This does not imply support for Linux or macOS game installations.

## Repository layout

```text
ShoppingCartMod/       mod project, manifest, and original source
validation/           automated regression checks and boundary stubs
docs/                 release notes
build.ps1 / build.sh   build and optional installation
test.ps1              regression runner
package.ps1           release packager
```

The mod uses the game's built-in mod loader and Harmony patches, while retaining native purchase, pricing, and card-removal flows. Game assemblies are referenced only from the developer's own installation.

## License

The project's original code and embedded cart icon are licensed under the [MIT License](LICENSE), copyright lin. Game and third-party components are outside this project's license grant; see [third-party notices](THIRD_PARTY_NOTICES.md). This is an unofficial mod.
