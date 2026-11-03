# Claude Code watcher (1 Nov 2026)

The Claude Code widget on both apps, built from the design brief below (kept as Ali approved it). The technical choices are in [decision 014](../decisions/014-claude-code-bridge.md), [decision 015](../decisions/015-claude-sign-in-and-telemetry.md) (the fix that made it work with the Claude app's Code tab) [decision 016](../decisions/016-claude-projects-and-threads.md) (projects with their threads, and the refresh intervals) [decision 023](../decisions/023-claude-trust-the-sources.md) (which sessions are alive, Stuck, the plan check and the telemetry port) [decision 024](../decisions/024-claude-honest-numbers.md) (where costs come from, the side panel facts, and Windows first for packages 2–4) [decision 025](../decisions/025-claude-instant-and-everywhere.md) (watchers, notifications, phone alerts, helpers on the car, resolved without the app) and [decision 026](../decisions/026-claude-watcher-on-the-phone.md) (packages 2 and 3 on the phone).

## On the phone (decision 026, v0.42.0)

Packages 2 and 3 reach the Android app. The phone's card shows the four facts picked on the PC for the period, "~" before an API value with unpriced models, the 5-hour tank's "full in … at this pace", the plan chips, and each car's pit crew and task sticker. Rows say "Retrying after an API error · HTTP 529 · attempt 3" or "Closed in Claude Code · 1 h ago". Holding a thread opens a sheet with what the PC's tooltip says, with Go to it. The phone raises a notification for each new alert while the app is open (Settings → Widgets → Notify me when a Claude Code session needs me, on by default; silent in the PC's Quiet mode), and a tap opens the Claude card's page. There is no background watcher.

## Instant and everywhere (decision 025, v0.41.0)

Package 3 of the Watcher action plan, Windows only (the phone link already sends what the phone will show).

- **At once.** While the card is on a page, Windows says when a session file, Claude Code's session list, a log or a Claude app thread record changes, and the card follows straight away. Reading everything every 2 s becomes a 30 s fallback; a shown card with sessions running is still worked out at the chosen interval, since turns and revving go by the clock.
- **Windows notifications.** When a session needs you, gets stuck, or finishes a turn of 10 minutes or more, a Windows notification names the project, the thread and what it waits for. A click brings the session forward. Settings → Claude → Notify me in Windows (on by default); Quiet mode keeps it silent.
- **Phone alerts.** Every answer to the phone carries the last ten minutes of alerts, numbered (`claudeAlerts`, `claudeAlertSeq`, `claudeQuiet`), so the phone can raise its own notification within one poll.
- **The pit crew.** A car carries a small figure for each helper (subagent) running, up to three then "+N", and a "3/5" sticker for its task list on cars wide enough. A lead whose helpers are still out is working, not on your turn. The tip says "2 helpers working · tasks 3 / 5".
- **Resolved without the Claude app.** Closing Claude Code at its prompt, logging out or `/clear` keeps the thread, resolved and parked under its cover ("Closed in Claude Code" in the tip); a later reply or a resume takes it back out, and the Claude app's own record wins where there is one. SessionStart's title names the car.
- **Garages and settings.** A `--worktree` session parks in the garage of the folder its worktree came from. An organisation's managed Claude Code settings that override the hooks, status line or telemetry are named in Settings → Claude and in the Connect sheet. `CLAUDE_CONFIG_DIR` set for the user is honoured even when Widget Screen started first.
- **Lighter.** The project groups are worked out once per view, and a thread's tip is written when it opens, from what the card shows then.

## Honest numbers (decision 024, v0.40.0)

Package 2 of the Watcher action plan, Windows only (Ali, 2 Nov 2026: packages 2–4 on Windows first, the phone afterwards). The phone link already sends the new numbers.

- **Costs.** Claude Code's own cost from telemetry comes first, then list prices. A reply from an unknown model shares what's left of the session's total from the status line. With neither, the API value gets a "~" and a tip naming the model; it never shows a made-up $0. The tip says where the costs came from and compares Claude Code's count for the running sessions with the widget's.
- **Status line.** It refreshes every 30 s (unless you set your own) and keeps Claude Code's cost, clocks, lines, context size, style, agent and worktree. The thread's tip shows the mode, agent, worktree and style.
- **Side panel facts.** Settings → Claude → Side panel facts picks four, for the card's period:

  | Fact | Value | Words under it |
  | --- | --- | --- |
  | Tokens | tokens | period · replies or API value |
  | Cache hits | % of input from the cache | CACHE HITS |
  | Tool calls | calls from the logs | how many asked you |
  | Lines | +added | −removed · threads |
  | Slowest tool | its average run | SLOWEST · TOOL |
  | Tool failures | % failed | N OF M FAILED |
  | API errors | errors in the last hour | API ERRORS · LAST HOUR |
  | Helpers | subagents' share of tokens | HELPERS · period |
  | Edits | accepted | EDITS OK · N REJECTED |
  | Commits | commits | COMMITS · N PRS |
  | Active time | Claude's | CLAUDE · YOU time |

  API errors and a failure rate of 20% or more show in the warning colour. Facts that need telemetry show "—" until it arrives.
- **Revving** means a tool call has run past twice its usual time for that tool (3 to 60 s); a request after the call ends it.
- **Retrying.** A car stopped by an API error says "Retrying after an API error · HTTP 529 · attempt 3" in its tip.
- **Time to the limit.** The 5-hour tank says "full in 1 h 40 at this pace", in the warning colour, when the last half hour's pace fills it before the reset.
- **Plan chips** under the plan row, up to six: narrower weekly windows beyond the two the week tile names, extra usage (on or off, percent, used of limit), and a gateway's spend limit. A chip warns from 80%. The chips step aside while a session needs you, so the ask strip and the ledger keep their room.
- **Helpers apart.** A thread's tip says "Own 12.4k · helpers 3.1k tokens today".
- **First scan.** Large logs are read from the newest end in 1 MB chunks, at most 64 MB a scan, down to the week's edge.

## Trust the sources (decision 023, v0.39.0)

Package 1 of the Watcher action plan, both apps:

- **No live session is dropped.** A session goes only with its own SessionEnd or when its process has exited (any process name; a reused pid doesn't count). `/clear` keeps it. Sessions only Claude Code's list or its logs know show too, and Connect reads them at once.
- **Stuck.** A turn that ended on an API error shows a spinning car, a "Stuck" chip and why: "Rate limited, resets 14:05", "Overloaded, try again", "Sign in again", "Reply too long" and so on. It stays until the next prompt. The heading counts "N stuck", and stuck threads rank after the ones that need you.
- **Usage-limit waits.** Claude Code carrying on by itself is Working; waiting for Enter is Needs you; giving up is Stuck.
- **Context ring.** A ring on the car's corner from 60% of the context window (muted), 80% and 95% (warning, filled at 95%). The tip says "Context 82% used, compaction soon". Medium shows the fullest car's ring per garage.
- **No usage meters.** When Anthropic says the plan has none (403), the plan row says "Your plan has no usage meters." and the sign-in stays.
- **Telemetry port.** If something else holds the port Claude Code sends telemetry to, the side panel says "Telemetry off: port 43832 in use" and Settings → Claude offers Update connection, which moves it to a free port (43832–43839).
- **More hooks** for failed tools, denied permissions, helpers, tasks, model switches and folder changes; Settings → Claude offers Update connection once to add them.

## Archived and hidden threads (decision 020)

- **Idle:** open, your turn for 30 minutes or more. The engine is off, the car is dimmed and the chip is grey.
- **Ended:** the car is under its cover.
- **Archived:** the thread was archived in the Claude app (`isArchived`, the only such flag on the PC; there is no "resolved" one). The car is under its cover, dimmed the most, with an "Archived" chip, and it parks last in its project.

- **Resolved** (since v0.34.1, Ali's choice): the Claude app's summary of the latest reply says "completed". It looks like Archived, with a "Resolved" chip. Live state wins: if it works, needs you or compacts, that shows instead. "Review ready" and "blocked" don't count.

Settings → Claude has two choices:

- **Hide resolved and archived threads** (on by default).
- **Hide idle threads** (off by default).

They apply to the Large ledger, the Medium rows, the phone and the heading's thread count. Hidden threads still count in their project's tokens, cache reads, replies, models and lines, and the project line ends "+N hidden". See [decision 020](../decisions/020-claude-archived-and-hidden.md).

## Fixes from the live app (decision 019)

From Ali's first look at v0.32.1, both apps:

- **Motion.** The cars move in Quiet mode unless Settings → Claude → Keep the cars still in Quiet mode is ticked.
- **States.** Needs you (beacon and hazards), Your turn (brake lights on, Glacier chip) and Idle (after 30 minutes or before anything ran: lights off, 70%, grey chip).
- **Ledger columns.** Tokens without cache reads, a Cache reads column on Large, and Replies in place of the API value unless Settings → Claude shows it.
- **Plan meters.** The usage answer's `limits` list is read, and "No 5-hour limit on your plan right now" shows when the plan has none.
- **Projects.** Rolling periods, and a quiet garage ("Quiet · last used Oct 25") for every ticked known project.
- **Lanes.** Projects, with a busy project's threads under it.
- **Dust.** Working cars kick up dust behind their tyres.

See [decision 019](../decisions/019-claude-live-fixes.md) for the evidence.

## Thread names (decision 018)

Ali reported (1 Nov 2026) that threads had no names: the Claude app titles every Remote Control thread "Remote Control session (from a project)". Placeholders now count as no title. A thread is named by a rename, then a generated title (Claude Code's or the app's), then the first line of its first request (60 characters, cut at a word). An id label ("Thread 1b2187") is the last resort. Remote Control shows as where it runs ("Remote Control · ended 1 h ago"), in the ledger, the Pit stop strip and tooltips, on both apps. The name is picked again on every refresh, so a better title replaces the request.

Since v0.41.1 (Ali's report, 2 Nov 2026: "threads and names of threads dont show up"): the Claude app now titles these threads "Local session (from a project)", which also counts as a placeholder (so do "Local session" and "Cloud session"). After the app's title, Claude Code's own name for the session from its session list (`nameSource` "derived") comes before the first request, unless it only repeats the folder. Two copies of the app on the same settings file had stacked on the touchscreen, the hidden one without the telemetry port; a second start now brings the running copy forward (out of the tray or up from the taskbar, without taking the focus) and closes (`SingleInstance`, keyed on the settings path, so renders and `--settings` copies still run side by side; a copy restarting for full-sensors mode is waited for up to 8 seconds).

## Only this PC's threads (v0.42.1)

Ali reported (3 Nov 2026) five running threads in a project while the card showed one. On his PC every source (Claude Code's session list, the bridge, the Claude app's thread records, the logs) held exactly one thread for that project, a Remote Control one; the other four were claude.ai project threads running in the cloud, which leave nothing on the PC. There is no supported way for the widget to list cloud threads (its sign-in is `user:profile`, for the plan meters only), so the card stays a view of this PC and says so: the heading's count ends "on this PC", the empty state is "No Claude Code threads on this PC …", and on Windows the summary's tooltip explains it. Same words on the phone.

## v2: the pit wall (decision 017)

Ali approved the v2 design on 1 Nov 2026: [claude-watcher-v2.html](claude-watcher-v2.html) is the spec (its layouts, sample data, drawing code and animation table). He chose the drawn pixel-art cars over generated art, so the page's image-prompt pack and its "one thing to confirm" panel don't apply. This section supersedes the critters described below; the sources, the project filter, the refresh intervals and folding per project stay as they were. The technical choices are in [decision 017](../decisions/017-claude-pit-wall.md).

As built, both apps:

- **Large** (from 640 × 420) has these rows:
  - **Heading:** "CLAUDE CODE · PIT WALL" (from 900 units of body; "CLAUDE CODE" below that), a dot and the live summary, the Sample or "as of" flag, and the 5 h / Today / Week switch (64 × 48 buttons).
  - **Plan row** (124 units tall):
    - the 5-hour tank: gauge, percentage, "on pace for 92%", "resets 4:18 pm · in 1 h 38 m";
    - the week: "WEEK · LAP 5 OF 7", "resets Wed 9:00 am", the percentage, pace, the Opus/Sonnet windows, and seven laps with day names and the flag;
    - from 860 units of body, the revs: gauge with a redline, tokens a minute, the hour's burn line.
    - Without plan limits, one tile says what they need.
  - **Pit stop strip:** only while a thread needs Ali. Its car with hazards, "Pit stop: title · project", "Wants to use Bash · waiting 2 min · Claude app" and Go to it (shown when its window can be found).
  - **Ledger,** which scrolls inside the card:
    - Garage rows are 52 units: the garage (a 48-unit tap target), name, "3 threads · 2 running · Claude app" (or "4 cars in the garage · tap to roll them out" with a count bubble), a share bar, tokens, ≈ $, model mix and lines.
    - Car rows are 36 units: car, chip (Needs you, Working, Tool run, Compacting, Retrying, Done, Ended, Ready), title, where and age, tokens, ≈ $, mix and lines. A running car's row brings its window forward, and ended rows are at 62%.
    - The models column needs a 560-unit table, lines 660.
  - **Side panel** (300 units, from 1000 units of body): the 5-hour lanes for the four busiest projects, with times under them, and four facts.
- **Medium:**
  - the heading (title, summary, flag);
  - two mini tiles (5-HOUR or WEEK, the reset time or day, the percentage, "pace 92%", and a pace meter with the projection hatched);
  - then 44-unit garage rows (taller at bigger text sizes): garage, name with up to three inline cars (or the count bubble), "Needs you · title" in Amber, tokens and $ (from 400 wide).
- **Small:**
  - the car that needs Ali (or the most urgent running one), a third of the card's width;
  - its project, "Needs you · 2 min" or "Working · 6 min", and "+1 working · 4 more threads";
  - then "5-HOUR 62%", "pace 92% · 4:18 pm" and the pace meter, when the text above leaves room.
- **The card's border** still turns Amber 2 units when a thread needs Ali, and the header pill shows a still car with its hazards on.
- **Motion,** per the spec's animation table: stepped 8-frame cycles on one frame clock per card. It stops when the card is hidden, the window is minimised or in the tray, the phone app is in the background, or Quiet mode is on (state swaps only). Rolling in and out takes 1.2 s in the project's rows: tail lights toward you going in, headlights toward you coming out. Only cars that show in the ledger move.
- **Data added:** pace and laps from the reset times; tool decisions, results, API errors and lines changed from telemetry; cache reads per reply; and a PreCompact hook for compacting. Connect adds the hook, so older connections offer Update connection once.

Renders: `tests\size-design-renders.ps1` scenes `claude-small` (300 × 200), `claude-medium` (472 × 310), `claude-large` (1216 × 576), `claude-large-narrow` (900 × 576), `claude-large-min` (640 × 420), `claude-wide`, `claude-needs`, `claude-needs-small`, `claude-needs-large` and `claude-pill`.

Not verified: the phone's views on a device (no device or emulator was available), the roll on the phone, real telemetry for tools and lines from a session started after Update connection, and a real compaction firing PreCompact.

## Projects and their threads (decision 016)

Ali asked (1 Nov 2026) for the projects with their threads under them, then for each project to be a big critter with its threads as baby critters that it eats and spits out. This section supersedes the Medium and Large layouts described further down (the lineup, the session rows and the usage panel). Small is unchanged.

- **Threads** are every Claude Code session of the last week, running or ended: running sessions as before, plus the Claude app's thread records (its sidebar titles) and Claude Code's logs. A thread sits under the folder it started in, with worktrees under the folder they were made from. The period switch (5 h / Today / Week, kept in settings.xml, default Today) shows the threads active in it, and running ones always.
- **A project's big critter** has its body and colour from the project's name, as before. Its face is its most urgent running thread's: Needs you is Amber with arms up and a hop. With nothing running it sleeps. Under it are its name and "3 threads · 1.8M · ≈ $2.01".
- **Baby critters** are 40% of their project's critter (at least 24 units on Large, 16 on Medium) in the same body and colour. Each has its own face; an ended thread sleeps at half opacity. On Large each baby shows its title (up to two lines, ellipsis) and its tokens for the period, or "Needs you · 2 min". The tooltip holds the full title, the state or "Ended · last active", usage, model and where it runs. Tapping a running baby brings its window forward.
- **Folding:** tap a project's critter. Its babies shrink and slide into its opened mouth, and the mouth closes with a gulp (350 ms). Folded, it has puffed cheeks, a belly and a count bubble on its body's top right. Tap again and the mouth opens and the babies pop back out with a small overshoot. Quiet mode skips the motion. The fold is kept per project in settings.xml and shared with the phone (`claude.collapse`). A folded project still turns Amber, and keeps its bubble ring Amber, when a thread inside needs you.
- **Large** (≥ 640 × 420): the heading, a strip with the two plan meters, up to three narrower weekly windows (from 1000 units wide) and the 5 h / Today / Week switch, then a column per project at least 216 wide (`CardSizing.ClaudeColumns`). The rest are counted in a last "+N more projects" column with the first four names. Babies sit in a grid of `ClaudeBabiesPerRow` cells, centred when there are only a few, and the grid scrolls inside its column when it doesn't fit.
- **Medium:** the heading, a row of project critters (one per 136 units, at most six, the last becoming "+N more projects") with up to three babies each and "+N", over the meter strip. Babies show only when the card leaves room for a 40-unit parent above them (`ClaudeMediumBabies`).
- **Empty:** "No Claude Code threads today." (or "in the last 5 hours." or "this week."), with the dozing critter.
- **Settings → Claude → Refresh:** "Update the card every [−][2][+] seconds" (minimum 1) and "Check plan limits every [−][300][+] seconds" (minimum 10, with the note "Anthropic may slow down very frequent checks." below 60). A refused check (429, 5xx) doubles the wait up to 15 minutes, and the card shows "Plan limits as of hh:mm". Quiet mode doesn't stretch either. The project list says how many threads each project had this week.
- **Phone:** the same designs, critters and motion. Its card asks for the `claude` section every "update the card" seconds (at least 2), and the rest of the dashboard keeps its own pace.

## Since the fix (decision 015)

The first version only worked fully for terminal sessions. Ali runs Claude Code in the Claude desktop app's Code tab, which never runs a status line and keeps its sign-in out of `.credentials.json`, so the plan meters stayed empty. Idle sessions that started before Connect didn't show either. What changed:

- **Sessions come from three sources, merged by newest news.** The hook files, Claude Code's own session list (`sessions\<pid>.json`: busy, idle or waiting) and telemetry events. Every running session shows, including Code tab sessions and ones started before Connect. A Code tab session's tap brings the Claude app forward, and its Large row says "Desktop", "VS Code" or "Terminal".
- **Needs you** comes from the permission hooks (Notification and the new PermissionRequest) and from Claude Code's "waiting" status ("Has a question for you").
- **Plan meters** come from Settings → Claude → **Sign in with Claude**: the widget's own browser sign-in (`user:profile` only, DPAPI in `claude-session.dat`, renews itself), checked every 5 minutes while signed in, whether or not Claude Code is open. The status line stays a second source.
  - Signed out, the meters area says "Sign in to Claude in Settings → Claude to see plan limits."
  - A failed check keeps the numbers with "Plan limits as of 10:42" (Large) and "AS OF 10:42" (Medium's flag).
  - An ended sign-in says "Your Claude sign-in has ended. Sign in again in Settings → Claude."
  - Opus, Sonnet and model-scoped weekly windows, when the plan has them, add lines under the Large meters.
- **Usage is tokens and cost.** Every project row and session shows tokens and "≈ $" (what it would cost at API prices). It comes from the logs (history) plus telemetry (live, Claude Code's own cost), with each reply counted once by its request id.
- **Medium shows plan and projects together.** Usage by project sits beside the critter lineup from 400 units of body width (a 190–300 column, up to four rows), under it on a tall narrow card (up to three), and the two plan meters stay in the strip. At the preferred 472 × 310 that is two project rows beside two critters.
- **Small** shows the 5-hour percentage beside its meter when there is room for a caption line (body height ≥ 8 + 104 × text scale). Otherwise it shows the bar alone, as before.
- **Settings → Claude** is now three parts:
  - Your Claude plan: sign in, sign out and status.
  - Claude Code: Connect, or Update connection for a v0.23–v0.25 connection, Disconnect, the telemetry status or the keys that keep it out, and the chime.
  - Projects: "Show new projects automatically" and a tick per project, newest first, with its last-used date and this week's tokens and cost. Unticked projects leave the card's sessions and usage on both apps. The plan meters always show the whole plan.
- **The phone's `claude` section** adds `plan`, `where` and `cost` per session, `cost` per project row, and `limits.extra`. The PC filters projects before it answers.

## As built (v0.23.0)

Where the build differs from the brief, or fills in what it left open:

- ~~The plan check borrowed Claude Code's token from `.credentials.json`~~ (replaced by the widget's own sign-in in decision 015: the Code tab keeps no token there).
- **Small is under 360 wide or 280 tall** (the brief suggested 240): below 280 the lineup's critters, names and the meter strip don't all fit at 140% text.
- **Entries are found again by the exe's name** (`ClaudeHook.exe` in the command), not the full `WidgetScreen/claude/` path, so isolated settings folders (checks, `--settings`) work the same.
- **The Notification hook only matches `permission_prompt|elicitation_dialog`.** `idle_prompt` needs no hook (it stays Done), so it isn't registered.
- **A session's project is the folder it started in.** Claude Code's log lines carry the shell's current folder, which moves when a session changes into a subfolder; counting by that split one project into many (36 "projects" from 19 sessions on Ali's PC), so each session keeps its first folder.
- **The Medium lineup says the state alone** ("Needs you"); Small and Large say how long ("Needs you · 2 min"). The hook's permission message is shortened: "Claude needs your permission to use Bash" reads "Wants to use Bash".
- **The phone's pill** comes from two small fields in every `/state` answer, `claudeNeeds` and `claudePage`, because the phone only knows the page it shows; the full `claude` section is sent only while the card is on the phone's page. The phone's not-connected line points to "Settings → Claude on the PC".
- **Settings → Claude** connects and disconnects right away (like Spotify), each through the confirm sheet, which now sits above Settings. The two checkboxes are saved with Save changes.
- **Updating Widget Screen refreshes the bridge**: at start, a connected app copies its own `ClaudeHook.exe` over the one in `%LOCALAPPDATA%\WidgetScreen\claude` when they differ (skipped while Claude Code is running it). Uninstalling Widget Screen doesn't disconnect Claude Code; disconnect first, or Claude Code reports a missing hook.
- **Renders and checks use a labelled fixture** (`--render-claude-sample`, `--render-claude-needs`; "SAMPLE" in the card's heading). `tests\size-design-renders.ps1` renders `claude-small`, `claude-medium`, `claude-large`, `claude-needs`, `claude-needs-large` and `claude-pill`.

Not verified at v0.23.0: real hooks firing in Claude Code, bringing a real terminal forward, the plan check against the live endpoint, and the phone card on a device. (Hooks firing from the Code tab was confirmed live on 1 Nov 2026; see decision 015.)

## The brief

User request (Ali, 1 Nov 2026): a widget for **Claude only** that shows the Claude Code sessions running on this PC, **lights up when one is waiting on him** (via Claude Code hooks), tracks **usage per project and per session**, and shows the **plan's usage limits from his normal Claude subscription, not the API**. Design with the Impeccable skill (authorized by Ali for this widget): **fun and wacky, but inside the app's palettes and accents**. Both apps (Windows widget + Android remote card), per CLAUDE.md.

Shaped with Impeccable (`shape`). The app's visual world is established (spec §3.3–3.5, `docs/design/widget-size-views.md`), so this card inherits it: Surface card, Raised tiles, Segoe UI with Light numerals, MDL2 glyphs, drawn paths for anything pictorial, no images, no emoji, no new fonts. The wackiness lives in one place: the critters.

### 1. Where the data comes from (all local, nothing leaves the PC)

### Plan limits: Claude Code's status line (official, no API, no token)
Claude Code passes JSON on stdin to the configured `statusLine` command. For claude.ai Pro/Max subscribers it includes:
```json
"rate_limits": {
  "five_hour": { "used_percentage": 23.5, "resets_at": 1738425600 },
  "seven_day": { "used_percentage": 41.2, "resets_at": 1738857600 }
}
```
`resets_at` is Unix epoch seconds per the docs (code.claude.com/docs/en/statusline); one GitHub issue shows an ISO string, so **parse both**. The field appears only after the session's first API response, either window may be absent, and windows drop once `resets_at` passes. Same payload carries `session_id`, `session_name` (AI title or /rename), `cwd`, `workspace.project_dir`, `transcript_path`, `model.display_name`, `context_window.used_percentage`, `cost.total_cost_usd`, `version`.
This is the primary source.

### Plan limits refresher: the usage endpoint (Ali approved, 1 Nov 2026: "API can be used but not to consume credits but to integrate Claude and fetch data")
While the card is visible and the status-line numbers are older than 5 minutes (e.g. no session open), fetch `GET https://api.anthropic.com/api/oauth/usage` with `Authorization: Bearer <accessToken>` and `anthropic-beta: oauth-2025-04-20`, using the access token from `%USERPROFILE%\.claude\.credentials.json` (`claudeAiOauth.accessToken`, check `expiresAt` ms first). Response (undocumented, parse defensively): `five_hour` / `seven_day` / `seven_day_opus` (may be null) each `{ utilization: 0–100, resets_at: ISO-8601 }`. It costs no credits.
Rules: read-only, at most every 5 min (15 in Quiet mode), only while the card is shown; send the token **only** to `api.anthropic.com` over HTTPS (use `PreferIPv4Handler`); never log, store, copy or send it to the phone; **never refresh the token** (that would rotate Claude Code's own sign-in); on 401/expired/missing file/any error, stop quietly and keep the status-line numbers. Settings → Claude checkbox "Check plan limits online when no session is open" (on). The phone receives only the percentages and reset times. Show which source and age in the Large design ("as of 10:42").

### Session states: Claude Code hooks
Hook input (stdin JSON) always has `session_id`, `transcript_path`, `cwd`, `hook_event_name`. Register these, all `"type":"command"`, `"async": true` except none need to block:

| Event | Matcher | Card state |
| --- | --- | --- |
| `SessionStart` | (all) | **Ready** (idle, sleepy) |
| `UserPromptSubmit` | — | **Working** |
| `PostToolUse` | (all) | **Working** (clears Needs you after a permission was granted) |
| `Notification` | `permission_prompt\|elicitation_dialog` | **Needs you** (keep the `message` text, e.g. "Claude needs your permission to use Bash") |
| `Notification` | `idle_prompt` | stays **Done** (no escalation) |
| `Stop` | — | **Done** (finished, your move) |
| `SessionEnd` | (all) | remove |

Sessions killed without `SessionEnd` (terminal closed) must not linger: the hook records the Claude Code process id (walk up from the hook process's parent to the first `claude`/`node` process) and the app drops a session whose process is gone, or after 12 h with no event.

### The bridge: `ClaudeHook.exe`
- A tiny separate console exe (built by `build.ps1` with `csc.exe` like the installer; no packages) with two modes: `ClaudeHook.exe hook` and `ClaudeHook.exe statusline`.
- On **Connect** the app copies it to a stable path, `%LOCALAPPDATA%\WidgetScreen\claude\ClaudeHook.exe`, so release folders can change without breaking Claude Code's settings. Use forward slashes in the command (Git Bash eats backslashes; see the docs' Windows section).
- `hook` mode reads stdin and writes `%LOCALAPPDATA%\WidgetScreen\claude\sessions\<session_id>.json` atomically (state, since, project dir, title, message, pid, last event time). One file per session, so concurrent hooks never fight. Never blocks, always exits 0, swallows every error (a broken widget must never break Claude Code).
- `statusline` mode writes `limits.json` (both windows + `updatedAt`) and merges `session_name`, model and context % into the session file, then **prints a status line**. If Ali already had a `statusLine`, Connect saves the old command and `statusline` mode runs it with the same stdin and prints its output unchanged. With none, print a short line: `5h 23% · week 41% · ctx 8%` (omit what's missing). Keep it under ~100 ms; .NET Framework console startup is fine.
- The app never needs to be running for the bridge to work; it reads the folder when it starts.

### Usage per project and per session: the session logs
`%USERPROFILE%\.claude\projects\<encoded-cwd>\<session-id>.jsonl` (honour `CLAUDE_CONFIG_DIR`). Assistant lines carry `message.usage` (`input_tokens`, `output_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens`), `message.model`, `message.id`, `requestId`, `timestamp`, `cwd`, `sessionId`. **Dedupe by `message.id` + `requestId`** (streamed messages repeat their usage on several lines). Project name = last folder of `cwd` (not the encoded folder name). Tokens shown = all four counts added (the usual way usage tools count). Read incrementally: remember each file's length and only read appended bytes; skip files whose mtime is older than the longest period shown. Never load whole multi-MB files every tick. Periods: **5 h** and **Week** use the plan windows (`resets_at` − 5 h / − 7 d) when known, otherwise rolling; **Today** is local midnight.

### 2. The concept: critters

Each running session is a **critter**: a small drawn blob with two eyes and a mouth, drawn as paths (`CardGraphics` on Windows, a View in `views/` on Android). Faces read from across the desk faster than words, and they shrink to one critter on a small card.

- **Body**: a rounded blob in one of three shapes (bean, tall pill, squat round) and one of three accents (Lime, Glacier, Lavender), both picked by a stable hash of the project folder, so each project always looks like the same critter. **Amber is reserved for Needs you.** Eyes white (Text) with Page-coloured pupils; mouth a Page-coloured stroke. A 1-unit ControlBorder outline keeps it crisp on Slate/Warm.
- **Expressions** (the whole joke lives here):
  - **Working**: pupils look down and to the side, a three-dot "thinking" trail beside the head. Pupils hop to the other side on alternate 1 s heartbeat ticks (no new timer). Quiet mode: still.
  - **Needs you**: body turns **Amber**, eyes wide (bigger whites, small pupils), two little arms raised (short rounded strokes), an "!" drawn above. It hops 4 units up/down on alternate heartbeat ticks. Quiet mode: arms up, no hop.
  - **Done**: happy closed eyes (two upward arcs), small smile. Body in its accent.
  - **Ready** (started, nothing asked yet): half-closed eyes (flat lines), body in Raised with accent outline, a small drawn "z".
- Session order: Needs you first (longest waiting first), then Working, Done, Ready.

### "Lights up"
- When any session is **Needs you**, the card's border becomes Amber 2 px and the heading reads e.g. "api-server needs you". This is the one state allowed to shout.
- If the Claude card is hidden on another page, a small Amber pill appears in the header beside the page switcher: a mini critter + "Claude needs you". Tapping it switches to that page. Nothing appears when the card is not shown on any page.
- Optional chime when a session starts needing you (Settings → Claude, off by default; silent in Quiet mode; reuse the Focus chime mechanism).
- Phone: the same Amber border and header pill on its dashboard; one haptic tick (`Ui.haptic`) when a session newly needs you while the app is open. No Android notifications (the phone only polls while open).

### 3. Three designs (`CardSizing.Claude`, same numbers on the phone)

Thresholds follow the pattern of Stats/Weather; tune by render. Suggested: **Small** under 360 wide or under 240 tall; **Large** from 640 × 420; Medium between. Preferred size ~ 472 × 310; minimum ~ 260 × 180.

- **Small, the loudest critter.** One big critter (the most urgent session) at the left; at the right the project name (Title size, one line, ellipsis) and the state in words: "Needs you · 2 min", "Working", "Done". Under it "+3 more" if there are more sessions. A 6-unit 5-hour meter along the bottom edge (accent; Amber from 80%). No heading.
- **Medium, the lineup.** Heading "Claude Code" with a count at the right ("3 running"). A row of up to five critters (about 64–96 units tall), each with its project name under it (13–15, ellipsis) and its state word; Needs you ones first. Below, a Raised strip with the two plan meters side by side: "5-hour 23%  resets 3:40 pm" and "Week 41%  resets Thu". Meters are 10-segment chunky bars (4-unit gaps, radius 3) in the accent, Amber from 80%, full Amber and "Limit reached · back 3:40 pm" at 100.
- **Large, the burrow.** Left two thirds: one row per session (64-unit rows): mini critter, title (`session_name`, else project), project and model in Muted, state word with how long ("Needs you · 2 min"), and that session's tokens for the chosen period at the right in Light numerals. If a session needs you, its row shows the hook's message in a second line ("Wants to run Bash"). Right third, a Raised panel: the two plan meters stacked (bigger), then a **period switch** (5 h / Today / Week, three 48-unit segments, same look as the Focus presets' selected state) and **usage by project**: up to six rows of project name + tokens + a thin share bar (accent), the rest as "Other". Tokens formatted 12.4k / 1.2M / 3.4B.

Every design: text never leaves the card at 140% text and with long project names (same bounds check as the other cards).

### Taps
- Tapping a session (critter or row) brings its **terminal or editor window** to the front on the PC, when one could be found (the hook records the first ancestor process with a main window, e.g. WindowsTerminal.exe, Code.exe, powershell's console). Best effort; if none is known, the tap does nothing and the row shows no affordance. This is the one place the dashboard is allowed to activate another window, because the user asked for it. On the phone, tapping does the same thing on the PC (through the link).
- The period switch and nothing else are the other controls. No buttons to stop or answer Claude from the dashboard (out of scope).

### 4. Honest states (copy)

| State | Card shows |
| --- | --- |
| Not connected (bridge not set up) | A Ready critter and "Connect Claude Code in Settings → Claude." |
| Connected, no sessions | A dozing critter and "No Claude Code sessions running." |
| Plan meters unknown | Meters area: "Plan limits show after Claude Code's next reply." (API-key users and logged-out users never get them; say "Plan limits need a Claude Pro or Max login." once a session has run with no `rate_limits`.) |
| Meters stale (> 15 min old) | Keep the numbers, add "as of 10:42" in Muted. |
| Logs folder missing / unreadable | Usage area: "Can't read Claude Code's logs." Sessions still work. |
| Fixture (renders/tests) | Labelled "Sample" in the heading, like other fixtures. |

### 5. Settings → Claude (new tab, after Discord)

- Status line: "Connected" / "Not connected", and which Claude folder (default `%USERPROFILE%\.claude`, or `CLAUDE_CONFIG_DIR`).
- **Connect Claude Code** (accent button). Confirm with `ConfirmSheet`: "This adds Widget Screen's hooks and status line to your Claude Code settings (settings.json). A backup is saved next to it. Your current status line keeps showing." Then: back up `settings.json` to `settings.json.widgetscreen-backup`, add the hooks (append to existing arrays, never replace another hook), set `statusLine` (saving the old one in `%LOCALAPPDATA%\WidgetScreen\claude\previous-statusline.json`), write atomically, keep unknown keys and formatting as far as practical. Mark our entries so they can be found again (the command path contains `WidgetScreen/claude/ClaudeHook.exe`).
- **Disconnect**: removes exactly our entries and restores the previous status line. Also ConfirmSheet.
- Checkbox: "Chime when a session needs me" (off by default).
- If settings.json is invalid JSON, refuse to touch it and say so.
- Unit-test the merge/unmerge on sample settings files (existing hooks, existing statusLine, none, invalid) in an isolated temp folder. **Never** modify Ali's real `~/.claude/settings.json` from automated checks or during development; only the app's Connect button does that, when he presses it.

### 6. Phone link

- `/state` gains a `claude` section only when the card is visible on the phone: `connected`, `sessions` [{id, project, title, state, since (epoch s), message, model, tokens (for each period), canFocus}], `limits` {fiveHour {pct, resetsAt}, week {pct, resetsAt}, updatedAt}, `usage` {fiveHour/today/week: [{project, tokens}]}.
- New action to focus a session's window (`claude/focus` with the session id), following the existing action pattern and pairing checks. Nothing else; don't widen the link.
- Phone card: the same three designs and critters drawn in `views/` (shared shape/expression logic in `core/`, no `android.*` imports, with JVM checks), the period switch is local to the phone.

### 7. Resource budget

- Session files: check the folder's newest mtime on the 1 s heartbeat (cheap); re-read only changed files.
- Logs: incremental read every 10 s while the card is visible (30 s in Quiet mode); nothing while hidden.
- Critter motion uses the existing heartbeat; Quiet mode stops it.

### 8. Anti-goals
- No requests that use credits (no Messages API calls). The only internet request is the read-only usage check above.
- No ChatGPT/other assistants.
- No Anthropic logo; the critters are our own.
- No answering or stopping Claude from the dashboard.
- Don't change how other cards look.
