package com.alialyami.widgetscreen.widgets;

import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.ClaudeText;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.views.ClaudeGauges;
import com.alialyami.widgetscreen.views.Stickers;

import java.time.ZoneId;

// Claude Code on the PC (decisions 027 and 028, docs/design/claude-card.md), the same three tiles as the Windows
// ClaudeCard: the 5-hour use as a needle gauge, the week as seven laps, and tokens per minute as a needle gauge over its
// burn line. CardSizing.claudeArrange lays them out in one row, the two plan tiles over the tokens, or one column, and
// the card is rebuilt only when its size changes. It reads the PC's claude section with the other cards.
public final class ClaudeWidget extends Widget {
    private TextView status;
    private TextView fiveValue, fiveNote, fivePace, weekValue, weekLap, weekNote, weekPace, tokensValue, tokensNote;
    private ClaudeGauges.Gauge fiveGauge, tokensGauge;
    private ClaudeGauges.Laps laps;
    private ClaudeGauges.Burn burn;
    private int arrangement;
    private double labelHeight;

    public ClaudeWidget(Dashboard dash) { super("Claude", dash); }

    @Override public String part() { return "claude"; }

    private PcState.Claude claude() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.claude; }
    private static double now() { return System.currentTimeMillis() / 1000.0; }
    // For the checks: how the tiles were laid out.
    public int arrangement() { return arrangement; }

    @Override protected View create() {
        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(strip(), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        status = sticker(false);
        header.addView(status, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));

        // The body: the card less its 24-unit padding, the 28-unit heading and the 8-unit gap under it.
        labelHeight = Math.ceil(ui.small() * CardSizing.CLAUDE_LINE_HEIGHT) + 2;
        double bodyWidth = Math.max(1, width - 48), bodyHeight = Math.max(1, height - 48 - 36), gap = CardSizing.CLAUDE_GAP;
        arrangement = CardSizing.claudeArrange(bodyWidth, bodyHeight);
        int columns = CardSizing.claudeGridColumns(arrangement), rows = CardSizing.claudeGridRows(arrangement);
        double tileWidth = (bodyWidth - gap * (columns - 1)) / columns, tileHeight = (bodyHeight - gap * (rows - 1)) / rows;
        // Under the two plan tiles, the tokens tile takes the whole width.
        boolean wide = arrangement == CardSizing.CLAUDE_TWO_OVER_ONE;
        View[] tiles = { fiveHourTile(tileWidth, tileHeight), weekTile(tileWidth, tileHeight), tokensTile(wide ? bodyWidth : tileWidth, tileHeight) };
        LinearLayout grid = ui.column();
        int next = 0;
        for (int r = 0; r < rows; r++) {
            LinearLayout line = ui.row(); line.setGravity(Gravity.FILL_VERTICAL);
            int across = wide && r == 1 ? 1 : columns;
            for (int c = 0; c < across; c++)
                line.addView(tiles[next++], ui.margins(ui.ll(0, Ui.MATCH, 1), c == 0 ? 0 : gap / 2, 0, c == across - 1 ? 0 : gap / 2, 0));
            grid.addView(line, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, r == 0 ? 0 : gap, 0, 0));
        }
        column.addView(grid, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        return ui.card(column, 24);
    }

    private FrameLayout tile(View content, double pad) {
        FrameLayout tile = new FrameLayout(ui.context);
        tile.setBackground(ui.shape(ui.theme.raised, 0, 0, 14));
        tile.setPadding(ui.px(pad), ui.px(pad), ui.px(pad), ui.px(pad));
        tile.addView(content, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        return tile;
    }
    private TextView label(String text) { TextView view = ui.label(text); view.setSingleLine(true); view.setEllipsize(android.text.TextUtils.TruncateAt.END); return view; }
    private TextView reading(double size) {
        TextView view = Ui.tabular(ui.text("—", (float) size, ui.theme.text));
        view.setTypeface(Ui.LIGHT); view.setSingleLine(true);
        return view;
    }
    private TextView note() { TextView view = ui.caption(""); view.setSingleLine(true); view.setEllipsize(android.text.TextUtils.TruncateAt.END); return view; }

    // A gauge tile: its name over the gauge, with the reading and the lines under it beside the gauge in a wide tile
    // and under it otherwise; the reading shrinks, and lines step aside, to fit (CardSizing.claudeFit).
    private View gaugeTile(String name, double w, double h, ClaudeGauges.Gauge gauge, TextView value, View[] lines, double[] heights) {
        double pad = CardSizing.claudePadding(w, h), inner = Math.max(1, w - 2 * pad), body = Math.max(1, h - 2 * pad - labelHeight);
        boolean beside = CardSizing.claudeGaugeBeside(inner, body);
        double gaugeWidth = CardSizing.claudeGaugeWidth(inner, body), textWidth = inner - gaugeWidth - CardSizing.CLAUDE_GAUGE_GAP;
        double[] size = { beside ? Math.min(CardSizing.claudeValueSize(w, h), Math.floor(textWidth / 2.3)) : CardSizing.claudeValueSize(w, h) };
        int shown = CardSizing.claudeFit(beside ? body : body * CardSizing.CLAUDE_TEXT_SHARE, heights, size);
        value.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(size[0]));
        LinearLayout text = ui.column(); text.setGravity(beside ? Gravity.CENTER_VERTICAL : Gravity.CENTER_HORIZONTAL);
        if (!beside) value.setGravity(Gravity.CENTER_HORIZONTAL);
        text.addView(value, ui.ll(Ui.MATCH, Ui.WRAP));
        for (int i = 0; i < lines.length && i < shown; i++) {
            if (!beside && lines[i] instanceof TextView) ((TextView) lines[i]).setGravity(Gravity.CENTER_HORIZONTAL);
            boolean isBurn = lines[i] instanceof ClaudeGauges.Burn;
            text.addView(lines[i], isBurn ? ui.margins(ui.ll(Ui.MATCH, ui.px(CardSizing.CLAUDE_BURN_HEIGHT)), 0, 2, 0, 4) : ui.ll(Ui.MATCH, Ui.WRAP));
        }
        LinearLayout layout = ui.column();
        layout.addView(label(name), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 2));
        LinearLayout content = beside ? ui.row() : ui.column();
        if (beside) {
            content.addView(gauge, ui.ll(ui.px(gaugeWidth), Ui.MATCH));
            content.addView(text, ui.margins(ui.ll(0, Ui.MATCH, 1), CardSizing.CLAUDE_GAUGE_GAP, 0, 0, 0));
        } else {
            content.addView(gauge, ui.ll(Ui.MATCH, 0, 1));
            content.addView(text, ui.ll(Ui.MATCH, Ui.WRAP));
        }
        layout.addView(content, ui.ll(Ui.MATCH, 0, 1));
        return tile(layout, pad);
    }

    // The 5-hour use: a needle gauge with the pace tick and the projection, the reading, when it resets and the pace.
    private View fiveHourTile(double w, double h) {
        fiveGauge = new ClaudeGauges.Gauge(ui.context, ui.theme, dash.ink(id), ui.unit);
        fiveValue = reading(32); fiveNote = note(); fivePace = note();
        return gaugeTile("5-HOUR", w, h, fiveGauge, fiveValue, new View[] { fiveNote, fivePace }, new double[] { CardSizing.CLAUDE_LINE, CardSizing.CLAUDE_LINE });
    }

    // Tokens per minute: a needle gauge with a redline, the last five minutes' rate, and the half hour's burn line.
    private View tokensTile(double w, double h) {
        tokensGauge = new ClaudeGauges.Gauge(ui.context, ui.theme, dash.ink(id), ui.unit);
        tokensGauge.red = ClaudeText.REDLINE;
        tokensValue = reading(32); tokensNote = note();
        burn = new ClaudeGauges.Burn(ui.context, ui.theme, dash.ink(id), ui.unit);
        return gaugeTile("TOKENS / MIN", w, h, tokensGauge, tokensValue, new View[] { burn, tokensNote }, new double[] { CardSizing.CLAUDE_BURN_ROOM, CardSizing.CLAUDE_LINE });
    }

    // The week as a race: the lap it's on, the reading with the pace and the reset beside it, and seven laps, one a
    // day, with the projection hatched and a flag at the reset.
    private View weekTile(double w, double h) {
        double pad = CardSizing.claudePadding(w, h), inner = Math.max(1, w - 2 * pad), body = Math.max(1, h - 2 * pad - labelHeight);
        double lapsHeight = CardSizing.claudeLapsHeight(body), room = Math.max(1, body - lapsHeight - CardSizing.CLAUDE_LAPS_GAP);
        double size = Math.max(18, Math.min(CardSizing.claudeValueSize(w, h), Math.floor(room / CardSizing.CLAUDE_LINE_HEIGHT)));
        int notes = CardSizing.claudeWeekNotes(inner, room, size);
        LinearLayout layout = ui.column();
        LinearLayout top = ui.row(); top.setGravity(Gravity.BOTTOM);
        top.addView(label("WEEKLY"), ui.ll(0, Ui.WRAP, 1));
        weekLap = note();
        top.addView(weekLap, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 8, 0, 0, 0));
        layout.addView(top, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 2));
        LinearLayout middle = ui.row(); middle.setGravity(Gravity.CENTER_VERTICAL);
        weekValue = reading(size);
        middle.addView(weekValue, ui.ll(Ui.WRAP, Ui.WRAP));
        LinearLayout lines = ui.column(); lines.setGravity(Gravity.CENTER_VERTICAL);
        weekNote = note(); weekPace = note();
        weekNote.setGravity(Gravity.END); weekPace.setGravity(Gravity.END);
        if (notes >= 1) lines.addView(weekPace, ui.ll(Ui.MATCH, Ui.WRAP));
        if (notes >= 2) lines.addView(weekNote, ui.ll(Ui.MATCH, Ui.WRAP));
        middle.addView(lines, ui.margins(ui.ll(0, Ui.WRAP, 1), 10, 0, 0, 0));
        layout.addView(middle, ui.ll(Ui.MATCH, 0, 1));
        laps = new ClaudeGauges.Laps(ui.context, ui.theme, dash.ink(id), ui.unit);
        layout.addView(laps, ui.margins(ui.ll(Ui.MATCH, ui.px(lapsHeight)), 0, CardSizing.CLAUDE_LAPS_GAP, 0, 0));
        return tile(layout, pad);
    }

    @Override public void render() {
        if (!built()) return;
        PcState.Claude c = claude();
        status.setText(!dash.online() ? "PC OFFLINE" : c != null && c.sample ? "SAMPLE" : "");
        double now = now(); boolean use24 = dash.use24(); ZoneId zone = ZoneId.systemDefault();
        int ink = dash.ink(id);
        boolean stale = ClaudeText.stale(c);
        PcState.ClaudeWindow five = c == null ? null : c.fiveHour, week = c == null ? null : c.week;

        double pct = ClaudeText.percent(five, now), elapsed = ClaudeText.elapsed(five, ClaudeText.FIVE_HOURS, now), projection = ClaudeText.projection(pct, elapsed);
        fiveGauge.setInk(ink);
        fiveGauge.value = Double.isNaN(pct) ? 0 : pct / 100; fiveGauge.projection = Double.isNaN(projection) ? Double.NaN : projection / 100;
        fiveGauge.pace = elapsed; fiveGauge.warn = pct >= 90; fiveGauge.invalidate();
        fiveValue.setText(c == null ? "—" : ClaudeText.percentText(c, false, now)); fiveValue.setTextColor(pct >= 90 ? Theme.WARNING : ui.theme.text);
        fiveNote.setText(c == null ? "" : ClaudeText.planNote(c, false, now, use24, zone));
        pace(fivePace, projection);
        Stickers.stale(fiveGauge, stale); Stickers.stale(fiveValue, stale);

        pct = ClaudeText.percent(week, now); elapsed = ClaudeText.elapsed(week, ClaudeText.WEEK, now); projection = ClaudeText.projection(pct, elapsed);
        laps.setInk(ink);
        laps.percent = pct; laps.projection = projection; laps.now = ClaudeText.lapNow(elapsed); laps.warn = pct >= 90; laps.days = ClaudeText.lapDays(week, now, zone); laps.invalidate();
        weekLap.setText(ClaudeText.lapText(laps.now));
        weekValue.setText(c == null ? "—" : ClaudeText.percentText(c, true, now)); weekValue.setTextColor(pct >= 90 ? Theme.WARNING : ui.theme.text);
        String weekWords = c == null ? "" : ClaudeText.planNote(c, true, now, use24, zone);
        pace(weekPace, projection);
        // Signed out, or with nothing to pace, the note says why where the pace would be.
        if (weekPace.getText().length() == 0) { weekPace.setText(weekWords); weekNote.setText(""); } else weekNote.setText(weekWords);
        Stickers.stale(laps, stale); Stickers.stale(weekValue, stale);

        double scale = ClaudeText.scale(c == null ? 0 : ClaudeText.peak(c.minutes)), rate = c == null ? 0 : ClaudeText.rate(c.minutes);
        boolean counted = c != null && c.logsRead && c.logs;
        tokensGauge.setInk(ink);
        tokensGauge.value = counted ? Math.min(1, rate / scale) : 0; tokensGauge.high = ClaudeText.tokens(scale); tokensGauge.invalidate();
        tokensValue.setText(c == null ? "—" : ClaudeText.tokensValue(c)); tokensValue.setTextColor(counted && rate >= scale * ClaudeText.REDLINE ? Theme.WARNING : ui.theme.text);
        tokensNote.setText(c == null ? (dash.online() ? "Waiting for the PC…" : "") : ClaudeText.tokensNote(c));
        burn.setInk(ink);
        burn.values = counted ? c.minutes : new double[0]; burn.maximum = scale; burn.invalidate();
    }

    private void pace(TextView line, double projection) {
        line.setText(ClaudeText.pace(projection));
        line.setTextColor(ClaudeText.warns(projection) ? Theme.WARNING : ui.theme.muted);
    }

    // Times like "Resets 3:40 PM" move on with the clock, and a reset passes without a new reading.
    @Override public void tick(int ticks, boolean quiet) { if (ticks % 30 == 0 && active()) render(); }
}
