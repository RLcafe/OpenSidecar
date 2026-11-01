using System;
using System.Collections.Generic;

namespace WidgetScreen
{
    // The prayer times for one place and method, worked out once per day and kept (yesterday, today and tomorrow are
    // enough to find the next prayer at any moment). Times are UTC; the dashboard shows them in the PC's time zone.
    public sealed class PrayerSchedule
    {
        public string City { get; private set; }
        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        public string MethodId { get; private set; }
        public bool Hanafi { get; private set; }
        private readonly Dictionary<DateTime, PrayerDay> days = new Dictionary<DateTime, PrayerDay>();

        public PrayerSchedule(string city, double latitude, double longitude, string method, bool hanafi)
        {
            City = city; Latitude = latitude; Longitude = longitude; MethodId = method; Hanafi = hanafi;
        }

        // The place a schedule is for: Settings → Prayer's own city, or else the weather city; null when there's neither.
        public static PrayerSchedule For(AppSettings settings)
        {
            if (!String.IsNullOrWhiteSpace(settings.PrayerLocation)) return new PrayerSchedule(settings.PrayerLocation, settings.PrayerLatitude, settings.PrayerLongitude, settings.PrayerMethod, settings.PrayerHanafi);
            if (!String.IsNullOrWhiteSpace(settings.WeatherLocation)) return new PrayerSchedule(settings.WeatherLocation, settings.Latitude, settings.Longitude, settings.PrayerMethod, settings.PrayerHanafi);
            return null;
        }
        public bool Same(PrayerSchedule other)
        {
            return other != null && other.City == City && other.Latitude == Latitude && other.Longitude == Longitude && other.MethodId == MethodId && other.Hanafi == Hanafi;
        }

        public PrayerDay Day(DateTime localDate)
        {
            PrayerDay day;
            if (!days.TryGetValue(localDate.Date, out day))
            {
                if (days.Count > 8) days.Clear();
                day = PrayerService.Calculate(localDate.Date, Latitude, Longitude, MethodId, Hanafi);
                days[localDate.Date] = day;
            }
            return day;
        }

        public PrayerNext Next(DateTime utcNow, TimeZoneInfo local)
        {
            var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), local).Date;
            return PrayerService.Next(Day(today.AddDays(-1)), Day(today), Day(today.AddDays(1)), utcNow);
        }
    }
}
