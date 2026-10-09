# UI polish, distill and clarify (10 Oct 2026)

A refinement pass over the dashboard and Settings, requested by the user with Impeccable (`polish distill and clarify the UI and UX`). The visual system, layout, widgets and behavior stay as they were. This note records what changed and what was removed, so later work doesn't reintroduce it.

## Principle

The dashboard is glanced at from arm's length while you work or play. Every word on it should tell you something true right now. Steady-state labels that only restate the obvious ("LIVE", "EVERY 2 SEC", "Playing on this PC") were removed; the same slots now speak only when something is wrong or needs a decision.

## Dashboard

- **Status line** starts empty and clears itself about ten seconds after a message, using the existing 1 s heartbeat (no new timer). Edit-layout details and a settings-load warning stay until replaced. The tagline "Ready. A little more control, a little less switching." is gone.
- **Footer hint** (`WINDOW PREVIEW · F11 SECOND SCREEN`) only shows keyboard hints while editing the layout. The Second screen button's tooltip still names F11 and Escape.
- **Quiet mode** is a plain toggle labelled "Quiet mode". When it's on, it takes the same selected look as the chosen Focus preset (Selected background, 2 px accent border), and its accessible name says on or off. "Quiet mode: off" read like an action.
- **Focus timer**: the status line says something useful instead of encouragement: empty when idle, "Ends at 12:35" while running (follows the 24-hour setting), "Paused", and "Done. Take a break." The time grows to 84 when the card is at least 440 tall, so the default full-column layout reads from across the room. The decorative timer glyph next to the title was removed.
- **Now Playing**: no hint while things work. Empty state reads "Nothing playing / Start music or a video in any app." The source label is blank when nothing plays or the app isn't one we name, instead of "WINDOWS MEDIA" or "MEDIA SESSION".
- **Sound mixer**: "● LIVE" and "OUTPUT LEVEL · LIVE" only appear as warnings (UNAVAILABLE, paused while editing, paused in Quiet mode). "APPLICATIONS · 3 SESSIONS" became "APPS · 3". The output device name now sits left with the chevron right after it (it used to drift to the centre on wide cards). The microphone button shows a microphone glyph and states the current state ("Microphone on", "Microphone muted"), not the gesture ("tap to mute"). App percentages scale with text size.
- **System stats**: the title matches its name everywhere else ("SYSTEM STATS", was "SYSTEM PULSE"), and the polling-interval label is gone.
- **Weather**: no placeholder flag when unconfigured; "No city yet", "Choose a city to see current conditions." and a "Choose city" button.
- **Spotify, not connected**: one empty state (cover-tile glyph, "Connect Spotify", one line of what you get, a "Set up Spotify" button that opens Settings → Spotify) replaces the tabs, the empty list, the disabled Like strip and the status line, which used to repeat "Connect in Settings → Spotify" twice. The header button reads "Account" (it was a second "Settings") and hides until connected.
- **Spotify tabs** use the shared selected look.
- **Caption style**: the smallest text (status flags, hints, credits, page count) is a 13 px `Caption` style instead of scattered 11–12 px values.

## Settings

- Title is "Settings" (was "Make yourself at home" plus a tagline).
- Section headings name the thing: Appearance, Readability, Display, Layout, System stats, Weather, Connection, First-time setup.
- Redundant helper text removed (the corners slider already shows its value; the footer already has Save and Cancel).
- The Widgets tab button is "Edit layout", the same words as the header button. Its note now says what actually happens: opening the editor closes Settings without saving other changes.
- System stats choices use plain words: CPU usage, GPU usage, Memory (RAM) usage, Network speed, Time since restart.
- Weather is one column with a City label, instead of a near-empty left column.
- Spotify first-time setup is three numbered steps, because the order matters.
- Quick Launch's Start menu picker has its own labels ("Start menu app", "Filter the list by name") instead of "filter by name in the box on the right".
- The footer status starts empty instead of "Settings are stored locally on this PC."

## Removed

- **"Arrange dashboard for Spotify + Now Playing"** (Settings → Spotify) and `LayoutEditor.ArrangeSpotify`. Since the default layout became the Spotify layout, it produced the same arrangement as Reset layout in the editor, and its description still mentioned the Clock widget that no longer exists. Reset layout covers it.

## Not changed

- Unicode hearts on the Like button (tests and the spec pin "♡ Like" / "♥ Liked").
- Focus preset order (25, 50, 5) and every touch target size.
- The close button's position and behavior.
