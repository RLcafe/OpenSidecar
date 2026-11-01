package com.alialyami.widgetscreen.widgets;

import android.os.SystemClock;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.core.Weather;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Http;
import com.alialyami.widgetscreen.views.GlyphView;
import com.alialyami.widgetscreen.views.ReadingParts;
import com.alialyami.widgetscreen.views.Stickers;

import java.time.Instant;
import java.time.LocalDateTime;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.Locale;

// Weather for the city and units chosen on the PC, in the same three designs as the Windows WeatherCard
// (CardSizing.weather; docs/design/widget-size-views.md), dressed as a sticker sheet (phase 3,
// docs/design/sticker-sheet.md): the reading sits on a sky band tinted by the condition and the time of day, with the
// condition glyph on a die-cut sticker. Small is a glance (temperature and glyph, city, high and low); Medium is today
// (the reading, the condition and city, and Feels, Humidity and Rain chips, plus the 12-hour curve on a tall card);
// Large adds more chips and a forecast row with the 12-hour curve over rain bars and the sunrise–sunset arc. The
// phone asks Open-Meteo itself (it only needs the city's position from the PC), refreshes every 15 minutes while the
// card is shown, and a tap on the card updates it now.
public final class WeatherWidget extends Widget {
    private TextView condition, described, temperature, city, message, credit, sunrise, sunset;
    private FrameLayout glyph, sky;
    private LinearLayout chips;
    private ReadingParts.TemperatureCurve curve;
    private ReadingParts.SunArc arc;
    private View curveFrame, arcFrame, values, middle;
    // Not set up, or no reading because the service can't be reached: a dashed outline with the reason.
    private TextView blank;
    private int design, chipCount;
    private double glyphSize;
    private String stickerKey = "";
    private Weather.Snapshot last;
    private boolean busy, offline, imperial;
    private String location = "";
    private double latitude, longitude;
    private long lastAttempt = -1;

    public WeatherWidget(Dashboard dash) { super("Weather", dash); }

    @Override public String part() { return "weather"; }

