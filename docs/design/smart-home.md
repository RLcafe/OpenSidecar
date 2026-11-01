# Smart home

User request, 1 Nov 2026 (Windows and the phone). A Smart home card turns Home Assistant lights, switches and plugs on and off, dims lights and sets thermostats. It is hidden by default; turn it on in Edit layout's Widgets list.

## Home Assistant

Widget Screen talks to Home Assistant's REST API (`HomeAssistantService`) with a **long-lived access token** the user makes in their Home Assistant profile (Security → Long-lived access tokens). No add-on, integration or cloud account is needed, and nothing is installed on the Home Assistant side.

- **Settings → Home** (after Calendar): the address (`http://homeassistant.local:8123`, `192.168.1.20:8123`; http is assumed and any path is dropped), the token in a password box, Connect and Disconnect. Connect checks both with `GET /api/config` and says the name Home Assistant gives itself. Connecting and disconnecting take effect at once; the device choice is saved with Save changes.
- **The token** is kept only in `home-assistant.dat` next to `settings.xml`, encrypted with DPAPI for the Windows account, together with the address. It is never shown again, never written to `settings.xml`, never logged and never sent to the phone. Disconnect deletes the file.
- **Home network only.** Every address Home Assistant's name resolves to must be private (10/8, 172.16/12, 192.168/16, fc00::/7), link-local or loopback, the same rule as the phone link (`DashboardController.IsLocal`). It is checked before the first request and again every five minutes, so the token can't be sent to the internet even if a name later resolves elsewhere. Nabu Casa and other remote addresses are refused on purpose.
- **Devices:** after connecting, the right side lists every `light`, `switch` (smart plugs are switches in Home Assistant) and `climate` entity, by kind then name; tick up to 24. The chosen ids are kept in order in `AppSettings.HomeEntities`. Other domains (fans, covers, scenes, sensors) aren't offered; they'd each need their own controls.
- **Changes** go through Home Assistant's services: `<domain>/toggle` for lights and switches, `climate/turn_on`/`turn_off`, `light/turn_on` with `brightness_pct` (0 turns it off), and `climate/set_temperature` rounded to the device's step (`target_temp_step`, default 0.5) and kept within `min_temp`/`max_temp`. Entity ids are checked against the three domains and plain characters before any call.

## Reading

`GET /api/states` every ten seconds while the card is shown on the PC or a phone asks for it (twenty in Quiet mode), and right after every change so the card shows what really happened rather than guessing. Nothing is read while the card is hidden, nothing is chosen, or Home Assistant isn't connected. A failed read keeps the last states and the heading says NOT UPDATING (OFFLINE when nothing has been read yet), with the reason in its tooltip; a refused token says to make a new one.

## The card (`HomeCard`, `CardSizing.Home`)

| Design | Size | Shows |
| --- | --- | --- |
| Small | under 340 wide or 220 tall | Every device as a 56-unit row (glyph, name, state) in a list that scrolls; a tap turns it on or off |
| Medium | from 340 × 220 | Tiles at least 180 wide (`HomeGrid`): glyph beside the name over the state; a thermostat's − and + stacked beside its tile, or under it once the tile is `HomeTile` tall |
| Large | from 560 × 360 | The same tiles, with a brightness slider under dimmable lights when there's room |

On tiles the glyph and border take the accent and the selected fill when the device is on; an unavailable device is dimmed and can't be tapped. Glyphs: a bulb for lights, power for switches, a thermometer for thermostats (Segoe MDL2 on Windows, drawn in `Glyphs.java` on the phone). States read "On · 60%", "Off", "21.5° → 23°", "Unavailable". With more devices than fit, a 56-wide pager column with up and down arrows takes the right edge; few devices make the tiles grow to fill the card. The tile minimum heights follow the text size (`HomeContent`, `HomeCell`, `HomeTile`). The card's minimum is 240 × 152.

A dimmer sends its value when it's let go; while it's held the card doesn't redraw under the finger.

## The phone

The `home` section has `connected`, `chosen` (how many are ticked), `read`, `problem` and the chosen devices in order with id, name, domain, available, on, the PC's state text, dimmable, brightness, current and target temperature, min, max and step. `home.toggle {id}`, `home.brightness {id, value}` and `home.temperature {id, value}` change a device on the card; any other id is refused. `HomeWidget.java` draws the same three designs from the same numbers (`CardSizing.home`, `homeGrid`), and `HomeText` steps thermostats like the PC. Home Assistant is set up on the PC; the phone never learns its address or token.

## Not done, and why

- **OAuth sign-in** to Home Assistant instead of a token: it needs a browser redirect back to the app, and a long-lived token is what Home Assistant suggests for this kind of local tool.
- **Live updates over Home Assistant's WebSocket**: the ten-second poll is light and keeps one simple request path.
- **Talking to Home Assistant from the phone directly**: it would need the token on the phone; the PC already is the phone's bridge.

## Checks

`tests/Home/HomeTests.cs`: parsing states (kinds kept, brightness as a percentage, thermostat range and step, unavailable devices, broken answers); the home-network rule (private and loopback allowed, public addresses refused before anything is sent); a fake Home Assistant on loopback that checks the Bearer token, refuses a wrong one, and records every service call (toggle, thermostat off, brightness 40 and 0, temperature rounded to the step and clamped); the token not readable in the stored file; bad entity ids refused; the card at six sizes at 140% text with everything inside and 48-unit buttons, pictures saved to `artifacts\tests\home-*.png`; and Settings → Home connecting, listing, saving the choice and a tile tap reaching Home Assistant, then Disconnect forgetting it. The phone-link checks read the section and refuse an unknown device. `AndroidTests.java` covers parsing, steps, wording and the grid.

Not tried: a real Home Assistant (none is on the development network); the fake one answers the same endpoints with the same shapes.
