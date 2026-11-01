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

import java.time.Instant;
import java.time.LocalDateTime;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.Locale;

// Weather for the city and units chosen on the PC, in the same three designs as the Windows WeatherCard
// (CardSizing.weather; docs/design/widget-size-views.md): Small is a glance (temperature and glyph, city, high and
// low); Medium is today (a large glyph and temperature, the condition and city, and Feels, Wind and High / Low as three
// facts, plus three hours on a tall card); Large is the forecast (the same, larger, with five hours in a strip). The
// phone asks Open-Meteo itself (it only needs the city's position from the PC), refreshes every 15 minutes while the
// card is shown, and a tap on the card updates it now.
public final class WeatherWidget extends Widget {
    private TextView condition, described, temperature, city, message, credit;
    private GlyphView glyph;
    private LinearLayout facts, hours;
    private View hoursFrame;
    private int design, hourCount;
    private Weather.Snapshot last;
    private boolean busy, offline, imperial;
    private String location = "";
    private double latitude, longitude;
    private long lastAttempt = -1;

    public WeatherWidget(Dashboard dash) { super("Weather", dash); }

    @Override public String part() { return "weather"; }

    @Override protected View create() {
        design = CardSizing.weather(width, height);
        condition = described = null; facts = hours = null; hoursFrame = null;
        double bodyWidth = Math.max(1, width - 48), bodyHeight = Math.max(1, height - 48);
        View view = design == CardSizing.SMALL ? glance(bodyHeight) : today(bodyWidth, bodyHeight, design == CardSizing.LARGE);
        FrameLayout card = ui.card(view, 24);
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
        double size = CardSizing.weatherTemperature(CardSizing.SMALL, width, height - 26 - (detail ? 20 : 0) - 20);
        LinearLayout top = ui.row();
        temperature = reading("—°", size);
        top.addView(temperature, ui.ll(0, Ui.WRAP, 1));
        double glyphSize = Math.max(28, Math.min(72, Math.round(size * 0.9)));
        glyph = new GlyphView(ui.context, "cloud", ui.theme.accent, ui.fpx(glyphSize));
        top.addView(glyph, ui.ll(ui.px(glyphSize), ui.px(glyphSize)));
        view.addView(top, ui.ll(Ui.MATCH, 0, 1));
        city = ui.body("");
        view.addView(city, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 2, 0, 0));
        message = ui.caption("");
        if (detail) view.addView(message, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        credit = ui.caption("");
        view.addView(credit, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        return view;
    }

    // Medium and Large: a heading, the glyph and temperature beside the condition and city, three facts, the next
    // hours (three on a tall Medium card, up to five in a strip on Large) and the credit.
    private View today(double width, double height, boolean large) {
        boolean showHours = large || height >= 300 - 48;
        LinearLayout view = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(strip(), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        condition = sticker(false);
        header.addView(condition, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        view.addView(header, ui.ll(Ui.MATCH, Ui.WRAP));
        header.setMinimumHeight(ui.px(26));
        double hoursHeight = !showHours ? 0 : large ? 104 + 12 : 64 + 10;
        double size = CardSizing.weatherTemperature(design, this.width, height - 26 - 62 - hoursHeight - 26);
        double glyphSize = Math.round(size * (large ? 0.8 : 0.85));
        LinearLayout hero = ui.row();
        glyph = new GlyphView(ui.context, "cloud", ui.theme.accent, ui.fpx(glyphSize));
        hero.addView(glyph, ui.margins(ui.ll(ui.px(glyphSize), ui.px(glyphSize)), 0, 0, large ? 20 : 16, 0));
        temperature = reading("—°", size);
        hero.addView(temperature, ui.ll(Ui.WRAP, Ui.WRAP));
        LinearLayout words = ui.column();
        described = ui.text("", large ? ui.title() : ui.body(), ui.theme.text);
        words.addView(described, ui.ll(Ui.MATCH, Ui.WRAP));
        city = Ui.wrap(large ? ui.text("", ui.body(), ui.theme.muted) : ui.smallText(""), 2);
        words.addView(city, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        hero.addView(words, ui.margins(ui.ll(0, Ui.WRAP, 1), large ? 20 : 16, 0, 0, 0));
        LinearLayout heroArea = ui.column(); heroArea.setGravity(Gravity.CENTER_VERTICAL);
        heroArea.addView(hero, ui.ll(Ui.MATCH, Ui.WRAP));
        view.addView(heroArea, ui.ll(Ui.MATCH, 0, 1));
        // The facts, or in their place a message while there is nothing to show.
        FrameLayout middle = new FrameLayout(ui.context);
        facts = ui.row();
        middle.addView(facts, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        message = Ui.wrap(ui.smallText(""), 2);
        middle.addView(message, ui.frame(Ui.MATCH, Ui.WRAP, Gravity.CENTER_VERTICAL));
        view.addView(middle, ui.margins(ui.ll(Ui.MATCH, ui.px(52)), 0, 10, 0, 0));
        if (showHours) {
            hourCount = large ? CardSizing.weatherHours(this.width) : 3;
            hours = ui.row();
            if (large) { hoursFrame = tile(hours, 16, 8, 10); view.addView(hoursFrame, ui.margins(ui.ll(Ui.MATCH, ui.px(104)), 0, 12, 0, 0)); }
            else { hoursFrame = hours; view.addView(hours, ui.margins(ui.ll(Ui.MATCH, ui.px(64)), 0, 10, 0, 0)); }
        }
        credit = ui.caption("");
        view.addView(credit, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
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
        boolean hasCity = !location.isEmpty(), reading = last != null && !offline, pcAway = w == null && !dash.online();
        temperature.setText(last != null && hasCity ? Math.round(last.temperature) + "°" : "—°");
        city.setText(hasCity ? location : pcAway ? "PC offline" : "No city yet");
        if (reading) { glyph.setGlyph(Weather.family(last.code)); glyph.setVisibility(View.VISIBLE); } else glyph.setVisibility(View.INVISIBLE);
        if (condition != null) {
            condition.setText(offline && hasCity ? "OFFLINE" : "");
            condition.setTextColor(offline ? Theme.WARNING : ui.theme.muted);
            described.setText(reading ? Weather.describe(last.code) : "");
            described.setVisibility(reading ? View.VISIBLE : View.GONE);
        }
        String note = !hasCity ? (pcAway ? dash.offlineText() : "Choose a city on the PC to see the weather here.")
            : offline ? (last == null ? "Can't reach the weather service. Tap the card to try again." : "Showing the last update. Tap the card to try again.")
            : last == null ? "Updating weather…" : null;
        if (design == CardSizing.SMALL) {
            String highLow = last != null && last.high != null && last.low != null ? "↑ " + Math.round(last.high) + "°   ↓ " + Math.round(last.low) + "°" : null;
            message.setText(note != null ? (offline && hasCity ? "Offline. Tap to try again." : note) : highLow != null ? highLow : Weather.describe(last.code));
            message.setTextColor(offline && hasCity ? Theme.WARNING : ui.theme.muted);
        } else {
            message.setText(note == null ? "" : note);
            message.setVisibility(note != null ? View.VISIBLE : View.GONE);
            facts.setVisibility(note == null ? View.VISIBLE : View.GONE);
            showFacts(note == null ? last : null);
            showHours(reading ? last : null);
        }
        if (last != null && hasCity) {
            // The "updated" time follows the PC clock's 12- or 24-hour choice, as on Windows.
            LocalDateTime retrieved = LocalDateTime.ofInstant(Instant.ofEpochMilli(last.retrievedMs), ZoneId.systemDefault());
            credit.setText(busy ? "Open-Meteo  ·  updating…" : "Open-Meteo  ·  updated " + Format.endsAt(retrieved, dash.use24()));
        } else credit.setText(busy ? "Open-Meteo  ·  updating…" : "Weather data by Open-Meteo");
    }

    // Feels, Wind and High / Low, each a small tile with its name over its value.
    private void showFacts(Weather.Snapshot snapshot) {
        facts.removeAllViews();
        if (snapshot == null) return;
        String[][] items = Weather.facts(snapshot, imperial);
        for (int i = 0; i < items.length; i++) {
            LinearLayout column = ui.column(); column.setGravity(Gravity.CENTER_VERTICAL);
            column.addView(ui.caption(items[i][0]), ui.ll(Ui.WRAP, Ui.WRAP));
            column.addView(ui.body(items[i][1]), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 1, 0, 0));
            View tile = tile(column, 12, 12, 0);
            tile.setPadding(ui.px(12), 0, ui.px(8), 0);
            facts.addView(tile, ui.margins(ui.ll(0, Ui.MATCH, 1), i == 0 ? 0 : 4, 0, i == items.length - 1 ? 0 : 4, 0));
        }
    }

    // The next hours: the time (Now in the accent), the condition glyph and the temperature.
    private void showHours(Weather.Snapshot snapshot) {
        if (hours == null) return;
        hours.removeAllViews();
        boolean any = snapshot != null && !snapshot.hours.isEmpty();
        hoursFrame.setVisibility(any ? View.VISIBLE : View.INVISIBLE);
        if (!any) return;
        double size = hoursFrame != hours ? 26 : 18;
        for (int i = 0; i < Math.min(hourCount, snapshot.hours.size()); i++) {
            Weather.Hour hour = snapshot.hours.get(i);
            LinearLayout column = ui.column(); column.setGravity(Gravity.CENTER);
            TextView time = ui.caption(i == 0 ? "Now" : Weather.hourLabel(hour.time, dash.use24()));
            if (i == 0) { time.setTextColor(ui.theme.accent); time.setTypeface(Ui.MEDIUM); }
            column.addView(time, ui.ll(Ui.WRAP, Ui.WRAP));
            column.addView(new GlyphView(ui.context, Weather.family(hour.code), ui.theme.muted, ui.fpx(size)), ui.margins(ui.ll(ui.px(size), ui.px(size)), 0, 4, 0, 4));
            column.addView(ui.body(Math.round(hour.temperature) + "°"), ui.ll(Ui.WRAP, Ui.WRAP));
            hours.addView(column, ui.ll(0, Ui.WRAP, 1));
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
