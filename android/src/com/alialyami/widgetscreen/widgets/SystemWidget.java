package com.alialyami.widgetscreen.widgets;

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
import com.alialyami.widgetscreen.views.ProgressLine;
import com.alialyami.widgetscreen.views.RingGauge;
import com.alialyami.widgetscreen.views.Sparkline;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

// The PC's System stats, the readings chosen on the PC, in the same three designs as the Windows SystemStatsCard
// (CardSizing.stats; docs/design/widget-size-views.md): Small lists the readings with a thin bar under each
// percentage; Medium shows CPU, GPU and RAM as rings over a network and uptime strip; Large gives each reading a tile
// with its recent history, and uptime moves to the heading. The history is kept here, so a new design starts full.
public final class SystemWidget extends Widget {
    private static final int HISTORY = 48;
    private static final double GAP = 12;
    private TextView status, headerUptime;
    private final Map<String, Part> parts = new HashMap<String, Part>();
    private final List<Double> cpuHistory = new ArrayList<Double>(), gpuHistory = new ArrayList<Double>(), ramHistory = new ArrayList<Double>(), downHistory = new ArrayList<Double>(), upHistory = new ArrayList<Double>();
    private String builtFor = "";
    private int design;

    // What one reading is drawn with in the current design; any of these may be missing.
    private static final class Part {
        TextView value, detail, second;
        RingGauge ring;
        ProgressLine bar;
        Sparkline graph;
    }

    public SystemWidget(Dashboard dash) { super("System", dash); }

    @Override public String part() { return "system"; }

