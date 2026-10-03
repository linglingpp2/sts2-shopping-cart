# Workshop publishing assets

Workshop item: [Shop Shopping Cart](https://steamcommunity.com/sharedfiles/filedetails/?id=3812514991) (item ID `3812514991`).

The BBCode description is stored in `description.bbcode`. The final cover is `cover.png` (768 × 768 PNG, below Steam's 1 MB limit); its generation prompt and provenance are recorded in `COVER.md`. The cover is an original illustration, not a gameplay screenshot.

Version 1.0.3 targets Windows and the game's `public-beta` branch, tested with game 0.111.0. Both Workshop branch bounds are `public-beta`. The Workshop payload uses the same reviewed DLL and manifest as the GitHub 1.0.3 release, plus the MIT license and player installation note.

Use Mega Crit's official ModUploader. Put the payload in a workspace's `content` folder, the cover at `image.png`, and the metadata in `workshop.json`. Upload privately first. Keep `mod_id.txt` containing item ID `3812514991` and reuse the same workspace for updates so they update the existing item.

Before checking the subscription, move any manual same-ID copy out of the entire game `mods` directory. Steam Workshop and manual copies of the same version are not both loaded by the game's mod loader. Keep the subscription after verification; retain the manual copy as a backup outside `mods`.

The DLL actually downloaded from Steam passed 50 isolated regression checks and an independent engine UI fixture check. These use real game assemblies with UI and some synchronization boundaries replaced. They do not establish full campaign, physical-controller or multiplayer compatibility; other game branches, versions, and platforms also remain unverified. Publishing notes must continue to state those limits.

Official uploader: https://github.com/megacrit/sts2-mod-uploader
