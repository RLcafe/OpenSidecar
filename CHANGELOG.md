# Changelog

Released versions of Widget Screen, newest first. Each version is an annotated `vX.Y.Z` tag on `main`. Branches that haven't been merged yet are in [BRANCHES.md](BRANCHES.md), along with the version bump rules.

## Unreleased

Nothing yet.

## v0.16.0 (31 Oct 2026)

Six buttons a page (user request; Windows and the phone, the same rules on both):

- **Quick Launch and the Soundboard always show six buttons**, whatever size the card is. Small cards no longer drop to one or two buttons with lots of pages, and big cards no longer fill up with many small buttons: the six buttons grow with the card instead.
- The six sit 3 × 2, 2 × 3, in one row or in one column, whichever suits the card's shape while keeping every button at full touch size. With fewer than six buttons or sounds, they fill the card.
- On the smallest Quick Launch cards the buttons show just their icon; the name is in the tooltip. Wider cards put the name beside the icon, taller ones under it.
- The smallest sizes went up to fit six buttons: Quick Launch 288 × 204 and the Soundboard 300 × 240, also on small screens. A saved card smaller than that grows to the new minimum.

## v0.15.0 (31 Oct 2026)

Much more resizable widgets (user request, designed with the Impeccable skill; Windows and the phone, the same rules on both):

- **Edge grips.** Edit layout adds a grip in the middle of each side, next to the four corner grips. Drag one to change only the width or only the height. They show on sides long enough to keep clear of the corners.
- **Much smaller cards.** Every widget can now be made a lot smaller: Quick Launch down to 200 × 152 (one button with paging), Weather and the Soundboard to about 240 × 152, Focus to 280 × 192, Now Playing to 312 × 200, the mixer to 320 × 224, Spotify to 360 × 360. Your saved layouts and the default ones don't change.
- **Cards rearrange instead of cutting off.** Below a card's comfortable size it folds its secondary rows away and keeps its main reading and buttons at full touch size: Focus folds the presets into one length button beside Start that steps through them; Now Playing becomes the song with the cover and the buttons; the mixer moves the microphone beside the master slider and, on a short card, keeps the app list one tap away behind an **Apps** button; System stats drop the graphs and bars on narrow cards so the numbers stay big; Weather drops the details line; Spotify gets a smaller now-playing strip; the Connect Spotify and Connect Discord cards keep their button.
- Big cards look the same as before.

## v0.14.0 (31 Oct 2026)

Easier widget organizing (user request; Windows and the phone, the same rules on both):

- **Widgets snap to each other.** With Snap on, a card you move or resize lines up with the other widgets' edges, sits one gap beside them, or centres on them when it comes within a few units, and snaps to the screen's edges. Anywhere else it follows the 8-unit grid as before. Resizing never goes under a card's minimum size.
- **Guide lines** show which edges or centres a card lines up with while you hold it, and disappear when you let go. With Snap off, cards move freely as before.
- **Arrange** in Edit layout lines up the shown widgets neatly in their current order, column by column, filling the screen without gaps or overlaps. Hidden widgets keep their places. It is one undo step, and nothing is saved until Save layout. Reset layout still goes back to the default widgets.
- On the phone, Edit layout gets the same snapping, guides and Arrange button, and saves to the PC as before.

## v0.13.0 (31 Oct 2026)

