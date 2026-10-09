# Baseline verification — 9 October 2026

## Completed

Built the actual current working-tree source as an isolated executable in `artifacts/specification`, using:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Test -OutputDirectory artifacts\specification
```

Build exited 0. **All 47 automated checks passed.** The suite includes:

- Native XAML/template loading; XML defaults, normalization, round-trip, atomic replacement and malformed-file preservation.
- Theme preview/cancel, cross-tab saving, free layout migration, minimum sizes, corner transforms, bounds, undo/redo, hide/show, reset persistence and failed-save recovery.
- Quick Launch validation, migration, arbitrary/empty decks, transactions, reorder, failed-save handling and compact pagination at 115% text.
- Spotify HTTP fixtures, PKCE/callback validation, encrypted credentials, restart restoration, token refresh, cancellation/in-flight generation handling, error/rate-limit states, inline browsing, likes and seeking guards.
- Shared scrubber preview/commit behavior, timer boundary behavior, weather parsing and GPU aggregation.
- Read-only live CPU/RAM/GPU, Windows output/session enumeration and Windows current media-session access. These read checks succeeded on this host. No real playback, liked-song or volume changes were performed by this suite.

Rendered eleven 1920 × 720 PNG references with the documentation exporter and visually inspected all eleven. References use inactive integrations and labelled display fixtures; the exporter does not open a visible app window or use personal preferences. The fixed-size dashboard, six Settings tabs, edit toolbar, alternate palettes/text scale and optional widgets are represented.

Checked the document's geometry, settings defaults, color values, control templates, API paths, polling intervals and state transitions against the source. Included literal XAML and a hashed source archive to preserve details beyond the prose. Validated local documentation links, image dimensions, source archive entries and SHA-256 hashes.

## Not established by these checks

No new live Spotify authorization, account allowlist/Premium verification, real library edits, live weather request, physical touch test, screen unplug/replug test, exclusive fullscreen game test, or CPU/GPU overhead benchmark was performed for this documentation task. External policies, account state, drivers and Windows rendering differences can affect runtime results. Existing integration/manual test procedures remain relevant.

This task changes documentation and adds a reference-export helper; it does not modify application behavior. Existing uncommitted application work is captured as the baseline and preserved.
