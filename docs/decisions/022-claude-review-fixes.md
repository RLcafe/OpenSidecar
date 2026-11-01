# 022 — Claude watcher review fixes

On 1 Nov 2026 the Claude watcher was reviewed against its spec (`docs/design/claude-watcher-v2.html`), the later decisions (017–020) and the code, on both apps. Ali asked for all eleven findings to be fixed, on Windows and Android, in this order.

## Decisions

1. **"Your turn" stays visible.** v0.34.1 made a running thread Resolved as soon as the Claude app marked its latest reply "completed". About two turns in three end that way, so threads vanished the moment they finished.
   - A running thread is Resolved only after it has sat idle for 30 minutes (`ClaudePace.TurnIdle`). Ended threads still turn Resolved at once.
   - Working, needs you and compacting always win (`ClaudePace.State`).

2. **Tool calls and lines changed come from the logs.** Telemetry only reached the widget from some sessions. On Ali's PC the card showed 8 tools and no lines, while the logs held 1,878 tool calls and 426 edits.
   - `ClaudeLogActivity` counts each `tool_use` block by its id, and each edit or write result's `structuredPatch` "+" and "−" lines (a new file's lines when it has no patch). Only counts are kept; no text.
   - Telemetry still says which calls asked you and which were denied. When it saw fewer calls than the logs, the fact reads "TOOLS · N ASKED · PARTIAL" and the tooltip explains why.
   - The phone gets `askedPartial`.

3. **Medium fits at its default size (472 × 310).**
   - The heading keeps the two most urgent parts of the summary.
   - The project line leaves out where it runs, so "+N hidden" fits.
   - The replies column shows the number alone, with the word in the tooltip.

4. **Titles stay readable at 140%.** A thread title keeps room for about 14 characters (`CardSizing.ClaudeTitleMin`). First the "Desktop · 2 min" text steps aside (it stays in the tooltip), then the Cache reads column (`ClaudeWhere`, `ClaudeCache`).

5. **The frame clock stops when the window is minimised.** The heartbeat skips the card while minimised, so it was never told to stop. Now the window's StateChanged event tells it (`UpdateClaudeLive`).

6. **Reading never waits on a scan.**
   - `ClaudeUsage` publishes a snapshot after each scan and each telemetry reply, and the getters read that snapshot without the lock.
   - Telemetry usage is added outside the activity lock.
   - The first scan of a week of logs used to freeze the dashboard for about a second.

7. **The ledger shows whole rows** (`CardSizing.ClaudeWholeRows`). The list's height is the longest run of whole rows from the top; the rest scroll.

8. **Rolling keeps covers and passes under the chips.**
   - The roll overlay sits under the card's content.
   - Ended, archived and resolved cars roll under their cover at 60% opacity, with their lights off.

9. **The label strip.** The Claude card wears the v0.35.0 label strip in Coral (`Inks.Default("Claude")`). Its ink still can't be changed in Edit layout: the rest of its design is its own.

10. **The freshness stamp.** Large says "LIVE · AS OF 2:40 PM" when the plan numbers are current, and "AS OF …" alone when they're old, as before.

11. **Hardening.**
    - The telemetry receiver accepts only `application/json`, so a web page can't post to it without a Content-Type.
    - The window found for a process is forgotten when the process leaves Claude Code's list, and looked up again after 10 minutes.
    - Connect and Disconnect keep dated copies of `settings.json` (`settings.json.widgetscreen-backup-yyyyMMdd-HHmmss`, the newest five) beside the latest backup.
    - Checks only ever use temporary folders.

## Tradeoffs

- Counting lines from the logs reads each edit result's patch. Only the number of "+" and "−" lines is kept, never the text.
- A thread that is really finished but still open now shows as "Your turn", then Idle, for half an hour before it can be hidden as Resolved. That is the price of never hiding a live turn.
- The Claude strip's ink is fixed rather than pickable, to keep the pit wall's palette.

## Verification

- GitHub CI runs `build.ps1 -Test`, `package.ps1 -Test` and `build-android.ps1 -Test`.
- New checks are in `tests/Claude/ClaudeReviewTests.cs`, `ClaudeSourcesTests.cs` and `ClaudeTreeTests.cs`, plus the phone's `AndroidTests`.
- The minimise and roll check opens a real window off screen.
- The real-window checks for 1, 3, 4 and 5 ran on the PC with the labelled sample.
