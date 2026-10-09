# 005 — Sound mixer output devices

The user asked for three things in the Sound mixer: show every app's volume slider whatever output it plays on, a button on each app to see and change its output, and a way to change the master output from the top of the widget.

## Decision

- **All outputs.** `AudioService.Read` enumerates every active render endpoint (`IMMDeviceEnumerator.EnumAudioEndpoints`) and reads the sessions on each, default output first. App volume and mute still use each session's own `ISimpleAudioVolume`.
- **One row per app where it plays.** Windows keeps an idle session on the old output after an app moves, and a System sounds session on every output. `AudioService.VisibleSessions` shows an app's idle sessions only when it isn't playing elsewhere (and, if it is idle on several outputs, only on the output it is routed to), and shows System sounds on the default output only. Sessions of one process on one output are still not merged.
- **Master output.** Tapping *Master volume* and the device name opens an in-card list of outputs. Choosing one calls `IPolicyConfig::SetDefaultEndpoint` for the console, multimedia and communications roles, like the Windows volume flyout. The gear still opens Windows sound settings.
- **Per-app output.** Each app row has a 48 × 48 output button (speaker, headphone or display glyph from the endpoint form factor). It opens the same in-card list with *Windows default* plus every output; the app's current output is marked *Playing here now*. Choosing one calls `IAudioPolicyConfigFactory::SetPersistedDefaultAudioEndpoint` for the app's process (console and multimedia roles); *Windows default* clears it. Windows stores the choice per app, as on Settings → Sound → Volume mixer. An app on a non-default output also shows the output name after its own name.
- **No popup window.** The list is a panel inside the mixer card (`OutputPicker`), so it scales with the dashboard `Viewbox`, needs no separate window over a fullscreen game, and closes when layout editing or Settings opens.

## Rationale and tradeoffs

Windows has no documented API for changing the default output or an app's output. `IPolicyConfig` (CLSID `870AF99C-…`, IID `F8679F50-…`) has been stable since Windows 7 and is what common output switchers use. `Windows.Media.Internal.AudioPolicyConfig` is the WinRT factory behind Windows' own per-app device page and is what EarTrumpet uses. It is reached with `RoGetActivationFactory`, trying IID `ab3d4648-…` (Windows 10 21H2 and Windows 11) and then `2a59116d-…` (earlier Windows 10). The set/get methods are called through vtable slots 25 and 26, and device ids are wrapped as `\\?\SWD#MMDEVAPI#{id}#{e6327cad-dcec-4949-ae8a-991e976a79d2}`.

- A future Windows build could change the undocumented layout. If the factory can't be activated, the picker shows the outputs read-only and points to Windows sound settings; master volume and app sliders keep working. A wrong vtable slot can't be caught, so the read-only check in the tests (`AppOutput` for the test process) is the canary to run after a Windows feature update.
- Some apps (for example ones using the legacy `PlaySound` path) keep their open stream on the old output until they restart. The picker says so. Modern apps generally move immediately.
- Reading every output's sessions costs a few extra in-process COM calls per audio refresh (every 2 s, or 5 s in Quiet mode). There is no new timer.
- System sounds can't be routed per app, so its picker is read-only.
- A rerouted app gets a new session on the new output. The dashboard reuses that app's existing row so it doesn't jump to the bottom of the list.
