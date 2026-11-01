# Changelog

Released versions of Widget Screen, newest first. Each version is an annotated `vX.Y.Z` tag on `main`. Branches that haven't been merged yet are in [BRANCHES.md](BRANCHES.md), along with the version bump rules.

## Unreleased

Archived and idle Claude threads (Ali's request; Windows and the phone; decision 020):

- **Archived threads stand apart.** A thread you archived in the Claude app now shows as **Archived**: its car is under its cover, dimmed more than an ended one, and it sits last in its project. Before, archived threads were left out completely.
- **Settings → Claude → Hide archived threads** (on by default) and **Hide idle threads** (off by default). They apply to the PC's card, the phone and the heading's thread count. Hidden threads still count in their project's tokens, replies and lines, and the project says "+2 hidden".
- The PC has no "resolved" flag; the Claude app's archive is the only one. Ended threads stay Ended.

## v0.33.2 (1 Nov 2026)

Claude ledger and timeline polish (Ali's requests; Windows and the phone):

- **The Models column names the model** ("Opus 4.8", "Fable 5.1") in its colour, instead of a bar that was solid green for every thread. A thread that mixed models shows its top two with shares ("Opus 70% · Sonnet 30%"); hovering on the PC lists them all.
- **Columns fit at larger text sizes.** Models and Lines step aside when the text is bigger, so thread titles keep their room, and Replies stays readable.
- **The 5-hour timeline shows one lane per project**, with taller bars and no lanes for single threads.

## v0.33.1 (1 Nov 2026)

- **Clearer dust behind working cars** (Ali's request; Windows and the phone). Light puffs kick up behind each tyre at ground level and drift back and away: two per tyre, four when the thread burns hard. Before, the dust was faint specks hidden under the car.

## v0.33.0 (1 Nov 2026)

Claude watcher fixes from the live app (Ali's report; Windows and the phone; decision 019):

- **The cars move again.** Quiet mode was holding them still. Now they keep driving, revving and rolling into their garages in Quiet mode too. Settings → Claude → **Keep the cars still in Quiet mode** brings back the old way. They still stop whenever the card is hidden or the window is minimised.
- **"Waiting for me" stands apart from idle.**
  - **Needs you:** amber hazards, an amber roof beacon and an Amber chip.
  - **Your turn:** Claude has finished; brake lights on and a blue chip.
  - **Idle:** your turn for over 30 minutes, or nothing asked yet; engine off, dimmed, grey chip.
  - The heading counts "your turn".
- **Plan meters match the Claude app.** The plan's newer limits list is read (for example "Fable only 2%"). When the plan has no 5-hour window, the card says so instead of showing an empty meter.
- **Tokens no longer count cache reads**, which made them hundreds of times too big (207M became 2.1M for one thread). Cache reads have their own column. The API value is hidden by default, since your plan doesn't pay those prices; replies show instead. Settings → Claude can bring the API value back.
- **Every project shows.**
  - **Periods are rolling:** the last 5 hours, today, the last 7 days. "Week" used to follow the plan's week, which had just reset.
  - **Quiet garages:** a ticked project with no thread in the period still has a garage, dimmed, with when it was last used.
- **The 5-hour chart** has a lane for every project used in that time, and under a busy project, its busiest threads.
- **Working cars kick up dust** behind their tyres, more when they burn more, over a ground line that scrolls by.

## v0.32.1 (1 Nov 2026)

Claude thread names (Ali's report; Windows and the phone; decision 018):

- **Threads have real names again.** The Claude app calls every thread started through Remote Control "Remote Control session (from a project)", and the card showed that. Placeholders like that (and "New session", "Untitled") no longer count as a name.
- **The name, in order:** a name you gave the thread, a title the Claude app or Claude Code made for it, then the first line of the thread's first request (up to 60 characters). A short label from its id ("Thread 1b2187") is the last resort.
- **Remote Control is where a thread runs, not its name.** It shows beside the name ("Remote Control · 6 min", "Remote Control · ended 1 h ago").
- **A better title takes over** as soon as one appears. The phone shows the same names.

## v0.32.0 (1 Nov 2026)

Claude Watcher v2, the pit wall (Ali's approved design; Windows and the phone; decision 017):

- **Projects are pit garages and threads are race cars,** drawn in code as pixel art (no images). A car's class follows the model the thread used most: a grand tourer for Opus, a sports car for Sonnet, a hot hatch for Haiku. Its colour is the project's. No faces: a car shows what its thread is doing.
  - **Working:** its wheels turn as fast as it burns tokens, with exhaust puffs when heavy.
  - **Long tool run:** it revs on its brakes.
  - **Needs you:** hazards blink and the headlights flash.
  - **Compacting:** it's up on the jack.
  - **Rate-limited or an API error:** wheelspin in tyre smoke.
  - **Done:** the brake lights glow, then fade.
  - **Ended:** a cover drops over it.
  - Near the 5-hour limit a fuel lamp blinks; at the redline the brake discs glow.
  - Garages show a light under the shutter when busy, an amber beacon when a car inside needs you, and their car count when folded.
- **Tap a garage to roll its cars in or out.** Going in, each car backs out of its row, turns and drives into the bay with its tail lights toward you. Coming out, it drives toward you with its headlights on, then into its row. It takes about a second; Quiet mode skips the motion.
- **Large is a pit wall:**
  - the 5-hour tank gauge with your pace ("on pace for 92%", Amber when you'd run out);
  - the week as seven day-laps;
  - a rev counter in tokens a minute with the last hour's burn;
  - a pit stop strip for the thread waiting on you, with what it wants and **Go to it**;
  - a ledger of projects and threads with tokens, ≈ $, model mix and lines added and removed;
  - on wide cards, a 5-hour timeline per project and today's tokens, cache hit rate, tool calls and lines.
- **Medium** shows the two plan meters with pace over your top projects with their cars. **Small** shows the car that needs you over the 5-hour meter.
- **Motion costs nothing when unseen:** the cars only move while the card is on screen, the window isn't minimised and Quiet mode is off.
- **Settings → Claude offers Update connection once:** it adds a PreCompact hook so compacting shows. Tool calls and lines changed come from Claude Code's telemetry, in sessions started after connecting.
- Not yet tried: the phone card on a device, and live tool, lines and compaction readings from a real session.

## v0.31.0 (1 Nov 2026)

System stats upgrade with hardware sensors (user request; Windows, with the phone showing the same):

- **More readings:** CPU temperature, GPU temperature, video memory (VRAM) and the frame rate of the game in front, beside CPU, GPU, RAM, network and uptime. GPU temperature and VRAM come from Windows itself and need nothing extra. Every reading this PC can't give says why (for example "Needs admin: full-sensors mode" or "Needs the PawnIO driver") instead of showing a number.
- **Graph styles for each reading:** Auto, Line, Bars, Sparkline, Ring or Gauge, in the new Settings → Stats tab, with a checkbox per reading. Temperatures follow Settings → Weather's units and show °C or °F. The designs fit all nine readings, and a small card counts the ones it can't show.
- **LibreHardwareMonitor 0.9.6** for CPU temperatures: pinned by SHA-256, checked before it loads, with its MPL-2.0 licence and a third-party notice. Setup has a new **Install hardware sensors (LibreHardwareMonitor)** checkbox (on by default) that says what the extra packages are; untick it and the app runs without them. The PawnIO driver it needs for CPU temperatures isn't included: Settings says whether it's installed and links to pawnio.eu.
- **Full-sensors mode** (Settings → Stats): one Windows prompt registers a task that starts Widget Screen with administrator rights, for CPU temperatures and frame counting. Apps started from Quick Launch, macros and Games still start without administrator rights. Turning it off, or uninstalling, removes the task.
- **Frame rates** from RivaTuner Statistics Server when it runs (any game), or by counting DirectX frames where Windows allows it (administrators and Performance Log Users).

See `docs/decisions/016-hardware-sensors.md` and `docs/design/system-sensors.md`.

## v0.30.0 (1 Nov 2026)

Projects with their threads on the Claude Code card, and refresh times you choose (Ali's request; Windows and the phone; decision 016).

- **Each project is a big critter, and its threads are baby critters under it.** That covers every Claude Code thread of the chosen period (5 h, Today or Week), running or ended, with the titles the Claude app's sidebar shows. A thread sits under the folder it started in, with worktrees under their project. Ended threads sleep, dimmed. A project's face follows its most urgent running thread, so Needs you still turns it Amber. Under each project: how many threads and its tokens and "≈ $". Tap a running baby to bring its window forward.
- **Tap a project to fold its threads in.** The babies get eaten (they shrink into its mouth), and it shows a count bubble and full cheeks. Tap again and they pop back out. Quiet mode skips the motion. The choice is kept and shared with the phone, and a folded project still lights up when a thread inside needs you.
- **Large:** the plan meters and period switch in a strip, then a column per project with its babies, scrolling inside the column when there are many. **Medium:** a row of projects with up to three babies each, over the plan meters. Small is unchanged.
- **Settings → Claude → Refresh:** "Update the card every … seconds" (default 2, minimum 1) and "Check plan limits every … seconds" (default 300, minimum 10, with a note under 60 that Anthropic may slow down very frequent checks). If Anthropic answers 429 or 5xx, the check waits twice as long each time (up to 15 minutes), and the card shows "Plan limits as of hh:mm" until it gets through again. Quiet mode no longer stretches these. The phone follows the card's interval, at most every 2 seconds.
- Settings → Claude → Projects shows how many threads each project had this week.

## v0.29.0 (1 Nov 2026)

The Claude Code widget now works with the Claude app's Code tab (Ali's report: "it's not working"; Windows and the phone; decision 015). The fix was merged just before the v0.28.0 tag, so that tag already contains it; this is where it's described.

- **Every Claude Code session shows**, whether it runs in the Claude app's Code tab, a terminal or an editor, including ones started before you connected. Before, idle Code tab sessions never showed. The card now also reads Claude Code's own list of running sessions. Needs you also catches permission dialogs (a new permission hook) and questions.
- **Plan meters that actually fill: Settings → Claude → Sign in with Claude.** The Code tab never sends the plan's limits, and keeps Claude Code's sign-in where the widget can't use it, so the meters stayed empty. Widget Screen now has its own Claude sign-in in your browser. It can only read your profile and plan usage (no chats, no credits), is kept encrypted on this PC and renews itself. It checks your 5-hour and weekly limits every 5 minutes, whether or not Claude Code is open. When a check fails, the card keeps the numbers and says "Plan limits as of 10:42"; when the sign-in ends, it says so. Opus and Sonnet weekly limits show too when your plan has them. Your phone gets only the percentages.
- **Live usage through Claude Code's own telemetry.** Connect (or **Update connection**, for a connection from v0.23–v0.27) turns on Claude Code's OpenTelemetry and sends it only to Widget Screen on this PC (127.0.0.1, port 43832, not reachable from the network). It never includes prompts, replies or tool details. Telemetry settings you already have are left alone. It reaches sessions started after you update.
- **Usage per project and per session, with cost.** Tokens and what they'd cost at API prices ("≈ $"; your plan doesn't pay these), from Claude Code's logs plus telemetry, each reply counted once. A session that moves into a worktree stays under the folder it started in.
- **Choose which projects show:** Settings → Claude → Projects has a tick per project (newest first, with when it was last used and this week's tokens and cost) and "Show new projects automatically". Unticked projects leave the card's sessions and usage on the PC and the phone. The plan meters always show your whole plan.
- **Medium shows the plan and the projects together** (project rows beside the critters, plan meters below). Small shows the 5-hour percentage when there's room. The phone's card matches.
- Verified on this PC, read-only: the hooks fire from Code tab sessions, and the new reader finds the three running desktop sessions with their states and this week's usage per project. Not yet tried: your own sign-in (it needs your browser and account), telemetry from a session restarted after Update connection, and the phone card on a device.

## v0.28.0 (1 Nov 2026)

### Added

- **Smart home widget** (Windows and the phone): turn Home Assistant lights, switches and plugs on and off, dim lights and set thermostats from the dashboard. Settings → Home connects with Home Assistant's address and a long-lived access token (kept encrypted for your Windows account; home-network addresses only) and picks up to 24 devices. Small rows that scroll, Medium tiles, Large tiles with dimmers. The phone controls the same devices through the PC and never gets the token. Hidden by default. See `docs/design/smart-home.md`.

## v0.27.0 (1 Nov 2026)

A Games widget (user request; Windows and the phone):

- **Your installed games, one tap to play.** Games from Steam, the Epic Games Launcher and the Xbox app, recently played first, six a page like Quick Launch. A tap starts the game through its store. Turn it on in Edit layout; it starts hidden.
- **Playtime only where it's real.** Steam games show how long you've played and when you last played. Epic and the Xbox app keep that online, so their games show nothing rather than a guess.
- **Read from the PC, no accounts.** The stores' own files are read with their apps closed and no internet. Choose which stores to list in Settings → Widgets → Games.
- **The phone** shows the same games and starts them on the PC.
- Not yet tried: starting games from the card (no check starts a game), Xbox games (none on the development PC; checked with sample files), and the phone on a device.

## v0.26.0 (1 Nov 2026)

A Calendar and agenda widget (user request; Windows and the phone, the same designs on both):

- **What's next, with a countdown.** The next event's day, time, title and place, and "in 25 m" (or "now · 45 m left"). Turn it on in Edit layout; it starts hidden.
- **From your calendar feeds, no sign-in.** Add up to eight read-only calendar addresses in the new Settings → Calendar tab: Google Calendar's "Secret address in iCal format", an Outlook calendar published as ICS, or any other .ics address. The widget only reads them. Signing in with a Google or Microsoft account instead would need an app registered with them, which this app doesn't have.
- **The addresses stay private.** Anyone with one could read that calendar, so they're kept encrypted for your Windows account, shown shortened in Settings, and never sent to your phone.
- **Repeating events, time zones and changes** are understood: weekly, monthly ("last Friday"), yearly and daily repeats, skipped and moved occurrences, cancelled events, all-day events, and each feed's own time zones and daylight saving.
- **Updated every 15 minutes** while the widget is shown (30 in Quiet mode), and when you tap the card. If a feed can't be read, it keeps its last copy and the heading says so.
- **Small, medium and large designs**: the next event; the next event with the ones after it; and an agenda for two weeks grouped by day, with a coloured dot per calendar.
- Settings tabs are a little narrower so all twelve stay on one row.
- Not yet tried with real Google and Outlook feeds (the checks use feeds in their formats), or on a phone.

## v0.25.0 (1 Nov 2026)

A Macros widget (user request; Windows and the phone):

- **Stream Deck–style buttons that run several steps in a row.** Each macro is a chain of up to 12 steps: a keyboard shortcut, opening an app, file, folder or website, a dashboard or media control (mute, microphone, Focus, Quiet mode, volume, play/pause…), a wait, or a script (.ps1, .bat, .cmd or .vbs). Six buttons a page, like Quick Launch, growing with the card. Turn it on in Edit layout; it starts hidden.
- **A new Macros tab in Settings, after Quick Launch**, to make and edit them: name, icon and colour, then the steps in words, each one movable and changeable, with a step editor that says what's wrong before a step is added.
- **Nothing consequential runs by surprise.** A macro with a script, a step that a Quick Launch button would confirm (arguments, Delete, Alt+F4), or "Ask before running" turned on asks first, listing every step. One macro runs at a time, its button shows it's running, and a step that fails stops the rest and says which step it was.
- **On the phone**, the same buttons run the macros on the PC, asking first with the same list when needed. Macros are made on the PC.
- Not yet tried: real hotkey, launch and script steps (the checks only use waits and Quiet mode), and the phone card on a device.

## v0.24.0 (1 Nov 2026)

A Clipboard history widget (user request; Windows, with the phone as a remote):

- **The last texts you copied, one tap from pasting again.** A tap puts the text back on the clipboard and pastes it into the app you're using; the dashboard never takes the keyboard away from that app. Turn it on in Edit layout; it starts hidden.
- **Private copies stay private.** Text that password managers mark as private (the same marks Windows' own clipboard history respects) is never kept. The clipboard is only watched while the widget is on one of your pages, and Windows tells the app about each copy, so nothing polls.
- **Pause and Clear** on the card (Clear asks first). Settings → Widgets → Clipboard history sets how many texts to keep (5–50, default 10).
- **Memory only by default.** The history is gone when the app closes, unless you turn on "Keep the history when Widget Screen closes", which saves it encrypted for your Windows account.
- **Small, medium and large designs**: one-line rows; rows with when each was copied; and two-line previews with their length.
- The phone shows short previews of the PC's history and pastes on the PC. It can't keep a history of the phone's own copies, because Android doesn't let apps watch the clipboard in the background. Not yet tried on a phone, and a live copy and paste weren't tried by the checks (they would touch your real clipboard).

## v0.23.0 (1 Nov 2026)

A Claude Code widget (user request; Windows and the phone, the same designs on both):

- **Your Claude Code sessions as critters.** Each session on this PC is a little drawn creature, the same shape and colour for the same project every time: dozing when it's ready, eyes darting while it works, a happy face when it's done. Turn it on in Edit layout's Widgets list; it starts hidden.
- **It lights up when Claude needs you.** A session waiting for a permission or an answer turns Amber, throws its arms up and hops; the card gets an Amber border and the heading names the project ("api-server needs you"). If the card is on another page, a "Claude needs you" pill appears in the header and takes you there. An optional chime (Settings → Claude, off by default, silent in Quiet mode); the phone taps once.
- **Your plan's limits.** The 5-hour and weekly meters (Amber from 80%, "Limit reached · back 3:40 pm" at 100%), from Claude Code's status line and a read-only check of your plan's usage every 5 minutes with Claude Code's own sign-in. It uses no credits, and the sign-in is never stored, changed or sent anywhere else; the phone gets only the percentages. Settings → Claude can turn the check off.
- **Usage per project and per session**, counted from Claude Code's logs on this PC, for the 5-hour window, today or the week.
- **Tap a session to bring its terminal or editor to the front** (from the phone too).
- **Small, medium and large designs**: one big critter with its project and state; a lineup of critters over the two meters; and a row per session (title, project, model, how long, tokens) beside a panel with the meters, a period switch and usage by project.
- **Settings → Claude → Connect Claude Code** adds Widget Screen's hooks and status line to Claude Code's `settings.json`, after saving a backup next to it (`settings.json.widgetscreen-backup`). Your own hooks stay, and a status line you already had keeps showing. Disconnect takes out exactly what Connect added. The bridge, `ClaudeHook.exe`, comes with the app and the setup exe.
- Not yet tried with real Claude Code hooks firing, bringing a real terminal forward, the live usage check, or on a phone (labelled sample data shows the designs).

## v0.22.0 (1 Nov 2026)

A Notes and to-do widget (user request; Windows and the phone, the same designs on both):

- **A scratchpad and a to-do list on the dashboard.** Type notes, add to-dos with Add (or Enter), tick them off, delete one, or clear all the done ones (it asks first). The heading counts what's left ("3 TO DO"). Turn it on in Edit layout; it starts hidden.
- **Kept on the PC** in `notes.xml` next to your settings, saved a second after you stop typing and at once for list changes. Typing in the notes takes the keyboard like any text box on the dashboard.
- **Edit from your phone too.** The phone shows the same notes and list and changes them on the PC; what you type on the phone stays on screen until the PC has it.
- **Small, medium and large designs**: tabs for Notes and To-do on smaller cards, and both side by side on a large one.
- Not yet tried on a phone or emulator.

## v0.21.0 (1 Nov 2026)

A Prayer times widget (user request; Windows and the phone, the same designs on both):

- **Today's five prayers and a countdown to the next.** The next prayer shows its time and "in 1 h 12 m"; past prayers are dimmed and the next one is in your accent colour. For 20 minutes after a prayer begins, the card's heading says so ("ASR NOW"). Turn it on in Edit layout; it starts hidden.
- **Worked out on the PC, with no internet.** The times come from the sun's position with the standard astronomical method. Choose the calculation method (Umm al-Qura, Muslim World League, ISNA, Egypt, Karachi, Gulf, Kuwait, Qatar, Singapore, Turkey, Tehran or Jafari; the default follows your region) and the Asr school (Standard or Hanafi). Far-north summers use the angle-based rule, like published timetables. Checked against published timetables for Riyadh and London, within two minutes.
- **A new Prayer tab in Settings, after Weather.** Search for a city (the same city search as Weather), or leave it to follow the weather city.
- **An optional alert on the PC at each prayer time**, with an audio file you choose (such as an adhan recording) or a short built-in chime. Quiet mode keeps it silent, and a Stop button shows on the card and on your phone while it plays. It is off until you turn it on.
- **Small, medium and large designs**: the next prayer alone; the next prayer beside today's five times; and the next prayer with its progress, sunrise, the Hijri date and the method, over the times as rows or (on a wide card) six tiles.
- The phone shows the PC's times and counts down by itself; the alert plays on the PC only. Not yet tried on a phone, and the alert hasn't been heard at a real prayer time yet.

## v0.20.0 (1 Nov 2026)

A World clocks widget (user request; Windows and the phone, the same designs on both):

- **Two or three time zones side by side.** Each shows a city name, its local time, its day ("Today", "Tomorrow", "Yesterday") and how many hours it is from the PC ("+6 h", "−5:30 h"). Turn it on in Edit layout's Widgets list; it starts hidden, so your layout doesn't change.
- **Choose the cities in Settings → Widgets → World clocks**: up to three of Windows' time zones, each with a city name you can change (it starts as the zone's first city). With none chosen, a tap on the card opens that section.
- **No internet needed.** Times come from Windows' own time zone rules, so daylight saving switches by itself. The card follows the 12- or 24-hour clock setting.
- **Small, medium and large designs**: rows with the time at the right (cities that don't fit are counted in the heading); the cities side by side with a large time; and a tile per city with an even larger time and the date.
- The phone shows the PC's cities and keeps time itself between readings. Not yet tried on a phone or emulator.

## v0.19.0 (1 Nov 2026)

A Storage widget (user request; Windows and the phone, the same designs on both):

- **Free space on the PC's drives.** Each fixed drive shows how much is free, with a bar or ring of used space. Network, removable and optical drives aren't listed. Turn it on in Edit layout's Widgets list; it starts hidden, so your layout doesn't change.
- **A warning when a drive runs low.** A drive with less free space than the threshold in Settings → Widgets → Storage (1–50%, default 10%) turns to the warning colour, and the card's heading names it ("C: LOW", or "2 DRIVES LOW").
- **Small, medium and large designs**: a list of letters and free space; rows with each drive's name and "used of total" (dropping that line to fit four drives on the usual card); and a tile per drive with a ring and the free percentage. Drives that don't fit are counted in a last line ("+3 more drives").
- Drives are read once a minute while the card is shown (every two minutes in Quiet mode), off the dashboard's thread, and the phone shares the same reading.
- Not yet tried on a phone or emulator.

## v0.18.0 (31 Oct 2026)

Small, medium and large designs for every widget (user request; Windows and the phone, the same sizes on both). Small is each card's folded layout and Medium is the card you know; Large is new:

- **Focus** draws the session as a ring around the countdown, with the status and today's sessions inside it. The default full-height Focus column opens in this design.
- **Now Playing** centres a big cover over the song, with the seek bar and buttons underneath.
- **Sound mixer**: Master, Compact and Full are its small, medium and large designs, and a Full mixer 760 wide or more lists the apps in two columns.
- **Spotify**, 760 wide or more, moves the song into a side panel with a cover as wide as the panel over the title, artist and Like.
- **Quick Launch and Soundboard** buttons become tiles on big cards: the icon, the name and what the button does ("Website · www.google.com", "App", "Hotkey · Ctrl+Shift+M"), or where a sound comes from.
- Not yet tried on a phone or emulator, with a connected Discord, or with a real Spotify account (a labelled test fixture shows the Spotify panel).

## v0.17.0 (31 Oct 2026)

Small, medium and large designs, starting with System stats and Weather (user request; Windows and the phone, the same sizes on both):

- **Each card has three designs and switches between them as you resize it** in Edit layout. Your layouts open in the Medium design, which is the size they already had.
- **System stats.** Small is a clean list: each reading's name and a large number, with a thin bar under CPU, GPU and RAM. Medium shows CPU, GPU and RAM as rings with the percentage inside, and the network rates and uptime in a strip below. Large gives each reading its own tile with a graph of the last minute and a half; network graphs download and upload together, and uptime moves to the heading.
- **Weather.** Small is a glance: temperature and condition icon, city, today's high and low. Medium shows a large icon beside the temperature, the condition and city, and Feels, Wind and High / Low as three small tiles (plus the next three hours on a tall card). Large is a forecast with the next five hours in a strip.
- A reading at 90% or more still turns to the warning colour. Nothing new is polled: the graphs use the readings the card already gets.
- Not yet tried on a phone or emulator; the phone's designs build and their sizes are checked like the PC's.

## v0.16.0 (31 Oct 2026)

Six buttons a page (user request; Windows and the phone, the same rules on both):

- **Quick Launch and the Soundboard always show six buttons**, whatever size the card is. Small cards no longer drop to one or two buttons with lots of pages, and big cards no longer fill up with many small buttons: the six buttons grow with the card instead.
- The six sit 3 × 2, 2 × 3, in one row or in one column, whichever suits the card's shape while keeping every button at full touch size. With fewer than six buttons or sounds, they fill the card.
- On the smallest Quick Launch cards the buttons show just their icon; the name is in the tooltip. Wider cards put the name beside the icon, taller ones under it.
- The smallest sizes went up to fit six buttons: Quick Launch 288 × 204 and the Soundboard 300 × 240, also on small screens. A saved card smaller than that grows to the new minimum.

## v0.15.0 (31 Oct 2026)

Much more resizable widgets (user request, designed with the Impeccable skill; Windows and the phone, the same rules on both):

- **Edge grips.** Edit layout adds a grip in the middle of each side, next to the four corner grips. Drag one to change only the width or only the height. They show on sides long enough to keep clear of the corners.
- **Much smaller cards.** Every widget can now be made a lot smaller: Quick Launch down to 200 × 152 (one button with paging), Weather and the Soundboard to about 240 × 152, Focus to 280 × 192, Now Playing to 312 × 200, the mixer to 320 × 224, Spotify to 360 × 360. Your saved layouts and the default ones don't change.
- **Cards rearrange instead of cutting off.** Below a card's comfortable size it folds its secondary rows away and keeps its main reading and buttons at full touch size: Focus folds the presets into one length button beside Start that steps through them; Now Playing becomes the song with the cover and the buttons; the mixer moves the microphone beside the master slider and, on a short card, keeps the app list one tap away behind an **Apps** button; System stats drop the graphs and bars on narrow cards so the numbers stay big; Weather drops the details line; Spotify gets a smaller now-playing strip; the Connect Spotify and Connect Discord cards keep their button.
- Big cards look the same as before.

## v0.14.0 (31 Oct 2026)

Easier widget organizing (user request; Windows and the phone, the same rules on both):

- **Widgets snap to each other.** With Snap on, a card you move or resize lines up with the other widgets' edges, sits one gap beside them, or centres on them when it comes within a few units, and snaps to the screen's edges. Anywhere else it follows the 8-unit grid as before. Resizing never goes under a card's minimum size.
- **Guide lines** show which edges or centres a card lines up with while you hold it, and disappear when you let go. With Snap off, cards move freely as before.
- **Arrange** in Edit layout lines up the shown widgets neatly in their current order, column by column, filling the screen without gaps or overlaps. Hidden widgets keep their places. It is one undo step, and nothing is saved until Save layout. Reset layout still goes back to the default widgets.
- On the phone, Edit layout gets the same snapping, guides and Arrange button, and saves to the PC as before.

## v0.13.0 (31 Oct 2026)

Keep the game focused (user request; Windows only, the phone is a separate device and never touches the PC's focus):

- **Tapping the dashboard no longer takes the focus** from the game or app on your main screen. Buttons, sliders and dropdowns work as before; Windows just leaves the keyboard where it was.
- **Text boxes still take the keyboard** (Spotify search, Settings fields), so you can type; click back into the game and the dashboard stops taking the focus again.
- The window coming back from the tray when your phone disconnects, opening at sign-in, and a monitor being unplugged or replugged no longer grab the focus either.
- **Settings → Display → Keep the focus on the game or app I'm using**, on by default. With it on, F11 and Escape reach the dashboard only after tapping a text box; turning it off brings back the old behavior.

## v0.12.0 (31 Oct 2026)

A setup exe (user request; Windows only, the APK is already Android's installer):

- **A setup exe**, `WidgetScreen-Setup-vX.Y.Z.exe`, in every release folder. It installs Widget Screen for the signed-in user in `%LOCALAPPDATA%\Programs\Widget Screen` without administrator rights, adds a Start menu shortcut, starts the app when you sign in (a checkbox, on by default), and adds an entry to Settings → Apps → Installed apps.
- **Updating**: running a newer setup replaces the installed copy in place, closing it first, and keeps the startup choice. Settings, sign-ins and paired phones stay in `%LOCALAPPDATA%\WidgetScreen` as before.
- **Uninstalling** from Installed apps removes the app, both shortcuts and the entry. Settings, sign-ins and paired phones are kept unless you tick the box to delete them.
- Silent switches for scripting: `/S`, `--no-startup`, `--launch`, `/uninstall`, `--remove-settings`.

## v0.11.0 (22 Oct 2026)

Phone-native polish (audit branch 10; phone only, see the design note for what the PC side already has):

- **One calm offline card** at the top of the upright phone list while the PC is away ("Can't reach DESKTOP · trying again in 15 s", with Try now and Settings → PC), instead of a sticky status line.
- **A firewall hint right after pairing**: when the first reading after pairing fails, the phone says to allow Widget Screen on private networks in Windows Firewall, where the problem usually is.
- **Edit layout on a phone**: a readout of the card's size and place sits at the top of the card while a finger drags it, and the Widgets panel gains Move and Size nudge arrows (8 units a tap with Snap on, 1 without) for the fine moves a finger can't do.
- **Keep the screen on: Always, While charging or Never** (Settings → Display). New installs get While charging, which suits a phone on a stand; existing settings keep what they had.
- **Haptics**: a light tap on every button.
- **Android 13+ Back** goes through the system's back callback (predictive back), so Back keeps closing panels and sheets on new Android versions.

## 0.10.0 (22 Oct 2026)

Merges `feature/quick-launch-polish`, audit branch 9 (`docs/design/quick-launch-polish.md`).

- **A colour per button**: Settings → Quick Launch → Icon color picks one of the four accents for a button's glyph (or the theme's accent), so Spotify, Browser and Files aren't six identical green glyphs. The phone shows the same colours.
- **Control buttons show their state**: Mute, Microphone, Focus and Quiet mode buttons take the selected look while the thing they toggle is on, on both apps.
- **Drag to reorder** buttons in Settings → Quick Launch, with a mouse or a finger; the up and down buttons stay.
- **A hotkey names its target**: a hotkey button's tooltip (and the phone's confirmation) says which app it would go to right now, for example "Win+Shift+S → chrome".

## 0.9.0 (22 Oct 2026)

Merges `feature/readable-small-screens`, audit branch 8 (`docs/design/any-screen-size.md`).

- **Text size goes up to 140%** (Settings → Theme on the PC, Settings → Theme on the phone), for small screens and screens you sit far from. Above 115% some cards show less at once.
- **Small high-DPI screens keep readable text**: on Windows, a secondary screen smaller than 1920 × 720 on a system with display scaling above 100% now scales the dashboard up with that scaling and lets the widget canvas shrink to 60% instead of 80%. Screens at least 1920 × 720 are not affected, so the owner's panel looks exactly as before.

## 0.8.0 (22 Oct 2026)

Merges `feature/weather`, audit branch 7 (`docs/design/weather.md`).

- **Units**: Settings → Weather chooses Celsius and km/h or Fahrenheit and mph (the region's default until you pick); the phone follows the PC.
- **Today's high and low** in the details line ("↑ 25° ↓ 14°") and **the next hours** (now, +3 h, +6 h) on a tall enough card, on both apps.
- **A condition glyph** (sun, partly cloudy, cloud, fog, rain, snow, storm) beside the condition, drawn as a line path on the PC and in `Glyphs.java` on the phone.
- **The whole card is the refresh tap**; the Refresh button is gone. With no city, the card says where to choose one (and opens Settings → Weather on the PC).

## 0.7.0 (22 Oct 2026)

Merges `feature/now-playing`, audit branch 6.

- **The Now Playing position moves every second** while a song plays, on the PC and the phone, instead of jumping every three seconds; readings still correct it.
- **Any media app is named** in the card's corner (Firefox, VLC, Media Player, Tidal and so on, or a tidied app id), not only Spotify, Chrome and Edge. The phone receives the name too.
- **The playing app's volume is one tap away**: a volume button on the card opens a small panel with that app's mixer slider (master volume when the app has no mixer row), on both apps.
- Titles stay on one line (the full title is in the tooltip or content description), so the artist no longer gets pushed around by long names.

## 0.6.1 (22 Oct 2026)

Merges `feature/confirm-sheet`, audit branch 5 (`docs/design/confirm-sheet.md`).

- **One themed confirm sheet.** Quick Launch buttons that ask first now ask in a sheet over the dashboard (52 px Cancel and Run buttons, Escape or a tap outside cancels) instead of a Windows message box; the phone shows the same sheet instead of the system dialog.
- **First-launch tips** in the header ("Swipe sideways to switch pages · Settings → Phone pairs your phone"; the phone's own line on the phone) with a Got it button that puts them away for good. They show while there is one page and room beside the clock.
- **Settings → Phone comes before Discord**, since it's used far more often.
- **Tray mode with the phone is now off by default** (Settings → Phone). The dashboard vanishing the moment the phone app opened surprised more than it helped when both screens were in use; turn it on if you only ever use the phone.

## 0.6.0 (22 Oct 2026)

Merges `feature/focus-finish`, audit branch 4 (`docs/design/focus-finish.md`).

- **A finished focus session is hard to miss**: the whole card takes the accent tint, the time reads 00:00 with "Tap to clear", a chime plays (Settings → Theme, on by default, silent in Quiet mode), and the phone shows and sounds the same. A tap on the card clears it; otherwise it clears itself after a minute.
- **A fourth preset of your own length**: the "…" button opens a 5 to 180 minute picker inside the card; the length starts a session and stays as the fourth preset on both apps.
- **Sessions today** under the progress bar ("3 sessions today · 1 h 40 m"), counted on the PC and shown on the phone.
- A paused session dims the time so the pause reads from across the room.

## 0.5.0 (22 Oct 2026)

Merges `feature/mixer-events`, audit branch 3 (decision 011).

- **The sound mixer no longer does its Windows audio work on the dashboard's thread.** Outputs and app sessions are read on a dedicated audio thread, output names are cached, and Windows' own device notifications trigger a read the moment an output appears, leaves or becomes the default, so a new headset shows up at once instead of at the next 2-second beat.
- Apps in the mixer are listed playing first, idle next and System sounds last, on both apps.
- The live output levels sit under the master slider, where the sound is, and the row disappears when levels are off or paused instead of showing a dotted line; silence draws nothing.
- A phone and the dashboard share each audio, system and Now Playing reading for a second, so a connected phone no longer doubles the PC's work; the layout version the phone checks is only recomputed after a settings save, an edit or a screen change.
- Network adapters are enumerated every 30 s (and when Windows reports a change) rather than on every System stats tick.
- The phone no longer refreshes every card after each slider nudge, so mixer rows stop flickering mid-drag.

## 0.4.2 (22 Oct 2026)

Merges `fix/phone-link`, audit branch 2.

- **Find PCs on this Wi-Fi** now sends the discovery packet to each network interface's own broadcast address (with 255.255.255.255 as a fallback), which routers and many phones accept where the limited broadcast was dropped.
- The phone backs off while the PC is away (5 s, 15 s, then every minute) instead of knocking every 2 s with the screen on; a tap or a settings change still asks straight away.
- The phone remembers the PC's 12/24-hour clock choice, so the clock no longer flips to 12-hour while the PC is off.
- While the phone is editing the layout it no longer asks the PC for the layout section it would ignore.
- Phone link hardening: requests from web pages (an `Origin` or fetch header, or a host name that isn't this PC's address) are refused, so a page in your browser can't reach the link through DNS rebinding; at most 32 connections are served at once; the phone only accepts home-network addresses (private, link-local, `.local`), so a typo can't send its token to the internet.
- Settings → Phone lists each paired phone with when it paired and was last seen, and a **Forget** button per phone; **Forget all phones** stays.

## 0.4.1 (22 Oct 2026)

Merges `fix/save-settings`, the first branch from the app audit of 22 Oct 2026.

- **Fixed Settings → Save changes deleting every layout page except the first and turning phone control off.** The panel now saves on top of a copy of the current settings, so pages, the current page, Quiet mode and the Phone settings survive a save. A check covers it.
- The dashboard no longer closes when something unexpected goes wrong on the UI thread: the error is logged to `error.log` and shown on the status line, and the dashboard carries on (renders still fail loudly, and a storm of errors still exits).
- The phone link refuses a command whose number is missing or malformed instead of acting on it.
- "Opened ms-settings:sound" became "Opened Windows sound settings."
- Weather's "updated" time follows the 12- or 24-hour clock choice on both apps.

## 0.4.0 (21 Oct 2026)

Merges `feature/android-app`, then `feature/any-screen`, which was stacked on it.

From `feature/android-app` (decisions 007, 008 and 009, `docs/design/android-app.md`):

- **Android app**: a native Android version in `android/`, built by `scripts\build-android.ps1` into an installable `WidgetScreen.apk` (no Gradle, no libraries, no WebView), with the same widgets, palettes and accents. Release folders include the APK when the Android SDK is installed.
- **The phone is a remote for the PC**: it shows the PC's dashboard and every card controls the PC over home Wi-Fi: Focus, Now Playing, Quick Launch, the sound mixer with app outputs and the microphone, System stats, Weather, Spotify through the PC's connection and the Discord soundboard. The phone's own Settings are Theme, Display, Widgets and PC (find the PC and pair with its code). Sideways screens and upright tablets get the dashboard, upright phones get the cards in a list, and Settings → Display can keep the app sideways.
- **Settings → Phone** in the Windows app turns on the phone link for the local network (TCP 43830, UDP discovery on 43831), paired with a six-digit code. Paired phones are stored as token hashes in DPAPI-encrypted `remote-devices.dat`.
- **Tray mode**: a Hide to tray button in the header, and the window moves to the tray by itself while a paired phone shows the dashboard (on by default in Settings → Phone). Windows only.
- Fixed the phone showing no data right after pairing until the app restarted.

From `feature/any-screen` (decision 010, `docs/design/any-screen-size.md`):

- **Any screen size**: the dashboard keeps its 1920 × 720 design scale and lays itself out on a canvas shaped like the screen, so wider or taller second screens get more room, small screens keep readable text (the canvas may shrink to 80%), and narrow ones stack the header. The 1920 × 720 screen looks exactly as before. Layouts are saved per screen shape; a new shape stretches the closest saved layout, or arranges the page's widgets for it. Upright tablets get the dashboard; upright phones keep the list.
- **Layout pages**: up to eight named pages, each with its own widgets and layouts. Switch with the arrows in the header or a sideways swipe; add, rename, reorder and delete pages in Edit layout. The page shown is shared with the phone.
- **The phone mirrors the PC**: the phone shows the PC's layout and page, stretched to its screen, lists the PC's cards in order when upright, and saves its own Edit layout changes to the PC. The phone link gained a layout section and page commands.
- **Edit layout on Windows** uses the phone's list: a check mark per widget and the pages, instead of the widget dropdown.
- **Cards size their content**: Quick Launch fits as many buttons as its card holds (and grows them when they all fit), Focus, the Now Playing cover and the Weather temperature grow with their cards, and Spotify shows two or three columns on wide cards.
- `--render-size WxH` and `tests\screen-size-renders.ps1` render the dashboard for other screens.

## 0.3.0 (20 Oct 2026)

Merges `feature/discord-soundboard`.

- **Discord soundboard**: a new Soundboard widget plays your servers' soundboard sounds as you, in the voice channel you're in, through the Discord app on this PC. Connect it in the new Settings → Discord tab with your own Discord app's Client ID and Client Secret; Discord asks you once to authorize it. The widget starts hidden (turn it on in Edit layout), lists the current server's sounds first, then Discord's built-in sounds, and reacts to joining or leaving voice without polling. App details and sign-in are stored encrypted in `discord-session.dat` (decision 006). The live Discord sign-in and playback haven't been verified yet.

## 0.2.3 (11 Oct 2026)

Merges `feature/app-identity`.

- **App identity**: the exe has a logo (Focus in Lime beside Now Playing, Quick Launch and the mixer on a Midnight tile), shown in Explorer, on the window and in the taskbar. Its properties show the app name "Widget Screen", the author "Ali Alyami" and the version, which the build now takes from the newest tag.
- **Release copies**: `scripts\release.ps1` builds a tagged version from a clean checkout of its tag, runs every check and puts a self-contained copy in `releases\vX.Y.Z\` (git-ignored). The desktop shortcut opens the release copy instead of the `artifacts\app` build. See "Releases" in `BRANCHES.md`.

## 0.2.2 (10 Oct 2026)

Merges `feature/search-play`.

- **Spotify search plays songs**: tapping a search result now plays the song, like tapping a song in a playlist or album, instead of opening it in Spotify. Spotify's own autoplay decides what follows. If no Spotify device is active, the widget opens Spotify and retries. Connections without the playback permission still open the link (decision 003, updated).

## 0.2.1 (10 Oct 2026)

Merges `feature/ui-polish`, a polish, distill and clarify pass over the dashboard and Settings (`docs/design/ui-polish.md`).

- **Quieter dashboard**: labels that never change are gone (LIVE, EVERY 2 SEC, "Playing on this PC", the footer F11 hint and the tagline). The status line clears itself after about ten seconds.
- **Clearer states**: Quiet mode is a toggle that lights up when on, Focus shows when the session ends, and the microphone button states whether it's on or muted.
- **Spotify, not connected**: one "Connect Spotify" empty state with a Set up Spotify button replaces the empty tabs, list and Like strip. The card's header button is now "Account".
- **Polish**: a bigger Focus time in tall cards, the mixer's output name aligned left, a shared selected look for Spotify tabs, and a 13 px caption style for the smallest text.
- **Settings**: plainer headings and shorter copy, Weather in one column with the results list hidden until there are results, and Spotify setup as three numbered steps.
- **Removed** the "Arrange dashboard for Spotify + Now Playing" button. Reset layout does the same.

## 0.2.0 (10 Oct 2026)

Merges `feature/mixer-output-devices` and `feature/header-clock-stats`.

- **Sound mixer outputs**: every app's volume slider shows, whatever output it plays on. Each app has an output button to see and move it to another device, and tapping Master volume picks the default output (decision 005, `docs/design/mixer-output-devices.md`).
- **Header clock**: the time and date always show at the top left of the header, including in Edit layout. The Clock widget and the header brand are gone. Old layouts drop the Clock, and Focus grows into its space when it sat directly below it (`docs/design/header-clock-and-stats.md`).
- **Default layout**: Focus fills the left column, followed by Now Playing, Quick Launch, Sound Mixer and Spotify. Custom layouts are kept.
- **System stats scale with their widget**, using one to three columns that fill the card.
- Branch and version tracking: `BRANCHES.md`, this changelog, and worktrees under the git-ignored `.worktrees\` folder.

## 0.1.0 (9 Oct 2026)

First working version, built on the as-built WPF baseline from `design specfication/`.

- **Touchscreen polish** (`touchscreen-polish.md`): the Settings panel is 1440 × 672 with a footer Cancel, inputs and buttons are at least 52 px tall, slider thumbs are 48 × 48 and capture touch directly, the scrollbar area is 40 px, small text is 15, Focus presets show the selected one, and press-and-hold and flicks are off.
- **Spotify library widget**: tabs are Albums (recently played), Playlists and Search with cover thumbnails, plus a bottom strip with the cover, song, artist and Like. The Playing tab and playback controls are gone (decision 004).
- **Play in collection**: tapping a song inside an album or playlist plays it there, so playback continues through the collection (decision 003).
- **Fixed** Spotify sign-in timing out on networks with unrouted IPv6. Web requests now prefer IPv4 when a host has both.
- **Fixed** a crash on startup when Spotify was already connected.
