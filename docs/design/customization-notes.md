# Dashboard customization — 2026-10-08

The column sizing approach described here was superseded by [live layout editing](live-layout-notes.md) on 2026-10-09. This note records the earlier implementation.

This extends the existing native WPF dashboard and preserves its Segoe UI typography, MDL2 icons, dark surfaces, touch controls, and four-column arrangement. Settings remains a focused overlay, with persistent Save and Cancel actions and four tabs: Theme, Display, Widgets, and Shortcuts & Weather.

Theme provides three complete palettes (Midnight, Slate, Warm), the existing four accent colors, adjustable card corners, and 90–115% text scaling. Changes preview immediately; closing the overlay restores the saved appearance. Display separates monitor selection and fullscreen launch behavior from appearance.

Widget sizing divides the fixed 1856 × 576 content area. Column width preferences distribute 220 px beyond minimum widths that protect existing controls. Stacked widgets share width and split height; the mixer has independent height. Quick-launch buttons become horizontal at smaller heights. Size labels display actual resulting dimensions. Reset affects size preferences only. The system column's minimum height is 326 px, matching the verified large-text layout, so uptime stays inside the card.

CPU, GPU, RAM, network, and uptime can be selected independently. Turning off all metrics hides System Pulse and gives its column to weather. GPU readings aggregate processes per physical engine and show the busiest engine across adapters. Missing counter support is visibly unavailable, without suppressing CPU or RAM. Sampling runs on a worker thread, every two seconds or five in Quiet mode; hidden metrics and an entirely hidden system widget do not poll.

Verification: 14 automated checks pass, including XML migration/round trips, preview cancellation, cross-tab saving, metric visibility, layout bounds, GPU aggregation, and read-only live Windows integrations. GPU usage was read successfully on this PC. The native render suite covers eight 1920 × 720 cases: normal dashboard, 115% text with swapped columns, all four tabs, resized widgets, and all stats hidden. The initial sandbox captures also exercised unavailable GPU/media/session states. A second pass with desktop access confirmed live GPU/media readings and compact shortcut labels. Physical touchscreen gestures and exclusive-fullscreen game behavior remain unverified.

No new packages, accounts, or background services are introduced. The web-oriented Impeccable detector does not validate WPF; verification uses native rendering and control interaction checks.
