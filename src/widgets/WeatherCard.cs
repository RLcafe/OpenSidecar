using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WidgetScreen
{
    // Weather in three designs that follow the card's size (CardSizing.Weather; docs/design/widget-size-views.md),
    // dressed as a sticker sheet (phase 3, docs/design/sticker-sheet.md): the reading sits on a sky band tinted by the
    // condition and the time of day, with the condition glyph on a die-cut sticker. Small is a glance (temperature and
    // glyph, city, high and low); Medium is today (the reading, the condition and city, and Feels, Humidity and Rain
    // chips, plus the 12-hour curve on a tall card); Large adds more chips and a forecast row with the 12-hour
    // temperature curve over rain bars and the sunrise–sunset arc. The last reading lives here, so a rebuilt design
    // starts full. The phone's WeatherWidget.java draws the same three.
    public sealed class WeatherCard : CardParts
    {
        private readonly Grid host;
        private readonly PartNames names;
        private Rect bounds = Rect.Empty;
        private string built = "";
        private CardSizing.Design design;
        // What the card shows: the city, the last reading, and whether an update is running or failed.
        private string location = "";
        private WeatherSnapshot last;
        private bool busy, offline, imperial, use24;
        private string empty = "Tap to choose a city.";
        private TextBlock temperature, city, condition, described, message, credit, sunrise, sunset;
        private ContentControl glyph;
        private Rectangle sky;
        private UniformGrid chips;
        private int chipCount;
        private TemperatureCurve curve;
        private FrameworkElement curveFrame, arcFrame, values;
        // Not set up, or no reading because the service can't be reached: a dashed outline with the reason.
        private Grid blank, middle;
        private SunArc arc;
        private double glyphSize;

        public WeatherCard(FrameworkElement scope, Grid host) { this.host = host; names = new PartNames(scope); TextScale = 1; }

        public CardSizing.Design Current { get { return design; } }
        // The label strip of the Medium and Large designs (Small has no heading), and the text size it follows.
        public LabelStrip Strip { get; private set; }
        public double TextScale { get; set; }

        public void Fit(Rect card)
        {
            bounds = card;
            string key = Math.Round(card.Width) + "x" + Math.Round(card.Height);
            if (Strip != null) Strip.Fit(card.Width, TextScale);
            if (key == built) return;
            built = key; Build(); Render();
        }

        // The city and units chosen in Settings → Weather; a new city or unit drops the old reading.
        public void Configure(string location, bool imperial, bool use24)
        {
            location = location ?? "";
            if (location != this.location || imperial != this.imperial) { last = null; offline = false; }
            this.location = location; this.imperial = imperial; this.use24 = use24;
            Render();
        }
        public void Updating() { busy = true; Render(); }
        public void Reading(WeatherSnapshot snapshot) { busy = false; offline = false; last = snapshot; Render(); }
        public void Failed() { busy = false; offline = true; Render(); }
        public void Forget() { busy = false; offline = false; last = null; Render(); }
        public WeatherSnapshot Last { get { return last; } }

        private void Build()
        {
            host.Children.Clear(); names.Clear(); chips = null; middle = null; curve = null; curveFrame = arcFrame = null; arc = null; sunrise = sunset = null; condition = null; described = null; Strip = null;
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48);
            design = CardSizing.Weather(bounds.Width, bounds.Height);
            FrameworkElement view = design == CardSizing.Design.Small ? BuildGlance(width, height) : BuildToday(width, height, design == CardSizing.Design.Large);
            host.Children.Add(names.Add(view, "WeatherView"));
        }

        // The sky band behind the reading: a rounded panel in the sky's ink fading downwards, the content over it.
        private Grid SkyBand(UIElement content, Thickness padding)
        {
            var band = new Grid();
            sky = names.Add(new Rectangle { RadiusX = 16, RadiusY = 16, OpacityMask = Fade, IsHitTestVisible = false }, "WeatherSky");
            band.Children.Add(sky);
            var inner = new Border { Padding = padding, Child = content };
            band.Children.Add(inner);
            return band;
        }
        private static readonly Brush Fade = MakeFade();
        private static Brush MakeFade()
        {
            var brush = new LinearGradientBrush(Colors.Black, Color.FromArgb(0x10, 0, 0, 0), 90); brush.Freeze();
            return brush;
        }

        // The condition glyph on a die-cut disc sticker in the sky's ink, drawn in dark text.
        private ContentControl GlyphSticker(double size)
        {
            return new ContentControl { Width = size + Stickers.Keyline, Height = size + Stickers.Lift + Stickers.Keyline, VerticalAlignment = VerticalAlignment.Center, Focusable = false, IsTabStop = false };
        }

        // Small: no heading; the temperature and glyph sticker on the sky band, then the city, high and low, and the credit.
        private FrameworkElement BuildGlance(double width, double height)
        {
            bool detail = height >= 136;
            var view = Rows(Star, Auto, Auto, Auto);
            double size = CardSizing.WeatherTemperature(CardSizing.Design.Small, bounds.Width, height - 26 - (detail ? 20 : 0) - 20 - 8);
            var top = Columns(Star, Auto);
            temperature = names.Add(Reading("—°", size), "WeatherTemperature"); temperature.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(temperature);
            glyphSize = Math.Max(28, Math.Min(64, Math.Round(size * 0.8)));
            glyph = names.Add(GlyphSticker(glyphSize), "WeatherGlyph");
            top.Children.Add(At(glyph, 0, 1));
            var band = SkyBand(top, new Thickness(10, 4, 10, 4));
            values = band;
            view.Children.Add(band);
            city = names.Add(Text("", "BodySize", "Text"), "WeatherCity"); city.Margin = new Thickness(0, 4, 0, 0); view.Children.Add(At(city, 1));
            message = Caption(""); message.Margin = new Thickness(0, 4, 0, 0); if (detail) view.Children.Add(At(message, 2));
            credit = names.Add(Caption(""), "WeatherCredit"); credit.Margin = new Thickness(0, 4, 0, 0); view.Children.Add(At(credit, 3));
            view.Children.Add(Blank(0, 3));
            return view;
        }

        // Medium and Large: a heading; the glyph sticker and temperature beside the condition and city on the sky band;
        // a rail of chips; on Large (and a tall Medium) the 12-hour curve, beside the sun arc on Large; and the credit.
        private FrameworkElement BuildToday(double width, double height, bool large)
        {
            bool showCurve = large || height >= 300 - 48;
            bool showArc = large && width >= 380;
            var view = Rows(Auto, Star, Auto, Auto, Auto);
            // The label strip and the condition as a status sticker (docs/design/sticker-sheet.md).
            var header = Columns(Auto, Star); header.MinHeight = Stickers.StripHeight;
            Strip = LabelStrip.Create("Weather"); Strip.Fit(bounds.Width, TextScale); header.Children.Add(names.Add(Strip.Host, "WeatherStrip"));
            condition = names.Add(Caption(""), "WeatherCondition"); condition.HorizontalAlignment = HorizontalAlignment.Right; condition.Margin = new Thickness(12, 0, 0, 0);
            header.Children.Add(At(condition, 0, 1));
            StatusSticker.Wrap(condition, "Weather", false);
            view.Children.Add(header);
            double padY = large ? 6 : 4;
            double chart = large ? Math.Max(92, Math.Min(150, Math.Round((height - Stickers.StripHeight - 65 - 21) * 0.36))) : 64;
            double curveHeight = !showCurve ? 0 : chart + (large ? 10 : 8);
            double size = CardSizing.WeatherTemperature(design, bounds.Width, height - Stickers.StripHeight - padY * 2 - 65 - curveHeight - 21);
            glyphSize = Math.Round(size * (large ? 0.8 : 0.8));
            var hero = Columns(Auto, Auto, Star); hero.VerticalAlignment = VerticalAlignment.Center;
            glyph = names.Add(GlyphSticker(glyphSize), "WeatherGlyph"); glyph.Margin = new Thickness(0, 0, large ? 18 : 14, 0);
            hero.Children.Add(glyph);
            temperature = names.Add(Reading("—°", size), "WeatherTemperature"); temperature.VerticalAlignment = VerticalAlignment.Center;
            hero.Children.Add(At(temperature, 0, 1));
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(large ? 18 : 14, 0, 0, 0) };
            described = Text("", large ? "TitleSize" : "BodySize", "Text"); described.TextTrimming = TextTrimming.CharacterEllipsis; words.Children.Add(described);
            city = names.Add(Text("", large ? "BodySize" : "SmallSize", "Text"), "WeatherCity"); city.TextWrapping = TextWrapping.Wrap; city.MaxHeight = large ? 54 : 44; city.Margin = new Thickness(0, 4, 0, 0); city.Opacity = 0.78;
            words.Children.Add(city);
            hero.Children.Add(At(words, 0, 2));
            var band = SkyBand(hero, new Thickness(12, padY, 12, padY));
            view.Children.Add(At(band, 1));
            // The chips, or in their place a message while there is nothing to show.
            middle = new Grid { MinHeight = 57, Margin = new Thickness(0, 8, 0, 0) };
            chipCount = large ? CardSizing.WeatherChips(bounds.Width) : 3;
            chips = names.Add(new UniformGrid { Rows = 1 }, "WeatherChips"); middle.Children.Add(chips);
            message = Text("", "SmallSize", "Muted"); message.TextWrapping = TextWrapping.Wrap; message.MaxHeight = 57; message.VerticalAlignment = VerticalAlignment.Center;
            middle.Children.Add(message);
            view.Children.Add(At(middle, 2));
            if (showCurve)
            {
                curve = names.Add(new TemperatureCurve(), "WeatherCurve");
                var row = Columns(Star, Auto); row.Height = chart; row.Margin = new Thickness(0, large ? 10 : 8, 0, 0);
                if (large) { curveFrame = Tile(curve, 16, new Thickness(8, 6, 8, 6)); }
                else curveFrame = curve;
                row.Children.Add(curveFrame);
                if (showArc)
                {
                    arc = names.Add(new SunArc { Height = 46 }, "WeatherSunArc");
                    var times = Columns(Star, Star);
                    sunrise = Caption(""); sunset = Caption(""); sunset.HorizontalAlignment = HorizontalAlignment.Right;
                    times.Children.Add(sunrise); times.Children.Add(At(sunset, 0, 1));
                    arc.Height = Math.Max(46, chart - 12 - 22); var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; stack.Children.Add(arc); stack.Children.Add(times);
                    var tile = Tile(stack, 16, new Thickness(10, 6, 10, 6)); tile.Width = 150; tile.Margin = new Thickness(10, 0, 0, 0);
                    arcFrame = tile;
                    row.Children.Add(At(tile, 0, 1));
                }
                view.Children.Add(At(row, 3));
            }
            credit = names.Add(Caption(""), "WeatherCredit"); credit.VerticalAlignment = VerticalAlignment.Bottom; credit.Margin = new Thickness(0, 4, 0, 0);
            view.Children.Add(At(credit, 4));
            values = band;
            view.Children.Add(Blank(1, 3));
            return view;
        }
        private Grid Blank(int row, int span)
        {
            blank = names.Add(Stickers.Unavailable(""), "WeatherUnavailable"); blank.Visibility = Visibility.Collapsed; blank.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(blank, row); Grid.SetRowSpan(blank, span);
            return blank;
        }

        private void Render()
        {
            if (temperature == null) return;
            bool hasCity = location.Length > 0, reading = last != null && !offline;
            temperature.Text = last != null ? Math.Round(last.Temperature) + "°" : "—°";
            city.Text = hasCity ? location : "No city yet";
            DateTime now = WeatherService.CityNow(last, DateTime.UtcNow);
            bool day = last == null || WeatherService.IsDay(now, last.Sunrise, last.Sunset);
            string family = last != null ? WeatherService.Family(last.Code) : null;
            string ink = WeatherService.SkyInk(family ?? "cloud", day);
            sky.SetResourceReference(Shape.FillProperty, "Ink" + ink);
            sky.Opacity = last == null ? 0.12 : WeatherService.SkyStrength(family, day);
            glyph.Content = family == null ? null : Sticker(family, ink);
            Stickers.Stale(values, offline && last != null);
            // No city, or the service unreachable with nothing to show: the reason in a dashed outline instead of the reading.
            bool unset = !hasCity || (offline && last == null);
            blank.Visibility = unset ? Visibility.Visible : Visibility.Collapsed;
            ((TextBlock)blank.Children[1]).Text = !hasCity ? "No city yet. Tap the card to choose one." : "Can't reach the weather service. Tap the card to try again.";
            values.Visibility = unset ? Visibility.Hidden : Visibility.Visible;
            if (design == CardSizing.Design.Small) city.Visibility = message.Visibility = unset ? Visibility.Hidden : Visibility.Visible;
            if (middle != null) middle.Visibility = unset ? Visibility.Hidden : Visibility.Visible;
            if (condition != null)
            {
                condition.Text = offline ? "OFFLINE" : "";
                condition.SetResourceReference(TextBlock.ForegroundProperty, offline ? "Warning" : "Muted");
                described.Text = last != null ? WeatherService.Describe(last.Code) : "";
                described.Visibility = described.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            string note = !hasCity ? empty
                : offline ? (last == null ? "Can't reach the weather service. Tap the card to try again." : null)
                : last == null ? "Updating weather…" : null;
            if (design == CardSizing.Design.Small)
            {
                message.Text = note != null ? (offline ? "Offline. Tap to try again." : note) : offline ? "Offline: last update shown" : HighLow(last) ?? WeatherService.Describe(last.Code);
                message.SetResourceReference(TextBlock.ForegroundProperty, offline ? "Warning" : "Muted");
            }
            else
            {
                message.Text = note ?? ""; message.Visibility = note != null ? Visibility.Visible : Visibility.Collapsed;
                chips.Visibility = note == null ? Visibility.Visible : Visibility.Collapsed;
                Chips(note == null ? last : null);
                Stickers.Stale(chips, offline && last != null);
                Forecast(last, now);
            }
            credit.Text = last != null ? "Open-Meteo  ·  " + (busy ? "updating…" : (offline ? "last update " : "updated ") + last.Retrieved.ToString(use24 ? "HH:mm" : "h:mm tt", System.Globalization.CultureInfo.InvariantCulture))
                : busy ? "Open-Meteo  ·  updating…" : "Weather data by Open-Meteo";
        }
        // The glyph sticker: a disc in the sky's ink with the condition drawn in dark text over it.
        private Grid Sticker(string family, string ink)
        {
            var grid = new Grid();
            grid.Children.Add(new StickerShape(Stickers.Disc(glyphSize), "Ink" + ink) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(Stickers.Keyline / 2, Stickers.Keyline / 2, 0, 0) });
            double mark = Math.Round(glyphSize * 0.62);
            var drawn = WeatherGlyph(family, mark, "InkText");
            drawn.HorizontalAlignment = HorizontalAlignment.Left; drawn.VerticalAlignment = VerticalAlignment.Top;
            drawn.Margin = new Thickness(Stickers.Keyline / 2 + (glyphSize - mark) / 2, Stickers.Keyline / 2 + (glyphSize - mark) / 2, 0, 0);
            grid.Children.Add(drawn);
            return grid;
        }
        private static string HighLow(WeatherSnapshot snapshot)
        {
            return snapshot != null && snapshot.High.HasValue && snapshot.Low.HasValue ? "↑ " + Math.Round(snapshot.High.Value) + "°   ↓ " + Math.Round(snapshot.Low.Value) + "°" : null;
        }
        // The fact rail: Feels, Humidity, Rain and, on Large, UV, Wind and High / Low as far as they fit.
        private void Chips(WeatherSnapshot snapshot)
        {
            chips.Children.Clear();
            if (snapshot == null) return;
            var items = WeatherService.Chips(snapshot, imperial, chipCount);
            chips.Columns = Math.Max(1, items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                TextBlock value;
                var chip = Stickers.FactChip(items[i][0], items[i][1], out value);
                chip.Margin = new Thickness(i == 0 ? 0 : 4, 0, i == items.Length - 1 ? 0 : 4, 0);
                chips.Children.Add(chip);
            }
        }
        // The next 12 hours as a curve over rain bars (labels every three hours), and the sun's place between sunrise and sunset.
        private void Forecast(WeatherSnapshot snapshot, DateTime now)
        {
            if (curve != null)
            {
                bool any = snapshot != null && snapshot.Curve.Count >= 2;
                curveFrame.Visibility = any ? Visibility.Visible : Visibility.Hidden;
                if (any)
                {
                    var hours = snapshot.Curve;
                    curve.Show(hours.Select(h => h.Temperature).ToArray(), hours.Select(h => h.Rain).ToArray(),
                        hours.Select((h, i) => i == 0 ? "Now" : i % 3 == 0 ? WeatherService.HourLabel(h.Time, use24) : null).ToArray());
                }
            }
            if (arc != null)
            {
                double? part = snapshot == null ? null : WeatherService.SunPart(now, snapshot.Sunrise, snapshot.Sunset);
                arcFrame.Visibility = part.HasValue ? Visibility.Visible : Visibility.Collapsed;
                arc.Part = part ?? Double.NaN;
                sunrise.Text = part.HasValue ? "↑ " + WeatherService.ClockTime(snapshot.Sunrise.Value, use24) : "";
                sunset.Text = part.HasValue ? "↓ " + WeatherService.ClockTime(snapshot.Sunset.Value, use24) : "";
            }
        }
    }
}
