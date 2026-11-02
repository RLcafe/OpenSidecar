# 023 — A session is alive until its own SessionEnd or its pid is gone

On 2 Nov 2026 Ali asked for the Watcher action plan's first package ("Trust the sources", v0.39.0), from the review of the Claude watcher against Claude Code's documented hooks, status line, telemetry and usage answer. Windows and the phone.

## Decisions

1. **Liveness.** A hook file is removed only by its own `SessionEnd` or when its process has exited.
   - `ClaudeProcesses.Alive(pid, seenBy)` is true for any running process. The old check also wanted the process to be named `claude`, `node` or similar, so a session whose Claude Code ran under another name (a renamed or bundled exe) vanished on the next tick. The name only decides whether the window can be brought forward.
   - **Pid reuse:** a process that started more than a minute after the session was first seen isn't that session (`Since` is never earlier than the real start). A process whose start time can't be read counts as alive.
   - `SessionEnd` with reason `clear` keeps the file (the conversation continues in the same terminal) and marks it ended; any other reason deletes it. A session without a pid, or ended, is forgotten after 12 quiet hours.
   - Claude Code's own session list (`sessions\<pid>.json`) uses the same check with its `startedAt`.
2. **Sessions only the logs know.** A thread with a reply in its log in the last two minutes, and no hook file, shows as running (one started before Connect). The reply's own time decides, not the log file's (Claude Code touches a log about an hour after a turn). A session whose hook file went this run stays ended. Connect reads every source at once, so sessions already open show straight away.
3. **Stuck.** A new state for a turn that ended on an API error (`StopFailure`): the car's wheels spin, the chip says "Stuck", and the reason reads in plain words ("Rate limited, resets 14:05", "Overloaded, try again", "Sign in again", "Reply too long", …). It stays until the next prompt or turn, not just a minute. It ranks after Needs you and before Working. The error's details are kept to 200 characters in the session file and never go to the phone; the phone gets the type and the words.
4. **More hooks.** Connect adds `StopFailure`, `PostToolUseFailure` (failed tools counted, the last one's first line kept, interrupts skipped), `PostCompact` (compaction ends), `PermissionDenied` (counted), `SubagentStart`/`SubagentStop` (open helpers), `TaskCreated`/`TaskCompleted` (open tasks), `PostModelSwitch` (the model) and `CwdChanged`. `NeedsUpdate` sees an older connection, and Settings → Claude offers Update connection once.
   - `PreModelSwitch` is not registered: `PostModelSwitch` carries the same model, and a hook before the switch can only slow it.
5. **Notifications.** `permission_prompt`, `elicitation_dialog`, `elicitation_url_dialog` and `agent_needs_input` are Needs you; `agent_completed` is Done. The three usage-limit waits follow Claude Code's documentation rather than the plan's first reading:
   - `quota_auto_resume_fired`: Claude Code carries on by itself, so the car is Working.
   - `quota_auto_resume_stale`: it waits for Enter, so the car Needs you ("Your usage limit reset. Press Enter in Claude Code to carry on.").
   - `quota_auto_resume_disabled`: it stopped waiting without carrying on, so the car is Stuck ("Stopped waiting for the usage limit").
6. **The plan check.**
   - A 403 from the usage answer means the plan has no usage meters: the sign-in is kept, the plan row says "Your plan has no usage meters." and Settings → Claude says so.
   - A 401 ends the sign-in only when a forced refresh fails. A 401 after a refresh that worked is a failed check, retried as before.
   - The usage answer's `seven_day_cowork` ("Cowork week"), `seven_day_oauth_apps` ("Apps week") and `extra_usage` (on or off, monthly limit, used, percent) are read. The phone gets `extraUsage`; the tile comes in package 2.
   - The check stays the only outbound call, every 300 seconds while signed in. Nothing that uses credits is called, and Claude Code's own token is never read or refreshed.
7. **The telemetry port.** Connect binds the receiver first, trying 43832 and the next seven ports (43832–43839), and writes the port it got into Claude Code's settings. On every start the receiver binds the port written there. If that port is taken by something else, the card's side panel and Settings → Claude say "Telemetry off: port N in use", and Update connection moves it to a free one. The receiver stays on 127.0.0.1 only.
8. **Context on the car.** A small ring on the car's top-right corner shows how much of the context window is used, from 60% (muted), 80% (warning) and 95% (warning, filled), with "Context 82% used, compaction soon" in the tip. Medium shows only the fullest car's ring in each garage. The phone gets `context` per thread.
9. **Where it runs.** `ClaudeRegistry.Where` names Cursor, JetBrains, Zed and both Agent SDKs, and shows any other entrypoint as it is. Connect adds `OTEL_METRICS_INCLUDE_ENTRYPOINT=true`, so telemetry says where too.
10. **Phone link.** Each thread adds `reason`, `reasonText`, `context`, `workers`, `tasks` and `failed`; the section adds `warning`, and limits add `extraUsage`. `LastError` stays on the PC.

## Tradeoffs

- **One release, one pull request.** The plan splits a package past ten files into two pull requests. Package 1 touches more than ten, but its parts share the session file and the state, so splitting it would mean a release that writes fields nobody reads. It is one pull request, and can still be split on request.
- **Pid reuse uses `Since`,** the time the bridge first saw the session, instead of a new field. It's never earlier than the real start, so a reused pid is caught a minute late at worst.
- **Stuck keeps no retry count.** A `StopFailure` arrives after Claude Code stops retrying; counting attempts comes with the telemetry `api_error` in package 2.
- **Ports.** Eight ports cover a second Widget Screen or another tool on 43832 without scanning wider. When all eight are taken, Settings says so and telemetry stays off; the hooks, logs and session list still work.

## Checks

`tests/Claude/ClaudeTrustTests.cs` (Windows) and "Claude trusts the PC's sources…" in `android/tests` (phone): liveness with a dead, a reused and a renamed process; one fixture per new hook and notification; Stuck past a minute and cleared by the next prompt; sessions from the list or the logs only; 403, 401 and the newer usage fields; the busy-port warning and Connect on a free port; context ring thresholds and tips. All in throwaway folders: `CLAUDE_CONFIG_DIR` and `WIDGETSCREEN_CLAUDE_APP_DATA` point at temporary folders, and no real Claude Code file is touched.
