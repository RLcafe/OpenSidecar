# Phone-native polish (22 Oct 2026, from the app audit)

The phone is a faithful mirror of the PC; this note is the behaviour that is specific to being a phone. The PC side needs nothing new for any of it, except where noted.

- **Offline card.** In the upright list, a card at the top says "Can't reach <PC>" with the reason, when the next try is (5 s, 15 s, then every minute, from `PcLink.retryDelayMs`), a *Try now* button and a *Settings → PC* button. While it shows, the status line stays quiet. The sideways dashboard keeps the status line, which is where the PC shows the same thing.
- **Firewall hint.** When the first reading after pairing fails (`Pc.justPaired()` and one failure), the status line says to allow Widget Screen on private networks in Windows Firewall: that is where the problem almost always is, and the PC's own advice is on a screen the user isn't looking at.
- **Edit layout.** While a finger drags a card, `WidgetCanvas.Chrome` draws the card's size and place in an accent label at the top of the card, away from the finger. The Widgets panel has **Move** and **Size** arrows that nudge the selected card by 8 units with Snap on, 1 without (`LayoutEditor.nudgeSelected`, the Windows editor's arrow keys). Each nudge is one undo step.
- **Keep the screen on** is Always, While charging or Never (`AppSettings.keepScreenOn`, `keepsScreenOn(charging)`). New installs get While charging; an older file's `KeepScreenOn` true/false becomes Always/Never. `MainActivity` reads the sticky `ACTION_BATTERY_CHANGED` broadcast while started and re-applies the window flag when charging changes.
- **Haptics.** `Ui.haptic` wraps every button's click with `performHapticFeedback(CONTEXT_CLICK)`; the system's haptics setting can turn it off.
- **Back.** On Android 13+ the manifest enables `enableOnBackInvokedCallback` and `MainActivity` registers an `OnBackInvokedCallback` that runs the same `Dashboard.onBack` (close the confirm sheet, Settings, the Widgets panel or the editor; otherwise the task goes to the back). Older versions still use `onBackPressed`.

## Not done, and why

- **QR pairing** would need a QR decoder on the phone; Android has none built in, and the project takes no libraries. *Find PCs on this Wi-Fi* now broadcasts on each subnet (branch 2), which removes most of the typing.
- **List cards that wrap their content.** The cards' insides use weights to fill fixed heights (the mixer's app list, Spotify's results); letting the list rows wrap would collapse those. The fixed heights stay.
- **Home-screen widget** is its own piece of work (a `AppWidgetProvider` with its own polling) and stays a later branch.
