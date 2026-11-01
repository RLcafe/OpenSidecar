# 019 — Claude watcher fixes from the live app

Ali tried v0.32.1 on his PC (1 Nov 2026) and reported:

1. No animation (car states, roll in and out, pit stop).
2. "Waiting for me" looks the same as idle.
3. The usage and tokens don't match the Claude app.
4. The 5-hour chart shows one project.

He then added that his other projects don't show at all, asked for the cache reads as their own column, and asked for dust behind working cars. Everything below was checked read-only on his PC, without printing tokens or copying log contents.

## What was wrong

1. **Quiet mode was on** in his settings.xml, and v2 held every car still and skipped the roll in Quiet mode (decision 017). The running copy was the installed v0.32.1 (elevated, through the full-sensors task). Nothing else stopped the motion: a real window with Quiet mode off animated (checked frame by frame).
2. **"Done" and "Ready" looked alike.** Done's brake glow played once and then rested on a frame with its lights off. At 24 pixels, Ready and Done were the same car.
3. **Two causes:**
   - **The plan has no 5-hour window right now.** Anthropic's usage answer had `five_hour: null`, `seven_day` 16%, and a Fable-scoped weekly window (2%) only in a newer `limits` list, which the widget didn't read. The card showed an empty 5-hour meter and missed Fable.
   - **Tokens counted cache reads.** Today they were 555M of 562M tokens. For the threads in Ali's screenshot the counts were 207M, 64.4M, 63.5M, 52.2M, 34.9M and 29M; without cache reads they are 2.1M, 784k, 678k, 577k, 571k and 451k (the thread that showed 137M had reached 156M → 1.2M by the time of the check).
4. **Only one project had any use in the last five hours** ("Widget screen", 1,263 replies in 7 threads), so per-project lanes could only show one.
5. **Other projects didn't show at all:**
   - **"Week" was the plan's week.** The 5 h and Week periods followed the plan's own windows, and the week had reset that morning (Sun 04:00). So OTSEC (last used Oct 27), Photo Manager (Oct 25), Brainstorming (Oct 24) and cleanurtv (Oct 27) fell out.
   - **Quiet projects were dropped.** A project with no thread in the period had no garage.
   - **Some are unticked in Settings → Claude → Projects:** Police chase claude, cleanurtv and AEQ, plus an old Photo Manager worktree entry. Those were Ali's own ticks and stay as they are.

## Decision

- **Motion in Quiet mode.** The cars keep moving in Quiet mode unless Settings → Claude → **Keep the cars still in Quiet mode** is ticked (`ClaudeStillInQuiet`, off by default). The frame clock still stops while the card is hidden or the window is minimised or in the tray, and the phone follows the PC's `still` flag.
- **Distinct states:**
  - **Needs you:** a question or permission is waiting. Amber hazards, plus an amber roof beacon in every frame, and an Amber chip.
  - **Your turn:** Claude has finished its reply. Brake lights stay on with a breathing glow, and a Glacier "Your turn" chip.
  - **Idle:** your turn for 30 minutes, or nothing asked yet. Headlights off, the car at 70%, and a grey "Idle" chip.
  - Working, Ended and the others are unchanged. The heading counts "N your turn".
- **Plan limits.**
  - The usage answer's `limits` list is read: `weekly_all` fills the week, a five-hour kind fills the 5-hour window, and `weekly_scoped` with a model name adds "Fable week".
  - With no 5-hour window, the tank says "No 5-hour limit on your plan right now". Small shows the week instead, and the fuel lamp follows the week.
- **Tokens** are fresh input, cache writes and output. **Cache reads** have their own column on Large ("Cache reads") and in the facts tooltip. The cache hit rate is unchanged.
- **API value** (what the tokens would cost at API list prices) is hidden by default: Ali can't check it and his plan doesn't pay it. Settings → Claude → **Show what the tokens would cost at API list prices** (`ClaudeShowApiValue`) brings it back with the header "API value". Otherwise the column shows each thread's **replies** in the period, a number he can check against the thread.
- **Periods are rolling:** 5 h is the last five hours, Today from local midnight, Week the last seven days.
- **Every ticked project the PC knows gets a garage.** That covers projects in the Claude app's thread records (any age) and in Claude Code's logs (last 8 days). A project with no thread in the period sits after the busy ones as a quiet, dimmed garage ("Quiet · last used Oct 25").
- **Lanes:** one per project used in the last five hours (up to six). In the room left, the busiest threads of a project that had more than one are listed under it, labelled with their titles.
- **Working cars kick up dust** behind both tyres: pale specks drifting back and up for four frames, three per wheel when heavy. A dashed ground line scrolls back under them. Stuck keeps its tyre smoke.
- **The phone** gets `still`, `apiValue`, per-thread `replies` and `cache`, per-group `replies`, `cache` and `lastActive`, `facts.replies`, and lanes with `thread` and `color`. The link is unchanged otherwise.

## Tradeoffs

- **Rolling periods** no longer match the plan's windows exactly. The plan meters still show the plan's own percentages and resets.
- **The quiet garage list** only knows projects with Claude Code data on this PC. Work done in cloud sessions leaves none, so such a project can't show.
- **Cars move in Quiet mode by default,** which changes Quiet mode for this one card. The frame clock costs nothing while the card isn't visible, and the box restores the old behaviour.
