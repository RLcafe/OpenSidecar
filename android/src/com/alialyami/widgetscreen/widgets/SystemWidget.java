package com.alialyami.widgetscreen.widgets;

import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.AppSettings;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.core.StatsLayout;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.integrations.SystemReader;
import com.alialyami.widgetscreen.views.FitBox;
import com.alialyami.widgetscreen.views.ProgressLine;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

// System stats: battery, memory, storage, network and uptime rows at one design size, scaled to the card.
public final class SystemWidget extends Widget {
    private static final double ROW_MIN_WIDTH = 344, ROW_HEIGHT = 40, ROW_MARGIN_X = 6, ROW_MARGIN_Y = 3;
    private TextView status, batteryValue, batteryDetail, memoryValue, memoryDetail, storageValue, storageDetail, download, upload, uptime;
    private ProgressLine batteryBar, memoryBar, storageBar;

    public SystemWidget(Dashboard dash) { super("System", dash); }

    @Override protected View create() {
        AppSettings s = dash.settings;
        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(ui.label("SYSTEM STATS"), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        status = ui.caption("");
        status.setTextColor(Theme.WARNING);
        header.addView(status, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));

        List<View> rows = new ArrayList<View>();
        int count = (s.showBattery ? 1 : 0) + (s.showRam ? 1 : 0) + (s.showStorage ? 1 : 0) + (s.showNetwork ? 1 : 0) + (s.showUptime ? 1 : 0);
        double availableWidth = Math.max(1, width - 48), availableHeight = Math.max(1, height - 48 - 28 - 8);
        double cellWidth = ROW_MIN_WIDTH + 2 * ROW_MARGIN_X, cellHeight = ROW_HEIGHT + 2 * ROW_MARGIN_Y;
        int columns = StatsLayout.bestColumns(availableWidth, availableHeight, cellWidth, cellHeight, Math.max(1, count));
        double rowWidth = StatsLayout.rowWidth(availableWidth, availableHeight, ROW_MIN_WIDTH, 2 * ROW_MARGIN_X, cellHeight, Math.max(1, count), columns);
        if (s.showBattery) {
            batteryValue = value("—", 30); batteryBar = bar("Battery level"); batteryDetail = detail("Reading battery…");
            rows.add(meter("BATTERY", batteryValue, batteryBar, batteryDetail));
        }
        if (s.showRam) {
            memoryValue = value("—", 30); memoryBar = bar("RAM in use"); memoryDetail = detail("— / — GB");
            rows.add(meter("RAM", memoryValue, memoryBar, memoryDetail));
        }
        if (s.showStorage) {
            storageValue = value("—", 30); storageBar = bar("Storage used"); storageDetail = detail("— / — GB");
            rows.add(meter("STORAGE", storageValue, storageBar, storageDetail));
        }
        if (s.showNetwork) {
            LinearLayout row = ui.row();
            row.addView(name("NET"), ui.ll(ui.px(96), Ui.WRAP));
            download = value("↓ — KB/s", 21); download.setContentDescription("Download rate");
            row.addView(download, ui.ll(0, Ui.WRAP, 1));
            upload = value("↑ — KB/s", 21); upload.setContentDescription("Upload rate");
            row.addView(upload, ui.ll(0, Ui.WRAP, 1));
            rows.add(row);
        }
        if (s.showUptime) {
            LinearLayout row = ui.row();
            row.addView(name("UPTIME"), ui.ll(ui.px(96), Ui.WRAP));
            uptime = value("—", 21);
            row.addView(uptime, ui.ll(0, Ui.WRAP, 1));
            rows.add(row);
        }
        LinearLayout grid = ui.column();
        LinearLayout line = null;
        for (int i = 0; i < rows.size(); i++) {
            if (i % columns == 0) { line = ui.row(); grid.addView(line, ui.ll(Ui.WRAP, Ui.WRAP)); }
            LinearLayout.LayoutParams params = ui.ll(ui.px(rowWidth), ui.px(ROW_HEIGHT));
            params.setMargins(ui.px(ROW_MARGIN_X), ui.px(ROW_MARGIN_Y), ui.px(ROW_MARGIN_X), ui.px(ROW_MARGIN_Y));
            line.addView(rows.get(i), params);
        }
        column.addView(new FitBox(ui.context, grid), ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        return ui.card(column, 24);
    }

    private TextView name(String text) { TextView view = ui.text(text, 16, ui.theme.muted); view.setTypeface(Ui.MEDIUM); return view; }
    private TextView value(String text, float size) { return Ui.tabular(ui.text(text, size, ui.theme.text)); }
    private TextView detail(String text) { return ui.text(text, 14, ui.theme.muted); }
    private ProgressLine bar(String description) { ProgressLine bar = new ProgressLine(ui.context, ui.theme.accent, ui.theme.track); bar.setContentDescription(description); return bar; }

    private View meter(String label, TextView value, ProgressLine bar, TextView detail) {
        LinearLayout row = ui.row();
        row.addView(name(label), ui.ll(ui.px(96), Ui.WRAP));
        row.addView(value, ui.ll(ui.px(96), Ui.WRAP));
        LinearLayout right = ui.column();
        right.addView(bar, ui.ll(Ui.MATCH, ui.px(8)));
        right.addView(detail, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 5, 0, 0));
        row.addView(right, ui.ll(0, Ui.WRAP, 1));
        return row;
    }

