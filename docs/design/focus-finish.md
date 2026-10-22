# Focus timer: finish, custom length and sessions (22 Oct 2026)

From the app audit: the only sign that a session had ended was a 15 px status line that cleared after ten seconds, there was no length other than 25, 50 or 5 minutes, and nothing remembered the day's sessions. This note is the design for both apps; the spec's Focus contract (`design specfication/…`, §5) stays as the baseline it diverges from.

## The finish

- When the countdown reaches zero the card itself changes: a border behind the content takes the **accent colour at 15%** (`FocusFinished` on Windows, `finishedTint` on the phone), the time reads **00:00**, the status says **"Done. Take a break. Tap to clear."**, and the progress bar is full.
- A **chime** plays through the PC's default output (`System.Media.SystemSounds.Asterisk`, no asset) when *Play a chime when a focus session ends* is on (Settings → Theme, on by default) and Quiet mode is off. The phone plays its own notification sound when the PC's state says the session just finished and the chime is on there; the phone has no setting of its own, as with the rest of Focus.
- The finish is **dismissed by a tap anywhere on the card** (`focus.dismiss` from the phone) or clears itself after **60 s** (`FocusTimer.FinishShownFor`). Starting or resetting the timer clears it too.
- The status line still says "Focus session complete." so a glance at the footer works as before.

## The fourth preset

- A fourth button, **"…"**, opens a picker inside the card: the length in large type, a 5–180 minute slider in 5-minute steps, and *Start with this length*. Choosing a length starts a session and keeps the length as the button's label (for example **45m**) in `AppSettings.FocusCustomMinutes`.
- Tapping the labelled button selects that length like the other presets; tapping it while it's already the current length opens the picker to change it.
- The phone's picker is the same panel (`focus.custom` sends the minutes; the PC starts the session and keeps the preset).

## Sessions today

- A finished session adds to `FocusSessionsToday` and `FocusMinutesToday` in `settings.xml`, keyed by `FocusSessionDate`; the first session of a new day starts the count again.
- The card shows **"3 sessions today · 1 h 40 m"** under the progress bar (`FocusTimer.SessionsText`, `Format.sessions`), nothing before the first session. The phone receives the numbers in the `focus` section.

## Paused

- A paused session (stopped with time left) dims the countdown to the Muted colour on both apps; the status still says "Paused".
