# 1.0.4 compatibility validation

Checked on Windows on 2026-11-13. Steam's installed branch information and depot manifests identified these current public branches:

| Branch | Game version | Steam build | Checkout checks | Engine UI fixture |
| --- | --- | --- | --- | --- |
| `public` (stable) | v0.107.1 | 23811903 | 51/51 passed | Passed, MegaDot 4.5.1.m.12 |
| `public-beta` | v0.113.0 | 26625880 | 56/56 passed | Passed, MegaDot 4.5.1.m.14 |

One DLL was built against stable game references and used unchanged in both suites and both native UI fixtures. No game assemblies, downloaded depot files, or decompiled sources are redistributed. The stable validation copy was downloaded separately through Steam; the installed beta branch and player saves were not changed.

The original 1.0.3 fails to initialize on v0.113.0 because concrete shop slots no longer have `OnTryPurchase`. The update intercepts the shared `NMerchantSlot.OnSelected` method instead. It calls legacy purchase wrappers on stable and `MerchantSynchronizer.PurchaseLocalEntry` on beta, including for card removal. It does not fall back to an unsynchronized purchase when the new synchronizer is unavailable.

The isolated checkout suite retains the real game price hooks and purchase validation while replacing native UI and some command/transport boundaries. Additional beta cases run the original merchant synchronizer with an in-process transport, checking purchase IDs/messages, rejection while an inventory purchase is in flight, removal cancellation, exception cleanup, and refusal to purchase without a synchronizer. Production Harmony patches resolve on both versions.

The UI fixtures use each version's engine, assemblies, and font resources in a separate test profile. They check the panel and 32×32 icon, removal row, vertical scrolling, collapse/expand, simulated controller action release, visibility, pending-purchase lock, offscreen recovery, and close cleanup. They do not create a campaign or touch game saves.

Full gameplay, physical controllers, actual remote multiplayer peers, other platforms, and other game versions remain unverified. A simulated transport check is not a live multiplayer session.

## Workshop distribution

Stable and beta use explicit snapshots under the existing Workshop item: `public`–`public` and `public-beta`–`public-beta`. The DLL and manifest are identical; only the installation note's distribution suffix differs. A broad mixed branch range left Steam selecting the older beta snapshot, so it was replaced for distribution purposes by exact branch snapshots. The current beta subscription's complete downloaded payload was checked against its upload workspace. Historical snapshots may remain listed by Steam.

## Reproduce checkout checks

Run `test.ps1` or `test.sh` with `GameDataDir` pointing to legally installed references for the desired version. The harness detects the purchase API and compiles the corresponding fixture. These scripts do not install the mod or start the game. For a cross-version binary check, build the mod once against stable, then build the harness for each reference directory with `-p:CartDll` pointing to that same DLL, and set `STS2_GAME_DATA_DIR` for each execution.
