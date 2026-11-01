# 017 — Claude watcher v2: the pit wall, and pixel cars drawn in code

Ali approved the v2 design on 1 Nov 2026 ([docs/design/claude-watcher-v2.html](../design/claude-watcher-v2.html)): each project is a pit garage, each thread a race car, and the plan meters become car gauges. He chose the drawn pixel-art cars from the mock over generated image art, so no PNGs ship. This replaces the critters of decisions 014–016 on both apps; the data sources stay the same.

## Decision

- **Layouts.** The card keeps its three sizes (`CardSizing.Claude`: Small under 360 × 280, Large from 640 × 420) with new content.
  - **Large** is the pit wall. The heading has a live summary ("1 needs you · 2 running · 6 threads today") and the 5 h / Today / Week switch. Under it is the plan row:
    - the 5-hour tank: a needle gauge with a pace tick and a dashed Amber projection, "on pace for N%";
    - the week as seven day-laps with a chequered flag at the reset;
    - from 860 units of body, the rev counter: tokens a minute with a redline and the hour's burn line.
  - Then the pit stop strip for the thread that has waited longest on Ali, with its reason and Go to it.
  - Then the ledger of garages (projects) and cars (threads): state chip, title, where, age, tokens, ≈ $, model mix and lines added and removed. The models and lines columns drop on narrow tables, and the ledger scrolls inside the card.
  - From 1000 units of body there is a side panel: one 5-hour lane per project (10-minute bins) and four facts for today (tokens and ≈ $, cache hit rate, tool calls with how many asked Ali, lines changed).
  - **Medium** has two mini plan tiles with pace, then the top garages that fit, with up to three cars inline. A garage that needs Ali comes first and says so.
  - **Small** shows the car that needs Ali (or the busiest running one) and the 5-hour meter with its pace.
- **Cars and garages are drawn in code as pixel art.** Each sprite is a list of vector steps (`PitArt` on both apps, ported from the mock's `carSVG`, `wheel`, `carFrontSVG`, `carRearSVG` and garage drawings). It is drawn into a small bitmap at its native pixel size: 2 design units a pixel under 150 wide, 3 from there, so a 48-unit ledger car is 24 pixels. The alpha is then thresholded (above 80 is opaque, the rest clear) and colours snap to 17-unit steps. The bitmap is shown scaled up with nearest-neighbour (WPF `BitmapScalingMode.NearestNeighbor`, Android bitmap filtering off). Frames are cached by what they show. Fonts, glyphs and the no-images rule are unchanged.
- **What the cars show (no faces).** The class follows the model a thread used most: a grand tourer for Opus (and the bigger Fable and Mythos), a sports car for Sonnet, a hot hatch for Haiku. The colour is the project's accent (Lime, Glacier or Lavender by a hash of its name; Amber stays for "needs you"). Each state is an 8-frame cycle, stepped, not tweened:
  - **Working:** the wheels turn at 4–24 fps, set by the thread's tokens a minute; the body jitters a pixel; exhaust puffs at half the rev counter's scale.
  - **Long tool run** (a tool approved over 15 s ago and not finished): the car revs on its brakes.
  - **Needs you:** hazards blink and the headlights flash.
  - **Compacting:** up on the jack, with the front wheel off.
  - **Rate-limited or API error** (an `api_error` within a minute and no later request): wheelspin in tyre smoke.
  - **Done:** the brake lights glow, then fade (once).
  - **Ended:** lights off and a slate cover drops over it (once), at 60%.
  - **Near the 5-hour limit** (90%): a fuel lamp blinks and the car sputters.
  - **Burning at the redline:** the brake discs glow.
  - **Garages:** a lime light spill while busy; an amber beacon when a car inside needs Ali (folded or not); the shutter down with the car count when folded; dimmed when everything ended.
- **Rolling in and out** replaces eating and spitting (about 1.2 s, in the project's rows).
  - **Collapse:** the shutter rolls up, and each car backs out of its row with white reverse lights. It turns, then drives into the bay seen from behind, tail lights toward the viewer, braking at first and shrinking behind the walls. The overlay draws the bay, the cars inside, then the walls and shutter. Then the shutter drops and the count lights the sign.
  - **Expand:** each car drives out toward the viewer in front view with its headlights lit, growing. It turns, then drives to its row in side view with its wheels turning, and the shutter comes back half down.
  - Only cars that show in the ledger's visible part move.
- **One frame clock per card.** WPF uses a `DispatcherTimer` (`PitClock`); Android uses `View.postDelayed` in `ClaudeWidget`.
  - It runs at the rate the fastest moving sprite needs (at most 24 fps; 20 fps while rolling), and only while something moves.
  - It stops while the card is hidden, the window is minimised or in the tray, the phone app is in the background, or Quiet mode is on. Quiet mode keeps the state swaps (each state has a still frame) and drops the loops and the roll.
  - The header pill shows one still frame of a car with its hazards on.
- **New readings, from the sources that already existed:**
  - Pace and laps are worked out from each window's reset time.
  - Telemetry now keeps `tool_decision` (decision and source: "asked" means the user decided), `tool_result` and `api_error` events, and the `claude_code.lines_of_code.count` metric. Delta series add up; cumulative ones count only what they grew by.
  - Each reply keeps its cache-read and input tokens, for the cache hit rate.
  - A **PreCompact** hook marks a session compacting until the SessionStart that follows (or any next turn). Connect adds it, so a connection from v0.31.0 or earlier shows **Update connection** once.
  - Burn, lanes and facts only count projects ticked in Settings → Claude → Projects.
- **The phone** gets the same three designs through new fields in the `claude` section:
  - per thread: `pit`, `car`, `rate`, and `mix` and `lines` per period;
  - per group: `mix`, `added` and `removed`;
  - and `burn`, `lanes` and `facts`.
  
  The PC decides each car's state; the phone only draws. Folding still goes through `claude.collapse`. Nothing else on the link changed, and no token is ever sent.

## Rationale

- Code-drawn sprites keep the "no images" rule and cost nothing to ship. They recolour for every project and theme, and they are identical on both apps because both draw the same steps. Rendering at native size and thresholding gives true hard pixels instead of blurred vectors.
- Stepped frames on one clock that stops when unseen keep the motion cheap during games, which is the dashboard's first job.
- Everything on the card comes from data the watcher already collects. The only new collection is telemetry it was already receiving, and one hook.

## Tradeoffs

- **Pixel cars are small.** A 24-pixel car shows its state through big, simple cues (wheels, lights, smoke, the jack), not detail. Larger cars use 3-unit pixels.
- **Telemetry-only facts.** Tool calls, asked and denied, lines changed, long tool runs and API errors read "—" or never show without telemetry. That means a connection without telemetry, or sessions started before Connect or Update connection.
- **Compacting needs Update connection** for the PreCompact hook. Before that, a compacting session shows as working.
- **Another timer.** The frame clock is a second timer besides the 1 s heartbeat, allowed because Ali asked for the motion. It runs only while something visible moves.
- **The phone's views** need a device or emulator to see; none was available when this was built. Its drawing code (the same steps) was rendered on the JVM to compare with the PC's sprites.