    private PcState.System stats() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.system; }

    // Which rows the PC shows; the card is rebuilt when that changes.
    private static String selection(PcState.System s) { return s == null ? "" : (s.showCpu ? "c" : "") + (s.showGpu ? "g" : "") + (s.showRam ? "r" : "") + (s.showNetwork ? "n" : "") + (s.showUptime ? "u" : ""); }
    private static boolean percent(String id) { return id.equals("Cpu") || id.equals("Gpu") || id.equals("Ram"); }
    private static String title(String id) { return id.equals("Cpu") ? "CPU" : id.equals("Gpu") ? "GPU" : id.equals("Ram") ? "RAM" : id.equals("Network") ? "NET" : "UPTIME"; }

    @Override protected View create() {
        PcState.System s = stats();
        builtFor = selection(s);
        parts.clear();
        List<String> shown = new ArrayList<String>();
        if (s == null || s.showCpu) shown.add("Cpu");
        if (s == null || s.showGpu) shown.add("Gpu");
        if (s == null || s.showRam) shown.add("Ram");
        if (s == null || s.showNetwork) shown.add("Network");
        if (s == null || s.showUptime) shown.add("Uptime");
        boolean uptime = shown.contains("Uptime"), network = shown.contains("Network"), anyPercent = false, anyTile = false;
        for (String id : shown) { if (percent(id)) anyPercent = true; if (!id.equals("Uptime")) anyTile = true; }
        design = CardSizing.stats(width, height);
        // Rings need a percentage to show and tiles a reading to graph; otherwise the list says it best.
        if (design == CardSizing.MEDIUM && !anyPercent) design = CardSizing.SMALL;
        if (design == CardSizing.LARGE && !anyTile) design = CardSizing.SMALL;

        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(ui.label("SYSTEM STATS"), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        LinearLayout right = ui.row();
        headerUptime = ui.caption("");
        headerUptime.setVisibility(design == CardSizing.LARGE && uptime ? View.VISIBLE : View.GONE);
        right.addView(headerUptime, ui.ll(Ui.WRAP, Ui.WRAP));
        status = ui.caption("");
        status.setTextColor(Theme.WARNING);
        right.addView(status, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, 0, 0));
        header.addView(right, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));

        // The body: the card less its 24-unit padding, the 28-unit heading and the 8-unit gap under it.
        double bodyWidth = Math.max(1, width - 48), bodyHeight = Math.max(1, height - 48 - 36);
        View body;
        if (shown.isEmpty()) body = ui.caption("No readings chosen on the PC.");
        else if (design == CardSizing.SMALL) body = list(shown, bodyHeight);
        else if (design == CardSizing.MEDIUM) body = rings(shown, bodyWidth, bodyHeight, network, uptime);
        else { List<String> tiles = new ArrayList<String>(shown); tiles.remove("Uptime"); body = tiles(tiles, bodyWidth, bodyHeight, uptime); }
        column.addView(body, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        return ui.card(column, 24);
    }

    private TextView reading(String text, double size) {
        TextView view = Ui.tabular(ui.text(text, (float) size, ui.theme.text));
        view.setTypeface(Ui.LIGHT);
        return view;
    }
    private ProgressLine bar() { return new ProgressLine(ui.context, ui.theme.accent, ui.theme.track); }
    private View tile(View content, double radius, double padX, double padTop, double padBottom) {
        FrameLayout tile = new FrameLayout(ui.context);
        tile.setBackground(ui.shape(ui.theme.raised, 0, 0, radius));
        tile.setPadding(ui.px(padX), ui.px(padTop), ui.px(padX), ui.px(padBottom));
        tile.addView(content, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        return tile;
    }

    // Small: one row per reading, the name at the left and the number at the right, a thin bar under percentages.
    private View list(List<String> shown, double height) {
        LinearLayout list = ui.column();
        double rowHeight = (height - 8 * (shown.size() - 1)) / shown.size(), size = CardSizing.statsListValue(rowHeight);
        for (int i = 0; i < shown.size(); i++) {
            String id = shown.get(i); Part part = new Part(); parts.put(id, part);
            LinearLayout row = ui.column(); row.setGravity(Gravity.CENTER_VERTICAL);
            LinearLayout top = ui.row();
            top.addView(ui.label(title(id)), ui.ll(0, Ui.WRAP, 1));
            if (id.equals("Network")) {
                double line = Math.max(15, Math.min(20, Math.round(size * 0.55)));
                LinearLayout rates = ui.column(); rates.setGravity(Gravity.END);
                part.value = Ui.tabular(ui.text("↓ —", (float) line, ui.theme.text)); rates.addView(part.value, ui.ll(Ui.WRAP, Ui.WRAP));
                part.second = Ui.tabular(ui.text("↑ —", (float) line, ui.theme.text)); rates.addView(part.second, ui.ll(Ui.WRAP, Ui.WRAP));
                top.addView(rates, ui.ll(Ui.WRAP, Ui.WRAP));
            } else {
                part.value = reading("—", id.equals("Uptime") ? Math.round(size * 0.72) : size);
                top.addView(part.value, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, 0, 0));
            }
            row.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
            if (percent(id)) { part.bar = bar(); row.addView(part.bar, ui.margins(ui.ll(Ui.MATCH, ui.px(4)), 0, 4, 0, 0)); }
            list.addView(row, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, i == 0 ? 0 : 8, 0, 0));
        }
        return list;
    }

    // Medium: CPU, GPU and RAM as rings side by side, with the network rates and uptime in a strip under them.
    private View rings(List<String> shown, double width, double height, boolean network, boolean uptime) {
        List<String> rings = new ArrayList<String>();
        for (String id : shown) if (percent(id)) rings.add(id);
        boolean strip = network || uptime;
        LinearLayout view = ui.column();
        double cell = width / rings.size(), diameter = CardSizing.statsRing(cell, height - (strip ? 56 + GAP : 0) - 48);
        LinearLayout row = ui.row();
        for (String id : rings) {
            Part part = new Part(); parts.put(id, part);
            LinearLayout column = ui.column(); column.setGravity(Gravity.CENTER);
            FrameLayout dial = new FrameLayout(ui.context);
            part.ring = new RingGauge(ui.context, ui.theme.accent, ui.theme.track, ui.fpx(Math.max(5, Math.min(12, Math.round(diameter * 0.075)))));
            dial.addView(part.ring, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
            part.value = reading("—", Math.round(diameter * 0.27));
            dial.addView(part.value, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.CENTER));
            column.addView(dial, ui.ll(ui.px(diameter), ui.px(diameter)));
            TextView label = ui.label(title(id)); label.setGravity(Gravity.CENTER);
            column.addView(label, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 8, 0, 0));
            part.detail = ui.caption(" "); part.detail.setGravity(Gravity.CENTER); part.detail.setMaxWidth(ui.px(Math.max(40, cell - 8)));
            column.addView(part.detail, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 2, 0, 0));
            row.addView(column, ui.ll(0, Ui.WRAP, 1));
        }
        LinearLayout ringArea = ui.column(); ringArea.setGravity(Gravity.CENTER_VERTICAL);
        ringArea.addView(row, ui.ll(Ui.MATCH, Ui.WRAP));
        view.addView(ringArea, ui.ll(Ui.MATCH, 0, 1));
        if (strip) {
            LinearLayout line = ui.row();
            if (network) {
                Part part = new Part(); parts.put("Network", part);
                part.value = Ui.tabular(ui.body("↓ —")); line.addView(part.value, ui.ll(0, Ui.WRAP, 1));
                part.second = Ui.tabular(ui.body("↑ —")); line.addView(part.second, ui.margins(ui.ll(0, Ui.WRAP, 1), 12, 0, 0, 0));
            }
            if (uptime) {
                Part part = new Part(); parts.put("Uptime", part);
                part.value = ui.smallText("—");
                line.addView(part.value, network ? ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, 0, 0) : ui.ll(0, Ui.WRAP, 1));
            }
            view.addView(tile(line, 14, 16, 0, 0), ui.margins(ui.ll(Ui.MATCH, ui.px(56)), 0, GAP, 0, 0));
        }
        return view;
    }

    // Large: a tile per reading with its name, a detail, the number and its last 48 readings as a graph.
    private View tiles(List<String> shown, double width, double height, boolean uptime) {
        int columns = CardSizing.statsTileColumns(width, shown.size()), rows = (shown.size() + columns - 1) / columns;
        double tileHeight = (height - GAP * (rows - 1)) / rows, size = CardSizing.statsTileValue(tileHeight);
        LinearLayout view = ui.column();
        for (int r = 0; r < rows; r++) {
            LinearLayout line = ui.row(); line.setGravity(Gravity.FILL_VERTICAL);
            int count = Math.min(columns, shown.size() - r * columns);
            for (int c = 0; c < count; c++) {
                String id = shown.get(r * columns + c); Part part = new Part(); parts.put(id, part);
                LinearLayout content = ui.column();
                LinearLayout top = ui.row();
                top.addView(ui.label(title(id)), ui.ll(Ui.WRAP, Ui.WRAP));
                part.detail = ui.caption(""); part.detail.setGravity(Gravity.END);
                top.addView(part.detail, ui.margins(ui.ll(0, Ui.WRAP, 1), 8, 0, 0, 0));
                content.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
                part.value = reading("—", id.equals("Network") ? Math.round(size * 0.66) : size);
                content.addView(part.value, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
                part.graph = new Sparkline(ui.context, ui.theme.accent, ui.theme.track, id.equals("Network") ? ui.theme.muted : 0, ui.fpx(2), id.equals("Network") ? 0 : 100);
                content.addView(part.graph, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
                line.addView(tile(content, 16, 16, 14, 12), ui.margins(ui.ll(0, Ui.MATCH, 1), c == 0 ? 0 : GAP / 2, 0, c == count - 1 ? 0 : GAP / 2, 0));
            }
            view.addView(line, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, r == 0 ? 0 : GAP, 0, 0));
        }
        if (uptime) { Part part = new Part(); part.value = headerUptime; parts.put("Uptime", part); }
        return view;
    }

    // A new reading from the PC adds to the history; rebuilds and redraws don't.
    @Override public void stateChanged() {
        PcState.System s = stats();
        boolean ok = s != null && !s.error;
        push(cpuHistory, ok ? s.cpu : null);
        push(gpuHistory, ok ? s.gpu : null);
        push(ramHistory, ok ? s.memoryPercent : null);
        push(downHistory, ok ? s.download : null);
        push(upHistory, ok ? s.upload : null);
        render();
    }
    private static void push(List<Double> history, Double value) {
        if (value == null) { history.clear(); return; }
        history.add(value); if (history.size() > HISTORY) history.remove(0);
    }

    // Fills the current design from the last reading. A reading at 90% or more turns to the warning colour.
    @Override public void render() {
        if (!built()) return;
        PcState.System s = stats();
        if (s != null && !s.error && !selection(s).equals(builtFor)) { dash.rebuildWidget(id); return; }
        boolean ok = s != null && !s.error;
        status.setText(!dash.online() ? "PC OFFLINE" : ok ? "" : "UNAVAILABLE");
        percent("Cpu", ok ? s.cpu : null, null, cpuHistory);
        percent("Gpu", ok ? s.gpu : null, ok ? s.gpuStatus : "Unavailable", gpuHistory);
        percent("Ram", ok ? s.memoryPercent : null, ok && s.memoryPercent != null ? String.format(Locale.US, "%.1f / %.1f GB", s.memoryUsedGB, s.memoryTotalGB) : "Unavailable", ramHistory);
        Part part = parts.get("Network");
        if (part != null) {
            String down = ok && s.download != null ? Format.rate(s.download) : "— KB/s", up = ok && s.upload != null ? Format.rate(s.upload) : "— KB/s";
            part.value.setText("↓ " + down);
            if (part.second != null) part.second.setText("↑ " + up);
            if (part.detail != null) part.detail.setText("↑ " + up);
            if (part.graph != null) part.graph.setValues(downHistory, upHistory, HISTORY);
        }
        part = parts.get("Uptime");
        if (part != null) {
            String text = ok ? Format.uptime((long) s.uptimeMs, design == CardSizing.MEDIUM && width < 440 ? 2 : 3) : "—";
            part.value.setText(design == CardSizing.LARGE && ok ? "Up " + text : text);
        }
    }
    private void percent(String id, Double value, String detail, List<Double> history) {
        Part part = parts.get(id);
        if (part == null) return;
        double reading = value == null ? 0 : value;
        int color = reading >= 90 ? Theme.WARNING : ui.theme.accent;
        part.value.setText(value == null ? "—" : Math.round(reading) + "%");
        part.value.setTextColor(reading >= 90 ? Theme.WARNING : ui.theme.text);
        if (part.detail != null) part.detail.setText(detail == null ? "" : detail);
        if (part.ring != null) { part.ring.setValue(reading); part.ring.setFill(color); }
        if (part.bar != null) { part.bar.setValue(reading / 100); part.bar.setFill(color); }
        if (part.graph != null) { part.graph.setColor(color); part.graph.setValues(history, HISTORY); }
    }
}
