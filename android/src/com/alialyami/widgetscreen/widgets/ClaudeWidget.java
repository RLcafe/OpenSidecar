package com.alialyami.widgetscreen.widgets;

import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.ClaudeText;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.views.ClaudeMascot;
import com.alialyami.widgetscreen.views.FlowLayout;
import com.alialyami.widgetscreen.views.HistoryBars;
import com.alialyami.widgetscreen.views.ProgressLine;
import com.alialyami.widgetscreen.views.Stickers;

import java.time.ZoneId;
import java.util.ArrayList;
import java.util.List;

// Claude Code on the PC (decision 027, docs/design/claude-card.md), the same four tiles as the Windows ClaudeCard: the
// plan's 5-hour and weekly use, tokens per minute, and the mascot with how many sessions are done, need you, are
// blocked or working. CardSizing.claudeColumns lays them out in one row, a 2 × 2 square or one column, and the card is
// rebuilt only when its size changes. It reads the PC's claude section with the other cards.
public final class ClaudeWidget extends Widget {
    private TextView status;
    private Plan five, week;
    private TextView tokensValue, tokensNote;
    private HistoryBars history;
    private ClaudeMascot mascot;
    private LinearLayout countsList;
    private FlowLayout countsFlow;
    private boolean compactCounts;
    private int columns;

    private static final class Plan { TextView value, note; ProgressLine bar; }

    public ClaudeWidget(Dashboard dash) { super("Claude", dash); }

    @Override public String part() { return "claude"; }

