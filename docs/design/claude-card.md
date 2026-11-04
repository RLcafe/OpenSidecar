# The Claude Code card

The simple Claude Code card (Ali, 4 Nov 2026; decision 027). It replaces the Claude watcher (decisions 014–026): the pit wall, the critters before it, the per-thread lists and everything that fed them are gone from both apps.

> "I want to remove the complexity we worked on, take it down to a simple widget … four dashboards on the top, which shows the 5 hour, the weekly, and the tokens per minute. And I want a fourth one, which shows the official Claude mascot … Make a similar one to it. And it only indicates if there's an action pending … So just a general count under the mascot."

## What it shows

Four tiles, the same on the PC and the phone:

| Tile | Big reading | Under it |
| --- | --- | --- |
| **5-HOUR** | The plan's 5-hour use, as a percentage, with a bar in the card's ink (Amber from 90%) | "Resets 3:40 PM" |
| **WEEKLY** | The plan's weekly use, the same way | "Resets Sat 9 AM" |
| **TOKENS / MIN** | Tokens a minute over the last five minutes | A bar a minute for the last 30 minutes in a tall tile, then "Last 5 minutes" |
| **Mascot** | An original pixel robot | How many sessions need you, are blocked, are done and are working |

The counts are a coloured dot and a number each: **Amber** needs you, **Coral** blocked, **Mint** done, **Glacier** working (the extra stat). Only the kinds with sessions show; nothing open says "All quiet". Beside the mascot in a roomy tile each count has its words ("3 need you"); elsewhere the words are in the tooltip (Windows) and the content description (Android). The counts never name a session, a project or a thread.

The mascot is our own 16 × 16 pixel robot (antenna, visor, ears, arms and a chest panel), drawn in code from the same rows on both apps (`ClaudeMascot.Sprite`, `ClaudeText.SPRITE`), in the spirit of Claude's loading-screen mascot but not a copy of it. Its body wears the card's ink (Coral by default). Its antenna light, eyes and chest panel take the colour of the most urgent count, and its eyes change with it: wide open for needs you, crosses for blocked, looking aside for working, smiling for done, closed and dimmed when nothing is open. It is drawn at a whole number of units a pixel so it stays crisp, and it never animates.

## Layout

`CardSizing.ClaudeColumns` (Windows) and `claudeColumns` (Android) put the four tiles in **one row**, a **2 × 2 square** or **one column**, whichever leaves each tile closest to 1.25 : 1. A long card shows a row, a squarish one a square and a tall one a column, and the choice follows the card live while it is resized. Inside a tile the big reading follows the tile's size (18–64 units); the note under it shows from 96 units tall and the token history from 150. The mascot goes beside the counts when the tile is at least 1.35 times as wide as it is tall, and over them otherwise. The card's minimum is 360 × 280.

The card wears a label strip and status sticker like every other card (it used to be the exception), and its ink is chosen in Edit layout like theirs. The sticker says SAMPLE on the labelled fixture (`--render-claude-sample`, `--render-claude-needs`) and PC OFFLINE on the phone when the PC is away.

## Where the numbers come from

Everything is read-only. Nothing hooks into Claude Code, listens on a port or watches folders.

- **5-hour and weekly:** the widget's own Sign in with Claude (Settings → Claude; PKCE with Claude Code's public client, `user:profile` only, DPAPI `claude-session.dat`), `api.anthropic.com/api/oauth/usage` every 5 minutes while signed in and the card is on a page. A 429 or 5xx doubles the wait up to 15 minutes. A 403 means the plan has no limits and keeps the sign-in; a refused refresh ends it. A failed check keeps the last numbers, dimmed, "As of …". The tokens never leave Anthropic's hosts or reach the phone.
- **Sessions:** Claude Code's session list (`sessions\<pid>.json`: busy is working, waiting is needs you, idle is done) for sessions whose process still runs, and the Claude app's thread records (`claude-code-sessions\…\local_*.json`) for its own threads. The app's latest-reply summary (only when it summarizes the latest reply) refines an idle session: `review_ready` counts as needs you and `blocked` as blocked. App threads that aren't running count when they aren't archived and were active in the last 24 hours. Read every 2 s while the card shows; only changed files are parsed.
- **Tokens per minute and blocked by an error:** Claude Code's logs (`projects\…\*.jsonl`), every 10 s off the UI thread while the card shows. Each reply counts once (by request id) with its fresh input, cache writes and output; cache reads don't count. Logs seen for the first time are read from their tail (8 MB for one written in the last half hour, 64 KB otherwise, 64 MB a scan). A session whose latest entry is an API error counts as blocked until its next prompt.

Without a sign-in the plan tiles say "Sign in: Settings → Claude" (on the phone, "Sign in on the PC: Settings → Claude"), and a tap on the PC's card opens that tab. Without logs the tokens tile says so.

## The phone

The phone link's `claude` section is just the tiles' readings: `sample`, `plan` (signedOut, reading, current, failed, expired, noMeters), `updatedAt`, `fiveHour` and `week` (`pct`, `resetsAt`), `logs`, `logsRead`, `minutes` (30 numbers, oldest first) and `done`, `needsYou`, `blocked`, `working`. It never carries a token, a path or a session's name. The phone asks for it with the other cards (every 2 s, 5 s in the PC's Quiet mode, only while the card is shown), and `ClaudeWidget` draws the same four tiles from `core/ClaudeText` and `core/CardSizing`.

## The old connection

Earlier versions' Connect Claude Code added hooks, a status line and telemetry settings to Claude Code's `settings.json`, all running `ClaudeHook.exe`. `ClaudeCleanup` takes exactly those out once at startup, off the UI thread: it keeps a dated backup next to the file, removes only commands that run `ClaudeHook.exe`, removes the telemetry keys Connect recorded while they still hold its values, puts the user's earlier status line back, and deletes the old bridge folder (`%LOCALAPPDATA%\WidgetScreen\claude`). It leaves a `settings.json` that isn't valid JSON alone and says so in Settings → Claude, with a Remove the old hooks button to try again. The setup exe also deletes an installed `ClaudeHook.exe`.

## What was removed

On both apps: the pit wall and critters, projects and threads, periods, facts, lanes, the ledger, costs, models, context rings, crews, the header pill, the chime, Windows and phone notifications, `ClaudeHook.exe`, Connect and Update connection, the telemetry receiver on 127.0.0.1:43832, the folder watchers, Settings → Claude's intervals, projects, facts and filters, the phone's Notify me setting and its notification permission, and the `claude.focus` and `claude.collapse` commands.
