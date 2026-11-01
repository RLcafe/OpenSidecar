# 015 — The Claude watcher's sources: Claude Code's session list, its own Claude sign-in, and telemetry on loopback

Ali reported (1 Nov 2026) that the Claude Code watcher from v0.23.0 (decision 014) "is not working" and asked for a final solution, not a workaround. He then added: use Claude Code's official OpenTelemetry, make usage per project solid, and let him choose which projects appear.

## What was wrong

Checked read-only on Ali's PC (no tokens or log contents read):

- **Ali runs Claude Code only in the Claude desktop app's Code tab** (`claude.exe` 2.1.309 under `%LOCALAPPDATA%\Claude-Data`, entrypoint `claude-desktop`). There is no `claude` CLI on PATH and `CLAUDE_CONFIG_DIR` isn't set.
- **Connect worked and the hooks fire.** settings.json held all six hooks and the status line, with an absolute, quoted, forward-slash path that Git Bash runs. Every session active after Connect wrote its file.
- **The plan meters could never fill**, which is the main breakage, for two reasons. (1) The Code tab never runs a status line, so `rate_limits` never arrive and `limits.json` was never written. (2) `.credentials.json` held only the plugins' MCP sign-ins, not Claude's: the desktop app hands Claude Code its sign-in directly. So the usage check found no token and went quiet for an hour, every time.
- **Sessions already open before Connect, and sitting idle, never showed**, because no hook had fired for them yet.
- **Session names and models stayed empty**, because they only came from the status line.

## Decision