Keep the game focused (user request; Windows only, the phone is a separate device and never touches the PC's focus):

- **Tapping the dashboard no longer takes the focus** from the game or app on your main screen. Buttons, sliders and dropdowns work as before; Windows just leaves the keyboard where it was.
- **Text boxes still take the keyboard** (Spotify search, Settings fields), so you can type; click back into the game and the dashboard stops taking the focus again.
- The window coming back from the tray when your phone disconnects, opening at sign-in, and a monitor being unplugged or replugged no longer grab the focus either.
- **Settings → Display → Keep the focus on the game or app I'm using**, on by default. With it on, F11 and Escape reach the dashboard only after tapping a text box; turning it off brings back the old behavior.

## v0.12.0 (31 Oct 2026)

A setup exe (user request; Windows only, the APK is already Android's installer):

- **A setup exe**, `WidgetScreen-Setup-vX.Y.Z.exe`, in every release folder. It installs Widget Screen for the signed-in user in `%LOCALAPPDATA%\Programs\Widget Screen` without administrator rights, adds a Start menu shortcut, starts the app when you sign in (a checkbox, on by default), and adds an entry to Settings → Apps → Installed apps.
- **Updating**: running a newer setup replaces the installed copy in place, closing it first, and keeps the startup choice. Settings, sign-ins and paired phones stay in `%LOCALAPPDATA%\WidgetScreen` as before.
- **Uninstalling** from Installed apps removes the app, both shortcuts and the entry. Settings, sign-ins and paired phones are kept unless you tick the box to delete them.
- Silent switches for scripting: `/S`, `--no-startup`, `--launch`, `/uninstall`, `--remove-settings`.

## v0.11.0 (22 Oct 2026)

Phone-native polish (audit branch 10; phone only, see the design note for what the PC side already has):

- **One calm offline card** at the top of the upright phone list while the PC is away ("Can't reach DESKTOP · trying again in 15 s", with Try now and Settings → PC), instead of a sticky status line.
- **A firewall hint right after pairing**: when the first reading after pairing fails, the phone says to allow Widget Screen on private networks in Windows Firewall, where the problem usually is.
- **Edit layout on a phone**: a readout of the card's size and place sits at the top of the card while a finger drags it, and the Widgets panel gains Move and Size nudge arrows (8 units a tap with Snap on, 1 without) for the fine moves a finger can't do.
- **Keep the screen on: Always, While charging or Never** (Settings → Display). New installs get While charging, which suits a phone on a stand; existing settings keep what they had.
- **Haptics**: a light tap on every button.
- **Android 13+ Back** goes through the system's back callback (predictive back), so Back keeps closing panels and sheets on new Android versions.

## 0.10.0 (22 Oct 2026)

Merges `feature/quick-launch-polish`, audit branch 9 (`docs/design/quick-launch-polish.md`).

- **A colour per button**: Settings → Quick Launch → Icon color picks one of the four accents for a button's glyph (or the theme's accent), so Spotify, Browser and Files aren't six identical green glyphs. The phone shows the same colours.
- **Control buttons show their state**: Mute, Microphone, Focus and Quiet mode buttons take the selected look while the thing they toggle is on, on both apps.
- **Drag to reorder** buttons in Settings → Quick Launch, with a mouse or a finger; the up and down buttons stay.
- **A hotkey names its target**: a hotkey button's tooltip (and the phone's confirmation) says which app it would go to right now, for example "Win+Shift+S → chrome".

## 0.9.0 (22 Oct 2026)

Merges `feature/readable-small-screens`, audit branch 8 (`docs/design/any-screen-size.md`).

- **Text size goes up to 140%** (Settings → Theme on the PC, Settings → Theme on the phone), for small screens and screens you sit far from. Above 115% some cards show less at once.
- **Small high-DPI screens keep readable text**: on Windows, a secondary screen smaller than 1920 × 720 on a system with display scaling above 100% now scales the dashboard up with that scaling and lets the widget canvas shrink to 60% instead of 80%. Screens at least 1920 × 720 are not affected, so the owner's panel looks exactly as before.

## 0.8.0 (22 Oct 2026)

Merges `feature/weather`, audit branch 7 (`docs/design/weather.md`).

