# Package a ReStreet mod for Thunderstore

ReStreet Mod Switcher installs compatible packages from **Mods → Thunderstore**. It creates a game folder from the player's Vanilla installation, applies the package there and selects the mod profile. The profile has its own World Tour saves. Updating the same package under the same team keeps that profile's save location.

## 1. Prepare the mod files

Build and test the mod against a clean ReStreet installation. Record the ReStreet version you tested. For new mods, prefer loose files under `Game/data/` or `Game/dlc/` and preserve their paths relative to `Game`. Include the assets needed for the mod to work. [Thunderstore packaging guide](https://wiki.thunderstore.io/mods/creating-a-package)

Keep installers, scripts, stock game files, backups and save data out of the package. If a manual installer copies files, put those mod files in the package layout. The Switcher creates the destination and the game creates saves as players use the profile.

## 2. Add archive patches when needed

A loose asset can require an index patch if the game still reads its original copy from a numbered archive. If your mod edits the archive itself, include the difference from Vanilla using [BuildThunderstorePatch.py](../Tools/BuildThunderstorePatch.py). Keep a clean Vanilla copy for the first input and a copy with the mod installed for the second. Use Python 3.11 or later.

~~~powershell
python .\Tools\BuildThunderstorePatch.py '.\Vanilla\Game\data3.big' '.\Modded\Game\data3.big' '.\package\restreet-patches\data3.big'
python .\Tools\BuildThunderstorePatch.py '.\Vanilla\Game\data3.bh' '.\Modded\Game\data3.bh' '.\package\restreet-patches\data3.bh'
~~~

Use the matching archive and index from the same modded installation. Available patch targets include `data0.big` through `data7.big` and their `.bh` indexes, `data/loc/locale.big` and the `2dheads.big` and `2dheads.bh` portrait files. A `.big` archive patch requires **Switcher v0.2.2 or later**. Use the same relative paths under `restreet-patches/` as in `Game/`.

The patch records the original file's SHA-256. A player with a different base file will get an incompatibility message. If the resulting patch is nearly as large as the modified archive, check for unchanged stock data shifted to new offsets. Prefer loose files where possible, or preserve the existing asset offsets and append the modified data when repacking a numbered archive.

## 3. Add the Thunderstore metadata

Use a 256 × 256 PNG for `icon.png`. Write `README.md` in UTF-8 and include the installation steps, required ReStreet and Switcher versions, mod credits and original links. Put `manifest.json`, `README.md` and `icon.png` at the ZIP root. A package can contain loose assets, archive patches or both.

~~~text
package/
  manifest.json
  README.md
  icon.png
  Game/
    data/
      sceneassets/
        ...
  restreet-patches/
    data3.big
    data3.bh
~~~

An example `manifest.json` follows.

~~~json
{
  "name": "Example_Street_Mod",
  "version_number": "1.0.0",
  "website_url": "",
  "description": "Updated teams and kits for ReStreet.",
  "dependencies": []
}
~~~

Use the package name and team consistently when publishing an update. Increment `version_number` for each release. Add dependencies only when the mod requires another published Thunderstore package. [Thunderstore manifest and version format](https://wiki.thunderstore.io/mods/creating-a-package#manifest)

## 4. Zip and test

Archive the **contents** of `package`. When you open the ZIP, `manifest.json` should be visible immediately.

~~~powershell
Compress-Archive -Path '.\package\*' -DestinationPath '.\Example_Street_Mod-1.0.0.zip'
~~~

1. Check that the modded game starts with the clean ReStreet version you documented.
2. Upload the ZIP to the **ReStreet** community under the mod owner's team, with the **Mods** category.
3. Once the package appears in the catalogue, install it using **Mods → Thunderstore → Install mod** from a clean Switcher profile. Confirm that the mod is selected and the expected content appears in-game.
4. Start a new World Tour and confirm that Vanilla and the mod maintain separate saves. When an updated ZIP is available, install it under the same team and package name and confirm that the mod's existing save still loads.
