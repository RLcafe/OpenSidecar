# 011 — Audio on its own thread, and readings shared with the phone

The app audit of 22 Oct 2026 found the sound mixer's cost concentrated in one place: every 2 s the dashboard enumerated every output, opened two property stores per output, enumerated the sessions of every output and asked the routing API where idle apps were, on the UI thread; a connected phone made the PC do all of it again for its own `/state`. The user asked for the audit's recommendations to be implemented.

## Decision

- **One audio thread.** `AudioWorker` owns the `AudioService` on a background MTA thread and answers every call as a `Task`, in order. The dashboard only awaits: `RefreshAudio`, the mixer's buttons and sliders, the output pickers, the level meter's 100 ms peak and the phone's commands all go through it. `AudioService` itself is unchanged in what it does and is still used directly by the live checks.
- **Device notifications.** `AudioService` registers an `IMMNotificationClient`. When an output appears, leaves, changes state or becomes the default it drops its name cache and raises `DevicesChanged`; the dashboard reads the mixer right away instead of at the next beat. Session-level events (`IAudioSessionEvents`) were *not* adopted: they need a session manager and a sink kept alive per output and per session, which is a lot of COM lifetime for a change the 2 s poll already catches.
- **Cached output names.** Friendly names and form factors are kept by device id until a notification arrives, so a read no longer opens property stores.
- **Shared readings.** The dashboard and a phone's `/state` share the last audio, system and Now Playing reading for one second (`DashboardController.SnapshotLifetime`), and a read asked for while one is in flight joins it. A change made from either side reads fresh.
- **Order.** `AudioService.Ordered` lists playing apps first, idle apps next and System sounds last, on the PC and in what the phone receives; rows are reordered in place, never rebuilt.
- **Level meter.** The meter sits under the master slider and its row collapses when levels are off, paused or the mixer is hidden; silence draws nothing. "Unavailable" is still said, because that is something wrong.
- **Cheaper housekeeping.** `SystemService` keeps the adapter list for 30 s and refreshes it on `NetworkChange` events; the phone's layout version is recomputed only after a settings save (`SaveSettingsFile` counts them), an edit or a screen change.
- **Android.** The phone has no audio enumeration of its own; it inherits the ordering from the PC, and slider moves no longer refresh every card (`Dashboard.commandQuietly`), which removed the mid-drag flicker.

## Tradeoffs

- Awaiting the worker means mixer changes from a button are applied a few milliseconds later than a direct call; nothing visible, and the UI never blocks on COM.
- The worker is a second thread the app keeps for its lifetime. It is idle between calls.
- A device notification storm (a dock being plugged in) collapses into one or two reads, because a read requested during a read runs once afterwards.
