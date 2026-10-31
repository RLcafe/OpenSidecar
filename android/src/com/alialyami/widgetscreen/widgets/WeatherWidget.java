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
import com.alialyami.widgetscreen.core.Weather;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Http;
import com.alialyami.widgetscreen.views.GlyphView;

import java.time.Instant;
import java.time.LocalDateTime;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.Locale;

// Weather for the city and units chosen on the PC. The phone asks Open-Meteo itself (it only needs the city's
// position from the PC), refreshes every 15 minutes while the card is shown, and a tap on the card updates it now.
public final class WeatherWidget extends Widget {
    private TextView condition, temperature, city, details, credit;
    private GlyphView glyph;
    private LinearLayout hoursRow;
    private final TextView[] hourTimes = new TextView[3], hourTemps = new TextView[3];
    private Weather.Snapshot last;
    private boolean busy, offline, imperial;
    private String location = "";
    private double latitude, longitude;
    private long lastAttempt = -1;

    public WeatherWidget(Dashboard dash) { super("Weather", dash); }

    @Override public String part() { return "weather"; }

    @Override protected View create() {
        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(ui.label("WEATHER"), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        LinearLayout right = ui.row();
        glyph = new GlyphView(ui.context, "cloud", ui.theme.accent, ui.fpx(18));
        glyph.setVisibility(View.GONE);
        right.addView(glyph, ui.margins(ui.ll(ui.px(20), ui.px(20)), 0, 0, 8, 0));
        condition = ui.caption("");
        right.addView(condition, ui.ll(Ui.WRAP, Ui.WRAP));
        header.addView(right, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(26)));

        LinearLayout body = ui.row();
        // The temperature grows with the card (CardSizing, as on the PC).
        double size = CardSizing.weatherTemperature(width, height);
        temperature = ui.text("—°", (float) size, ui.theme.text);
        temperature.setTypeface(Ui.LIGHT);
        body.addView(temperature, ui.ll(ui.px(size * 2), Ui.WRAP));
        LinearLayout text = ui.column();
        city = Ui.wrap(ui.body("No city yet"), height < 250 ? 1 : 2);
        text.addView(city, ui.ll(Ui.MATCH, Ui.WRAP));
        details = Ui.wrap(ui.smallText(""), 2);
        // Under 200 tall the card keeps the temperature and city (CardSizing.weatherTiny, as on the PC).
        if (!CardSizing.weatherTiny(height)) text.addView(details, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 5, 0, 0));
        body.addView(text, ui.ll(0, Ui.WRAP, 1));
        column.addView(body, ui.ll(Ui.MATCH, 0, 1));

        // The next hours (now, +3 h, +6 h) on a tall enough card.
        hoursRow = ui.row();
        for (int i = 0; i < 3; i++) {
            LinearLayout hour = ui.column();
            hour.setGravity(Gravity.CENTER_HORIZONTAL);
            hourTimes[i] = ui.caption(""); hourTimes[i].setGravity(Gravity.CENTER);
            hour.addView(hourTimes[i], ui.ll(Ui.WRAP, Ui.WRAP));
            hourTemps[i] = ui.body(""); hourTemps[i].setGravity(Gravity.CENTER);
            hour.addView(hourTemps[i], ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 2, 0, 0));
            hoursRow.addView(hour, ui.ll(0, Ui.WRAP, 1));
        }
        hoursRow.setVisibility(View.GONE);
        column.addView(hoursRow, ui.margins(ui.ll(Ui.MATCH, ui.px(54)), 0, 6, 0, 0));

        credit = ui.caption("Weather data by Open-Meteo");
        credit.setGravity(Gravity.BOTTOM);
        column.addView(credit, ui.ll(Ui.MATCH, ui.px(20)));
        FrameLayout card = ui.card(column, 24);
        // The whole card is the refresh tap; without a city it says where to choose one.
        card.setClickable(true);
        card.setContentDescription("Weather. Tap to update.");
        card.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) {
            if (location.isEmpty()) dash.say("Choose a city on the PC: Settings → Weather.");
            else refresh(true);
        } });
        return card;
    }

    @Override public void render() {
        if (!built()) return;
        PcState state = dash.state();
        PcState.Weather w = state == null ? null : state.weather;
        if (w != null && (!w.location.equals(location) || w.latitude != latitude || w.longitude != longitude || w.imperial != imperial)) {
            // A new city or new units on the PC drop the old reading.
            location = w.location; latitude = w.latitude; longitude = w.longitude; imperial = w.imperial; last = null; offline = false; lastAttempt = -1;
        }
        if (location.isEmpty()) {
            temperature.setText("—°"); city.setText(w == null && !dash.online() ? "PC offline" : "No city yet");
            details.setText(w == null && !dash.online() ? dash.offlineText() : "Choose a city on the PC to see the weather here.");
            condition.setText(""); glyph.setVisibility(View.GONE); showHours(null); credit.setText("Weather data by Open-Meteo");
            return;
        }
        city.setText(location);
        if (last != null) {
            temperature.setText(Math.round(last.temperature) + "°");
            // The "updated" time follows the PC clock's 12- or 24-hour choice, as on Windows.
            LocalDateTime retrieved = LocalDateTime.ofInstant(Instant.ofEpochMilli(last.retrievedMs), ZoneId.systemDefault());
            credit.setText(busy ? "Open-Meteo  ·  updating…" : "Open-Meteo  ·  updated " + Format.endsAt(retrieved, dash.use24()));
        } else { temperature.setText("—°"); credit.setText(busy ? "Open-Meteo  ·  updating…" : "Weather data by Open-Meteo"); }
        if (offline) {
            condition.setText("OFFLINE"); glyph.setVisibility(View.GONE);
            details.setText(last == null ? "Can't reach the weather service. Tap the card to try again." : "Showing the last update. Tap the card to try again.");
        } else if (last != null) {
            condition.setText(Weather.describe(last.code)); glyph.setGlyph(Weather.family(last.code)); glyph.setVisibility(View.VISIBLE);
            details.setText(Weather.details(last, imperial));
        } else { condition.setText(""); glyph.setVisibility(View.GONE); details.setText("Updating weather…"); }
        showHours(offline ? null : last);
    }

    private void showHours(Weather.Snapshot snapshot) {
        boolean show = snapshot != null && !snapshot.hours.isEmpty() && height >= 300;
        hoursRow.setVisibility(show ? View.VISIBLE : View.GONE);
        for (int i = 0; i < 3; i++) {
            boolean has = show && i < snapshot.hours.size();
            hourTimes[i].setText(has ? (i == 0 ? "Now" : Weather.hourLabel(snapshot.hours.get(i).time, dash.use24())) : "");
            hourTemps[i].setText(has ? Math.round(snapshot.hours.get(i).temperature) + "°" : "");
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
