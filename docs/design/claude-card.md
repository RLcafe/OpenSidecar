# The Claude Code card

The simple Claude Code card (Ali, 4 Nov 2026; decision 027, with the instruments brought back by decision 028). It replaces the Claude watcher (decisions 014–026): the pit wall, the critters before it, the per-thread lists and everything that fed them are gone from both apps.

> Decision 027, the first ask: "I want to remove the complexity we worked on, take it down to a simple widget … four dashboards on the top, which shows the 5 hour, the weekly, and the tokens per minute. And I want a fourth one, which shows the official Claude mascot … Make a similar one to it. And it only indicates if there's an action pending … So just a general count under the mascot."

## What it shows

Three tiles, the same on the PC and the phone:

| Tile | Instrument | Reading and lines |
| --- | --- | --- |
| **5-HOUR** | A half-circle needle gauge: the use as an arc in the card's ink (Amber from 90%), a pace tick for how much of the five hours has passed, and the projection (where this pace ends at the reset) as a dashed Amber arc past the needle | The percentage, "Resets 3:40 PM", "on pace for 92%" (Amber from 90%) |
| **WEEKLY** | The week as a race: seven laps, one a day from the window's start with the day's name under each, filled by the use (each lap a seventh of the limit), the projection hatched past it, the lap the week is on outlined, and a chequered flag at the reset | The percentage, "LAP 4 OF 7", the pace and "Resets Sat 9 AM" |
| **TOKENS / MIN** | A needle gauge with a redline at three quarters of its scale (10k a minute, or the next round step above the half hour's busiest minute), over a burn line of the last 30 minutes with the redline dashed and the latest minute marked | Tokens a minute over the last five minutes (Amber past the redline), "Last 5 minutes" |

The instruments are the old card's (the pit wall of decision 017), redrawn in the card's ink and theme (`ClaudeGauges.cs`, `views/ClaudeGauges.java`; decision 028). The pace and the projection come from the two percentages and their reset times, so nothing new is read: the pace is how much of the window has passed (`ClaudePace.Elapsed`), and the projection is the use divided by it, shown from the window's first twentieth.

The robot tile with its session counts was removed by decision 028 (Ali, 4 Nov 2026: "remove the robot dashboard, keep the other three"): it could only count the sessions on this PC, never the threads running in the cloud. Its sprite, the counts and the code that read Claude Code's session list and the Claude app's thread records are gone.

## Layout

`CardSizing.ClaudeArrange` (Windows) and `claudeArrange` (Android) put the three tiles in **one row**, the two plan tiles **over a full-width tokens tile**, or **one column**, whichever leaves the tiles on average closest to 1.25 : 1. A long card shows a row, a squarish one two over one and a tall one a column, and the choice follows the card live while it is resized. Inside a tile the big reading follows the tile's size (18–64 units). A gauge sits beside its reading when the tile is at least 2.4 times as wide as the room under its name (the gauge takes up to 45% of the width), and over it otherwise; the reading shrinks to 22 units to keep its first line, and the other lines (the burn line among them) step aside when they don't fit (`CardSizing.ClaudeFit`). The week's laps are 30 units tall with the day names when that leaves a 22-unit reading, and 14 without them; narrow laps show the days' first letters. The card's minimum is 360 × 280.

The card wears a label strip and status sticker like every other card (it used to be the exception), and its ink is chosen in Edit layout like theirs. The sticker says SAMPLE on the labelled fixture (`--render-claude-sample`) and PC OFFLINE on the phone when the PC is away.

## Where the numbers come from

Everything is read-only. Nothing hooks into Claude Code, listens on a port, watches folders or reads its sessions.

- **5-hour and weekly:** the widget's own Sign in with Claude (Settings → Claude; PKCE with Claude Code's public client, `user:profile` only, DPAPI `claude-session.dat`), `api.anthropic.com/api/oauth/usage` every 5 minutes while signed in and the card is on a page. A 429 or 5xx doubles the wait up to 15 minutes. A 403 means the plan has no limits and keeps the sign-in; a refused refresh ends it. A failed check keeps the last numbers, dimmed, "As of …". The tokens never leave Anthropic's hosts or reach the phone.
- **Tokens per minute:** Claude Code's logs (`projects\…\*.jsonl`), every 10 s off the UI thread while the card shows. Each reply counts once (by request id) with its fresh input, cache writes and output; cache reads don't count. Only logs written in the last 31 minutes are looked at; one seen for the first time is read from its last 8 MB, and a scan reads at most 64 MB.

Without a sign-in the plan tiles say "Sign in: Settings → Claude" (on the phone, "Sign in on the PC: Settings → Claude"), and a tap on the PC's card opens that tab. Without logs the tokens tile says so.

## The phone

The phone link's `claude` section is just the tiles' readings: `sample`, `plan` (signedOut, reading, current, failed, expired, noMeters), `updatedAt`, `fiveHour` and `week` (`pct`, `resetsAt`), `logs`, `logsRead` and `minutes` (30 numbers, oldest first). It never carries a token, a path, a count or a session's name. The phone asks for it with the other cards (every 2 s, 5 s in the PC's Quiet mode, only while the card is shown), works the pace out with its own clock, and `ClaudeWidget` draws the same three tiles from `core/ClaudeText` and `core/CardSizing`.

## The old connection

Earlier versions' Connect Claude Code added hooks, a status line and telemetry settings to Claude Code's `settings.json`, all running `ClaudeHook.exe`. `ClaudeCleanup` takes exactly those out once at startup, off the UI thread: it keeps a dated backup next to the file, removes only commands that run `ClaudeHook.exe`, removes the telemetry keys Connect recorded while they still hold its values, puts the user's earlier status line back, and deletes the old bridge folder (`%LOCALAPPDATA%\WidgetScreen\claude`). It leaves a `settings.json` that isn't valid JSON alone and says so in Settings → Claude, with a Remove the old hooks button to try again. The setup exe also deletes an installed `ClaudeHook.exe`.

## What was removed

On both apps: the pit wall and critters, projects and threads, periods, facts, lanes, the ledger, costs, models, context rings, crews, the header pill, the chime, Windows and phone notifications, `ClaudeHook.exe`, Connect and Update connection, the telemetry receiver on 127.0.0.1:43832, the folder watchers, Settings → Claude's intervals, projects, facts and filters, the phone's Notify me setting and its notification permission, and the `claude.focus` and `claude.collapse` commands.

Decision 028 also removed the robot mascot tile, its four counts (done, needs you, blocked, working), `ClaudeSessions` (Claude Code's session list and the Claude app's thread records), the API-error check in the logs that fed Blocked, `--render-claude-needs` and the phone link's `done`, `needsYou`, `blocked` and `working`.
