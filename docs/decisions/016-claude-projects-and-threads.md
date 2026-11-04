# 016 — Claude projects with their threads, and refresh intervals the user picks

> **Superseded by [027](027-simple-claude-card.md) (4 Nov 2026).** The Claude Code card is now four simple tiles, and what this record describes was removed. It stays as history.

Ali reported (1 Nov 2026), on v0.29.0: "it's not showing the projects and threads under the projects. also add a way to make the update time 2 seconds or any value I want." He then asked for the projects to be big critters with their threads as baby critters that a tap on the project eats (folds in) and spits back out.

## What was wrong

Checked read-only on Ali's PC (no tokens or log contents printed):

- The running app was the installed v0.29.0, signed in to Claude, with the telemetry receiver listening. Its Claude Code connection dates from an earlier Widget Screen, so Settings → Claude offers **Update connection** (which adds the permission hook and telemetry).
- The data was there. The app's own scan of `~\.claude` found 7 projects and 28 session logs from the last 8 days (1,634 replies counted), and Claude Code's session list held 2 running Code-tab sessions.
- **The card never showed threads.** v0.29.0 lists only the sessions running now (two critters, both "Widget screen", untitled) and usage by project as token totals with nothing under them. On Medium those rows also drop out when space is short.
- **Thread titles live in the Claude app, not in Claude Code.** Claude Code's session list only carries a name the user typed. The titles the app's sidebar shows are in the app's own per-thread records: `claude-code-sessions\<org>\<account>\local_*.json` under its data folder (`%LOCALAPPDATA%\Claude-Data\<profile>` for this install), with `cliSessionId`, `originCwd`, `title`, `isArchived`, `createdAt` and `lastActivityAt`. There were 44 records, all auto-titled.
- The card itself was hidden on every page in Ali's layouts, and two projects were unticked in Settings → Claude → Projects. Both are his own choices, left as they are.

## Decision

- **Threads come from three sources, merged by Claude Code session id.** Running sessions are read as before. Ended threads of the last week come from the Claude app's records (read-only, only the fields above; archived ones stay hidden, as in its sidebar) and from Claude Code's logs (`projects\…\<session>.jsonl`; helper logs under `subagents\` count toward their session but aren't threads).
- **A thread's title** is the one the user typed (Claude Code's list, or a `custom-title` line in its log), else the Claude app's title, else "Untitled · 3:40 pm". Prompts are never shown.
- **A thread's project** is the folder it started in: the app's `originCwd`, else the log's first `cwd`. A worktree (`…\.claude\worktrees\x`, `…\.worktrees\x`) counts under the folder it was made from.
- **The period** (5 h, Today, Week) filters threads by last activity, and running threads always show. It's kept in settings.xml (`ClaudePeriod`, default Today); the phone starts from it and keeps its own switch.
- **Folding** is kept per project folder in settings.xml (`ClaudeCollapsed`) and shared with the phone through `claude.collapse`. A folded project still turns Amber when a thread inside it needs you.
- **The eat and spit motion** (350 ms) is user-requested, so it's allowed under the no-decorative-animation rule. Quiet mode makes it instant. Windows uses `DoubleAnimation` on a scale and translate per baby plus a keyframed `Mouth` on the critter; Android uses one `ValueAnimator`.
- **Two intervals in Settings → Claude → Refresh**, whole seconds with −/+ buttons:
  - **Card** (`ClaudeCardSeconds`): default 2, minimum 1. Sessions, thread records, telemetry totals and logs are read again this often while the card is on a page. It runs on the existing heartbeat with a timestamp; there's no new timer.
  - **Plan check** (`ClaudePlanSeconds`): default 300, minimum 10. Below 60, Settings says "Anthropic may slow down very frequent checks".
  - When the plan check gets a 429 or a 5xx, the wait doubles each time, up to 15 minutes (or the chosen interval if that's longer). The card says "Plan limits as of hh:mm" until the next success, which goes back to the chosen interval. Other failures (no internet) retry at the chosen interval.
  - Quiet mode no longer stretches either interval: the user's numbers win.
- **The phone** asks for the `claude` section on the card's interval, but never more often than every 2 s. Readings without it keep the last one. The section adds `every`, `period`, `threads` (each once, with its tokens and cost per period) and `groups` per period (folder key, name, folded, usage and thread ids).

## Tradeoffs

- The Claude app's records aren't a documented interface. They're read defensively and only for the fields above. If the format changes, threads fall back to the logs and lose their app titles; nothing breaks.
- Reading every 2 s costs a few directory listings. Files are parsed only when their write time or length changes, and the logs are read only from where the last read stopped. A 1 s interval doubles that work, which is still small.
- More projects than fit get a "+N more projects" column rather than horizontal scrolling. A sideways swipe already changes the page. Many threads scroll inside their project's column.
