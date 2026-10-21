package com.alialyami.widgetscreen.widgets;

import android.os.SystemClock;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Weather;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Http;

import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

// Weather for the city chosen on the PC. The phone asks Open-Meteo itself (it only needs the city's position
// from the PC) and refreshes every 15 minutes while the card is shown.
public final class WeatherWidget extends Widget {
    private TextView condition, temperature, city, details, button, credit;
    private Weather.Snapshot last;
    private boolean busy, offline;
    private String location = "";
    private double latitude, longitude;
    private long lastAttempt = -1;

    public WeatherWidget(Dashboard dash) { super("Weather", dash); }

    @Override public String part() { return "weather"; }

    @Override protected View create() {
        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(ui.label("WEATHER"), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        condition = ui.caption("");
        header.addView(condition, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
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
        text.addView(details, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 5, 0, 0));
        body.addView(text, ui.ll(0, Ui.WRAP, 1));
        column.addView(body, ui.ll(Ui.MATCH, 0, 1));

        button = ui.button("Refresh weather", new View.OnClickListener() { @Override public void onClick(View v) {
            if (location.isEmpty()) dash.say("Choose a city on the PC: Settings → Weather.");
            else refresh(true);
        } });
        button.setTextSize(TypedValue.COMPLEX_UNIT_PX, ui.fpx(16));
        button.setPadding(ui.px(12), ui.px(4), ui.px(12), ui.px(4));
        column.addView(button, ui.ll(Ui.MATCH, ui.px(48)));
        credit = ui.caption("Weather data by Open-Meteo");
        credit.setGravity(Gravity.BOTTOM);
        column.addView(credit, ui.ll(Ui.MATCH, ui.px(20)));
        return ui.card(column, 24);
    }

    @Override public void render() {
        if (!built()) return;
        PcState state = dash.state();
        PcState.Weather w = state == null ? null : state.weather;
        if (w != null && (!w.location.equals(location) || w.latitude != latitude || w.longitude != longitude)) {
            // A new city on the PC drops the old reading.
            location = w.location; latitude = w.latitude; longitude = w.longitude; last = null; offline = false; lastAttempt = -1;
        }
        if (w == null && location.isEmpty()) {
            temperature.setText("—°"); city.setText(dash.online() ? "No city yet" : "PC offline"); details.setText(dash.online() ? "" : dash.offlineText());
            condition.setText(""); button.setText("Choose city"); Ui.enable(button, false); credit.setText("Weather data by Open-Meteo");
            return;
        }
        if (location.isEmpty()) {
            temperature.setText("—°"); city.setText("No city yet"); details.setText("Choose a city on the PC to see current conditions.");
            condition.setText(""); button.setText("Choose city"); Ui.enable(button, true); credit.setText("Weather data by Open-Meteo");
            return;
        }
        city.setText(location);
        Ui.enable(button, !busy);
        button.setText(busy ? "Updating…" : "Refresh weather");
        if (last != null) {
            temperature.setText(Math.round(last.temperature) + "°");
            credit.setText("Open-Meteo  ·  updated " + new SimpleDateFormat("HH:mm", Locale.US).format(new Date(last.retrievedMs)));
        } else temperature.setText("—°");
        if (offline) {
            condition.setText("OFFLINE");
            details.setText(last == null ? "Can't reach the weather service. Tap Refresh to try again." : "Showing the last update. Tap Refresh to try again.");
        } else if (last != null) { condition.setText(Weather.describe(last.code)); details.setText(Weather.details(last)); }
        else { condition.setText(""); details.setText("Updating weather…"); }
    }

    public void refresh(boolean force) {
        if (busy || !active() || location.isEmpty()) return;
        if (!force && lastAttempt >= 0 && SystemClock.elapsedRealtime() - lastAttempt < 15 * 60000L) return;
        busy = true; lastAttempt = SystemClock.elapsedRealtime();
        final String requested = location;
        final double lat = latitude, lon = longitude;
        render();
        Async.run(new Async.Work<Weather.Snapshot>() {
            @Override public Weather.Snapshot run() throws Exception { return Weather.parse(Http.getText(Weather.forecastUrl(lat, lon)), System.currentTimeMillis()); }
        }, new Async.Done<Weather.Snapshot>() {
            @Override public void ok(Weather.Snapshot snapshot) { busy = false; if (requested.equals(location)) { last = snapshot; offline = false; } render(); }
        }, new Async.Failed() {
            @Override public void error(Exception error) { busy = false; if (requested.equals(location)) offline = true; render(); }
        });
    }

    @Override public void tick(int ticks, boolean quiet) { refresh(false); }
}
