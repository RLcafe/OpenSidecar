using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    // The World clocks widget (docs/design/world-clocks.md): up to three time zones from Windows' own zone list, picked
    // in Settings → Widgets. The card follows the one-second heartbeat while it's shown; a phone gets each zone's
    // current offset and works the times out itself.
    public sealed partial class DashboardController
    {
        private WorldClocksCard clocksCard;
        private List<ZoneOption> zoneOptions;
        // The card, for the checks (tests/Clocks/ClockTests.cs).
        public WorldClocksCard ClocksView { get { return clocksCard; } }

        private sealed class ZoneOption
        {
            public string Id, Label;
            public override string ToString() { return Label; }
        }

        private void InitializeClocks()
        {
            clocksCard = new WorldClocksCard(window, Control<Grid>("ClocksBody"), Control<TextBlock>("ClocksStatus"));
            // A tap on the empty card opens the zone picker.
            Action tapped = () => {
                if (Editor.Editing || !clocksCard.Empty) return;
                OpenSettings(); Control<TabControl>("SettingsTabs").SelectedIndex = 2; window.UpdateLayout();
                Control<ComboBox>("ClockZone0").BringIntoView(); Control<ComboBox>("ClockZone0").Focus();
            };
            Control<Border>("ClocksCard").PreviewMouseLeftButtonUp += (sender, e) => tapped();
            Control<Border>("ClocksCard").PreviewTouchUp += (sender, e) => tapped();
            for (int i = 0; i < WorldClockService.MaxClocks; i++)
            {
                var zone = Control<ComboBox>("ClockZone" + i); var label = Control<TextBox>("ClockName" + i);
                // A new zone brings its first city as the name, unless a name of the user's own is typed there.
                zone.SelectionChanged += (sender, e) => {
                    var previous = e.RemovedItems.Count > 0 ? e.RemovedItems[0] as ZoneOption : null;
                    var next = zone.SelectedItem as ZoneOption;
                    string before = previous == null || previous.Id == null ? "" : WorldClockService.DefaultLabel(WorldClockService.Find(previous.Id));
                    if (label.Text.Trim().Length == 0 || label.Text.Trim() == before) label.Text = next == null || next.Id == null ? "" : WorldClockService.DefaultLabel(WorldClockService.Find(next.Id));
                    label.IsEnabled = next != null && next.Id != null;
                };
            }
        }

        private void TickClocks() { if (Editor.IsVisible("Clocks")) clocksCard.Tick(); }

        // Settings → Widgets: the zones Windows knows, with None first.
        private void OpenClockSettings()
        {
            if (zoneOptions == null)
            {
                zoneOptions = new List<ZoneOption> { new ZoneOption { Id = null, Label = "None" } };
                try { zoneOptions.AddRange(TimeZoneInfo.GetSystemTimeZones().Select(z => new ZoneOption { Id = z.Id, Label = z.DisplayName })); }
                catch (Exception) { }
            }
            var clocks = settings.WorldClocks ?? new WorldClock[0];
            for (int i = 0; i < WorldClockService.MaxClocks; i++)
            {
                var zone = Control<ComboBox>("ClockZone" + i); var label = Control<TextBox>("ClockName" + i);
                zone.ItemsSource = zoneOptions;
                var clock = i < clocks.Length ? clocks[i] : null;
                zone.SelectedItem = clock == null ? zoneOptions[0] : zoneOptions.FirstOrDefault(o => o.Id == clock.Zone) ?? zoneOptions[0];
                label.Text = clock == null ? "" : clock.Label; label.IsEnabled = clock != null;
            }
        }
        private WorldClock[] ClockChoices()
        {
            var result = new List<WorldClock>();
            for (int i = 0; i < WorldClockService.MaxClocks; i++)
            {
                var option = Control<ComboBox>("ClockZone" + i).SelectedItem as ZoneOption;
                if (option != null && option.Id != null) result.Add(new WorldClock { Zone = option.Id, Label = Control<TextBox>("ClockName" + i).Text });
            }
            return WorldClockService.Clean(result);
        }

        // The phone's clocks section: each zone's name and offset from UTC now, and the PC's own offset, so the phone
        // shows the times and days against the PC's clock between readings.
        private Dictionary<string, object> RemoteClocks()
        {
            var now = DateTime.UtcNow;
            var readings = WorldClockService.Read(settings.WorldClocks, now, TimeZoneInfo.Local);
            return D("pcOffset", (int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(now).TotalMinutes),
                "clocks", readings.Select(r => (object)D("label", r.Label, "offset", r.OffsetMinutes)).ToArray());
        }
    }
}
