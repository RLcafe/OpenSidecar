# 025: The card reacts at once and tells you wherever you are (package 3)

On 2 Nov 2026, Ali asked for the third package of the Watcher action plan, "Instant and everywhere" (v0.41.0), to be started once v0.40.0 had been released. Like packages 2 and 4, this package is for Windows first (decision 024). The PC side of the phone link sends the new fields. The phone will read and draw them in the Android pass after package 4.

## Decisions

1. **The card reacts to files changing.** It no longer re-reads them every 2 seconds.
   - While the Claude card is on any page, `ClaudeWatch` watches these folders with Windows' own folder watcher (`FileSystemWatcher`):
     - the bridge folder (`sessions\` and `limits.json`)
     - Claude Code's `sessions\` (its session list)
     - Claude Code's `projects\` (its logs)
     - the Claude app's `claude-code-sessions` folders
   - The first change after the dashboard last looked asks for one look on the dashboard thread. Later changes only add to what is pending, so a burst of writes means one look.
   - Telemetry arriving pokes the watcher the same way.
   - Only the source that changed is read: session files and limits, or logs.
   - Logs are scanned at most once per Settings → Claude interval (2 s by default), only while the card is on the page shown, as before.
   - The old read of every source becomes a fallback every 30 s.
   - If a watcher fails (its buffer overflows or a folder disappears), the chosen interval comes back until the watchers are set up again. They are set up again every 10 seconds, and that is also how folders created later are picked up.
   - When the card is on no page, nothing is watched.
   - A shown card with a running session is still worked out again at the chosen interval, with no file read. Your turn, revving, Idle after 30 minutes and Resolved after 30 idle minutes all depend on the clock, not on a file.
   - The plan said "on the next frame". The dashboard has no per-frame loop outside the cards' frame clocks, so the look is posted to the dashboard thread straight away. That is the same thing without a new timer.
2. **The project groups are worked out once per view.**
   - `ClaudeView.Groups` keeps its result for each period and filter (Hide resolved and archived, Hide idle) until the view's threads change.
   - Folds are applied on every call, because a tap changes them without making a new view.
   - Thread tooltips are built when they open, from the view on screen at that moment, not from the view the row was built from.
3. **Windows notifications.**
   - A notification shows when a session newly needs you, gets stuck, or finishes a turn of 10 minutes or more. Clicking it brings the session's window forward.
   - It is shown through a notification-area icon's balloon tip, which Windows 10 and 11 show as a notification. A WinRT toast from an unpackaged WPF app on .NET Framework would need the Windows SDK's metadata and an app id on a Start menu shortcut, which neither the portable copy nor the dev build has.
   - The icon stays while a session needs you or is stuck, and goes when none does.
   - Settings → Claude → "Notify me in Windows when a session needs me" is on by default. Quiet mode keeps it silent. The chime is unchanged and stays its own choice.
   - When several alerts arrive at once, the most urgent one shows and says how many more there are. Each notification replaces the last.
4. **Phone alerts, PC side.**
   - The phone link is still the phone asking every 2 s. There is no connection the PC can push on, and opening one would widen the link, which needs Ali's approval. Instead, every `/state` answer carries:
     - `claudeAlerts`: the last ten minutes of alerts, each with `seq`, `kind` (`need`, `stuck`, `done`), `session`, `project`, `title`, `text` and `at`
     - `claudeAlertSeq`, the newest number
     - `claudeQuiet`, the PC's Quiet mode
   - The phone raises a notification for each number it hasn't seen. That gives a notification on the phone within one poll, which meets the plan's "within 5 seconds" check, with no new port.
   - The text is the waiting message the card already shows. Prompts are never sent.
5. **Helpers and tasks on the car.**
   - `PitCrew` draws a small helper figure on the car for each subagent running now (up to three, then "+N"). On cars at least 40 units wide, it also draws a "3/5" sticker for tasks done.
   - A lead whose turn has ended (Your turn, or Ready) while its helpers are still running counts as Work. A Stop that lists a subagent still in the background keeps that worker. SubagentStop takes it away.
   - The tooltip says "2 helpers working · tasks 3 / 5".
   - Each thread sent to the phone gains `tasksDone`, `tasksTotal` and `endReason`.
6. **Resolved and named without the Claude app.**
   - A SessionEnd with reason `prompt_input_exit` (you closed Claude Code at its prompt), `logout` or `clear` keeps the session's file, marked ended with its reason. Any other reason still removes the file.
   - Without a Claude app record, such a thread shows as Resolved, parked under its cover. A reply logged more than two minutes after the end, or a new SessionStart for the same session, undoes that.
   - Where the Claude app has a record for the thread, the record decides, as before.
   - Ended files are kept for the week whatever their process does. The check for a running process skips them.
   - SessionStart's `session_title` names the car. The order of names from decision 018 is unchanged: a rename, the logs' custom title, the status line or hook's name, the app's title, then the first request.
7. **Profiles, managed settings and worktrees.**
   - `CLAUDE_CONFIG_DIR` was already honoured. It is now also read from the user's own environment, for a Widget Screen started before the variable was set.
   - Every Claude app profile under `Claude-Data` was already read (since decision 016).
   - Managed settings:
     - An organisation's `managed-settings.json` (under `ProgramData` or `Program Files`, `ClaudeCode`) outranks every other Claude Code settings file. Widget Screen reads it, never writes it, and names what it overrides:
       - hooks: `disableAllHooks` or `allowManagedHooksOnly`
       - the status line: `disableAllHooks` or a managed `statusLine`
       - telemetry: a managed `env` key Widget Screen sets, with a different value
     - These show in Settings → Claude and in the Connect sheet.
     - The plan suggested offering `settings.local.json` instead. It isn't offered, because managed settings outrank that file too, so writing there would only look like it worked.
   - A `--worktree` session's garage is the status line's `worktree.original_cwd`, before any guessing from the path.
   - The plan also named the WorktreeCreate hook. It isn't added: a WorktreeCreate hook replaces Claude Code's own worktree creation, so registering one would break `--worktree`.

## Tradeoffs

- **A killed terminal.** A session whose terminal was killed, with no SessionEnd, can take up to 30 seconds to leave the card, where it used to take 5. A normal exit sends SessionEnd, so most exits still show at once.
- **Notifications use the notification area's icon.** That means an extra icon while a session needs you. It also means there are no buttons on the notification. Allow and Deny from the notification belong to package 4.
- **A burst of alerts shows one notification.** On the PC, only the most urgent one is shown and it counts the rest. The phone gets every one.
- **`/clear` counts as resolved.** The plan said "parks". A cleared thread's work is over, so it goes under the cover with the threads the app marked completed.

## Checks

`tests/Claude/ClaudeInstantTests.cs` runs in throwaway folders. The checks point `WIDGETSCREEN_CLAUDE_MANAGED` at a missing file, so the PC's real managed settings are never read, and they turn notifications off. The checks:

- A burst of written files asks for one look. A log line is marked as a log change, telemetry pokes the watcher, and a missing folder isn't watched.
- The groups are worked out once per period and filter.
- Each alert fires once: Needs you, Stuck, and only a long turn's end. The phone's list keeps ten minutes.
- A lead with a helper out is Work. Its "3 / 5" chip and the tooltip words are checked.
- Exit, logout and `/clear` resolve the thread; the app's record, a later reply and a resume undo that; the title comes from SessionStart.
- A worktree's garage is its original folder.
- Each managed-settings case is named correctly.

ClaudeTrustTests' SessionEnd check now expects the new reasons to be kept. RemoteTests checks that the alerts and the crew fields are in the phone's answer.