    @Override protected View create() {
        design = CardSizing.weather(width, height);
        condition = described = sunrise = sunset = null; chips = null; middle = null; curve = null; arc = null; curveFrame = arcFrame = null; stickerKey = "";
        double bodyWidth = Math.max(1, width - 48), bodyHeight = Math.max(1, height - 48);
        View view = design == CardSizing.SMALL ? glance(bodyHeight) : today(bodyWidth, bodyHeight, design == CardSizing.LARGE);
        FrameLayout stack = new FrameLayout(ui.context);
        stack.addView(view, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        blank = Stickers.unavailable(ui, ""); blank.setVisibility(View.GONE);
        stack.addView(blank, ui.frame(Ui.MATCH, Ui.WRAP, Gravity.CENTER));
        FrameLayout card = ui.card(stack, 24);
        // The whole card is the refresh tap; without a city it says where to choose one.
        card.setClickable(true);
        card.setContentDescription("Weather. Tap to update.");
        card.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) {
            if (location.isEmpty()) dash.say("Choose a city on the PC: Settings → Weather.");
            else refresh(true);
        } });
        return card;
    }

    private TextView reading(String text, double size) {
        TextView view = Ui.tabular(ui.text(text, (float) size, ui.theme.text));
        view.setTypeface(Ui.LIGHT);
        return view;
    }
    private View tile(View content, double radius, double padX, double padY) {
        FrameLayout tile = new FrameLayout(ui.context);
        tile.setBackground(ui.shape(ui.theme.raised, 0, 0, radius));
        tile.setPadding(ui.px(padX), ui.px(padY), ui.px(padX), ui.px(padY));
        tile.addView(content, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        return tile;
    }

    // Small: no heading; the temperature and glyph fill the top, the city, high and low, and the credit follow.
    private View glance(double height) {
        boolean detail = height >= 136;
        LinearLayout view = ui.column();
        double size = CardSizing.weatherTemperature(CardSizing.SMALL, width, height - 26 - (detail ? 20 : 0) - 20 - 8);
        LinearLayout top = ui.row(); top.setGravity(Gravity.CENTER_VERTICAL);
        temperature = reading("—°", size);
        top.addView(temperature, ui.ll(0, Ui.WRAP, 1));
        glyphSize = Math.max(28, Math.min(64, Math.round(size * 0.8)));
        glyph = glyphSticker();
        top.addView(glyph, ui.ll(ui.px(glyphSize + Stickers.KEYLINE), ui.px(glyphSize + Stickers.LIFT + Stickers.KEYLINE)));
        view.addView(skyBand(top, 10, 4), ui.ll(Ui.MATCH, 0, 1));
        city = Ui.wrap(ui.body(""), 1);
        view.addView(city, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        message = ui.caption("");
        if (detail) view.addView(message, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        credit = ui.caption("");
        view.addView(credit, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        return view;
    }

    // The sky band behind the reading: a rounded panel in the sky's ink fading downwards (render sets its colours).
    private FrameLayout skyBand(View content, double padX, double padY) {
        sky = new FrameLayout(ui.context);
        sky.setPadding(ui.px(padX), ui.px(padY), ui.px(padX), ui.px(padY));
        sky.addView(content, ui.frame(Ui.MATCH, Ui.WRAP, Gravity.CENTER_VERTICAL));
        values = sky;
        return sky;
    }
    // The condition glyph on a die-cut disc sticker in the sky's ink, drawn in dark text (render fills it).
    private FrameLayout glyphSticker() { return new FrameLayout(ui.context); }
    private void fillSticker(String family, String ink) {
        String key = family + "/" + ink;
        if (key.equals(stickerKey)) return;
        stickerKey = key; glyph.removeAllViews();
        if (family == null) return;
        glyph.addView(new Stickers.StickerShape(ui, Stickers.disc(ui, glyphSize), Theme.ink(ink)), new FrameLayout.LayoutParams(Ui.WRAP, Ui.WRAP));
        double mark = Math.round(glyphSize * 0.62);
        FrameLayout.LayoutParams at = new FrameLayout.LayoutParams(ui.px(mark), ui.px(mark));
        at.leftMargin = ui.px(Stickers.KEYLINE / 2 + (glyphSize - mark) / 2); at.topMargin = ui.px(Stickers.KEYLINE / 2 + (glyphSize - mark) / 2);
        glyph.addView(new GlyphView(ui.context, family, Theme.INK_TEXT, ui.fpx(mark)), at);
    }

    // Medium and Large: a heading; the glyph sticker and temperature beside the condition and city on the sky band;
    // a rail of chips; on Large (and a tall Medium) the 12-hour curve, beside the sun arc on Large; and the credit.
    private View today(double width, double height, boolean large) {
        boolean showCurve = large || height >= 300 - 48, showArc = large && width >= 380;
        LinearLayout view = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(strip(), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        condition = sticker(false);
        header.addView(condition, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        view.addView(header, ui.ll(Ui.MATCH, Ui.WRAP));
        header.setMinimumHeight(ui.px(26));
        double padY = large ? 6 : 4;
        double chart = large ? Math.max(92, Math.min(150, Math.round((height - 28 - 65 - 21) * 0.36))) : 64;
        double curveHeight = !showCurve ? 0 : chart + (large ? 10 : 8);
        double size = CardSizing.weatherTemperature(design, this.width, height - 28 - padY * 2 - 65 - curveHeight - 21);
        glyphSize = Math.round(size * 0.8);
        LinearLayout hero = ui.row(); hero.setGravity(Gravity.CENTER_VERTICAL);
        glyph = glyphSticker();
        hero.addView(glyph, ui.margins(ui.ll(ui.px(glyphSize + Stickers.KEYLINE), ui.px(glyphSize + Stickers.LIFT + Stickers.KEYLINE)), 0, 0, large ? 18 : 14, 0));
        temperature = reading("—°", size);
        hero.addView(temperature, ui.ll(Ui.WRAP, Ui.WRAP));
        LinearLayout words = ui.column();
        described = Ui.wrap(ui.text("", large ? ui.title() : ui.body(), ui.theme.text), 1);
        words.addView(described, ui.ll(Ui.MATCH, Ui.WRAP));
        city = Ui.wrap(large ? ui.text("", ui.body(), ui.theme.text) : ui.smallText(""), 2);
        city.setTextColor(ui.theme.text); city.setAlpha(0.78f);
        words.addView(city, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        hero.addView(words, ui.margins(ui.ll(0, Ui.WRAP, 1), large ? 18 : 14, 0, 0, 0));
        view.addView(skyBand(hero, 12, padY), ui.ll(Ui.MATCH, 0, 1));
        // The chips, or in their place a message while there is nothing to show.
        FrameLayout middle = new FrameLayout(ui.context); this.middle = middle;
        chipCount = large ? CardSizing.weatherChips(this.width) : 3;
        chips = ui.row();
        middle.addView(chips, new FrameLayout.LayoutParams(Ui.MATCH, Ui.WRAP));
        message = Ui.wrap(ui.smallText(""), 2);
        middle.addView(message, ui.frame(Ui.MATCH, Ui.WRAP, Gravity.CENTER_VERTICAL));
        middle.setMinimumHeight(ui.px(57));
        view.addView(middle, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 8, 0, 0));
        if (showCurve) {
            LinearLayout row = ui.row();
            curve = new ReadingParts.TemperatureCurve(ui, dash.ink(id));
            if (large) { curveFrame = tile(curve, 16, 8, 6); } else curveFrame = curve;
            row.addView(curveFrame, ui.ll(0, Ui.MATCH, 1));
            if (showArc) {
                LinearLayout stack = ui.column(); stack.setGravity(Gravity.CENTER_VERTICAL);
                arc = new ReadingParts.SunArc(ui);
                stack.addView(arc, ui.ll(Ui.MATCH, ui.px(Math.max(46, chart - 12 - 22))));
                FrameLayout times = new FrameLayout(ui.context);
                sunrise = ui.caption(""); sunset = ui.caption("");
                times.addView(sunrise, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START));
                times.addView(sunset, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END));
                stack.addView(times, ui.ll(Ui.MATCH, Ui.WRAP));
                arcFrame = tile(stack, 16, 10, 6);
                row.addView(arcFrame, ui.margins(ui.ll(ui.px(150), Ui.MATCH), 10, 0, 0, 0));
            }
            view.addView(row, ui.margins(ui.ll(Ui.MATCH, ui.px(chart)), 0, large ? 10 : 8, 0, 0));
        }
        credit = ui.caption("");
        view.addView(credit, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        return view;
    }

    @Override public void render() {
        if (!built()) return;
        PcState state = dash.state();
        PcState.Weather w = state == null ? null : state.weather;
        if (w != null && (!w.location.equals(location) || w.latitude != latitude || w.longitude != longitude || w.imperial != imperial)) {
            // A new city or new units on the PC drop the old reading.
            location = w.location; latitude = w.latitude; longitude = w.longitude; imperial = w.imperial; last = null; offline = false; lastAttempt = -1;
        }
        boolean hasCity = !location.isEmpty(), shown = last != null && hasCity, pcAway = w == null && !dash.online();
        temperature.setText(shown ? Math.round(last.temperature) + "°" : "—°");
        city.setText(hasCity ? location : pcAway ? "PC offline" : "No city yet");
        String now = Weather.cityNow(last, System.currentTimeMillis());
        boolean day = !shown || Weather.isDay(now, last.sunrise, last.sunset);
        String family = shown ? Weather.family(last.code) : null;
        String ink = Weather.skyInk(family == null ? "cloud" : family, day);
        double strength = shown ? Weather.skyStrength(family, day) : 0.12;
        int tint = Theme.ink(ink), top = (tint & 0x00FFFFFF) | ((int) Math.round(255 * strength) << 24), bottom = (tint & 0x00FFFFFF) | ((int) Math.round(255 * strength * 0.08) << 24);
        android.graphics.drawable.GradientDrawable band = new android.graphics.drawable.GradientDrawable(android.graphics.drawable.GradientDrawable.Orientation.TOP_BOTTOM, new int[] { top, bottom });
        band.setCornerRadius(ui.fpx(16)); sky.setBackground(band);
        fillSticker(family, ink);
        Stickers.stale(values, offline && shown);
        // No city, or the service unreachable with nothing to show: the reason in a dashed outline instead of the reading.
        boolean unset = !hasCity || (offline && last == null);
        blank.setVisibility(unset ? View.VISIBLE : View.GONE);
        blank.setText(!hasCity ? (pcAway ? dash.offlineText() : "No city yet. Choose one on the PC: Settings → Weather.") : "Can't reach the weather service. Tap the card to try again.");
        values.setVisibility(unset ? View.INVISIBLE : View.VISIBLE);
        if (design == CardSizing.SMALL) { city.setVisibility(unset ? View.INVISIBLE : View.VISIBLE); message.setVisibility(unset ? View.INVISIBLE : View.VISIBLE); }
        if (middle != null) middle.setVisibility(unset ? View.INVISIBLE : View.VISIBLE);
        if (condition != null) {
            condition.setText(offline && hasCity ? "OFFLINE" : "");
            condition.setTextColor(offline ? Theme.WARNING : ui.theme.muted);
            described.setText(shown ? Weather.describe(last.code) : "");
            described.setVisibility(shown ? View.VISIBLE : View.GONE);
        }
        String note = !hasCity ? (pcAway ? dash.offlineText() : "Choose a city on the PC to see the weather here.")
            : offline ? (last == null ? "Can't reach the weather service. Tap the card to try again." : null)
            : last == null ? "Updating weather…" : null;
        if (design == CardSizing.SMALL) {
            String highLow = last != null && last.high != null && last.low != null ? "↑ " + Math.round(last.high) + "°   ↓ " + Math.round(last.low) + "°" : null;
            message.setText(note != null ? (offline && hasCity ? "Offline. Tap to try again." : note) : offline ? "Offline: last update shown" : highLow != null ? highLow : Weather.describe(last.code));
            message.setTextColor(offline && hasCity ? Theme.WARNING : ui.theme.muted);
        } else {
            message.setText(note == null ? "" : note);
            message.setVisibility(note != null ? View.VISIBLE : View.GONE);
            chips.setVisibility(note == null ? View.VISIBLE : View.GONE);
            showChips(note == null ? last : null);
            Stickers.stale(chips, offline && shown);
            showForecast(shown ? last : null, now);
        }
        if (shown) {
            // The "updated" time follows the PC clock's 12- or 24-hour choice, as on Windows.
            LocalDateTime retrieved = LocalDateTime.ofInstant(Instant.ofEpochMilli(last.retrievedMs), ZoneId.systemDefault());
            credit.setText(busy ? "Open-Meteo  ·  updating…" : "Open-Meteo  ·  " + (offline ? "last update " : "updated ") + Format.endsAt(retrieved, dash.use24()));
        } else credit.setText(busy ? "Open-Meteo  ·  updating…" : "Weather data by Open-Meteo");
    }

    // The fact rail: Feels, Humidity, Rain and, on Large, UV, Wind and High / Low as far as they fit.
    private void showChips(Weather.Snapshot snapshot) {
        chips.removeAllViews();
        if (snapshot == null) return;
        String[][] items = Weather.chips(snapshot, imperial, chipCount);
        for (int i = 0; i < items.length; i++)
            chips.addView(Stickers.factChip(ui, items[i][0], items[i][1]), ui.margins(ui.ll(0, Ui.WRAP, 1), i == 0 ? 0 : 4, 0, i == items.length - 1 ? 0 : 4, 0));
    }

    // The next 12 hours as a curve over rain bars (labels every three hours), and the sun's place between sunrise and sunset.
    private void showForecast(Weather.Snapshot snapshot, String now) {
        if (curve != null) {
            boolean any = snapshot != null && snapshot.curve.size() >= 2;
            curveFrame.setVisibility(any ? View.VISIBLE : View.INVISIBLE);
            if (any) {
                int n = snapshot.curve.size();
                double[] temperatures = new double[n]; Integer[] rain = new Integer[n]; String[] labels = new String[n];
                for (int i = 0; i < n; i++) {
                    Weather.Hour hour = snapshot.curve.get(i);
                    temperatures[i] = hour.temperature; rain[i] = hour.rain;
                    labels[i] = i == 0 ? "Now" : i % 3 == 0 ? Weather.hourLabel(hour.time, dash.use24()) : null;
                }
                curve.show(temperatures, rain, labels);
            }
        }
        if (arc != null) {
            Double part = snapshot == null ? null : Weather.sunPart(now, snapshot.sunrise, snapshot.sunset);
            arcFrame.setVisibility(part != null ? View.VISIBLE : View.GONE);
            arc.setPart(part == null ? Double.NaN : part);
            sunrise.setText(part != null ? "↑ " + Weather.clockTime(snapshot.sunrise, dash.use24()) : "");
            sunset.setText(part != null ? "↓ " + Weather.clockTime(snapshot.sunset, dash.use24()) : "");
        }
    }

    public void refresh(boolean force) {
        if (busy || !active() || location.isEmpty()) return;
        if (!force && lastAttempt >= 0 && SystemClock.elapsedRealtime() - lastAttempt < 15 * 60000L) return;
        busy = true; lastAttempt = SystemClock.elapsedRealtime();
        final String requested = location;
        final double lat = latitude, lon = longitude;
        final boolean units = imperial;
        // The city's local hour, as Open-Meteo labels its hours (timezone=auto): the phone's clock stands in for it.
        final String nowHour = LocalDateTime.now().format(DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:00", Locale.ROOT));
        render();
        Async.run(new Async.Work<Weather.Snapshot>() {
            @Override public Weather.Snapshot run() throws Exception { return Weather.parse(Http.getText(Weather.forecastUrl(lat, lon, units)), System.currentTimeMillis(), nowHour); }
        }, new Async.Done<Weather.Snapshot>() {
            @Override public void ok(Weather.Snapshot snapshot) { busy = false; if (requested.equals(location)) { last = snapshot; offline = false; } render(); }
        }, new Async.Failed() {
            @Override public void error(Exception error) { busy = false; if (requested.equals(location)) offline = true; render(); }
        });
    }

    @Override public void tick(int ticks, boolean quiet) { refresh(false); }
}
