# Workshop publishing assets

Workshop item: [Shop Shopping Cart](https://steamcommunity.com/sharedfiles/filedetails/?id=3812514991) (item ID `3812514991`).

The BBCode description is stored in `description.bbcode`. The final cover is `cover.png` (768 × 768 PNG, below Steam's 1 MB limit); its generation prompt and provenance are recorded in `COVER.md`. The cover is an original illustration, not a gameplay screenshot.

Version 1.0.4 supports Windows stable `public` v0.107.1 and `public-beta` v0.113.0 with one DLL. The Workshop range is `public` through `public-beta`. The payload uses the same reviewed DLL and manifest as the GitHub 1.0.4 release, plus the MIT license and player installation note.

Use Mega Crit's official ModUploader. Put the payload in a workspace's `content` folder, the cover at `image.png`, and the metadata in `workshop.json`. Upload privately first. Keep `mod_id.txt` containing item ID `3812514991` and reuse the same workspace for updates so they update the existing item.

Before checking the subscription, move any manual same-ID copy out of the entire game `mods` directory. Steam Workshop and manual copies of the same version are not both loaded by the game's mod loader. Keep the subscription after verification; retain the manual copy as a backup outside `mods`.

The same release DLL passed 51/51 stable and 56/56 beta checks and separate engine UI fixtures on both. These use real game assemblies with UI and transport boundaries replaced. They do not establish full campaign, physical-controller or real multiplayer compatibility; other versions and platforms remain unverified. See [compatibility validation](../COMPATIBILITY-1.0.4.md). Publishing notes must continue to state those limits.

Official uploader: https://github.com/megacrit/sts2-mod-uploader
