# Dashboard polish — 2026-10-08

Scope: refine the existing native Windows dashboard, retaining its dark surfaces, accent choices, widget arrangement, and Segoe UI/MDL2 icon system.

## Changes

- Corrected the system panel's row heights so network rates and uptime fit at 100% and 115% text size.
- Improved secondary-text contrast; tinted the clock's supporting text to its green surface.
- Made control focus visible and consistent, enlarged slider-thumb hit areas, and styled checkboxes, dropdown indicators, and scrollbars to match the dashboard.
- Kept Settings keyboard navigation inside its overlay and disabled the dashboard beneath it. Set location now brings the city field into view.
- Added full-title tooltips, consistent album artwork clipping, and stable-width time numerals.
- Cleared stale media/system readings on failure, distinguished unavailable application-volume access from unavailable output, and stopped the level display from claiming to be live when audio is unavailable.
- Prevented late city-search results from reopening dropdowns after Settings closes.
- Fixed XML deserialization that appended shortcuts to defaults and then reset them. Existing settings XML remains compatible.

## Verification

Ten smoke checks pass, including native XAML template loading, repeat saves/loads of edited shortcuts, and live read-only Windows audio/media access. Windows integration tests must run with ordinary desktop access; sandbox access denial is not evidence of a disconnected device.

`tests/render-checks.ps1` renders normal layout, 115% text with swapped columns, and Settings with isolated preferences. Generated captures remain under ignored `artifacts/polish/`.

The live window was inspected and Settings was opened through Windows automation. The remaining keyboard interaction check was not confirmed after the automation tool detected concurrent user input; no further automated input was sent.

No physical touchscreen gestures or exclusive-fullscreen game behavior have been verified. The web-oriented Impeccable detector is not a validator for this WPF/XAML interface; verification uses native rendering and Windows interaction instead.
