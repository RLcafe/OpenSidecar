# Widget Screen design: the Sticker Sheet

The dashboard is a dark sheet of cards, glanceable from arm's length on a 1920 × 720 touchscreen while a game or app runs on the main screen. Each card is a sticker on that sheet: it has its own ink, says what it is in a bold label strip, and says the one thing that matters now in a status sticker. Everything else stays calm. The full note is [docs/design/sticker-sheet.md](docs/design/sticker-sheet.md), and the technical record is [decision 021](docs/decisions/021-sticker-sheet-inks.md). The as-built spec in `design specfication/` (§3.3–3.5) still defines the palettes and accents.

## Surfaces

- **Palettes:** Midnight, Slate or Warm. Each has its own Page, Surface, Raised, Input, Track, ControlBorder, Selected, Muted and Text colours, and the cards are Surface on Page.
- **System accent:** one of Lime, Glacier, Lavender and Amber. It colours controls: primary buttons, slider fills, selection and the focus ring.
- **Inks:** eight widget colours that sit on dark surfaces and carry dark text `#11170E` at 9:1 or better.

| Ink | Hex | Worn by default |
| --- | --- | --- |
| Lime | `#BAF28B` | Sound mixer, Games |
| Glacier | `#8ED8F8` | Quick Launch, Clipboard, Weather, World clocks, Smart home |
| Lavender | `#C5AEFF` | Soundboard, Storage |
| Amber | `#F5C47D` | Warnings only |
| Coral | `#FF9E85` | Focus, Calendar |
| Mint | `#7FE6C0` | Spotify, Prayer times |
| Rose | `#FFA6CF` | Now Playing, Macros |
| Lemon | `#F0E36E` | System stats, Notes |

- **Changing inks.** Edit layout changes a widget's ink. Settings → Theme → Widget colours → Accent only makes every ink the system accent.
- **Exception:** the Claude Code card keeps its own pit-wall design (decision 017).

## A card

1. **Label strip** at the top left: a 28-unit pill in the ink with the name in Segoe UI Black 13, letter-spaced, in dark text. Narrow cards (under 200) show an ink dot and a short name instead.
2. **Status sticker** at the top right: one short live fact in an outlined ink pill, filled when something is active right now.
3. **Body:** the card's readings and controls. The Light numerals and the touch sizes (48, and 52 in Settings) are unchanged.
4. **Hero sticker** (from phase 2): one die-cut shape per card with a light keyline and a solid lift in the Page colour. It tilts 3° at most.
5. **Fact-rail chips and a footline** (from phase 2): short labelled facts, and where the data came from and when.

## Saying how things are

- **Working:** the ink.
- **Warning:** Amber with caution stripes and a glyph. Never colour alone.
- **Unavailable or not set up:** a dashed outline and the reason in plain words.
- **Offline or stale:** a stamp, with the last values at 60%.
- **Loading:** a static outline with "…". Nothing spins.

## Rules

- **Fonts and icons:** Segoe UI (Light for numerals, Black for strips, Semibold for values) and Segoe MDL2 glyphs. Android uses sans-serif-light, -black and -medium with glyphs drawn in code. No images or emoji for icons, and no custom fonts.
- **No shadows, blurs or effects.** Depth is a solid offset copy.
- **No decorative or continuous animation.** Only press feedback and one-shot state changes, all off in Quiet mode.
- **No new timers or polling for looks.** Drawn parts are built once per size and frozen.
- **The dashboard never scrolls.** Text goes up to 140%, captions never go below 13, and touch targets keep their minimums.
