# 026: The phone shows packages 2 and 3 of the Claude watcher

On 2 Nov 2026 Ali asked for the Watcher action plan's changes so far to reach the Android app: "implement the changes we made so far to reflect in android release. package 1 to 3". Package 1 (decision 023, v0.39.0) already changed both apps. Packages 2 (decision 024, v0.40.0) and 3 (decision 025, v0.41.0) changed only the Windows app, and the PC side of the phone link already sent their fields. Package 4 was cancelled, so this is the Android pass decision 024 promised. It changes `android/` only, plus a few more fields in each period's facts on the PC side (below).

## Decisions

1. **The phone reads everything the PC already sends.**
   - Package 2: `factsBy`, `factKinds`, `costSource`, `unpriced`, `limitEta`, `claudeCodeCost`, `estimateCost` and `limits.spend`; per thread `helperTokens`, `costUsd`, `mode`, `revving`, `errorStatus` and `errorAttempt`.
   - Package 3: `claudeAlerts`, `claudeAlertSeq` and `claudeQuiet` in every answer; per thread `tasksDone`, `tasksTotal` and `endReason`.
   - The words come from `core/ClaudeText.java`, a copy of the PC's `ClaudeText` (`fact`, `paceLine`, `planChips`, `unpriced`, `retrying`, `crew`, `endedBy`, `details`), so both apps say the same thing.
2. **Each period's facts carry a little more (PC side).** The phone's side panel follows the card's period, like the PC's. The PC's facts per period lacked what the Tokens, Tools and Lines facts need, so `factsBy` now also has `prompt`, `cacheRead`, `replies`, `asked`, `denied`, `linesThreads`, `telemetry` and `askedPartial`. A PC on v0.40.0 or v0.41.0 leaves them out, and the phone uses today's facts for them.
3. **The card, as on the PC.**
   - Large's side panel shows the four facts picked in Settings → Claude on the PC, for the period. API errors and a fifth or more of tool runs failing turn Amber. The tokens fact puts "~" before an API value that leaves out a model with no known price.
   - The 5-hour tank says "full in 1 h 40 at this pace" when it would fill before it resets, in Amber.
   - The plan chips (narrower weeks past the two the week tile names, extra usage, a gateway's spend limit) sit under the plan row, and step aside while a session needs you.
   - Cars carry their pit crew and a "3/5" task sticker (`PitSprite.crew`), drawn in code like the context ring. Medium's small inline cars show the crew only.
4. **No tooltips on a phone, so:**
   - A ledger row whose car is spinning after an API error says "Retrying after an API error · HTTP 529 · attempt 3" where it would say where it runs. A thread Claude Code closed says "Closed in Claude Code · 1 h ago".
   - Holding a thread (a ledger row, Medium's inline car or Small's car) opens the themed sheet with what the PC's tooltip says: state, retry, context, crew, how it ended, own and helpers' tokens, mode, Claude Code's own cost, tokens and model. Go to it shows when its window can be brought forward. The sheet is `Dashboard.info`, the confirm sheet with Close.
   - A fact's meaning is read aloud with it (content description). A tap target for it would need 48 units of height the side panel doesn't have.
5. **Phone notifications.**
   - The phone raises a notification for each alert number it hasn't seen (`ClaudeText.freshAlerts`, `ClaudeNotifier`). The first answer after the app starts only sets the mark, so nothing from before pops up. A PC that started again counts from 1, and the phone follows.
   - One notification per session: a newer one replaces it. A tap opens the app on the Claude card's page.
   - The words match the PC's notification: "Claude needs you · api-server", "Fix the login: Wants to use Bash". Placeholder titles become "Thread 1b2187".
   - Settings → Widgets → Notify me when a Claude Code session needs me (`AppSettings.claudeNotify`, on by default). The PC's Quiet mode keeps it silent. Android 13 and later asks for the notification permission once, when the Claude card is on a page on the PC.
   - The icon is the waiting car's silhouette drawn by `PitArt`, so no image file is added.
6. **Only while the app is open.** The phone asks the PC for `/state` only while the app is in front. Notifications therefore arrive while the app is open (another page, the list layout, a split screen). Watching from the background would need a foreground service polling the PC with a lasting notification, which costs battery and is a new kind of background work. Ali chose "Only while the app is open" (2 Nov 2026), so there is no background watcher.

## What can't be on the phone

- The Windows-only parts of packages 2 and 3 have nothing to do on the phone: the folder watchers, Connect's status line refresh, managed settings, telemetry and the chunked log scan all happen on the PC.
- Settings → Claude → Side panel facts stays on the PC; the phone shows the PC's choice.

## Checks

`android/tests/.../AndroidTests.java`:

- Package 2: each fact with its words and warnings, cleaned fact choices, an older PC's facts, the pace line, plan chips, thread numbers, retry, helpers and setup words, and a thread's details.
- Package 3: alerts once each, the first answer's mark, a PC that restarted, notification words, crew, tasks and end reasons, and the notify setting.
- The real answer from the Windows checks (`remote-state-sample.json`): each period's facts with replies and asked, the chosen four, and sample-2's crew.

`tests/Remote/RemoteTests.cs` checks that each period's facts carry the new fields.

Views, notifications and the permission prompt need a device or emulator; CI has neither.
