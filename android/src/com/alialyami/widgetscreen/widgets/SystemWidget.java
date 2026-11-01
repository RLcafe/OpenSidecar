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
import com.alialyami.widgetscreen.core.SystemText;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.views.ArcGauge;
import com.alialyami.widgetscreen.views.HistoryBars;
import com.alialyami.widgetscreen.views.ProgressLine;
import com.alialyami.widgetscreen.views.RingGauge;
import com.alialyami.widgetscreen.views.Sparkline;
import com.alialyami.widgetscreen.views.Stickers;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

// The PC's System stats, the readings and graph styles chosen on the PC, in the same three designs as the Windows
// SystemStatsCard (CardSizing.stats; docs/design/system-sensors.md): Small lists the readings, in columns when there are
// many, and counts any that don't fit; Medium gives each reading a cell in its style (ring, gauge, line, bars or
// sparkline) over a network and uptime strip; Large gives each a tile, and uptime moves to the heading. A design that
// can't fit its readings steps down. A reading the PC can't give says why. The history is kept here.
public final class SystemWidget extends Widget {
    private static final int HISTORY = 48;
    private static final double GAP = 12;
    private TextView status, headerUptime;
    private Stickers.Sticker peak;
    private final Map<String, Part> parts = new HashMap<String, Part>();
    private final Map<String, List<Double>> history = new HashMap<String, List<Double>>();
    private final List<Double> upHistory = new ArrayList<Double>();
    private String builtFor = "";
    private int design;

    // What one reading is drawn with in the current design; any of these may be missing.
    private static final class Part {
        String style;
        TextView value, detail, second, badge, range;
        RingGauge ring;
        ArcGauge gauge;
        ProgressLine bar;
        Sparkline graph;
        HistoryBars bars;
    }

    public SystemWidget(Dashboard dash) {
        super("System", dash);
        for (String id : SystemText.IDS) history.put(id, new ArrayList<Double>());
    }

    @Override public String part() { return "system"; }

