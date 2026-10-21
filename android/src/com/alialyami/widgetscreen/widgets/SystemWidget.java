package com.alialyami.widgetscreen.widgets;

import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.StatsLayout;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.views.FitBox;
import com.alialyami.widgetscreen.views.ProgressLine;
import com.alialyami.widgetscreen.views.Sparkline;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

// The PC's System stats: CPU (with its recent history), GPU, RAM, network and uptime, the readings chosen on
// the PC. Rows have one design size and scale with the card, like SystemStatsLayout on Windows.
public final class SystemWidget extends Widget {
    private static final double ROW_MIN_WIDTH = 324, ROW_HEIGHT = 40, ROW_MARGIN_X = 6, ROW_MARGIN_Y = 3;
    private TextView status, cpuValue, gpuValue, gpuDetail, memoryValue, memoryDetail, download, upload, uptime;
    private ProgressLine gpuBar, memoryBar;
    private Sparkline cpuGraph;
    private final List<Double> cpuHistory = new ArrayList<Double>();
    private String builtFor = "";

    public SystemWidget(Dashboard dash) { super("System", dash); }

    @Override public String part() { return "system"; }

    private PcState.System stats() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.system; }

    // Which rows the PC shows; the card is rebuilt when that changes.
    private static String selection(PcState.System s) { return s == null ? "" : (s.showCpu ? "c" : "") + (s.showGpu ? "g" : "") + (s.showRam ? "r" : "") + (s.showNetwork ? "n" : "") + (s.showUptime ? "u" : ""); }

    @Override protected View create() {
        PcState.System s = stats();
        builtFor = selection(s);
        boolean cpu = s == null || s.showCpu, gpu = s == null || s.showGpu, ram = s == null || s.showRam, net = s == null || s.showNetwork, up = s == null || s.showUptime;
        cpuValue = gpuValue = memoryValue = download = upload = uptime = null;
        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(ui.label("SYSTEM STATS"), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        status = ui.caption("");
        status.setTextColor(Theme.WARNING);
        header.addView(status, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));

        List<View> rows = new ArrayList<View>();
        if (cpu) {
            LinearLayout row = ui.row();
            row.addView(name("CPU"), ui.ll(ui.px(76), Ui.WRAP));
            cpuValue = value("—", 30);
            row.addView(cpuValue, ui.ll(ui.px(96), Ui.WRAP));
            cpuGraph = new Sparkline(ui.context, ui.theme.accent, ui.fpx(2));
            row.addView(cpuGraph, ui.ll(0, ui.px(32), 1));
            rows.add(row);
        }
        if (gpu) {
            gpuValue = value("—", 30); gpuBar = bar("GPU utilization"); gpuDetail = detail("Sampling…");
            rows.add(meter("GPU", gpuValue, gpuBar, gpuDetail));
        }
        if (ram) {
            memoryValue = value("—", 30); memoryBar = bar("RAM utilization"); memoryDetail = detail("— / — GB");
            rows.add(meter("RAM", memoryValue, memoryBar, memoryDetail));
        }
        if (net) {
            LinearLayout row = ui.row();
            row.addView(name("NET"), ui.ll(ui.px(76), Ui.WRAP));
            download = value("↓ — KB/s", 21); download.setContentDescription("Download rate");
            row.addView(download, ui.ll(0, Ui.WRAP, 1));
            upload = value("↑ — KB/s", 21); upload.setContentDescription("Upload rate");
            row.addView(upload, ui.ll(0, Ui.WRAP, 1));
            rows.add(row);
        }
        if (up) {
            LinearLayout row = ui.row();
            row.addView(name("UPTIME"), ui.ll(ui.px(76), Ui.WRAP));
            uptime = value("—", 21);
            row.addView(uptime, ui.ll(0, Ui.WRAP, 1));
            rows.add(row);
        }
        int count = Math.max(1, rows.size());
        double availableWidth = Math.max(1, width - 48), availableHeight = Math.max(1, height - 48 - 28 - 8);
        double cellHeight = ROW_HEIGHT + 2 * ROW_MARGIN_Y;
        int columns = StatsLayout.bestColumns(availableWidth, availableHeight, ROW_MIN_WIDTH + 2 * ROW_MARGIN_X, cellHeight, count);
        double rowWidth = StatsLayout.rowWidth(availableWidth, availableHeight, ROW_MIN_WIDTH, 2 * ROW_MARGIN_X, cellHeight, count, columns);
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
        row.addView(name(label), ui.ll(ui.px(76), Ui.WRAP));
        row.addView(value, ui.ll(ui.px(96), Ui.WRAP));
        LinearLayout right = ui.column();
        right.addView(bar, ui.ll(Ui.MATCH, ui.px(8)));
        right.addView(detail, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 5, 0, 0));
        row.addView(right, ui.ll(0, Ui.WRAP, 1));
        return row;
    }

    // A reading at 90% or more switches to the warning color so it stands out at a glance.
    private void load(TextView view, double percent) { view.setTextColor(percent >= 90 ? Theme.WARNING : ui.theme.text); }
    private static String percent(Double value) { return value == null ? "—" : Math.round(value) + "%"; }

    @Override public void render() {
        if (!built()) return;
        PcState.System s = stats();
        if (s != null && !s.error && !selection(s).equals(builtFor)) { dash.rebuildWidget(id); return; }
        boolean ok = s != null && !s.error;
        status.setText(!dash.online() ? "PC OFFLINE" : ok ? "" : "UNAVAILABLE");
        if (cpuValue != null) {
            cpuValue.setText(ok ? percent(s.cpu) : "—"); load(cpuValue, ok && s.cpu != null ? s.cpu : 0);
            if (ok && s.cpu != null) { cpuHistory.add(s.cpu); if (cpuHistory.size() > 48) cpuHistory.remove(0); } else cpuHistory.clear();
            cpuGraph.setValues(cpuHistory, 48);
        }
        if (gpuValue != null) {
            gpuValue.setText(ok ? percent(s.gpu) : "—"); load(gpuValue, ok && s.gpu != null ? s.gpu : 0);
            gpuBar.setValue(ok && s.gpu != null ? s.gpu / 100 : 0); gpuDetail.setText(ok ? s.gpuStatus : "Unavailable");
        }
        if (memoryValue != null) {
            memoryValue.setText(ok ? percent(s.memoryPercent) : "—"); load(memoryValue, ok && s.memoryPercent != null ? s.memoryPercent : 0);
            memoryBar.setValue(ok && s.memoryPercent != null ? s.memoryPercent / 100 : 0);
            memoryDetail.setText(ok && s.memoryPercent != null ? String.format(Locale.US, "%.1f / %.1f GB", s.memoryUsedGB, s.memoryTotalGB) : "Unavailable");
        }
        if (download != null) {
            download.setText("↓ " + (ok && s.download != null ? Format.rate(s.download) : "— KB/s"));
            upload.setText("↑ " + (ok && s.upload != null ? Format.rate(s.upload) : "— KB/s"));
        }
        if (uptime != null) uptime.setText(ok ? Format.uptime((long) s.uptimeMs) : "—");
    }
}