    // A reading that needs attention (90% full, or 15% battery and not charging) takes the warning color.
    private void load(TextView view, boolean warn) { view.setTextColor(warn ? Theme.WARNING : ui.theme.text); }

    public void refresh() {
        if (!active()) return;
        AppSettings s = dash.settings;
        SystemReader.Snapshot snapshot;
        try { snapshot = dash.system.read(s.showBattery, s.showRam, s.showStorage, s.showNetwork, s.showUptime); }
        catch (Exception e) { status.setText("UNAVAILABLE"); return; }
        status.setText("");
        if (s.showBattery && batteryValue != null) {
            if (snapshot.batteryAvailable) {
                batteryValue.setText(snapshot.batteryPercent + "%");
                load(batteryValue, snapshot.batteryPercent <= 15 && !snapshot.charging);
                batteryBar.setValue(snapshot.batteryPercent / 100.0);
                batteryDetail.setText((snapshot.charging ? "Charging" : "On battery") + (Double.isNaN(snapshot.batteryTemperature) ? "" : String.format(Locale.US, "  ·  %.0f°C", snapshot.batteryTemperature)));
            } else { batteryValue.setText("—"); batteryBar.setValue(0); batteryDetail.setText("Unavailable"); }
        }
        if (s.showRam && memoryValue != null) {
            if (snapshot.memoryAvailable) {
                double percent = 100.0 * snapshot.memoryUsed / snapshot.memoryTotal;
                memoryValue.setText(Format.percent(percent)); load(memoryValue, percent >= 90);
                memoryBar.setValue(percent / 100); memoryDetail.setText(Format.gigabytes(snapshot.memoryUsed) + " / " + Format.gigabytes(snapshot.memoryTotal) + " GB");
            } else { memoryValue.setText("—"); memoryBar.setValue(0); memoryDetail.setText("Unavailable"); }
        }
        if (s.showStorage && storageValue != null) {
            if (snapshot.storageAvailable) {
                double percent = 100.0 * snapshot.storageUsed / snapshot.storageTotal;
                storageValue.setText(Format.percent(percent)); load(storageValue, percent >= 90);
                storageBar.setValue(percent / 100); storageDetail.setText(Math.round(snapshot.storageUsed / 1e9) + " / " + Math.round(snapshot.storageTotal / 1e9) + " GB");
            } else { storageValue.setText("—"); storageBar.setValue(0); storageDetail.setText("Unavailable"); }
        }
        if (s.showNetwork && download != null) {
            download.setText("↓ " + (snapshot.networkAvailable ? Format.rate(snapshot.download) : "— KB/s"));
            upload.setText("↑ " + (snapshot.networkAvailable ? Format.rate(snapshot.upload) : "— KB/s"));
        }
        if (s.showUptime && uptime != null) uptime.setText(Format.uptime(snapshot.uptimeMs));
    }

    @Override public void render() { refresh(); }

    @Override public void tick(int ticks, boolean quiet) { if (ticks % (quiet ? 5 : 2) == 0) refresh(); }
}
