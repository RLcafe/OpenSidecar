# 018 — Claude thread names: no placeholders, the first request as a fallback

Ali reported (1 Nov 2026) that the Claude watcher showed threads without names: they were all called "Remote Control". He asked for it to be fixed in v2.

## What was wrong

Checked read-only on Ali's PC (titles and field names only; no log contents copied):

- **The placeholder.** 24 of the Claude app's 45 thread records (`claude-code-sessions\…\local_*.json`) have the title "Remote Control session (from a project)": every thread started through Remote Control gets it, with `titleSource` "auto". The watcher took the app's title as the thread's name (decision 016), so they all read the same.
- **No other title exists for those threads.**
  - The app's `postTurnSummary` is a status line about the last turn, not a title.
  - Claude Code's session list only has derived names ("widget-screen-ac", `nameSource` "derived").
  - The session logs have no summary or title entries.
- **The request is the only real name.** In a Remote Control thread it is the first prompt: the person's `<message>` inside a `<wake>` envelope, or a brief's `<note>` inside a `<relay>`. The relay often arrives as a line Claude Code marks `isMeta`.

## Decision

- **Placeholders count as no title** (`ClaudeTitles.Placeholder`): empty, "Remote Control", "Remote Control session (from a project)", "New session", "Untitled", "Claude Code" and the same pattern with "session", "chat", "thread" or a bracketed source.
- **A thread's name is picked in this order** (`ClaudeTitles.Pick` in `ClaudeService.View`):
  1. a name the user typed: a rename in Claude Code's session list, or a `custom-title` line in its log;
  2. a generated title: Claude Code's status-line session name, then the Claude app's title;
  3. the first line of the session's first request, at most 60 characters, cut at a word with "…" (`ClaudeTitles.FromPrompt`);
  4. last, a label from the session id ("Thread 1b2187"), made by the card on both apps.
- **What counts as the first request.** The log reader keeps it per session (`ClaudeUsage.Prompts`).
  - It skips helpers' lines (`isSidechain`), compacted summaries (`isCompactSummary`), tool results, and Claude Code's notes (`isMeta`), unless a note carries a request envelope.
  - From an envelope it takes the `<message>` (else the `<note>`) and drops `<system-reminder>` and command blocks.
- **Where a thread runs is shown beside the name, never as it.** A thread with a `remoteControlSpawn` (or a "Remote Control…" title) shows "Remote Control"; other Claude app threads show "Desktop", running or ended. The ledger and the Pit stop strip say "Remote Control · 6 min" or "Remote Control · ended 1 h ago".
- **Better titles take over.** The name is picked again on every refresh, so a rename or a real title from the app replaces the request as soon as it appears.
- **The phone** gets the PC's chosen name in the thread's existing `title` field and its `where`. It applies the same placeholder rule and id label. The link is unchanged.

## Rationale

- The request is what Ali recognises a thread by, and Remote Control threads have nothing else.
- Decision 016's "titles are typed ones or the app's, never prompts" assumed the app always had a title, which isn't true for Remote Control threads. Only the first line is used, cut to 60 characters. It is shown on Ali's own dashboard and phone, over the paired local link the card already uses. Nothing is stored, and telemetry still never carries prompts.

## Tradeoffs

- A request's first line can be short or vague ("continue", "Status update"). It is still the person's own words, and a rename wins.
- A Remote Control brief that arrives before the person's message names the thread by the brief's first line. The brief usually summarises the request.
- The placeholder list is a pattern over known wordings. A new wording of the app's placeholder would show until it's added.
