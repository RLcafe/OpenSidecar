# 020 — Archived threads, and hiding archived or idle ones

Ali asked (1 Nov 2026, both apps) to tell resolved or archived threads apart from idle ones, and for two choices in Settings → Claude: **Hide resolved/archived threads** (on by default) and **Hide idle threads** (off by default). Both apply to the Large ledger, the Medium rows, the phone and the heading counts. Hidden threads still count in their project's totals, with a small "+N hidden" note. If there is no archived flag on the PC, he asked to be told rather than have a rule guessed.

## What the PC knows

Checked read-only on Ali's PC:

- The Claude app's thread records (`claude-code-sessions\…\local_*.json`) carry `isArchived`. It is the only archive or resolve flag. None of his 45 records had it set.
- There is no "resolved" flag. The records also carry `postTurnSummary.status_category`, the app's own summary of the last turn: 25 "completed", 4 "review_ready" and 8 "blocked". That describes the last reply, not whether Ali closed the thread, so it isn't used.
- Claude Code's logs and session list have no archive state.

So **Archived means `isArchived` only**. Treating a "completed" thread, or one that ended more than a day ago, as resolved is left for Ali to decide.

## Decision

- **Four states for a thread that isn't running or waiting:**
  - **Idle:** open, your turn for 30 minutes or more. The engine is off, the car is dimmed and the chip is grey (decision 019, unchanged).
  - **Ended:** the session closed. The car is under its cover.
  - **Archived:** a new `PitState.Archived` (`"archived"` on the phone link). The car is drawn like Ended, under its cover, but dimmer (row 45%, against 62% for Ended). The chip says "Archived", without an outline, and the thread parks last in its project.
- **Archived threads are kept.** Until now they were dropped when the view was built (`ClaudeService.View`). Now they are marked `ClaudeSession.Archived` and they still find their project.
- **Settings → Claude:**
  - **Hide archived threads** is stored as `ClaudeShowArchived` (false, so hidden, by default; older settings files get the default).
  - **Hide idle threads** is stored as `ClaudeHideIdle` (false by default).
  - `ClaudePace.Hidden(state, showArchived, hideIdle)` decides on both platforms' data. The PC filters, and the phone shows what it's sent.
- **Counting:**
  - `ClaudeView.Groups` keeps tokens, cache reads, replies, models and lines from every thread in the period.
  - A group lists only the shown threads, with `Hidden` for the rest.
  - The project line ends "+N hidden". A project whose threads are all hidden reads "N hidden · Quiet · last used …".
  - The heading's thread count, the roll-in/out bubbles and the Medium inline cars count shown threads.
- **Phone link:** each thread gains `archived` (its `pit` is `"archived"`), and each group gains `hidden`. The phone draws an archived car as Ended (`PitArt.state("archived")` is END), at a lower opacity, with the same chip and words.
- Running sessions (Small's car, the pill, the needs-you list) aren't affected: an archived thread isn't running, and an idle running one still shows on Small.
- Settings → Claude → Projects counts a project's threads this week including archived ones.

## Tradeoffs

- Hiding idle threads hides open sessions too. A project whose only running thread is idle then looks asleep (its garage dims) until that thread works again. That's what "hide idle" asks for, and the thread is one setting away.
- No guessed "resolved" rule: with no flag on the PC, an ended thread stays Ended until Ali archives it in the Claude app or picks a rule.

## Resolved (Ali's choice, 1 Nov 2026, v0.34.1)

Ali chose that threads the Claude app marks "completed" count as resolved, as well as archived ones.

- **Resolved** (`PitState.Resolved`, `"resolved"` on the phone link with a `resolved` flag) is set when all of these hold:
  - the app's record has `postTurnSummary.status_category` "completed";
  - its `summarizes_uuid` matches `lastAssistantUuid`, so the summary is about the latest reply;
  - the thread isn't archived;
  - Claude Code's logs show nothing more than two minutes newer than the record's `lastActivityAt` (`ClaudeService.ResolvedSlack`). A new prompt un-resolves it until the app writes a new summary.

  Only the category and the two ids are read, never the summary's text (`ClaudeAppThread.Completed`).
- It looks like Archived: under the cover, dimmed the most, last in its project, with a "Resolved" chip (`ClaudePace.Parked`, `ClaudeText.parked`).
- **Live state wins.** A resolved thread that is working, needs Ali or is compacting shows that state and isn't hidden. A running thread whose turn has ended and whose latest reply was "completed" is Resolved.
- "Review ready" and "blocked" are not resolved; an ended one stays Ended.
- The setting is renamed **Hide resolved and archived threads**. It's the same stored value (`ClaudeShowArchived`), still on by default.
- On Ali's PC that is the 25 records marked "completed" (of 45), less any whose logs show newer work.