    private PcState.Claude claude() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.claude; }
    private static double now() { return System.currentTimeMillis() / 1000.0; }
    // For the checks: how the tiles were laid out.
    public int columns() { return columns; }

    @Override protected View create() {
        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(strip(), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        status = sticker(false);
        header.addView(status, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));

        // The body: the card less its 24-unit padding, the 28-unit heading and the 8-unit gap under it.
        double bodyWidth = Math.max(1, width - 48), bodyHeight = Math.max(1, height - 48 - 36), gap = CardSizing.CLAUDE_GAP;
        columns = CardSizing.claudeColumns(bodyWidth, bodyHeight);
        int rows = 4 / columns;
        double tileWidth = (bodyWidth - gap * (columns - 1)) / columns, tileHeight = (bodyHeight - gap * (rows - 1)) / rows;
        five = new Plan(); week = new Plan();
        View[] tiles = { planTile("5-HOUR", tileWidth, tileHeight, five), planTile("WEEKLY", tileWidth, tileHeight, week), tokensTile(tileWidth, tileHeight), mascotTile(tileWidth, tileHeight) };
        LinearLayout grid = ui.column();
        for (int r = 0; r < rows; r++) {
            LinearLayout line = ui.row(); line.setGravity(Gravity.FILL_VERTICAL);
            for (int c = 0; c < columns; c++)
                line.addView(tiles[r * columns + c], ui.margins(ui.ll(0, Ui.MATCH, 1), c == 0 ? 0 : gap / 2, 0, c == columns - 1 ? 0 : gap / 2, 0));
            grid.addView(line, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, r == 0 ? 0 : gap, 0, 0));
        }
        column.addView(grid, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        return ui.card(column, 24);
    }

    private FrameLayout tile(View content, double pad, boolean fill) {
        FrameLayout tile = new FrameLayout(ui.context);
        tile.setBackground(ui.shape(ui.theme.raised, 0, 0, 14));
        tile.setPadding(ui.px(pad), ui.px(pad), ui.px(pad), ui.px(pad));
        tile.addView(content, fill ? new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH) : ui.frame(Ui.MATCH, Ui.WRAP, Gravity.CENTER_VERTICAL));
        return tile;
    }
    private TextView label(String text) { TextView view = ui.label(text); view.setSingleLine(true); view.setEllipsize(android.text.TextUtils.TruncateAt.END); return view; }
    private TextView reading(double size) {
        TextView view = Ui.tabular(ui.text("—", (float) size, ui.theme.text));
        view.setTypeface(Ui.LIGHT); view.setSingleLine(true);
        return view;
    }
    private TextView note() { TextView view = ui.caption(""); view.setSingleLine(true); view.setEllipsize(android.text.TextUtils.TruncateAt.END); return view; }

    // A plan tile: its name, the percentage used, a bar, and when it resets.
    private View planTile(String name, double w, double h, Plan plan) {
        LinearLayout stack = ui.column();
        stack.addView(label(name), ui.ll(Ui.MATCH, Ui.WRAP));
        plan.value = reading(CardSizing.claudeValueSize(w, h));
        stack.addView(plan.value, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 2, 0, 0));
        plan.bar = new ProgressLine(ui.context, dash.ink(id), ui.theme.track);
        stack.addView(plan.bar, ui.margins(ui.ll(Ui.MATCH, ui.px(h >= 120 ? 8 : 6)), 0, 6, 0, 0));
        plan.note = note();
        if (CardSizing.claudeNote(h)) stack.addView(plan.note, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
        return tile(stack, CardSizing.claudePadding(w, h), false);
    }

    // Tokens per minute: the rate over the last five minutes and, in a tall tile, a bar a minute for half an hour.
    private View tokensTile(double w, double h) {
        LinearLayout stack = ui.column();
        stack.addView(label("TOKENS / MIN"), ui.ll(Ui.MATCH, Ui.WRAP));
        tokensValue = reading(CardSizing.claudeValueSize(w, h));
        stack.addView(tokensValue, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 2, 0, 0));
        history = null;
        boolean tall = CardSizing.claudeHistory(h);
        if (tall) {
            history = new HistoryBars(ui.context, dash.ink(id), ui.theme.track, 0, 0);
            history.setCapacity(30);
            stack.addView(history, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        }
        tokensNote = note();
        if (CardSizing.claudeNote(h)) stack.addView(tokensNote, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
        return tile(stack, CardSizing.claudePadding(w, h), tall);
    }

    // The mascot with the counts: beside it in a wide tile (a line each with words when there's room, else a dot and
    // a number each, two to a line), under it otherwise (two or four to a line).
    private View mascotTile(double w, double h) {
        double size = CardSizing.claudeMascotSize(w, h);
        boolean beside = CardSizing.claudeMascotBeside(w, h);
        compactCounts = !CardSizing.claudeCountWords(w, h);
        mascot = new ClaudeMascot(ui.context, dash.ink(id), ui.theme.page);
        countsList = null; countsFlow = null;
        View counts;
        if (compactCounts) { countsFlow = new FlowLayout(ui.context, 0); counts = countsFlow; }
        else { countsList = ui.column(); countsList.setGravity(Gravity.CENTER_VERTICAL); counts = countsList; }
        int countsWidth = compactCounts ? ui.px(CardSizing.claudeCountColumns(w, h) * CardSizing.CLAUDE_COUNT_WIDTH) : Ui.WRAP;
        LinearLayout layout = beside ? ui.row() : ui.column();
        layout.setGravity(Gravity.CENTER);
        layout.addView(mascot, ui.ll(ui.px(size), ui.px(size)));
        if (beside) layout.addView(counts, ui.margins(ui.ll(countsWidth, Ui.WRAP), compactCounts ? CardSizing.CLAUDE_MASCOT_GAP : 16, 0, 0, 0));
        else layout.addView(counts, ui.margins(ui.ll(countsWidth, Ui.WRAP), 0, 6, 0, 0));
        FrameLayout tile = new FrameLayout(ui.context);
        tile.setBackground(ui.shape(ui.theme.raised, 0, 0, 14));
        double pad = CardSizing.claudePadding(w, h);
        tile.setPadding(ui.px(pad), ui.px(pad), ui.px(pad), ui.px(pad));
        tile.addView(layout, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.CENTER));
        return tile;
    }

    // One count: a dot in its colour, the number, and its words when there's room.
    private View count(int kind, int n) {
        LinearLayout row = ui.row();
        row.setMinimumHeight(ui.px(CardSizing.CLAUDE_COUNTS_ROW));
        if (compactCounts) row.setMinimumWidth(ui.px(CardSizing.CLAUDE_COUNT_WIDTH));
        View dot = new View(ui.context);
        dot.setBackground(ui.shape(ClaudeText.color(kind), 0, 0, 5));
        row.addView(dot, ui.margins(ui.ll(ui.px(10), ui.px(10)), 0, 0, 6, 0));
        TextView number = Ui.tabular(ui.text(String.valueOf(n), ui.body(), ClaudeText.color(kind)));
        number.setTypeface(Ui.MEDIUM);
        row.addView(number, ui.ll(Ui.WRAP, Ui.WRAP));
        if (!compactCounts) row.addView(ui.text(" " + ClaudeText.words(kind, n), ui.small(), ui.theme.text), ui.ll(Ui.WRAP, Ui.WRAP));
        row.setContentDescription(n + " " + ClaudeText.words(kind, n));
        return row;
    }

    @Override public void render() {
        if (!built()) return;
        PcState.Claude c = claude();
        status.setText(!dash.online() ? "PC OFFLINE" : c != null && c.sample ? "SAMPLE" : "");
        double now = now(); boolean use24 = dash.use24(); ZoneId zone = ZoneId.systemDefault();
        fill(c, false, five, now, use24, zone);
        fill(c, true, week, now, use24, zone);
        tokensValue.setText(c == null ? "—" : ClaudeText.tokensValue(c));
        tokensNote.setText(c == null ? (dash.online() ? "Waiting for the PC…" : "") : ClaudeText.tokensNote(c));
        if (history != null) {
            List<Double> values = new ArrayList<Double>();
            if (c != null) for (double v : c.minutes) values.add(v);
            history.setValues(values, null);
        }
        int mood = ClaudeText.mood(c);
        int light = ClaudeText.moodColor(mood);
        mascot.set(mood, light == 0 ? ui.theme.muted : light);
        mascot.setContentDescription(c == null ? "Claude Code" : ClaudeText.summary(c));
        LinearLayout list = countsList; FlowLayout flow = countsFlow;
        if (list != null) list.removeAllViews();
        if (flow != null) flow.removeAllViews();
        int shown = 0;
        for (int kind : ClaudeText.ORDER) {
            int n = ClaudeText.count(c, kind);
            if (n <= 0) continue;
            View row = count(kind, n); shown++;
            if (list != null) list.addView(row, ui.ll(Ui.WRAP, Ui.WRAP));
            else flow.addView(row, new ViewGroup.LayoutParams(Ui.WRAP, Ui.WRAP));
        }
        if (shown == 0) {
            TextView quiet = ui.caption(c == null ? "" : "All quiet"); quiet.setGravity(Gravity.CENTER);
            if (list != null) list.addView(quiet, ui.ll(Ui.WRAP, Ui.WRAP)); else flow.addView(quiet, new ViewGroup.LayoutParams(Ui.WRAP, Ui.WRAP));
        }
    }

    private void fill(PcState.Claude c, boolean isWeek, Plan plan, double now, boolean use24, ZoneId zone) {
        plan.value.setText(c == null ? "—" : ClaudeText.percentText(c, isWeek, now));
        plan.note.setText(c == null ? "" : ClaudeText.planNote(c, isWeek, now, use24, zone));
        double pct = c == null ? Double.NaN : ClaudeText.percent(isWeek ? c.week : c.fiveHour, now);
        plan.bar.setValue(Double.isNaN(pct) ? 0 : pct / 100);
        plan.bar.setFill(!Double.isNaN(pct) && pct >= 90 ? Theme.WARNING : dash.ink(id));
        boolean stale = ClaudeText.stale(c);
        Stickers.stale(plan.value, stale); Stickers.stale(plan.bar, stale);
    }

    // Times like "Resets 3:40 PM" move on with the clock, and a reset passes without a new reading.
    @Override public void tick(int ticks, boolean quiet) { if (ticks % 30 == 0 && active()) render(); }
}
