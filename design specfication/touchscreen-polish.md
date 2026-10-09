# Touchscreen polish — 9 October 2026

Refines the existing WPF dashboard for its 1920 × 720 touchscreen. The palettes, widget arrangements, account connections, local preferences and native implementation remain intact. The earlier as-built reference is a historical snapshot from before this pass.

## Changes

- Settings uses a 1440 × 672 panel with more room for Weather, system metrics and Spotify instructions. Save and a new explicit Cancel button stay visible in the footer; both Cancel controls discard the same draft.
- Settings inputs and ordinary buttons have 52 px minimum heights. Dropdown rows have 52 px minimum heights and support vertical touch panning. Checkbox labels wrap within their available width.
- Sliders have 48 × 48 thumb targets. Master volume, individual app volumes, text size and widget corners capture a finger gesture directly, so the containing list cannot steal a slider drag. Text size and corner controls retain their discrete steps. Playback keeps its existing single seek on release and uses the rendered track geometry.
- Scrollbars have a 40 px wide transparent interaction area with a narrower visible thumb. Lists retain swipe scrolling; disabled dropdowns are visually distinct.
- Supporting text increases from 14 to 15 design pixels before the existing 90–115% text setting. Compact Focus spacing accommodates the larger text, including a two-line completion message.
- Focus duration highlights the current selection and exposes that state in its accessible name. The main Settings action has a text label, separated from Close. Microphone and session mute controls retain at least 48 px height.
- Press-and-hold right-click emulation and stylus flick shortcuts are disabled for this dashboard. No new timers, visual animation, dependencies or polling were added.

All dimensions are design pixels; the existing Viewbox scales them in windowed previews. Actual touch comfort depends on the monitor's physical size and Windows display scaling.

## Verification

`scripts/build.ps1 -Test` passes, including the existing Spotify, deck, settings, layout and read-only integration checks. New checks exercise touch-to-value mapping against the actual WPF track, rapid value changes before relayout, endpoint clamping, tick snapping, direction changes, and the footer Cancel transaction.

Fourteen native WPF captures were produced at 1920 × 720 with isolated preferences, covering all settings tabs, three palettes, 115% text, paged launch buttons, compact widgets and layout editing. The confirmation captures cover the corrected settings header and compact Focus spacing. Captures are in `artifacts/polish/`. Impeccable's detector returned no findings; it does not validate native WPF behavior.

Physical touch, on-screen keyboard behavior and operation during exclusive fullscreen games have not been verified. Live Spotify playback was not altered by these checks.

## Hardware checklist

1. Open the updated build on the secondary monitor at 1920 × 720. Verify readability at normal viewing distance, then try Theme → Text size at 115%.
2. Tap and drag master and app volume controls from both the thumb and the track. Drag outside the control before releasing; confirm the gesture ends cleanly and the volume does not jump on the next tap.
3. Drag text size and corners in Settings. Confirm discrete steps, live preview and no accidental list scrolling. Open a long dropdown and swipe through its rows.
4. Swipe through Spotify and Quick Launch settings. Confirm Save and Cancel remain reachable. Change a theme and deck button, Cancel, reopen, and confirm the saved settings remain.
5. With a connected media app, seek by tapping and dragging. Confirm the label previews during the drag and playback seeks once on release.
6. Try 25m, 50m and 5m; confirm the selection indicator follows. In Edit layout, move and resize widgets, then Cancel to restore the saved arrangement.
