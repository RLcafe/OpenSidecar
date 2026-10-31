using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WidgetScreen
{
    // Weather in three designs that follow the card's size (CardSizing.Weather; docs/design/widget-size-views.md).
    // Small is a glance (temperature and glyph, city, high and low); Medium is today (a large glyph and temperature,
    // the condition and city, and Feels, Wind and High / Low as three facts, plus three hours on a tall card); Large is
    // the forecast (the same, larger, with five hours in a strip). The last reading lives here, so a rebuilt design
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
        private TextBlock temperature, city, condition, described, message, credit;
        private ContentControl glyph;
        private Grid facts;
        private UniformGrid hours;
        private FrameworkElement hoursFrame;
        private double glyphSize;

        public WeatherCard(FrameworkElement scope, Grid host) { this.host = host; names = new PartNames(scope); }

        public CardSizing.Design Current { get { return design; } }

        public void Fit(Rect card)
        {
            bounds = card;
            string key = Math.Round(card.Width) + "x" + Math.Round(card.Height);
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
            host.Children.Clear(); names.Clear(); facts = null; hours = null; hoursFrame = null; condition = null; described = null;
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48);
            design = CardSizing.Weather(bounds.Width, bounds.Height);
            FrameworkElement view = design == CardSizing.Design.Small ? BuildGlance(width, height) : BuildToday(width, height, design == CardSizing.Design.Large);
            host.Children.Add(names.Add(view, "WeatherView"));
        }

        // Small: no heading; the temperature and glyph fill the top, the city, high and low, and the credit follow.
        private FrameworkElement BuildGlance(double width, double height)
        {
            bool detail = height >= 136;
            var view = Rows(Star, Auto, Auto, Auto);
            double size = CardSizing.WeatherTemperature(CardSizing.Design.Small, bounds.Width, height - 26 - (detail ? 20 : 0) - 20);
            var top = Columns(Star, Auto);
            temperature = names.Add(Reading("—°", size), "WeatherTemperature"); temperature.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(temperature);
            glyphSize = Math.Max(28, Math.Min(72, Math.Round(size * 0.9)));
            glyph = new ContentControl { Width = glyphSize, Height = glyphSize, VerticalAlignment = VerticalAlignment.Center, Focusable = false, IsTabStop = false };
            top.Children.Add(At(glyph, 0, 1));
            view.Children.Add(top);
            city = names.Add(Text("", "BodySize", "Text"), "WeatherCity"); city.Margin = new Thickness(0, 2, 0, 0); view.Children.Add(At(city, 1));
            message = Caption(""); message.Margin = new Thickness(0, 4, 0, 0); if (detail) view.Children.Add(At(message, 2));
            credit = names.Add(Caption(""), "WeatherCredit"); credit.Margin = new Thickness(0, 4, 0, 0); view.Children.Add(At(credit, 3));
            return view;
        }

        // Medium and Large: a heading, the glyph and temperature beside the condition and city, three facts, the next
        // hours (three on a tall Medium card, up to five in a strip on Large) and the credit.
        private FrameworkElement BuildToday(double width, double height, bool large)
        {
            bool showHours = large || height >= 300 - 48;
            var view = Rows(Auto, Star, Auto, Auto, Auto);
            var header = Columns(Auto, Star); header.MinHeight = 26; header.Children.Add(Label("WEATHER").Centered());
            condition = names.Add(Caption(""), "WeatherCondition"); condition.HorizontalAlignment = HorizontalAlignment.Right; condition.VerticalAlignment = VerticalAlignment.Center; condition.Margin = new Thickness(12, 0, 0, 0);
            header.Children.Add(At(condition, 0, 1));
            view.Children.Add(header);
            double hoursHeight = !showHours ? 0 : large ? 104 + 12 : 64 + 10;
            double size = CardSizing.WeatherTemperature(design, bounds.Width, height - 26 - 62 - hoursHeight - 26);
            glyphSize = Math.Round(size * (large ? 0.8 : 0.85));
            var hero = Columns(Auto, Auto, Star); hero.VerticalAlignment = VerticalAlignment.Center;
            glyph = new ContentControl { Width = glyphSize, Height = glyphSize, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, large ? 20 : 16, 0), Focusable = false, IsTabStop = false };
            hero.Children.Add(glyph);
            temperature = names.Add(Reading("—°", size), "WeatherTemperature"); temperature.VerticalAlignment = VerticalAlignment.Center;
            hero.Children.Add(At(temperature, 0, 1));
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(large ? 20 : 16, 0, 0, 0) };
            described = Text("", large ? "TitleSize" : "BodySize", "Text"); words.Children.Add(described);
            city = names.Add(Text("", large ? "BodySize" : "SmallSize", "Muted"), "WeatherCity"); city.TextWrapping = TextWrapping.Wrap; city.MaxHeight = large ? 54 : 44; city.Margin = new Thickness(0, 4, 0, 0);
            words.Children.Add(city);
            hero.Children.Add(At(words, 0, 2));
            view.Children.Add(At(hero, 1));
            // The facts, or in their place a message while there is nothing to show.
            var middle = new Grid { Height = 52, Margin = new Thickness(0, 10, 0, 0) };
            facts = new Grid(); middle.Children.Add(facts);
            message = Text("", "SmallSize", "Muted"); message.TextWrapping = TextWrapping.Wrap; message.MaxHeight = 52; message.VerticalAlignment = VerticalAlignment.Center;
            middle.Children.Add(message);
            view.Children.Add(At(middle, 2));
            if (showHours)
            {
                hours = new UniformGrid { Rows = 1, Columns = large ? CardSizing.WeatherHours(bounds.Width) : 3 };
                if (large) { var strip = Tile(hours, 16, new Thickness(8, 10, 8, 10)); strip.Height = 104; strip.Margin = new Thickness(0, 12, 0, 0); hoursFrame = strip; }
                else { hours.Height = 64; hours.Margin = new Thickness(0, 10, 0, 0); hoursFrame = hours; }
                view.Children.Add(At(hoursFrame, 3));
            }
            credit = names.Add(Caption(""), "WeatherCredit"); credit.VerticalAlignment = VerticalAlignment.Bottom; credit.Margin = new Thickness(0, 6, 0, 0);
            view.Children.Add(At(credit, 4));
            return view;
        }

        private void Render()
        {
            if (temperature == null) return;
            bool hasCity = location.Length > 0, reading = last != null && !offline;
            temperature.Text = last != null ? Math.Round(last.Temperature) + "°" : "—°";
            city.Text = hasCity ? location : "No city yet";
            string family = reading ? WeatherService.Family(last.Code) : null;
            glyph.Content = family == null ? null : WeatherGlyph(family, glyphSize, "Accent");
            if (condition != null)
            {
                condition.Text = offline ? "OFFLINE" : "";
                condition.SetResourceReference(TextBlock.ForegroundProperty, offline ? "Warning" : "Muted");
                described.Text = reading ? WeatherService.Describe(last.Code) : "";
                described.Visibility = described.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            string note = !hasCity ? empty
                : offline ? (last == null ? "Can't reach the weather service. Tap the card to try again." : "Showing the last update. Tap the card to try again.")
                : last == null ? "Updating weather…" : null;
            if (design == CardSizing.Design.Small)
            {
                message.Text = note != null ? (offline ? "Offline. Tap to try again." : note) : HighLow(last) ?? WeatherService.Describe(last.Code);
                message.SetResourceReference(TextBlock.ForegroundProperty, offline ? "Warning" : "Muted");
            }
            else
            {
                message.Text = note ?? ""; message.Visibility = note != null ? Visibility.Visible : Visibility.Collapsed;
                facts.Visibility = note == null ? Visibility.Visible : Visibility.Collapsed;
                Facts(note == null ? last : null);
                Hours(reading ? last : null);
            }
            credit.Text = last != null ? "Open-Meteo  ·  " + (busy ? "updating…" : "updated " + last.Retrieved.ToString(use24 ? "HH:mm" : "h:mm tt", System.Globalization.CultureInfo.InvariantCulture))
                : busy ? "Open-Meteo  ·  updating…" : "Weather data by Open-Meteo";
        }
        private static string HighLow(WeatherSnapshot snapshot)
        {
            return snapshot != null && snapshot.High.HasValue && snapshot.Low.HasValue ? "↑ " + Math.Round(snapshot.High.Value) + "°   ↓ " + Math.Round(snapshot.Low.Value) + "°" : null;
        }
        // Feels, Wind and High / Low, each a small tile with its name over its value.
        private void Facts(WeatherSnapshot snapshot)
        {
            facts.Children.Clear(); facts.ColumnDefinitions.Clear();
            if (snapshot == null) return;
            var items = WeatherService.Facts(snapshot, imperial);
            for (int i = 0; i < items.Length; i++)
            {
                facts.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
                var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                column.Children.Add(Caption(items[i][0]));
                var value = Text(items[i][1], "BodySize", "Text"); value.Margin = new Thickness(0, 1, 0, 0); column.Children.Add(value);
                var tile = Tile(column, 12, new Thickness(12, 0, 8, 0)); tile.Margin = new Thickness(i == 0 ? 0 : 4, 0, i == items.Length - 1 ? 0 : 4, 0);
                facts.Children.Add(At(tile, 0, i));
            }
        }
        // The next hours: the time (Now in the accent), the condition glyph and the temperature.
        private void Hours(WeatherSnapshot snapshot)
        {
            if (hours == null) return;
            hours.Children.Clear();
            bool any = snapshot != null && snapshot.Hours.Count > 0;
            hoursFrame.Visibility = any ? Visibility.Visible : Visibility.Hidden;
            if (!any) return;
            foreach (var hour in snapshot.Hours.Take(hours.Columns))
            {
                bool now = hour == snapshot.Hours[0];
                var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var time = Caption(now ? "Now" : WeatherService.HourLabel(hour.Time, use24)); time.HorizontalAlignment = HorizontalAlignment.Center;
                if (now) { time.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); time.FontWeight = FontWeights.SemiBold; }
                column.Children.Add(time);
                double size = hours.Columns > 3 || hoursFrame != hours ? 26 : 18;
                var mark = WeatherGlyph(WeatherService.Family(hour.Code), size, "Muted"); mark.Margin = new Thickness(0, 4, 0, 4); column.Children.Add(mark);
                var temperature = Text(Math.Round(hour.Temperature) + "°", "BodySize", "Text"); temperature.HorizontalAlignment = HorizontalAlignment.Center; column.Children.Add(temperature);
                hours.Children.Add(column);
            }
        }
    }
}