1. **Claude Code's own session list is a source.** Claude Code writes `sessions\<pid>.json` under its folder for every running session, whether it runs in the CLI, an editor or the desktop app: pid, sessionId, cwd, name and nameSource, status `busy`/`idle`/`waiting` with `waitingFor`, `statusUpdatedAt` and entrypoint. `ClaudeRegistry` reads it read-only and defensively, since it isn't documented. It finds sessions without waiting for a hook, tells Working from idle, and catches a waiting question. A file whose process has gone is ignored, and spare processes are skipped. The card's window lookup walks up from that pid (`ClaudeProcesses.OwnerOf`), which finds the Claude app for the Code tab.
2. **Sources merge by session, and the newest news wins.** The hook file (state and when it changed), Claude Code's list (`statusUpdatedAt`) and telemetry events (a prompt, a tool decision or result, or an API request means Working) are compared by time. Idle in the list means Done after a turn and Ready before one. A **PermissionRequest** hook joins the hooks, because the Code tab sends no permission Notification. Telemetry has no "waiting for permission" event, so Needs you still comes from the hooks and the list.
3. **Plan meters: the widget's own "Sign in with Claude"** (Settings → Claude), so they depend on neither Claude Code's credentials file nor a terminal status line.
   - **The flow** is the browser sign-in Claude Code's `/login` uses: OAuth 2 with PKCE, Claude Code's public client `9d1c250a-e61b-44d9-88ed-5944d1962f5e`, authorize at `https://claude.com/cai/oauth/authorize`, tokens at `https://platform.claude.com/v1/oauth/token`, and the redirect `http://localhost:<free port>/callback`, listened on 127.0.0.1 and ::1 for one sign-in only. These values were read from the installed Claude Code 2.1.309, read-only.
   - **It asks only for `user:profile`**, which is what `GET https://api.anthropic.com/api/oauth/usage` needs (`anthropic-beta: oauth-2025-04-20`).
   - **It is a separate grant** from Claude Code's. Its access and refresh tokens live DPAPI-encrypted in `claude-session.dat` next to `settings.xml` (git-ignored). It renews itself with its own refresh token, so it never rotates Claude Code's sign-in.
   - **It checks every 5 minutes while signed in** (Ali's request), whether or not Claude Code runs. A 401 gets one refresh and one retry.
   - **A refused refresh ends the sign-in.** The card and Settings then say "Your Claude sign-in has ended. Sign in again in Settings → Claude."
   - **A network or server failure keeps the last numbers** and shows "Plan limits as of 10:42".
   - **Sign out** deletes the file. The grant has no public revoke; claude.ai's settings list it.
   - **The tokens go only to `platform.claude.com` and `api.anthropic.com`**, over HTTPS through `PreferIPv4Handler`. They are never logged and never sent to the phone, which gets percentages only.
   - **The status line's numbers stay a second source**, and the newer one wins.
   - **`seven_day_opus`, `seven_day_sonnet` and `model_scoped` windows**, when present, show as extra lines in the Large panel ("Opus week 12%").
   - **The old check that borrowed Claude Code's token from `.credentials.json` is gone**, along with its "Check my plan's limits online" checkbox.
4. **Telemetry on loopback (Ali's request).**
   - **The receiver.** `ClaudeTelemetry` is a small OTLP/HTTP receiver bound to **127.0.0.1:43832** only. It drops any connection that isn't from this PC and accepts only `POST /v1/logs` and `POST /v1/metrics` with a JSON body of at most 4 MB (gzip and chunked allowed). Everything else gets 404, 405, 411, 413 or 415. It runs while Claude Code is connected and never touches the phone link (43830/43831).
   - **What Connect adds.** Connect, or **Update connection** for a connection made by v0.23–v0.25, adds to settings.json's `env`: `CLAUDE_CODE_ENABLE_TELEMETRY=1`, `OTEL_METRICS_EXPORTER=otlp`, `OTEL_LOGS_EXPORTER=otlp`, `OTEL_EXPORTER_OTLP_PROTOCOL=http/json`, `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:43832`, `OTEL_METRIC_EXPORT_INTERVAL=10000` and `OTEL_METRICS_INCLUDE_REPOSITORY=true`.
   - **Exactly-what-was-added.** The keys added are recorded in `claude\connected-env.json`, and Disconnect removes exactly those while they still hold Widget Screen's values.
   - **The user's own telemetry wins.** If settings.json already sets another `OTEL_*` value, telemetry is left out, nothing is overwritten, and Settings → Claude names the keys.
   - **No content.** Prompt, response and tool-detail logging (`OTEL_LOG_USER_PROMPTS`, `OTEL_LOG_TOOL_DETAILS`) are never turned on.
   - **What counts.** `claude_code.api_request` events carry model, the token counts, `cost_usd` and `request_id`, and they count usage. Metrics are sums with no request id, so they only mark a session active and are never added to the totals.
5. **Usage per project and per session, with cost.**
   - **The logs are the history.** They're read incrementally as before, for Today and Week from before the app ran.
   - **Telemetry is the live part.** Each reply counts once, keyed by its request id (message.id when there is none). A reply seen first in telemetry isn't counted again from the log, and the other way round. Telemetry's `cost_usd` replaces the log's estimate for the same reply.
   - **The project is the folder the session started in**, taken from the hook's cwd, Claude Code's list or the log's first cwd. A telemetry-only session falls back to `vcs.repository.name`.
   - **Cost** is labelled "≈ $ at API prices": telemetry's own, or for log lines worked out from the price table in Claude Code 2.1.309's model catalog (`ClaudePrices`). A Pro or Max plan doesn't pay these prices; they show what the use would cost.
6. **Choosing projects (Ali's request).**
   - **The list.** Settings → Claude → Projects lists every project the logs, Claude Code's list, the hooks and telemetry know, newest first. Each row shows when it was last used and this week's tokens and cost, with a tick.
   - **New projects.** "Show new projects automatically" is on by default; a project without a choice follows it.
   - **Where it's kept.** Choices are saved in settings.xml by folder (`ClaudeProjects`, `ClaudeShowNewProjects`).
   - **What it hides.** Unticked projects leave the card's sessions and per-project usage. The plan meters always show the whole plan.
   - **The phone.** The PC filters before it answers, so the phone's card shows the same selection. The phone's own Settings stay Theme, Display, Widgets and PC.

## Rationale

- Claude Code's session list is how Claude Code itself shows "other sessions on this machine". It is the only source that covers every way Ali runs Claude Code without any setup.
- An own sign-in is the one way to read plan limits that doesn't depend on how Claude Code was started, and it never touches Claude Code's credentials. `user:profile` lets it read the profile and the usage, but not chats or inference.
- Telemetry is Claude Code's official, documented export. On loopback it adds live per-session tokens and cost without reading any content.

## Tradeoffs

- **Undocumented surfaces.** Claude Code's session list, the usage endpoint and the use of Claude Code's OAuth client by another program are all undocumented and may change. Each is parsed defensively. If one breaks, the card falls back to the others: the hooks still give states, the logs still give usage, and the meters keep their last numbers with an "as of" time.
- **Telemetry needs the session restarted.** It only arrives from Claude Code sessions started after Connect or Update connection, and only while Widget Screen runs. Usage from before then comes from the logs.
- **The sign-in can't be tested here.** Signing in needs Ali's own browser and account, so the checks use a fake token server and fixtures.
- **One more loopback port.** 43832 is a second local listener (the phone link is the first). It answers only this PC and only Claude Code's export paths.
- **Android** has no Claude Code of its own. The phone shows the PC's view, so it gets the plan state, extra windows, cost and the filtered lists through its `claude` section.
