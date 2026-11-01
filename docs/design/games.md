# Games

User request, 1 Nov 2026 (Windows and the phone). A Games card lists the games installed on the PC from Steam, the Epic Games Launcher and the Xbox app, recently played first, and a tap starts one through its store. It is hidden by default; turn it on in Edit layout's Widgets list.

## Where the games come from (`GameLibrary`)

Everything is read from files and the registry the stores keep on the PC, with their apps closed and without the network.

| Store | Read from | Playtime and last played | Starts with |
| --- | --- | --- | --- |
| Steam | `SteamPath` in `HKCU\Software\Valve\Steam`; `steamapps\libraryfolders.vdf` for every library; each library's `appmanifest_*.acf` (fully installed only, `StateFlags` & 4) | From the most recently written `userdata\<id>\config\localconfig.vdf` (`Playtime` in minutes, `LastPlayed`), else the manifest's `LastPlayed` | `steam://rungameid/<appid>` |
| Epic | `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item` with the `games` category and a complete install | None: Epic keeps them online | `com.epicgames.launcher://apps/<namespace>%3A<item>%3A<app>?action=launch&silent=true` |
| Xbox app / Microsoft Store | Installed packages for this user (`AppModel\Repository\Packages`, their `PackageRootFolder`); a package is a game when the folder has the GDK's `MicrosoftGame.config` (its `ShellVisuals DefaultDisplayName`), with the app id from `AppxManifest.xml` | None: the Xbox app doesn't keep them on the PC | `shell:AppsFolder\<family name>!<app id>`, opened through Explorer |

Steam's own tools (Steamworks redistributables, Proton, the Linux runtime, SteamVR, Wallpaper Engine) aren't listed. Valve's `.vdf`/`.acf` files are read by a small KeyValues reader (quoted keys and values, nested blocks, comments, escapes).

**No guessing.** Where a store doesn't record playtime on the PC, the card shows none (not "0 h"); "Not played yet" appears only when Steam says the playtime is zero. Games with a known last-played time come first, newest first; the rest follow by name.

Only the three stores' launch addresses are ever opened (`LaunchAllowed` checks their exact shape), so a damaged or tampered manifest can't make a tap run something else.

## Reading

The library is read off the UI thread when the card is first shown, every ten minutes while it is shown (twenty in Quiet mode), and on the refresh button. Settings → Widgets → Games chooses the stores (all on by default); changing them reads the library again.

## The card

Quick Launch's six-a-page grid (`CardSizing.DeckGrid`, `DeckLabel`, `Tile`, `KeepsSix`, minimum 288 × 204): a game controller glyph in the store's colour (Steam Glacier, Epic Lavender, Xbox Lime) and the name; large cells are tiles with "Steam · 42 h played · 2 days ago". The header has the pager and a refresh button. While nothing has been read it says "Looking for games…", and with nothing found it says which stores it looked in.

No cover art: the dashboard draws icons as glyphs, and Epic and Xbox keep no local art to match Steam's.

## The phone

The `games` section lists up to 120 games (id, name, store, playtime or null, last played or null). `games.launch` with an id starts that game on the PC (an id the PC no longer has is refused), and `games.refresh` reads the library again. Stores are chosen on the PC.

## Checks

`tests/Games/GameTests.cs`: a fixture Steam folder with a second library, a comment, an escaped name, a redistributable and a game still downloading, and playtime from `localconfig.vdf`; fixture Epic manifests (a game, an application, an incomplete install, a broken file); fixture Xbox packages (a GDK game, an ordinary app, a missing folder); the order, wording and launch-address checks; a read-only scan of this PC's real libraries giving names and safe addresses; settings; and the card at three sizes at 140% text keeping its 48-unit buttons inside it. The phone-link checks read the section and refuse launching an unknown game. No check starts a game. `AndroidTests.java` covers parsing and wording.

Not tried: starting real games from the card or the phone (by design no check does), and Xbox games (none are installed on the development PC; the reader is checked with fixtures laid out like theirs).
