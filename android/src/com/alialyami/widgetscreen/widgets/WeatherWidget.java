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
import com.alialyami.widgetscreen.core.Weather;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Http;

import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

// Current conditions for one city from Open-Meteo, refreshed every 15 minutes while the card is shown.
public final class WeatherWidget extends Widget {
    private TextView condition, temperature, city, details, button, credit;
    private Weather.Snapshot last;
    private boolean busy, offline;
    private int generation;
    private long lastAttempt = -1;

    public WeatherWidget(Dashboard dash) { super("Weather", dash); }

    @Override protected View create() {
        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(ui.label("WEATHER"), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        condition = ui.caption("");
        header.addView(condition, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(26)));

        LinearLayout body = ui.row();
        temperature = ui.text("—°", 54, ui.theme.text);
        temperature.setTypeface(Ui.LIGHT);
        body.addView(temperature, ui.ll(ui.px(108), Ui.WRAP));
        LinearLayout text = ui.column();
        boolean compact = height < 250;
        city = Ui.wrap(ui.body("No city yet"), compact ? 1 : 2);
        text.addView(city, ui.ll(Ui.MATCH, Ui.WRAP));
        details = Ui.wrap(ui.smallText("Choose a city to see current conditions."), 2);
        text.addView(details, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 5, 0, 0));
        body.addView(text, ui.ll(0, Ui.WRAP, 1));
        column.addView(body, ui.ll(Ui.MATCH, 0, 1));

        button = ui.button("Choose city", new View.OnClickListener() { @Override public void onClick(View v) {
            if (dash.settings.weatherLocation.trim().isEmpty()) dash.openSettings("Weather"); else refresh(true);
        } });
        button.setTextSize(TypedValue.COMPLEX_UNIT_PX, ui.fpx(16));
        button.setPadding(ui.px(12), ui.px(4), ui.px(12), ui.px(4));
        column.addView(button, ui.ll(Ui.MATCH, ui.px(48)));
        credit = ui.caption("Weather data by Open-Meteo");
        credit.setGravity(Gravity.BOTTOM);
        column.addView(credit, ui.ll(Ui.MATCH, ui.px(20)));
        return ui.card(column, 24);
    }

    // A new city (or weather turned off) drops the old reading.
    public void locationChanged() { generation++; last = null; offline = false; lastAttempt = -1; render(); }

    @Override public void render() {
        if (!built()) return;
        String location = dash.settings.weatherLocation;
        if (location.trim().isEmpty()) {
            temperature.setText("—°"); city.setText("No city yet"); details.setText("Choose a city to see current conditions.");
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
        } else if (last != null) {
            condition.setText(Weather.describe(last.code));
            details.setText(Weather.details(last));
        } else { condition.setText(""); details.setText("Updating weather…"); }
    }

    public void refresh(boolean force) {
        if (busy || !active()) return;
        final String location = dash.settings.weatherLocation;
        if (location.trim().isEmpty()) { render(); return; }
        if (!force && lastAttempt >= 0 && SystemClock.elapsedRealtime() - lastAttempt < 15 * 60000L) return;
        busy = true; lastAttempt = SystemClock.elapsedRealtime();
        final int version = generation;
        final double latitude = dash.settings.latitude, longitude = dash.settings.longitude;
        render();
        Async.run(new Async.Work<Weather.Snapshot>() {
            @Override public Weather.Snapshot run() throws Exception { return Weather.parse(Http.getText(Weather.forecastUrl(latitude, longitude)), System.currentTimeMillis()); }
        }, new Async.Done<Weather.Snapshot>() {
            @Override public void ok(Weather.Snapshot snapshot) { busy = false; if (version == generation) { last = snapshot; offline = false; } render(); if (version != generation) refresh(true); }
        }, new Async.Failed() {
            @Override public void error(Exception error) { busy = false; if (version == generation) offline = true; render(); if (version != generation) refresh(true); }
        });
    }

    @Override public void tick(int ticks, boolean quiet) { refresh(false); }
}
