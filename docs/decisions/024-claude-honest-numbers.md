# 024 — Claude Code's own numbers first, and Windows first for packages 2–4

> **Superseded by [027](027-simple-claude-card.md) (4 Nov 2026).** The Claude Code card is now four simple tiles, and what this record describes was removed. It stays as history.

On 2 Nov 2026 Ali asked for the Watcher action plan's second package ("Honest numbers", v0.40.0). The same day he said: "skip for android for the remaining packages. do for windows only. after that we will implement for android."

## Decisions

1. **Windows first (packages 2, 3 and 4).** These packages change only the Windows app (`src/`) and its checks (`tests/`). The Android app (`android/`) is not changed, and its checks still run in CI to show nothing broke. This is an exception to CLAUDE.md's "Every change goes into both apps" rule, for these three packages only. The phone link still gets the new fields on the PC side, so the later Android pass only has to read and draw them. Once package 4 is merged, the Android work comes as its own package and release.
2. **Where a cost comes from.**
   1. Claude Code's own cost from telemetry: `api_request.cost_usd_micros`, else `cost_usd`.
   2. The price table, for a model it knows.
   3. A reply from a model the table doesn't know gets a share of what's left of its session's total from the status line (`cost.total_cost_usd`, minus the replies already priced), shared by reply size (`ClaudeService.Fill`).
   4. With none of these, the reply is unpriced (`ClaudeUsageEntry.Priced` false). Its cost counts as 0, but the card puts "~" before the API value and the tip names the model ("Price unknown for claude-x"). It never shows a made-up $0 as exact.
   - Each period records where its costs came from (`claude`, `statusline`, `prices` or `mixed`), and the API value's tip says so. The tip also compares Claude Code's total for the running sessions with the widget's week for them.
3. **The status line.**
   - Connect sets `refreshInterval: 30` on the status line it writes, so cost and time stay current while a session waits on helpers. A number the user already set (1 or more) is kept.
   - A connection without it offers Update connection once.
   - The bridge keeps Claude Code's cost, session and API time, lines added and removed, context size, `exceeds_200k_tokens`, output style, agent name and worktree name and branch in the session file. It also keeps a gateway's `rate_limits.spend_limit` in `limits.json`.
   - It rewrites the session file only for a cent of cost, a line, or a setting. The clocks alone count only once they have moved a minute, so a refresh every 30 s doesn't churn the disk.
4. **Telemetry read in full.**
   - Events: `tool_result` (tool, `tool_use_id`, success, duration, `error_type`), `api_request` (duration, speed, effort, `query_source`, agent, skill, MCP server), `api_error` (status, attempt), `subagent_completed` (type, tool uses, duration) and `permission_mode_changed` (the new mode).
   - Counters: `token.usage`, `cost.usage`, `active_time.total`, `session.count`, `commit.count`, `pull_request.count` and `code_edit_tool.decision`. A cumulative counter is turned into what changed since the last export, as lines already were.
   - Only names, categories and numbers are kept, never prompts, replies, commands or error messages.
5. **Revving.**
   - An allowed tool call stays open, by its `tool_use_id`, until its `tool_result`, or until a later `api_request` from the same session.
   - The car revs when an open call has run past twice that tool's median over its last 20 runs, clamped to 3–60 s. It falls back to 15 s for a tool with no history.
   - Without ids from telemetry, the old rule holds.
6. **Retries.** While a working session's last API error is newer than its last request, the car is Stuck (as before) and its tip says "Retrying after an API error · HTTP 529 · attempt 3".
7. **Side panel facts.**
   - Settings → Claude → Side panel facts picks four (`AppSettings.ClaudeFacts`, cleaned by `ClaudeFactKinds.Clean`; default tokens, cache hits, tool calls, lines). They follow the card's period.
   - Tool failures fall back to the hooks' failure counts over the logs' tool calls when telemetry sends no tool results, and the tip says so.
   - A fact that needs telemetry shows "—" and says so until telemetry arrives.
8. **Time to the 5-hour limit.**
   - The time comes from the share of the window each token has cost so far (percent over the window's tokens) and the last half hour's tokens a minute: `ClaudePace.Eta`.
   - It is shown only when the window would fill before it resets, and counts every project, as the plan meter does.
9. **The first scan in chunks.**
   - A log bigger than 2 MB seen for the first time is read from its head (64 KB, for the folder and first request) and then from its tail backwards, 1 MB at a time. It stops at the first chunk whose first line is older than the week kept.
   - One scan reads at most 64 MB (`ClaudeUsage.ScanBudget`), and the next carries on (`Behind`). Lines added later are read from the end as before.
10. **Helpers apart.** A reply in a subagent's log, or marked `isSidechain` or with an `agentId`, is a helper's (`Sidechain`). A thread's tip splits its own tokens from its helpers'. The helpers fact is their share of the period's tokens.
11. **Phone link (PC side only).**
    - The `claude` section adds `factsBy` (each period's facts), `factKinds`, `costSource`, `unpriced`, `limitEta`, `claudeCodeCost`, `estimateCost` and `limits.spend`.
    - Each thread adds `helperTokens`, `costUsd`, `mode`, `revving`, `errorStatus` and `errorAttempt`.
    - Names follow the section's existing camelCase rather than the plan's draft names (`cost_source`, `tool_slow`, …).

## Tradeoffs

- **The status line's total only fills unknown models.** It is the session's whole cost since it started, so it can't price single replies. Using it only for replies the price table can't price keeps every other number per reply.
- **Extra usage units.** Anthropic doesn't document whether `used_credits` and `monthly_limit` are dollars or credits. The chip shows the percent and the two numbers without "$".
- **ETA uses the whole plan.** The meter is the whole plan's, so the pace is too. Unticked projects still fill it.
- **Behind.** A scan that runs out of budget leaves the rest for the next scan, every 10 s while the card shows. Totals for older days fill in over a minute or so on the first start with very large logs.

## Checks

`tests/Claude/ClaudeNumbersTests.cs` covers these, all in throwaway folders, with no real Claude Code file read or written:

- A full status-line fixture and the spend limit, and that a refresh with only the clocks moved isn't written.
- Connect's refresh: the user's own kept, and Update for an old connection.
- Unknown models, micro-dollars and the status line's share.
- One OTLP fixture per event and counter.
- Revving from open calls, a call cleared by a later request, and the retry tip.
- Each fact from fixtures.
- Helpers' tokens.
- The ETA maths.
- A ~29 MB log scanned in chunks over several scans, stopping at the week's edge.
- Fact choices in settings.
