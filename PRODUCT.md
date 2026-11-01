# Product

<!-- impeccable:product-schema 1 -->

## Platform

android

## Users

PC users who want their everyday controls and readings within a glance or a tap while they work or play games on the main monitor, without alt-tabbing.

- **With a second screen:** a secondary touchscreen on the PC shows the dashboard full screen. The owner's is 1920 × 720, but other screens must work too (user, 21 Oct 2026).
- **Without a second screen:** an Android phone or tablet on the same Wi-Fi becomes the widget screen. It shows the PC's dashboard and every card controls the PC (user, 21 Oct 2026).

Built by and for Ali Alyami first, and meant to work for other people with either setup.

## Product Purpose

Widget Screen keeps the PC's small, frequent jobs off the main screen: the time, a focus timer, Windows Now Playing, a Quick Launch deck, the sound mixer with per-app outputs and the microphone, a Spotify library, a Discord soundboard, and optional System stats and Weather.

Success means the user never leaves their work or game for these things, the dashboard stays light enough not to cost frames, and one failing service never takes the rest down.

## Positioning

A native dashboard (WPF on Windows, framework views on Android) with no WebView, browser, cloud service or companion server. The phone or tablet turns into the PC's second screen through a link built into the Windows app, paired with a code and limited to the home network. Controls are real system controls: per-app volume and output routing, Windows media, launching apps, files, sites and hotkeys.

## Operating Context

- The PC dashboard runs full screen on the secondary display only, or as a preview window on the primary. It sits beside the main monitor during work and full-screen games.
- The phone or tablet sits on a stand or in the hand, upright or sideways, on the same home Wi-Fi as the PC. It polls the PC for the cards it shows.
- Quick Launch, Spotify, Discord, the weather city and the System stats readings are set up on the PC. The phone's own Settings are Theme, Display, Widgets and PC.
- Every change ships to both apps in the same branch and release (`CLAUDE.md`, `BRANCHES.md`, `CHANGELOG.md`).

## Capabilities and Constraints

- **Two apps, one design language.** The Windows app (WPF, .NET Framework 4.8) is the design source. The Android app (Java, framework views, minSdk 26, phones and tablets) keeps the same palettes, glyphs and cards, reworked so they work on a phone (user, 21 Oct 2026). Android's OS guarantees apply (system Back, window insets, 48 dp touch targets, readable text), but Material components and Material theming are not adopted. `Platform` says `android` because the Windows desktop has no value of its own here; the Windows rules live in `CLAUDE.md`.
- **Not locked to one resolution.** The dashboard must adapt to secondary screens other than 1920 × 720, and to phones and tablets in both orientations (user request, 21 Oct 2026). It keeps the 1920 × 720 design scale and lays out on a canvas shaped like the screen, with layouts per screen shape and pages; the phone mirrors the PC's layout, and upright phones get a scrolling card list (decision 010).
- **Light on resources.** One 1 s heartbeat on the PC, no polling for hidden widgets or metrics, motion optional and off in Quiet mode.
- **Honest states.** Loading, unavailable, disconnected and working are always distinguishable. Simulated readings are never shown as live, and fixtures are labelled.
- **Local and private.** Preferences are local XML. Spotify, Discord and phone pairings are stored encrypted (DPAPI on Windows, Android Keystore on the phone). The phone link is off by default, local addresses only.
- **Inspectable actions.** Every Quick Launch action is visible in Settings, and consequential ones (arguments, scripts, installers, Delete, Alt+F4) ask first.
- **Undecided:** the physical size of other people's screens and comfortable text and touch sizing on them; whether controls must work during exclusive-fullscreen games; how the app is distributed to other people (today there is no installer, store listing or updater).

## Brand Commitments

- Name **Widget Screen**, author **Ali Alyami**, logo `assets/WidgetScreen.ico` (also the Android launcher icon).
- The visual system in `design specfication/as-built-specification-and-design.md` §3.3–3.5 is binding: Midnight, Slate and Warm palettes with Lime, Glacier, Lavender and Amber accents. Since the Sticker Sheet redesign (decision 021), widgets also wear eight inks (the four accents plus Coral, Mint, Rose and Lemon); [DESIGN.md](DESIGN.md) describes it.
- System fonts only: Segoe UI, Segoe UI Light for the clock and Segoe MDL2 Assets glyphs on Windows; the system sans-serif and the line glyphs in `android/.../views/Glyphs.java` on Android. No images or emoji as icons, no custom fonts.
- Voice: short and plain. Dashboard text only speaks when something is true or wrong (`docs/design/ui-polish.md`).

## Evidence on Hand

- Renders of every screen and Settings tab at 1920 × 720 in `design specfication/as-built-reference/` (`screens.md`, `01…11-*.png`), labelled as fixtures.
- Decision records `docs/decisions/001…009` and design notes in `docs/design/`.
- No user research, testimonials, usage numbers or store listing exist. Physical touch, real phones on real Wi-Fi, other screen sizes and live Spotify and Discord use are unverified; don't claim them.

## Product Principles

1. **Glance, don't switch.** Anything on the dashboard is one look or one tap away and never pulls focus from the main screen.
2. **Truth over decoration.** Show what's real, say plainly when something is wrong, and stay quiet otherwise.
3. **The game comes first.** Native, light and idle when hidden; resource cost is a feature decision.
4. **One product on every screen.** Each change lands on the PC and the phone, in the same design language, adapted to the screen in hand.
5. **The user stays in control.** Touch targets at their minimums, actions inspectable, consequential ones confirmed, nothing leaves the home network.

## Accessibility & Inclusion

- Touch first: inputs, buttons and dropdown rows at least 52 px, slider thumbs 48 × 48, mute buttons at least 48 px on the PC; 48 dp targets on Android.
- Quiet mode turns motion off. There is no separate OS reduced-motion support yet.
