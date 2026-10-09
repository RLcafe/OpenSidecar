# Sound mixer output devices (9 Oct 2026)

This note updates spec §4.5 for the mixer output features. Decision record [005](../decisions/005-mixer-output-routing.md) covers the Windows APIs and tradeoffs.

## Changes from §4.5

- **Applications list.** Sessions come from every active output, not only the default. An app appears once, on the output it plays on. System sounds appears for the default output only. The count and the empty and unavailable texts are unchanged.
- **Master metadata row (66).** *Master volume* and the device name are now one transparent button (`OutputDeviceButton`, 66 px tall, offset 8 px left so the text stays aligned) with a Segoe MDL2 chevron `E70D` after the device name. It opens the master output list. It is disabled while audio is unavailable.
- **Session row (74).** Columns are 48 / flexible / 48 / 52 (was 48 / flexible / 48). The percent spans the last two columns. A new output button sits in row 2, column 4, 48 × 48 with a 4 px left margin, glyph `E7F5` speakers, `E7F6` headphones or headset, `E7F4` display audio. Its tooltip and accessible name give the output: "Output for Spotify: Headphones". When an app plays on a non-default output, its label continues with "  ·  " and the output name in Muted.
- **Output list.** `OutputPicker` covers mixer rows 1 to 5 (the SOUND MIXER header stays visible) on the Surface brush. Header: SmallSize label (MASTER OUTPUT or APP OUTPUT), TitleSize subject, 48 px close button `E8BB`. Choices are standard buttons at least 52 px tall with an Accent check `E73E` and Accent border on the current choice, and an optional SmallSize detail line. The list scrolls inside the card, opens scrolled to the current choice, and has a SmallSize note under it. Choosing the current entry only closes the list. Opening Settings or the layout editor closes it.
- **App list contents.** *Windows default* (detail: "Same as master · <output>") and then every active output in Windows order. The output the app plays on now says "Playing here now". Choices are disabled for System sounds or when Windows doesn't offer per-app routing, and the note explains why.
- **Footer messages.** "Sound now plays on <output>.", "<app> now plays on <output>.", "<app> now follows the master output.", and failure messages that point to the gear.