    private PcState.System stats() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.system; }

    // Which readings and styles the PC shows; the card is rebuilt when they change.
    private static String selection(PcState.System s) {
        if (s == null) return "";
        StringBuilder key = new StringBuilder(s.fahrenheit ? "F" : "C");
        for (String id : SystemText.IDS) if (SystemText.shown(s, id)) key.append(id).append('=').append(SystemText.style(s, id)).append(';');
        return key.toString();
    }
    private String style(PcState.System s, String id) { return SystemText.resolve(SystemText.style(s, id), id, design); }

    @Override protected View create() {
        PcState.System s = stats();
        builtFor = selection(s);
        parts.clear();
        List<String> shown = SystemText.shownIds(s);
        List<String> visuals = new ArrayList<String>();
        for (String id : shown) if (!id.equals("Network") && !id.equals("Uptime")) visuals.add(id);
        boolean uptime = shown.contains("Uptime"), network = shown.contains("Network");
        double bodyWidth = Math.max(1, width - 48), bodyHeight = Math.max(1, height - 48 - 36);
        int tiles = shown.size() - (uptime ? 1 : 0);
        design = CardSizing.stats(width, height);
        // Each design steps down when its readings wouldn't fit at a readable size.
        if (design == CardSizing.LARGE && (tiles == 0 || CardSizing.statsTileGrid(bodyWidth, bodyHeight, tiles, ui.textScale) == null)) design = CardSizing.MEDIUM;
        if (design == CardSizing.MEDIUM && (visuals.isEmpty() || CardSizing.statsCellGrid(bodyWidth, bodyHeight - (network || uptime ? 56 + GAP : 0), visuals.size(), ui.textScale) == null)) design = CardSizing.SMALL;

        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(strip(), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        LinearLayout right = ui.row();
        headerUptime = sticker(false);
        headerUptime.setVisibility(design == CardSizing.LARGE && uptime ? View.VISIBLE : View.GONE);
        right.addView(headerUptime, ui.ll(Ui.WRAP, Ui.WRAP));
        // The busiest of CPU, GPU, RAM and VRAM as a fact, filled from 80% (Sticker Sheet phase 3).
        peak = sticker(false);
        right.addView(peak, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, 0, 0));
        status = sticker(true);
        right.addView(status, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, 0, 0));
        header.addView(right, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));

        View body;
        if (shown.isEmpty()) body = ui.caption("No readings chosen on the PC.");
        else if (design == CardSizing.SMALL) body = list(shown, bodyWidth, bodyHeight);
        else if (design == CardSizing.MEDIUM) body = cells(s, visuals, bodyWidth, bodyHeight, network, uptime);
        else { List<String> ids = new ArrayList<String>(shown); ids.remove("Uptime"); body = tiles(s, ids, bodyWidth, bodyHeight, uptime); }
        column.addView(body, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        return ui.card(column, 24);
    }

    private TextView reading(String text, double size) {
        TextView view = Ui.tabular(ui.text(text, (float) size, ui.theme.text));
        view.setTypeface(Ui.LIGHT); view.setSingleLine(true); view.setEllipsize(android.text.TextUtils.TruncateAt.END);
        return view;
    }
    private TextView caption(String text) { TextView view = ui.caption(text); view.setSingleLine(true); view.setEllipsize(android.text.TextUtils.TruncateAt.END); return view; }
    private ProgressLine bar() { return new ProgressLine(ui.context, ui.theme.accent, ui.theme.track); }
    private View tile(View content, double radius, double padX, double padTop, double padBottom) {
        FrameLayout tile = new FrameLayout(ui.context);
        tile.setBackground(ui.shape(ui.theme.raised, 0, 0, radius));
        tile.setPadding(ui.px(padX), ui.px(padTop), ui.px(padX), ui.px(padBottom));
        tile.addView(content, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        return tile;
    }

    // Small: a row per reading in as many columns as it takes; a full card counts the readings left over.
    private View list(List<String> shown, double width, double height) {
        int[] grid = CardSizing.statsListGrid(width, height, shown.size(), ui.textScale);
        int columns = grid[0], rows = grid[1], fits = grid[2];
        double rowHeight = (height - 8 * (rows - 1)) / rows, size = CardSizing.statsListValue(rowHeight);
        LinearLayout list = ui.row(); list.setGravity(Gravity.TOP);
        for (int c = 0; c < columns; c++) {
            LinearLayout column = ui.column();
            for (int r = 0; r < rows; r++) {
                int i = c * rows + r;
                if (i >= shown.size() || i > fits) break;
                if (i == fits) {
                    TextView more = ui.caption((shown.size() - fits) + " more: make the card bigger");
                    column.addView(more, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, r == 0 ? 0 : 8, 0, 0));
                    break;
                }
                String id = shown.get(i); Part part = new Part(); part.style = "List"; parts.put(id, part);
                LinearLayout row = ui.column(); row.setGravity(Gravity.CENTER_VERTICAL);
                LinearLayout top = ui.row();
                top.addView(ui.label(SystemText.title(id)), ui.ll(Ui.WRAP, Ui.WRAP));
                if (id.equals("Network")) {
                    double line = Math.max(15, Math.min(20, Math.round(size * 0.55)));
                    LinearLayout rates = ui.column(); rates.setGravity(Gravity.END);
                    part.value = Ui.tabular(ui.text("↓ —", (float) line, ui.theme.text)); rates.addView(part.value, ui.ll(Ui.WRAP, Ui.WRAP));
                    part.second = Ui.tabular(ui.text("↑ —", (float) line, ui.theme.text)); rates.addView(part.second, ui.ll(Ui.WRAP, Ui.WRAP));
                    top.addView(rates, ui.ll(0, Ui.WRAP, 1));
                } else {
                    part.value = reading("—", id.equals("Uptime") ? Math.round(size * 0.72) : size); part.value.setGravity(Gravity.END);
                    top.addView(part.value, ui.margins(ui.ll(0, Ui.WRAP, 1), 12, 0, 0, 0));
                }
                row.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
                if (!id.equals("Network") && !id.equals("Uptime")) { part.bar = bar(); row.addView(part.bar, ui.margins(ui.ll(Ui.MATCH, ui.px(4)), 0, 4, 0, 0)); }
                column.addView(row, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, r == 0 ? 0 : 8, 0, 0));
            }
            // Unused rows keep the rows in a short column at their height.
            int used = Math.min(rows, Math.max(0, Math.min(shown.size(), fits + 1) - c * rows));
            for (int r = used; r < rows; r++) column.addView(new View(ui.context), ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
            list.addView(column, ui.margins(ui.ll(0, Ui.MATCH, 1), c == 0 ? 0 : 8, 0, c == columns - 1 ? 0 : 8, 0));
        }
        return list;
    }

    // Medium: a cell per reading in its style, with the network rates and uptime in a strip under them.
    private View cells(PcState.System s, List<String> visuals, double width, double height, boolean network, boolean uptime) {
        boolean strip = network || uptime;
        double room = height - (strip ? 56 + GAP : 0);
        int[] grid = CardSizing.statsCellGrid(width, room, visuals.size(), ui.textScale);
        int columns = grid[0], rows = grid[1];
        double cellWidth = width / columns, cellHeight = (room - GAP * (rows - 1)) / rows;
        LinearLayout view = ui.column();
        LinearLayout area = ui.column();
        for (int r = 0; r < rows; r++) {
            LinearLayout line = ui.row();
            for (int c = 0; c < columns; c++) {
                int i = r * columns + c;
                View cell;
                if (i < visuals.size()) {
                    String id = visuals.get(i); Part part = new Part(); part.style = style(s, id); parts.put(id, part);
                    cell = SystemText.isDial(part.style) ? dialCell(id, part, cellWidth, cellHeight) : graphCell(id, part, cellHeight);
                } else cell = new View(ui.context);
                line.addView(cell, ui.margins(ui.ll(0, Ui.MATCH, 1), 4, 0, 4, 0));
            }
            area.addView(line, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, r == 0 ? 0 : GAP, 0, 0));
        }
        view.addView(area, ui.ll(Ui.MATCH, 0, 1));
        if (strip) {
            LinearLayout line = ui.row();
            if (network) {
                Part part = new Part(); part.style = "Strip"; parts.put("Network", part);
                part.value = Ui.tabular(ui.body("↓ —")); line.addView(part.value, ui.ll(0, Ui.WRAP, 1));
                part.second = Ui.tabular(ui.body("↑ —")); line.addView(part.second, ui.margins(ui.ll(0, Ui.WRAP, 1), 12, 0, 0, 0));
            }
            if (uptime) {
                Part part = new Part(); part.style = "Strip"; parts.put("Uptime", part);
                part.value = ui.smallText("—");
                line.addView(part.value, network ? ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, 0, 0) : ui.ll(0, Ui.WRAP, 1));
            }
            view.addView(tile(line, 14, 16, 0, 0), ui.margins(ui.ll(Ui.MATCH, ui.px(56)), 0, GAP, 0, 0));
        }
        return view;
    }
    private View dialCell(String id, Part part, double cellWidth, double cellHeight) {
        LinearLayout column = ui.column(); column.setGravity(Gravity.CENTER);
        double diameter = CardSizing.statsRing(cellWidth, cellHeight - CardSizing.statsDialText(ui.textScale));
        column.addView(dial(id, part, diameter), ui.ll(ui.px(diameter), ui.px(diameter)));
        TextView label = ui.label(SystemText.title(id)); label.setGravity(Gravity.CENTER);
        column.addView(label, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 6, 0, 0));
        part.detail = caption(" "); part.detail.setGravity(Gravity.CENTER); part.detail.setMaxWidth(ui.px(Math.max(40, cellWidth - 8)));
        column.addView(part.detail, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 2, 0, 0));
        return column;
    }
    private View dial(String id, Part part, double diameter) {
        FrameLayout dial = new FrameLayout(ui.context);
        float thickness = ui.fpx(Math.max(5, Math.min(12, Math.round(diameter * 0.075))));
        if (part.style.equals("Gauge")) { part.gauge = new ArcGauge(ui.context, ui.theme.accent, ui.theme.track, thickness); dial.addView(part.gauge, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH)); }
        else { part.ring = new RingGauge(ui.context, ui.theme.accent, ui.theme.track, thickness); dial.addView(part.ring, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH)); }
        part.value = reading("—", Math.round(diameter * (part.style.equals("Gauge") ? 0.25 : 0.27)));
        dial.addView(part.value, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.CENTER));
        // CPU and GPU rings wear their temperature as a badge sticker at the top right.
        if (id.equals("Cpu") || id.equals("Gpu")) {
            part.badge = Ui.tabular(ui.text("", (float) Math.max(13, Math.round(diameter * 0.1)), Theme.INK_TEXT));
            part.badge.setTypeface(Stickers.BOLD); part.badge.setIncludeFontPadding(false); part.badge.setSingleLine(true);
            part.badge.setBackground(ui.shape(dash.ink(this.id), ui.theme.surface, 2, 10));
            part.badge.setPadding(ui.px(6), ui.px(1), ui.px(6), ui.px(1));
            part.badge.setVisibility(View.GONE);
            dial.addView(part.badge, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.TOP | Gravity.END));
        }
        return dial;
    }
    private View graphCell(String id, Part part, double cellHeight) {
        LinearLayout cell = ui.column();
        LinearLayout top = ui.row();
        top.addView(ui.label(SystemText.title(id)), ui.ll(Ui.WRAP, Ui.WRAP));
        part.value = reading("—", Math.max(20, Math.min(32, Math.round(cellHeight * 0.22)))); part.value.setGravity(Gravity.END);
        top.addView(part.value, ui.margins(ui.ll(0, Ui.WRAP, 1), 8, 0, 0, 0));
        cell.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
        cell.addView(graph(id, part), ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 6, 0, 4));
        part.detail = caption(" ");
        cell.addView(part.detail, ui.ll(Ui.MATCH, Ui.WRAP));
        return cell;
    }
    private View graph(String id, Part part) {
        double maximum = id.equals("Network") || id.equals("Fps") ? 0 : 100;
        int second = id.equals("Network") ? ui.theme.muted : 0;
        if (part.style.equals("Bars")) { part.bars = new HistoryBars(ui.context, ui.theme.accent, ui.theme.track, second, maximum); return part.bars; }
        boolean spark = part.style.equals("Sparkline");
        part.graph = new Sparkline(ui.context, ui.theme.accent, spark ? 0 : ui.theme.track, second, ui.fpx(spark ? 1.5 : 2), maximum);
        return part.graph;
    }

    // Large: a tile per reading with its name, a detail and the value over its graph, or a big dial.
    private View tiles(PcState.System s, List<String> ids, double width, double height, boolean uptime) {
        int[] grid = CardSizing.statsTileGrid(width, height, ids.size(), ui.textScale);
        int columns = grid[0], rows = grid[1];
        double tileWidth = (width - GAP * (columns - 1)) / columns, tileHeight = (height - GAP * (rows - 1)) / rows, size = CardSizing.statsTileValue(tileHeight);
        LinearLayout view = ui.column();
        for (int r = 0; r < rows; r++) {
            LinearLayout line = ui.row(); line.setGravity(Gravity.FILL_VERTICAL);
            for (int c = 0; c < columns; c++) {
                int i = r * columns + c;
                if (i >= ids.size()) { line.addView(new View(ui.context), ui.margins(ui.ll(0, Ui.MATCH, 1), GAP / 2, 0, 0, 0)); continue; }
                String id = ids.get(i); Part part = new Part(); part.style = style(s, id); parts.put(id, part);
                LinearLayout content = ui.column();
                LinearLayout top = ui.row();
                top.addView(ui.label(SystemText.title(id)), ui.ll(Ui.WRAP, Ui.WRAP));
                part.detail = caption(""); part.detail.setGravity(Gravity.END);
                top.addView(part.detail, ui.margins(ui.ll(0, Ui.WRAP, 1), 8, 0, 0, 0));
                content.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
                if (SystemText.isDial(part.style)) {
                    double diameter = CardSizing.statsRing(tileWidth - 32, tileHeight - 26 - 40 - 20);
                    FrameLayout holder = new FrameLayout(ui.context);
                    holder.addView(dial(id, part, diameter), ui.frame(ui.px(diameter), ui.px(diameter), Gravity.CENTER));
                    content.addView(holder, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 6, 0, 0));
                } else {
                    part.value = reading("—", id.equals("Network") ? Math.round(size * 0.66) : size);
                    content.addView(part.value, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
                    content.addView(graph(id, part), ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
                }
                // The lowest and highest of the history under each tile (Sticker Sheet phase 3).
                if (!id.equals("Network")) {
                    part.range = caption("");
                    if (SystemText.isDial(part.style)) part.range.setGravity(Gravity.CENTER);
                    content.addView(part.range, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
                }
                line.addView(tile(content, 16, 16, 14, 12), ui.margins(ui.ll(0, Ui.MATCH, 1), c == 0 ? 0 : GAP / 2, 0, c == columns - 1 ? 0 : GAP / 2, 0));
            }
            view.addView(line, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, r == 0 ? 0 : GAP, 0, 0));
        }
        if (uptime) { Part part = new Part(); part.style = "Heading"; part.value = headerUptime; parts.put("Uptime", part); }
        return view;
    }

    // A new reading from the PC adds to the history; rebuilds and redraws don't.
    @Override public void stateChanged() {
        PcState.System s = stats();
        Map<String, SystemText.Reading> readings = SystemText.readings(s, SystemText.fpsScale(history.get("Fps")));
        for (String id : SystemText.IDS) push(history.get(id), readings.get(id).value);
        push(upHistory, readings.get("Network").second);
        render();
    }
    private static void push(List<Double> list, Double value) {
        if (value == null) { list.clear(); return; }
        list.add(value); if (list.size() > HISTORY) list.remove(0);
    }

    // Fills the current design from the last reading: busy or hot readings in the warning colour, missing ones say why.
    @Override public void render() {
        if (!built()) return;
        PcState.System s = stats();
        if (s != null && !s.error && !selection(s).equals(builtFor)) { dash.rebuildWidget(id); return; }
        boolean ok = s != null && !s.error;
        status.setText(!dash.online() ? "PC OFFLINE" : ok ? "" : "UNAVAILABLE");
        double fpsScale = SystemText.fpsScale(history.get("Fps"));
        Map<String, SystemText.Reading> readings = SystemText.readings(s, fpsScale);
        String peakId = ok ? SystemText.peakId(readings, s) : null;
        peak.setText(peakId == null ? "" : SystemText.peakText(peakId, readings.get(peakId)));
        peak.setFilled(peakId != null && readings.get(peakId).percent >= SystemText.STRAINED);
        for (Map.Entry<String, Part> entry : parts.entrySet()) {
            String id = entry.getKey(); Part part = entry.getValue(); SystemText.Reading reading = readings.get(id);
            if (id.equals("Network")) {
                part.value.setText("↓ " + reading.text);
                if (part.second != null) part.second.setText("↑ " + reading.detail);
                if (part.detail != null) part.detail.setText("↑ " + reading.detail);
                if (part.graph != null) part.graph.setValues(history.get("Network"), upHistory, HISTORY);
                if (part.bars != null) part.bars.setValues(history.get("Network"), upHistory);
                continue;
            }
            if (id.equals("Uptime")) {
                String text = ok ? Format.uptime((long) s.uptimeMs, design == CardSizing.MEDIUM && width < 440 ? 2 : 3) : "—";
                part.value.setText(design == CardSizing.LARGE && ok ? "Up " + text : text);
                continue;
            }
            int color = reading.warning ? Theme.WARNING : ui.theme.accent;
            if (part.style.equals("List") && reading.missing()) { part.value.setText(reading.shortReason); part.value.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(13)); part.value.setTextColor(ui.theme.muted); }
            else { part.value.setText(reading.text); part.value.setTextColor(reading.warning ? Theme.WARNING : reading.missing() ? ui.theme.muted : ui.theme.text); }
            part.value.setContentDescription(SystemText.title(id) + ", " + (reading.missing() ? reading.longReason : reading.text));
            if (part.detail != null) part.detail.setText(reading.detail);
            if (part.badge != null) {
                String badge = SystemText.badge(readings.get(id.equals("Cpu") ? "CpuTemp" : "GpuTemp"));
                part.badge.setText(badge); part.badge.setVisibility(badge.isEmpty() ? View.GONE : View.VISIBLE);
            }
            if (part.range != null) part.range.setText(SystemText.range(id, history.get(id), ok && s.fahrenheit));
            if (part.ring != null) { part.ring.setValue(reading.percent); part.ring.setFill(color); }
            if (part.gauge != null) { part.gauge.setValue(reading.percent); part.gauge.setFill(color); }
            if (part.bar != null) { part.bar.setValue(reading.percent / 100); part.bar.setFill(color); }
            List<Double> values = history.get(id);
            if (part.graph != null) { if (id.equals("Fps")) part.graph.setMaximum(fpsScale); part.graph.setColor(color); part.graph.setValues(values, HISTORY); }
            if (part.bars != null) { if (id.equals("Fps")) part.bars.setMaximum(fpsScale); part.bars.setColor(color); part.bars.setValues(values, null); }
        }
    }
}
