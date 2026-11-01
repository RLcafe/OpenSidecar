using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WidgetScreen
{
    // A calculation method: the sun's angle below the horizon for Fajr and Isha (or Isha a fixed number of minutes
    // after Maghrib), and for Maghrib where the method sets one (docs/design/prayer-times.md).
    public sealed class PrayerMethod
    {
        public string Id, Name;
        public double Fajr, Isha, Maghrib;
        public int IshaMinutes;
        public override string ToString() { return Name; }
    }

    // One day's times, in UTC. Sunrise is shown but isn't one of the five prayers.
    public sealed class PrayerDay
    {
        public DateTime Date;
        public DateTime Fajr, Sunrise, Dhuhr, Asr, Sunset, Maghrib, Isha;
        public DateTime[] Prayers { get { return new[] { Fajr, Dhuhr, Asr, Maghrib, Isha }; } }
    }

    // The next prayer from a moment, the one before it, and whether the last one began a moment ago.
    public sealed class PrayerNext
    {
        public int Index;
        public DateTime At, Previous;
        public int CurrentIndex;
        public bool JustStarted;
    }

    // Prayer times worked out on this PC from the sun's position (the standard astronomical method of PrayTimes.org,
    // public domain): no network, no account. Methods and the Asr school are chosen in Settings → Prayer; latitudes
    // where the sun barely sets use the angle-based rule for Fajr and Isha.
    public static class PrayerService
    {
        public static readonly string[] Names = { "Fajr", "Dhuhr", "Asr", "Maghrib", "Isha" };
        public static readonly PrayerMethod[] Methods = {
            new PrayerMethod { Id = "MWL", Name = "Muslim World League", Fajr = 18, Isha = 17 },
            new PrayerMethod { Id = "Makkah", Name = "Umm al-Qura, Makkah", Fajr = 18.5, IshaMinutes = 90 },
            new PrayerMethod { Id = "ISNA", Name = "Islamic Society of North America", Fajr = 15, Isha = 15 },
            new PrayerMethod { Id = "Egypt", Name = "Egyptian General Authority of Survey", Fajr = 19.5, Isha = 17.5 },
            new PrayerMethod { Id = "Karachi", Name = "University of Islamic Sciences, Karachi", Fajr = 18, Isha = 18 },
            new PrayerMethod { Id = "Gulf", Name = "Gulf region", Fajr = 19.5, IshaMinutes = 90 },
            new PrayerMethod { Id = "Kuwait", Name = "Kuwait", Fajr = 18, Isha = 17.5 },
            new PrayerMethod { Id = "Qatar", Name = "Qatar", Fajr = 18, IshaMinutes = 90 },
            new PrayerMethod { Id = "Singapore", Name = "Singapore", Fajr = 20, Isha = 18 },
            new PrayerMethod { Id = "Turkey", Name = "Diyanet, Turkey", Fajr = 18, Isha = 17 },
            new PrayerMethod { Id = "Tehran", Name = "Institute of Geophysics, Tehran", Fajr = 17.7, Isha = 14, Maghrib = 4.5 },
            new PrayerMethod { Id = "Jafari", Name = "Shia Ithna Ashari (Jafari)", Fajr = 16, Isha = 14, Maghrib = 4 },
        };
        public static PrayerMethod Method(string id) { return Methods.FirstOrDefault(m => m.Id == id) ?? Methods[0]; }
        public static bool KnownMethod(string id) { return Methods.Any(m => m.Id == id); }
        // The method most mosques use in the PC's region, as a starting point.
        public static string RegionMethod()
        {
            string region;
            try { region = RegionInfo.CurrentRegion.TwoLetterISORegionName; } catch (Exception) { return "MWL"; }
            switch (region)
            {
                case "SA": return "Makkah";
                case "AE": case "BH": case "OM": return "Gulf";
                case "KW": return "Kuwait";
                case "QA": return "Qatar";
                case "EG": case "SD": case "LY": case "DZ": case "IQ": case "LB": case "SY": case "JO": return "Egypt";
                case "PK": case "IN": case "BD": case "AF": return "Karachi";
                case "US": case "CA": return "ISNA";
                case "SG": case "MY": case "ID": case "BN": return "Singapore";
                case "TR": return "Turkey";
                case "IR": return "Tehran";
                default: return "MWL";
            }
        }

        private static double Rad(double d) { return d * Math.PI / 180; }
        private static double Deg(double r) { return r * 180 / Math.PI; }
        private static double Fix(double a, double b) { a = a - b * Math.Floor(a / b); return a < 0 ? a + b : a; }

        // The sun's declination and the equation of time for a Julian day.
        private static void Sun(double jd, out double declination, out double equation)
        {
            double d = jd - 2451545.0;
            double g = Fix(357.529 + 0.98560028 * d, 360), q = Fix(280.459 + 0.98564736 * d, 360);
            double l = Fix(q + 1.915 * Math.Sin(Rad(g)) + 0.020 * Math.Sin(Rad(2 * g)), 360);
            double e = 23.439 - 0.00000036 * d;
            double ra = Fix(Deg(Math.Atan2(Math.Cos(Rad(e)) * Math.Sin(Rad(l)), Math.Cos(Rad(l)))) / 15, 24);
            equation = q / 15 - ra;
            declination = Deg(Math.Asin(Math.Sin(Rad(e)) * Math.Sin(Rad(l))));
        }
        private static double Julian(int year, int month, int day)
        {
            if (month <= 2) { year -= 1; month += 12; }
            double a = Math.Floor(year / 100.0), b = 2 - a + Math.Floor(a / 4);
            return Math.Floor(365.25 * (year + 4716)) + Math.Floor(30.6001 * (month + 1)) + day + b - 1524.5;
        }

        // Times for a calendar date at a place, in UTC.
        public static PrayerDay Calculate(DateTime date, double latitude, double longitude, string methodId, bool hanafi)
        {
            var method = Method(methodId);
            double jd = Julian(date.Year, date.Month, date.Day) - longitude / (15 * 24);
            Func<double, double> noon = t => { double dec, eq; Sun(jd + t, out dec, out eq); return Fix(12 - eq, 24); };
            Func<double, double, bool, double> angleTime = (angle, t, before) => {
                double dec, eq; Sun(jd + t, out dec, out eq);
                double cos = (-Math.Sin(Rad(angle)) - Math.Sin(Rad(dec)) * Math.Sin(Rad(latitude))) / (Math.Cos(Rad(dec)) * Math.Cos(Rad(latitude)));
                if (cos < -1 || cos > 1) return Double.NaN;
                double hours = Deg(Math.Acos(cos)) / 15, middle = noon(t);
                return middle + (before ? -hours : hours);
            };
            Func<double, double, double> asrTime = (factor, t) => {
                double dec, eq; Sun(jd + t, out dec, out eq);
                double angle = -Deg(Math.Atan(1 / (factor + Math.Tan(Rad(Math.Abs(latitude - dec))))));
                return angleTime(angle, t, false);
            };
            const double riseSet = 0.833;
            // First guesses as fractions of the day, refined once with the sun where each time falls.
            double fajr = angleTime(method.Fajr, 5 / 24.0, true), sunrise = angleTime(riseSet, 6 / 24.0, true), dhuhr = noon(12 / 24.0);
            double asr = asrTime(hanafi ? 2 : 1, 13 / 24.0), sunset = angleTime(riseSet, 18 / 24.0, false);
            double maghrib = method.Maghrib > 0 ? angleTime(method.Maghrib, 18 / 24.0, false) : sunset;
            double isha = method.IshaMinutes > 0 ? Double.NaN : angleTime(method.Isha, 18 / 24.0, false);
            // High latitudes (the angle-based rule): Fajr and Isha stay within angle/60 of the night from sunrise and
            // sunset, so they exist, and keep a sensible distance, where the sun never goes that far below the horizon.
            double night = Fix(sunrise - sunset, 24);
            if (!Double.IsNaN(sunrise) && !Double.IsNaN(sunset))
            {
                double fajrPortion = method.Fajr / 60 * night, ishaPortion = method.Isha / 60 * night, maghribPortion = method.Maghrib / 60 * night;
                if (Double.IsNaN(fajr) || Fix(sunrise - fajr, 24) > fajrPortion) fajr = sunrise - fajrPortion;
                if (method.IshaMinutes == 0 && (Double.IsNaN(isha) || Fix(isha - sunset, 24) > ishaPortion)) isha = sunset + ishaPortion;
                if (method.Maghrib > 0 && (Double.IsNaN(maghrib) || Fix(maghrib - sunset, 24) > maghribPortion)) maghrib = sunset + maghribPortion;
            }
            // Umm al-Qura sets Isha two hours after Maghrib in Ramadan.
            if (method.IshaMinutes > 0)
            {
                int minutes = method.IshaMinutes;
                if (method.Id == "Makkah") { try { if (new UmAlQuraCalendar().GetMonth(date) == 9) minutes = 120; } catch (ArgumentOutOfRangeException) { } }
                isha = maghrib + minutes / 60.0;
            }
            var midnight = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc);
            double shift = -longitude / 15;
            Func<double, DateTime> at = h => Double.IsNaN(h) ? DateTime.MinValue : midnight.AddMinutes(Math.Round((h + shift) * 60));
            return new PrayerDay { Date = date.Date, Fajr = at(fajr), Sunrise = at(sunrise), Dhuhr = at(dhuhr), Asr = at(asr), Sunset = at(sunset), Maghrib = at(maghrib), Isha = at(isha) };
        }

        // The next of the five prayers after a moment (UTC), from today's and tomorrow's times; a prayer that began in
        // the last NowFor minutes counts as just started.
        public static readonly TimeSpan NowFor = TimeSpan.FromMinutes(20);
        public static PrayerNext Next(PrayerDay yesterday, PrayerDay today, PrayerDay tomorrow, DateTime utcNow)
        {
            var all = new List<KeyValuePair<int, DateTime>>();
            foreach (var day in new[] { yesterday, today, tomorrow })
                for (int i = 0; i < 5; i++) if (day.Prayers[i] != DateTime.MinValue) all.Add(new KeyValuePair<int, DateTime>(i, day.Prayers[i]));
            all.Sort((a, b) => a.Value.CompareTo(b.Value));
            int next = all.FindIndex(p => p.Value > utcNow);
            if (next < 0) next = all.Count - 1;
            var previous = next > 0 ? all[next - 1] : all[0];
            return new PrayerNext { Index = all[next].Key, At = all[next].Value, Previous = previous.Value, CurrentIndex = previous.Key, JustStarted = next > 0 && utcNow - previous.Value < NowFor };
        }

        // "in 1 h 12 m", "in 12 m", "in under a minute".
        public static string Countdown(TimeSpan left)
        {
            if (left < TimeSpan.FromMinutes(1)) return "in under a minute";
            int minutes = (int)Math.Ceiling(left.TotalMinutes);
            return minutes >= 60 ? "in " + minutes / 60 + " h " + minutes % 60 + " m" : "in " + minutes + " m";
        }
        public static string Time(DateTime local, bool use24) { return local.ToString(use24 ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture); }

        // Today's Hijri date by the Umm al-Qura calendar, "12 Jumada al-Awwal 1448".
        private static readonly string[] Months = { "Muharram", "Safar", "Rabi al-Awwal", "Rabi al-Thani", "Jumada al-Awwal", "Jumada al-Thani", "Rajab", "Shaban", "Ramadan", "Shawwal", "Dhu al-Qadah", "Dhu al-Hijjah" };
        public static string Hijri(DateTime localDate)
        {
            try { var c = new UmAlQuraCalendar(); return c.GetDayOfMonth(localDate) + " " + Months[c.GetMonth(localDate) - 1] + " " + c.GetYear(localDate); }
            catch (ArgumentOutOfRangeException) { return ""; }
        }
    }
}