- **Units**: Settings → Weather chooses Celsius and km/h or Fahrenheit and mph (the region's default until you pick); the phone follows the PC.
- **Today's high and low** in the details line ("↑ 25° ↓ 14°") and **the next hours** (now, +3 h, +6 h) on a tall enough card, on both apps.
- **A condition glyph** (sun, partly cloudy, cloud, fog, rain, snow, storm) beside the condition, drawn as a line path on the PC and in `Glyphs.java` on the phone.
- **The whole card is the refresh tap**; the Refresh button is gone. With no city, the card says where to choose one (and opens Settings → Weather on the PC).

## 0.7.0 (22 Oct 2026)

Merges `feature/now-playing`, audit branch 6.

- **The Now Playing position moves every second** while a song plays, on the PC and the phone, instead of jumping every three seconds; readings still correct it.
- **Any media app is named** in the card's corner (Firefox, VLC, Media Player, Tidal and so on, or a tidied app id), not only Spotify, Chrome and Edge. The phone receives the name too.
- **The playing app's volume is one tap away**: a volume button on the card opens a small panel with that app's mixer slider (master volume when the app has no mixer row), on both apps.
- Titles stay on one line (the full title is in the tooltip or content description), so the artist no longer gets pushed around by long names.

## 0.6.1 (22 Oct 2026)

Merges `feature/confirm-sheet`, audit branch 5 (`docs/design/confirm-sheet.md`).

- **One themed confirm sheet.** Quick Launch buttons that ask first now ask in a sheet over the dashboard (52 px Cancel and Run buttons, Escape or a tap outside cancels) instead of a Windows message box; the phone shows the same sheet instead of the system dialog.
- **First-launch tips** in the header ("Swipe sideways to switch pages · Settings → Phone pairs your phone"; the phone's own line on the phone) with a Got it button that puts them away for good. They show while there is one page and room beside the clock.
- **Settings → Phone comes before Discord**, since it's used far more often.
- **Tray mode with the phone is now off by default** (Settings → Phone). The dashboard vanishing the moment the phone app opened surprised more than it helped when both screens were in use; turn it on if you only ever use the phone.

## 0.6.0 (22 Oct 2026)

Merges `feature/focus-finish`, audit branch 4 (`docs/design/focus-finish.md`).

- **A finished focus session is hard to miss**: the whole card takes the accent tint, the time reads 00:00 with "Tap to clear", a chime plays (Settings → Theme, on by default, silent in Quiet mode), and the phone shows and sounds the same. A tap on the card clears it; otherwise it clears itself after a minute.
- **A fourth preset of your own length**: the "…" button opens a 5 to 180 minute picker inside the card; the length starts a session and stays as the fourth preset on both apps.
- **Sessions today** under the progress bar ("3 sessions today · 1 h 40 m"), counted on the PC and shown on the phone.
- A paused session dims the time so the pause reads from across the room.

## 0.5.0 (22 Oct 2026)

Merges `feature/mixer-events`, audit branch 3 (decision 011).

- **The sound mixer no longer does its Windows audio work on the dashboard's thread.** Outputs and app sessions are read on a dedicated audio thread, output names are cached, and Windows' own device notifications trigger a read the moment an output appears, leaves or becomes the default, so a new headset shows up at once instead of at the next 2-second beat.
- Apps in the mixer are listed playing first, idle next and System sounds last, on both apps.
- The live output levels sit under the master slider, where the sound is, and the row disappears when levels are off or paused instead of showing a dotted line; silence draws nothing.
- A phone and the dashboard share each audio, system and Now Playing reading for a second, so a connected phone no longer doubles the PC's work; the layout version the phone checks is only recomputed after a settings save, an edit or a screen change.
- Network adapters are enumerated every 30 s (and when Windows reports a change) rather than on every System stats tick.
- The phone no longer refreshes every card after each slider nudge, so mixer rows stop flickering mid-drag.

## 0.4.2 (22 Oct 2026)

Merges `fix/phone-link`, audit branch 2.

- **Find PCs on this Wi-Fi** now sends the discovery packet to each network interface's own broadcast address (with 255.255.255.255 as a fallback), which routers and many phones accept where the limited broadcast was dropped.
- The phone backs off while the PC is away (5 s, 15 s, then every minute) instead of knocking every 2 s with the screen on; a tap or a settings change still asks straight away.
- The phone remembers the PC's 12/24-hour clock choice, so the clock no longer flips to 12-hour while the PC is off.
- While the phone is editing the layout it no longer asks the PC for the layout section it would ignore.
- Phone link hardening: requests from web pages (an `Origin` or fetch header, or a host name that isn't this PC's address) are refused, so a page in your browser can't reach the link through DNS rebinding; at most 32 connections are served at once; the phone only accepts home-network addresses (private, link-local, `.local`), so a typo can't send its token to the internet.
- Settings → Phone lists each paired phone with when it paired and was last seen, and a **Forget** button per phone; **Forget all phones** stays.

## 0.4.1 (22 Oct 2026)

Merges `fix/save-settings`, the first branch from the app audit of 22 Oct 2026.

- **Fixed Settings → Save changes deleting every layout page except the first and turning phone control off.** The panel now saves on top of a copy of the current settings, so pages, the current page, Quiet mode and the Phone settings survive a save. A check covers it.
- The dashboard no longer closes when something unexpected goes wrong on the UI thread: the error is logged to `error.log` and shown on the status line, and the dashboard carries on (renders still fail loudly, and a storm of errors still exits).
- The phone link refuses a command whose number is missing or malformed instead of acting on it.
- "Opened ms-settings:sound" became "Opened Windows sound settings."
- Weather's "updated" time follows the 12- or 24-hour clock choice on both apps.

## 0.4.0 (21 Oct 2026)

Merges `feature/android-app`, then `feature/any-screen`, which was stacked on it.

From `feature/android-app` (decisions 007, 008 and 009, `docs/design/android-app.md`):

- **Android app**: a native Android version in `android/`, built by `scripts\build-android.ps1` into an installable `WidgetScreen.apk` (no Gradle, no libraries, no WebView), with the same widgets, palettes and accents. Release folders include the APK when the Android SDK is installed.
- **The phone is a remote for the PC**: it shows the PC's dashboard and every card controls the PC over home Wi-Fi: Focus, Now Playing, Quick Launch, the sound mixer with app outputs and the microphone, System stats, Weather, Spotify through the PC's connection and the Discord soundboard. The phone's own Settings are Theme, Display, Widgets and PC (find the PC and pair with its code). Sideways screens and upright tablets get the dashboard, upright phones get the cards in a list, and Settings → Display can keep the app sideways.
- **Settings → Phone** in the Windows app turns on the phone link for the local network (TCP 43830, UDP discovery on 43831), paired with a six-digit code. Paired phones are stored as token hashes in DPAPI-encrypted `remote-devices.dat`.
- **Tray mode**: a Hide to tray button in the header, and the window moves to the tray by itself while a paired phone shows the dashboard (on by default in Settings → Phone). Windows only.
- Fixed the phone showing no data right after pairing until the app restarted.

From `feature/any-screen` (decision 010, `docs/design/any-screen-size.md`):

- **Any screen size**: the dashboard keeps its 1920 × 720 design scale and lays itself out on a canvas shaped like the screen, so wider or taller second screens get more room, small screens keep readable text (the canvas may shrink to 80%), and narrow ones stack the header. The 1920 × 720 screen looks exactly as before. Layouts are saved per screen shape; a new shape stretches the closest saved layout, or arranges the page's widgets for it. Upright tablets get the dashboard; upright phones keep the list.
- **Layout pages**: up to eight named pages, each with its own widgets and layouts. Switch with the arrows in the header or a sideways swipe; add, rename, reorder and delete pages in Edit layout. The page shown is shared with the phone.
- **The phone mirrors the PC**: the phone shows the PC's layout and page, stretched to its screen, lists the PC's cards in order when upright, and saves its own Edit layout changes to the PC. The phone link gained a layout section and page commands.
- **Edit layout on Windows** uses the phone's list: a check mark per widget and the pages, instead of the widget dropdown.
- **Cards size their content**: Quick Launch fits as many buttons as its card holds (and grows them when they all fit), Focus, the Now Playing cover and the Weather temperature grow with their cards, and Spotify shows two or three columns on wide cards.
- `--render-size WxH` and `tests\screen-size-renders.ps1` render the dashboard for other screens.

## 0.3.0 (20 Oct 2026)

Merges `feature/discord-soundboard`.

- **Discord soundboard**: a new Soundboard widget plays your servers' soundboard sounds as you, in the voice channel you're in, through the Discord app on this PC. Connect it in the new Settings → Discord tab with your own Discord app's Client ID and Client Secret; Discord asks you once to authorize it. The widget starts hidden (turn it on in Edit layout), lists the current server's sounds first, then Discord's built-in sounds, and reacts to joining or leaving voice without polling. App details and sign-in are stored encrypted in `discord-session.dat` (decision 006). The live Discord sign-in and playback haven't been verified yet.

## 0.2.3 (11 Oct 2026)

Merges `feature/app-identity`.

- **App identity**: the exe has a logo (Focus in Lime beside Now Playing, Quick Launch and the mixer on a Midnight tile), shown in Explorer, on the window and in the taskbar. Its properties show the app name "Widget Screen", the author "Ali Alyami" and the version, which the build now takes from the newest tag.
- **Release copies**: `scripts\release.ps1` builds a tagged version from a clean checkout of its tag, runs every check and puts a self-contained copy in `releases\vX.Y.Z\` (git-ignored). The desktop shortcut opens the release copy instead of the `artifacts\app` build. See "Releases" in `BRANCHES.md`.

## 0.2.2 (10 Oct 2026)

Merges `feature/search-play`.

- **Spotify search plays songs**: tapping a search result now plays the song, like tapping a song in a playlist or album, instead of opening it in Spotify. Spotify's own autoplay decides what follows. If no Spotify device is active, the widget opens Spotify and retries. Connections without the playback permission still open the link (decision 003, updated).

## 0.2.1 (10 Oct 2026)

Merges `feature/ui-polish`, a polish, distill and clarify pass over the dashboard and Settings (`docs/design/ui-polish.md`).

- **Quieter dashboard**: labels that never change are gone (LIVE, EVERY 2 SEC, "Playing on this PC", the footer F11 hint and the tagline). The status line clears itself after about ten seconds.
- **Clearer states**: Quiet mode is a toggle that lights up when on, Focus shows when the session ends, and the microphone button states whether it's on or muted.
- **Spotify, not connected**: one "Connect Spotify" empty state with a Set up Spotify button replaces the empty tabs, list and Like strip. The card's header button is now "Account".
- **Polish**: a bigger Focus time in tall cards, the mixer's output name aligned left, a shared selected look for Spotify tabs, and a 13 px caption style for the smallest text.
- **Settings**: plainer headings and shorter copy, Weather in one column with the results list hidden until there are results, and Spotify setup as three numbered steps.
- **Removed** the "Arrange dashboard for Spotify + Now Playing" button. Reset layout does the same.

## 0.2.0 (10 Oct 2026)

Merges `feature/mixer-output-devices` and `feature/header-clock-stats`.

- **Sound mixer outputs**: every app's volume slider shows, whatever output it plays on. Each app has an output button to see and move it to another device, and tapping Master volume picks the default output (decision 005, `docs/design/mixer-output-devices.md`).
- **Header clock**: the time and date always show at the top left of the header, including in Edit layout. The Clock widget and the header brand are gone. Old layouts drop the Clock, and Focus grows into its space when it sat directly below it (`docs/design/header-clock-and-stats.md`).
- **Default layout**: Focus fills the left column, followed by Now Playing, Quick Launch, Sound Mixer and Spotify. Custom layouts are kept.
- **System stats scale with their widget**, using one to three columns that fill the card.
- Branch and version tracking: `BRANCHES.md`, this changelog, and worktrees under the git-ignored `.worktrees\` folder.

## 0.1.0 (9 Oct 2026)

First working version, built on the as-built WPF baseline from `design specfication/`.

- **Touchscreen polish** (`touchscreen-polish.md`): the Settings panel is 1440 × 672 with a footer Cancel, inputs and buttons are at least 52 px tall, slider thumbs are 48 × 48 and capture touch directly, the scrollbar area is 40 px, small text is 15, Focus presets show the selected one, and press-and-hold and flicks are off.
- **Spotify library widget**: tabs are Albums (recently played), Playlists and Search with cover thumbnails, plus a bottom strip with the cover, song, artist and Like. The Playing tab and playback controls are gone (decision 004).
- **Play in collection**: tapping a song inside an album or playlist plays it there, so playback continues through the collection (decision 003).
- **Fixed** Spotify sign-in timing out on networks with unrouted IPv6. Web requests now prefer IPv4 when a host has both.
- **Fixed** a crash on startup when Spotify was already connected.
