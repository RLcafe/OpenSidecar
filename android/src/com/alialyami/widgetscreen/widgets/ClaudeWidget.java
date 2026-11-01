package com.alialyami.widgetscreen.widgets;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Rect;
import android.graphics.RectF;
import android.text.SpannableStringBuilder;
import android.text.Spanned;
import android.text.style.AbsoluteSizeSpan;
import android.text.style.ForegroundColorSpan;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.ClaudePit;
import com.alialyami.widgetscreen.core.ClaudeText;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.PitArt;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.views.PitSprite;
import com.alialyami.widgetscreen.views.PitViews;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

// Claude Code on the PC as a pit wall (docs/design/claude-watcher-v2.html; decision 017), in the same three designs as
// the Windows ClaudeCard (CardSizing.claude). Each project is a pit garage and each thread a pixel-art race car drawn in
// code (PitArt): the PC says which car and what it does; the phone draws it. Large: the heading with a live summary and
// the period switch, the plan row (5-hour tank, week laps, rev counter), the pit stop strip, the ledger of garages and
// cars beside the 5-hour lanes and today's facts. Medium: the two plan meters and the top garages. Small: the car that
// needs you and the 5-hour meter. Tapping a garage rolls its cars in or out (shared with the PC through
// claude.collapse); Quiet mode swaps states without motion. The cars move on the card's own frame clock, which runs
// only while the card is on screen, the app is in front and Quiet mode is off.
public final class ClaudeWidget extends Widget {
    private static final String[] PERIOD_DESCRIPTIONS = { "the 5-hour window", "today", "the week" };
    private static final double ROLL = 1.2;
    private int design, period = -1;
    private String builtFor = "";
    private FrameLayout card;
    private boolean quiet, live;
    private final Map<String, Boolean> pending = new HashMap<String, Boolean>();
    private final Map<String, Integer> seen = new HashMap<String, Integer>();
    private final List<CarPart> cars = new ArrayList<CarPart>();
    private final List<GaragePart> garages = new ArrayList<GaragePart>();
    private final List<Fill> fills = new ArrayList<Fill>();
    private ScrollView ledgerScroll;
    private RollView layer;
    private Roll rolling;
    private String rollOut;
    private long lastFrame;

    private interface Fill { void run(PcState.Claude c, double now); }
    private static final class CarPart { String id, key; PitSprite sprite; double width; boolean dim; }
    private static final class GaragePart { String key; PitSprite sprite; View tap; final List<CarPart> cars = new ArrayList<CarPart>(); }

    public ClaudeWidget(Dashboard dash) { super("Claude", dash); }

    @Override public String part() { return "claude"; }

    private PcState.Claude claude() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.claude; }
    private static double now() { return System.currentTimeMillis() / 1000.0; }
    private boolean folded(PcState.ClaudeGroup group) { Boolean asked = pending.get(group.key); return asked != null ? asked : group.collapsed; }
    private List<PcState.ClaudeGroup> groups(PcState.Claude c) { return c.groups[period]; }
    private PcState.ClaudeGroup group(PcState.Claude c, String key) { for (PcState.ClaudeGroup g : groups(c)) if (g.key.equals(key)) return g; return null; }
    private float scaled(double size) { return Math.round(size * ui.textScale); }
    private boolean counted(PcState.Claude c) { return c.logsRead && !c.logsFailed; }

    // What decides the design's parts: which threads and projects, which areas have something.
    private String shape(PcState.Claude c) {
        if (c == null) return "-";
        StringBuilder ids = new StringBuilder();
        for (PcState.ClaudeSession s : c.sessions) ids.append(s.id).append(':').append(s.canFocus).append(s.state.equals("needs") ? "!" : "").append(',');
        StringBuilder projects = new StringBuilder();
        for (PcState.ClaudeGroup g : groups(c)) {
            projects.append(g.key).append(folded(g) ? "!" : "").append(':');
            for (String id : g.threads) { PcState.ClaudeThread t = c.thread(id); projects.append(id).append(t != null && t.running && t.canFocus ? "*" : "").append(','); }
            projects.append(';');
        }
        StringBuilder lanes = new StringBuilder(); for (PcState.ClaudeLane l : c.lanes) lanes.append(l.key).append(',');
        return c.connected + "|" + c.sample + "|" + ids + "|" + projects + "|" + c.limitsKnown() + c.noPlan + c.plan + "|" + c.extra.size() + "|" + c.logsFailed + "|" + period + "|" + lanes + "|" + c.facts.telemetry + c.apiValue;
    }

    @Override protected View create() {
        PcState.Claude c = claude();
        if (period < 0 && c != null) period = ClaudeText.period(c.period);
        if (period < 0) period = 1;
        if (c != null) for (PcState.ClaudeGroup g : groups(c)) { Boolean asked = pending.get(g.key); if (asked != null && asked == g.collapsed) pending.remove(g.key); }
        builtFor = shape(c);
        stopRoll(false);
        cars.clear(); garages.clear(); fills.clear(); ledgerScroll = null;
        design = CardSizing.claude(width, height);
        double bodyWidth = Math.max(1, width - 48), bodyHeight = Math.max(1, height - 48);
        View content = c == null ? ui.caption("Waiting for the PC…") : design == CardSizing.SMALL ? small(c, bodyWidth, bodyHeight) : design == CardSizing.MEDIUM ? medium(c, bodyWidth, bodyHeight) : large(c, bodyWidth, bodyHeight);
        FrameLayout body = new FrameLayout(ui.context);
        body.addView(content, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        layer = new RollView(ui.context);
        body.addView(layer, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        card = ui.card(body, 24);
        if (rollOut != null) {
            final String key = rollOut; rollOut = null;
            for (final GaragePart g : garages) if (g.key.equals(key) && !g.cars.isEmpty()) {
                for (CarPart car : g.cars) car.sprite.setVisibility(View.INVISIBLE);
                g.sprite.setVisibility(View.INVISIBLE);
                card.post(new Runnable() { @Override public void run() { startRoll(g, true); } });
            }
        }
        return card;
    }

    @Override public void release() { stopClock(); stopRoll(false); super.release(); }

    // ----- Parts -----

    private CarPart car(PcState.ClaudeThread thread, String key, double carWidth, boolean dim) {
        CarPart part = new CarPart(); part.id = thread == null ? null : thread.id; part.key = key; part.width = carWidth; part.dim = dim;
        part.sprite = new PitSprite(ui.context);
        cars.add(part);
        return part;
    }
    private LinearLayout.LayoutParams carSize(double carWidth) { return ui.ll(ui.px(carWidth), ui.px(PitSprite.carHeight(carWidth))); }
    // A tappable thread: its area brings the thread's window forward on the PC, when one is known.
    private View tappable(View child, final PcState.ClaudeThread thread) {
        if (thread == null || !thread.running || !thread.canFocus) return child;
        child.setClickable(true);
        child.setBackground(ui.pressable(0x00000000, 0, 0, 8));
        child.setContentDescription(ClaudeText.threadTitle(thread, now(), dash.use24()) + ". Tap to show its window on the PC.");
        child.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "claude.focus", "id", thread.id)); } }));
        return child;
    }
    // A project's garage: a tap rolls its cars in or out.
    private GaragePart garage(final PcState.ClaudeGroup group, double size) {
        GaragePart part = new GaragePart(); part.key = group.key;
        part.sprite = new PitSprite(ui.context);
        FrameLayout tap = new FrameLayout(ui.context);
        tap.addView(part.sprite, ui.frame(ui.px(size), ui.px(size), Gravity.CENTER));
        tap.setMinimumWidth(ui.px(48)); tap.setMinimumHeight(ui.px(48));
        tap.setClickable(true); tap.setBackground(ui.pressable(0x00000000, 0, 0, 10));
        tap.setContentDescription(group.project + ", " + ClaudeText.threads(group.threads.size()) + (folded(group) ? ", in the garage. Tap to roll them out." : ". Tap to roll them into the garage."));
        final String key = group.key;
        tap.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { toggle(key); } }));
        part.tap = tap;
        garages.add(part);
        return part;
    }
    private TextView trimmed(TextView view) { Ui.wrap(view, 1); return view; }
    private TextView upper(String text, float size) { return trimmed(ui.text(text.toUpperCase(Locale.ROOT), size, ui.theme.muted)); }
    private TextView reading(String text, float size) { TextView view = Ui.tabular(ui.text(text, size, ui.theme.text)); view.setTypeface(Ui.LIGHT); view.setIncludeFontPadding(false); return view; }
    private CharSequence big(double percent, int color, float size) {
        SpannableStringBuilder text = new SpannableStringBuilder(String.valueOf(Math.round(percent)));
        text.setSpan(new ForegroundColorSpan(color), 0, text.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        int start = text.length(); text.append("%");
        text.setSpan(new ForegroundColorSpan(ui.theme.muted), start, text.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        text.setSpan(new AbsoluteSizeSpan(ui.px(size / 2)), start, text.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        return text;
    }
    private CharSequence lines(long added, long removed) {
        if (added == 0 && removed == 0) return "—";
        SpannableStringBuilder text = new SpannableStringBuilder(ClaudeText.added(added));
        text.setSpan(new ForegroundColorSpan(PitArt.LIME), 0, text.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        int start = text.length(); text.append(" ").append(ClaudeText.removed(removed));
        text.setSpan(new ForegroundColorSpan(0xFFF09A9A), start, text.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        return text;
    }
    private View tile(View child, double padX, double padY) {
        FrameLayout tile = new FrameLayout(ui.context);
        tile.setBackground(ui.shape(ui.theme.raised, 0, 0, 12));
        tile.setPadding(ui.px(padX), ui.px(padY), ui.px(padX), ui.px(padY));
        tile.addView(child, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        return tile;
    }

    // The heading row: the title, the live summary, the flag and (Large) the period switch.
    private View header(final boolean large, double bodyWidth) {
        LinearLayout row = ui.row();
        row.addView(ui.label(large && bodyWidth >= 900 ? "CLAUDE CODE · PIT WALL" : "CLAUDE CODE"), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 14, 0));
        final View dot = new View(ui.context);
        row.addView(dot, ui.margins(ui.ll(ui.px(8), ui.px(8)), 0, 0, 6, 0));
        final TextView summary = trimmed(ui.text("", scaled(14), ui.theme.text));
        row.addView(summary, ui.ll(0, Ui.WRAP, 1));
        final TextView flag = ui.caption("");
        row.addView(flag, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, large ? 12 : 0, 0));
        if (large) {
            for (int i = 0; i < 3; i++) {
                final int choice = i;
                TextView button = ui.button(ClaudeText.PERIOD_NAMES[i], new View.OnClickListener() { @Override public void onClick(View v) { period = choice; dash.rebuildWidget(id); } });
                button.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(ui.small()));
                button.setPadding(ui.px(4), ui.px(4), ui.px(4), ui.px(4));
                ui.select(button, i == period);
                button.setContentDescription("Threads and usage for " + PERIOD_DESCRIPTIONS[i] + (i == period ? ", selected" : ""));
                row.addView(button, ui.margins(ui.ll(ui.px(64), ui.px(48)), i == 0 ? 0 : 4, 0, 0, 0));
            }
        }
        fills.add(new Fill() { @Override public void run(PcState.Claude c, double now) {
            boolean needs = c.needsYou() > 0;
            dot.setBackground(ui.shape(needs ? Theme.WARNING : c.working() > 0 ? ui.theme.accent : ui.theme.muted, 0, 0, 4));
            summary.setText(ClaudeText.summary(c, period, large)); summary.setTextColor(needs ? Theme.WARNING : ui.theme.text);
            flag.setText(ClaudeText.flag(c, now, dash.use24()));
        } });
        return row;
    }

    // ----- Large -----

    private View large(PcState.Claude c, double width, double height) {
        LinearLayout column = ui.column();
        column.addView(header(true, width), ui.ll(Ui.MATCH, ui.px(48)));
        column.addView(planRow(c, width), ui.margins(ui.ll(Ui.MATCH, ui.px(CardSizing.CLAUDE_PLAN_HEIGHT)), 0, 12, 0, 0));
        PcState.ClaudeSession asking = null; for (PcState.ClaudeSession s : c.sessions) if ("needs".equals(s.state)) { asking = s; break; }
        if (asking != null) column.addView(askStrip(c, asking), ui.margins(ui.ll(Ui.MATCH, ui.px(Math.max(60, scaled(16) * 1.35 + 13 * 1.35 + 14))), 0, 12, 0, 0));
        boolean side = CardSizing.claudeSide(width);
        LinearLayout ledger = ui.row(); ledger.setGravity(Gravity.TOP);
        double table = side ? width - 16 - CardSizing.CLAUDE_SIDE_WIDTH : width;
        ledger.addView(ledger(c, table), ui.ll(0, Ui.MATCH, 1));
        if (side) ledger.addView(sidePanel(c), ui.margins(ui.ll(ui.px(CardSizing.CLAUDE_SIDE_WIDTH), Ui.MATCH), 16, 0, 0, 0));
        column.addView(ledger, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 12, 0, 0));
        return column;
    }

    private View planRow(PcState.Claude c, double width) {
        boolean revs = CardSizing.claudeRevs(width);
        LinearLayout row = ui.row(); row.setGravity(Gravity.FILL_VERTICAL);
        double tileWidth = revs ? (width - 24) / 3.5 : (width - 12) / 2;
        if (c.limitsKnown()) {
            row.addView(tankTile(tileWidth), ui.ll(0, Ui.MATCH, revs ? 1.1f : 1));
            row.addView(weekTile(), ui.margins(ui.ll(0, Ui.MATCH, revs ? 1.3f : 1), 12, 0, 0, 0));
        } else {
            TextView message = ui.text(ClaudeText.limitsMissing(c), 15, ui.theme.muted); Ui.wrap(message, 3); message.setGravity(Gravity.CENTER_VERTICAL);
            row.addView(tile(message, 14, 12), ui.ll(0, Ui.MATCH, revs ? 2.4f : 2));
        }
        if (revs) row.addView(revTile(tileWidth * 1.1), ui.margins(ui.ll(0, Ui.MATCH, 1.1f), 12, 0, 0, 0));
        return row;
    }
    private View tankTile(double tileWidth) {
        double size = tileWidth >= 330 ? 140 : 112;
        LinearLayout row = ui.row();
        final PitViews.Gauge gauge = new PitViews.Gauge(ui.context, ui.theme, ui.unit);
        row.addView(gauge, ui.ll(ui.px(size), ui.px(size * 0.6)));
        LinearLayout text = ui.column();
        text.addView(upper("5-hour tank", 13));
        final TextView value = reading("—", 40); text.addView(value);
        final TextView pace = trimmed(ui.text("", 13, ui.theme.muted)); text.addView(pace);
        final TextView reset = trimmed(ui.text("", 13, ui.theme.muted)); text.addView(reset);
        row.addView(text, ui.margins(ui.ll(0, Ui.WRAP, 1), 12, 0, 0, 0));
        fills.add(new Fill() { @Override public void run(PcState.Claude c, double now) {
            PcState.ClaudeWindow w = c.fiveHour;
            double pct = ClaudeText.percent(w, now), elapsed = ClaudePit.elapsed(w, ClaudePit.FIVE_HOURS, now), projection = ClaudePit.projection(pct, elapsed);
            gauge.value = pct / 100; gauge.projection = Double.isNaN(projection) ? Double.NaN : projection / 100; gauge.pace = elapsed; gauge.warn = pct >= 100; gauge.invalidate();
            if (w == null) value.setText("—"); else value.setText(big(pct, ClaudeText.warns(pct) ? Theme.WARNING : ui.theme.text, 40));
            pace.setText(ClaudePit.pace(projection)); pace.setTextColor(ClaudePit.warns(projection) || ClaudeText.warns(pct) ? Theme.WARNING : ui.theme.muted);
            reset.setText(w == null ? ClaudeText.NO_FIVE_HOUR : ClaudeText.fiveHourReset(w, now, dash.use24())); reset.setTextColor(pct >= 100 ? Theme.WARNING : ui.theme.muted);
        } });
        return tile(row, 12, 10);
    }
    private View weekTile() {
        LinearLayout stack = ui.column();
        FrameLayout top = new FrameLayout(ui.context);
        final TextView lap = upper("Week", 13); top.addView(lap, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START));
        final TextView resets = ui.text("", 13, ui.theme.muted); top.addView(resets, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END));
        stack.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
        LinearLayout middle = ui.row();
        final TextView value = reading("—", 40); middle.addView(value);
        LinearLayout notes = ui.column(); notes.setGravity(Gravity.END);
        final TextView pace = trimmed(ui.text("", 13, ui.theme.muted)); pace.setGravity(Gravity.END); notes.addView(pace, ui.ll(Ui.MATCH, Ui.WRAP));
        final TextView extra = trimmed(ui.text("", 13, ui.theme.muted)); extra.setGravity(Gravity.END); notes.addView(extra, ui.ll(Ui.MATCH, Ui.WRAP));
        middle.addView(notes, ui.margins(ui.ll(0, Ui.WRAP, 1), 12, 0, 0, 0));
        stack.addView(middle, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 2, 0, 4));
        final PitViews.Laps laps = new PitViews.Laps(ui.context, ui.theme, ui.unit);
        stack.addView(laps, ui.ll(Ui.MATCH, ui.px(30)));
        fills.add(new Fill() { @Override public void run(PcState.Claude c, double now) {
            PcState.ClaudeWindow w = c.week;
            double pct = ClaudeText.percent(w, now), elapsed = ClaudePit.elapsed(w, ClaudePit.WEEK, now), projection = ClaudePit.projection(pct, elapsed);
            lap.setText(ClaudeText.lap(ClaudePit.lapNow(elapsed)));
            resets.setText(w == null ? "" : ClaudeText.current(w, now) ? ClaudeText.weekReset(w, dash.use24()) : "reset");
            if (w == null) value.setText("—"); else value.setText(big(pct, ClaudeText.warns(pct) ? Theme.WARNING : ui.theme.text, 40));
            pace.setText(ClaudePit.pace(projection)); pace.setTextColor(ClaudePit.warns(projection) ? Theme.WARNING : ui.theme.muted);
            extra.setText(ClaudeText.extras(c, now));
            laps.percent = pct; laps.projection = projection; laps.now = ClaudePit.lapNow(elapsed); laps.days = ClaudeText.lapDays(w, now); laps.invalidate();
        } });
        return tile(stack, 14, 10);
    }
    private View revTile(double tileWidth) {
        double size = tileWidth >= 330 ? 140 : 112;
        LinearLayout row = ui.row();
        final PitViews.Gauge gauge = new PitViews.Gauge(ui.context, ui.theme, ui.unit); gauge.red = ClaudePit.REDLINE;
        row.addView(gauge, ui.ll(ui.px(size), ui.px(size * 0.6)));
        LinearLayout text = ui.column();
        text.addView(upper("Revs · tok/min", 13));
        final TextView value = reading("—", 40); text.addView(value);
        final PitViews.Burn burn = new PitViews.Burn(ui.context, ui.theme, ui.unit); text.addView(burn, ui.margins(ui.ll(Ui.MATCH, ui.px(24)), 0, 2, 0, 2));
        text.addView(ui.text("last 60 min", 13, ui.theme.muted));
        row.addView(text, ui.margins(ui.ll(0, Ui.WRAP, 1), 12, 0, 0, 0));
        fills.add(new Fill() { @Override public void run(PcState.Claude c, double now) {
            gauge.value = Math.min(1, c.rate / c.scale); gauge.high = ClaudeText.tokens((long) c.scale); gauge.invalidate();
            String rate = ClaudeText.tokens((long) c.rate), digits = rate.replaceAll("[kMB]$", ""), unit = rate.substring(digits.length());
            SpannableStringBuilder text = new SpannableStringBuilder(digits);
            text.setSpan(new ForegroundColorSpan(c.rate >= c.scale * ClaudePit.REDLINE ? Theme.WARNING : ui.theme.text), 0, text.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
            if (!unit.isEmpty()) { int start = text.length(); text.append(unit); text.setSpan(new ForegroundColorSpan(ui.theme.muted), start, text.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE); text.setSpan(new AbsoluteSizeSpan(ui.px(20)), start, text.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE); }
            value.setText(text);
            burn.values = c.burn; burn.maximum = c.scale; burn.invalidate();
        } });
        return tile(row, 12, 10);
    }

    // The pit stop strip: the thread waiting on you, what it wants, and Go to it.
    private View askStrip(PcState.Claude c, final PcState.ClaudeSession asking) {
        LinearLayout strip = ui.row();
        strip.setBackground(ui.shape(Ui.alpha(Theme.WARNING, 0.12f), Ui.alpha(Theme.WARNING, 0.45f), 1, 12));
        strip.setPadding(ui.px(10), ui.px(6), ui.px(8), ui.px(6));
        final PcState.ClaudeThread thread = c.thread(asking.id);
        CarPart part = car(thread, "", 62, false);
        strip.addView(part.sprite, ui.margins(carSize(62), 0, 0, 12, 0));
        LinearLayout words = ui.column();
        final TextView title = trimmed(ui.text("", scaled(16), ui.theme.text)); title.setTypeface(Ui.MEDIUM); words.addView(title);
        final TextView reason = trimmed(ui.text("", 13, Theme.WARNING)); words.addView(reason);
        strip.addView(words, ui.ll(0, Ui.WRAP, 1));
        if (asking.canFocus) {
            TextView go = ui.button("Go to it", new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "claude.focus", "id", asking.id)); } });
            go.setBackground(ui.pressable(Theme.WARNING, 0, 0, 12)); go.setTextColor(Theme.ON_ACCENT); go.setTypeface(Ui.MEDIUM);
            go.setContentDescription("Go to the thread that needs you");
            strip.addView(go, ui.margins(ui.ll(Ui.WRAP, ui.px(48)), 12, 0, 0, 0));
            go.setMinWidth(ui.px(96)); go.setPadding(ui.px(16), 0, ui.px(16), 0);
        }
        final String id = asking.id;
        fills.add(new Fill() { @Override public void run(PcState.Claude c, double now) {
            PcState.ClaudeSession s = null; for (PcState.ClaudeSession x : c.sessions) if (x.id.equals(id)) s = x;
            PcState.ClaudeThread t = c.thread(id);
            if (s == null) return;
            title.setText("Pit stop: " + (t == null ? s.shown() : ClaudeText.threadTitle(t, now, dash.use24())) + " · " + s.project);
            List<String> parts = new ArrayList<String>();
            String ask = ClaudeText.ask(s.message); if (!ask.isEmpty()) parts.add(ask);
            parts.add("waiting " + ClaudeText.duration(now - s.since));
            if (!s.where.isEmpty()) parts.add("Desktop".equals(s.where) ? "Claude app" : s.where);
            StringBuilder text = new StringBuilder(); for (String p : parts) { if (text.length() > 0) text.append(" · "); text.append(p); }
            reason.setText(text);
        } });
        return strip;
    }

    // The ledger: a header and a scrolling list of garages with their cars.
    private LinearLayout ledgerRow(double width, double rowHeight) {
        LinearLayout row = ui.row();
        row.setLayoutParams(ui.ll(Ui.MATCH, ui.px(rowHeight)));
        return row;
    }
    private void cell(LinearLayout row, View view, double width, int gravity) {
        if (view instanceof TextView) ((TextView) view).setGravity(gravity | Gravity.CENTER_VERTICAL);
        row.addView(view, ui.margins(ui.ll(ui.px(width), Ui.MATCH), 10, 0, 0, 0));
    }
    private View ledger(PcState.Claude c, double width) {
        LinearLayout table = ui.column();
        LinearLayout head = ui.row();
        head.addView(new View(ui.context), ui.ll(ui.px(52), 1));
        head.addView(upper("Project · thread", 12), ui.ll(0, Ui.WRAP, 1));
        cell(head, upper("Tokens", 12), scaled(72), Gravity.END);
        cell(head, upper("Cache reads", 12), scaled(80), Gravity.END);
        cell(head, upper(c.apiValue ? "API value" : "Replies", 12), scaled(64), Gravity.END);
        if (CardSizing.claudeModels(width)) cell(head, upper("Models", 12), 64, Gravity.START);
        if (CardSizing.claudeLines(width)) cell(head, upper("Lines", 12), scaled(84), Gravity.END);
        table.addView(head, ui.ll(Ui.MATCH, ui.px(24)));
        table.addView(ui.divider(1, ui.theme.controlBorder), ui.ll(Ui.MATCH, Math.max(1, ui.px(1))));
        LinearLayout list = ui.column();
        List<PcState.ClaudeGroup> groups = groups(c);
        if (groups.isEmpty()) list.addView(empty(c));
        for (int i = 0; i < groups.size(); i++) addGarageRows(c, list, groups.get(i), i, width);
        ScrollView scroll = new ScrollView(ui.context); scroll.setVerticalScrollBarEnabled(false); scroll.setNestedScrollingEnabled(true);
        scroll.addView(list, new FrameLayout.LayoutParams(Ui.MATCH, Ui.WRAP));
        ledgerScroll = scroll;
        table.addView(scroll, ui.ll(Ui.MATCH, 0, 1));
        return table;
    }
    private View empty(PcState.Claude c) {
        LinearLayout row = ui.row();
        CarPart part = car(null, "", 62, true);
        row.addView(part.sprite, carSize(62));
        TextView text = ui.text(!c.connected && c.threads.isEmpty() ? ClaudeText.NOT_CONNECTED : ClaudeText.noThreads(period), 15, ui.theme.muted); Ui.wrap(text, 3);
        row.addView(text, ui.margins(ui.ll(0, Ui.WRAP, 1), 16, 0, 0, 0));
        return row;
    }
    private void addGarageRows(PcState.Claude c, LinearLayout list, PcState.ClaudeGroup group, int index, double width) {
        final boolean isFolded = folded(group);
        double projectHeight = Math.max(52, Math.ceil(scaled(17) * 1.33 + 12 * 1.33 + 10)), threadHeight = Math.max(36, Math.ceil(scaled(14) * 1.33 + 10));
        if (index > 0) list.addView(ui.divider(1, ui.theme.controlBorder), ui.ll(Ui.MATCH, Math.max(1, ui.px(1))));
        LinearLayout row = ledgerRow(width, projectHeight);
        final GaragePart garage = garage(group, 40);
        row.addView(garage.tap, ui.ll(ui.px(52), Ui.MATCH));
        LinearLayout name = ui.column(); name.setGravity(Gravity.CENTER_VERTICAL);
        final TextView title = trimmed(ui.text(group.project, scaled(17), ui.theme.text)); title.setTypeface(Ui.MEDIUM); name.addView(title);
        final TextView info = trimmed(ui.text("", 12, ui.theme.muted)); name.addView(info);
        final View shareFill = new View(ui.context);
        FrameLayout share = new FrameLayout(ui.context);
        share.setBackground(ui.shape(ui.theme.track, 0, 0, 2));
        shareFill.setBackground(ui.shape(PitArt.accent(group.project), 0, 0, 2));
        share.addView(shareFill, new FrameLayout.LayoutParams(0, Ui.MATCH));
        name.addView(share, ui.margins(ui.ll(Ui.MATCH, ui.px(4)), 0, 4, 0, 0));
        row.addView(name, ui.margins(ui.ll(0, Ui.MATCH, 1), 4, 0, 0, 0));
        final TextView tokens = ui.text("", scaled(17), ui.theme.text); cell(row, tokens, scaled(72), Gravity.END);
        final TextView cached = ui.text("", scaled(15), ui.theme.muted); cell(row, cached, scaled(80), Gravity.END);
        final TextView dollars = ui.text("", scaled(15), ui.theme.muted); cell(row, dollars, scaled(64), Gravity.END);
        final PitViews.Mix mix = new PitViews.Mix(ui.context, ui.theme, ui.unit); if (CardSizing.claudeModels(width)) cell(row, mix, 64, Gravity.START);
        final TextView changed = ui.text("", 12, ui.theme.muted); if (CardSizing.claudeLines(width)) cell(row, changed, scaled(84), Gravity.END);
        list.addView(row);
        final String key = group.key;
        fills.add(new Fill() { @Override public void run(PcState.Claude c, double now) {
            PcState.ClaudeGroup g = group(c, key); if (g == null) return;
            boolean needs = ClaudeText.groupNeeds(c, g);
            title.setTextColor(needs ? Theme.WARNING : ui.theme.text);
            info.setText(isFolded ? ClaudeText.cars(g.threads.size()) + " in the garage · tap to roll them out" : ClaudeText.groupDetail(c, g, now));
            long total = 0; for (PcState.ClaudeGroup x : groups(c)) total += x.tokens;
            View parent = (View) shareFill.getParent();
            int full = parent == null ? 0 : parent.getWidth();
            ViewGroup.LayoutParams params = shareFill.getLayoutParams(); params.width = total <= 0 || full <= 0 ? 0 : (int) Math.round(full * (double) g.tokens / total); shareFill.setLayoutParams(params);
            tokens.setText(counted(c) ? ClaudeText.tokens(g.tokens) : "—"); cached.setText(counted(c) ? ClaudeText.tokens(g.cache) : "");
            dollars.setText(!counted(c) ? "" : c.apiValue ? "$" + ClaudeText.dollars(g.cost) : String.valueOf(g.replies));
            mix.shares = g.mix; mix.setContentDescription(ClaudeText.mix(g.mix)); mix.invalidate();
            changed.setText(lines(g.added, g.removed));
            renderGarage(c, garage, g);
        } });
        if (isFolded) return;
        for (int j = 0; j < group.threads.size(); j++) {
            final PcState.ClaudeThread thread = c.thread(group.threads.get(j));
            if (thread == null) continue;
            LinearLayout trow = ledgerRow(width, threadHeight);
            CarPart part = car(thread, group.key, 48, false);
            FrameLayout carCell = new FrameLayout(ui.context);
            carCell.addView(part.sprite, ui.frame(ui.px(48), ui.px(PitSprite.carHeight(48)), Gravity.CENTER));
            trow.addView(carCell, ui.ll(ui.px(52), Ui.MATCH));
            garage.cars.add(part);
            LinearLayout what = ui.row();
            final TextView chip = ui.text("", 11, ui.theme.text); chip.setPadding(ui.px(6), ui.px(1), ui.px(6), ui.px(1)); chip.setSingleLine(true);
            what.addView(chip, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 8, 0));
            final TextView name2 = trimmed(ui.text("", scaled(14), ui.theme.text)); what.addView(name2, ui.ll(0, Ui.WRAP, 1));
            final TextView where = ui.text("", 12, ui.theme.muted); where.setSingleLine(true); what.addView(where, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 8, 0, 0, 0));
            trow.addView(what, ui.margins(ui.ll(0, Ui.MATCH, 1), 4, 0, 0, 0));
            final TextView ttokens = ui.text("", scaled(14), ui.theme.text); cell(trow, ttokens, scaled(72), Gravity.END);
            final TextView tcached = ui.text("", scaled(14), ui.theme.muted); cell(trow, tcached, scaled(80), Gravity.END);
            final TextView tdollars = ui.text("", scaled(14), ui.theme.muted); cell(trow, tdollars, scaled(64), Gravity.END);
            final PitViews.Mix tmix = new PitViews.Mix(ui.context, ui.theme, ui.unit); if (CardSizing.claudeModels(width)) cell(trow, tmix, 64, Gravity.START);
            final TextView tlines = ui.text("", 12, ui.theme.muted); if (CardSizing.claudeLines(width)) cell(trow, tlines, scaled(84), Gravity.END);
            final View tap = tappable(trow, thread);
            list.addView(tap);
            final String tid = thread.id;
            fills.add(new Fill() { @Override public void run(PcState.Claude c, double now) {
                PcState.ClaudeThread t = c.thread(tid); if (t == null) return;
                tap.setAlpha(t.running ? 1f : 0.62f);
                chip.setText(ClaudeText.chip(t.pit).toUpperCase(Locale.ROOT));
                chip(chip, t.pit);
                name2.setText(ClaudeText.threadTitle(t, now, dash.use24())); name2.setTextColor("need".equals(t.pit) ? Theme.WARNING : ui.theme.text);
                where.setText(ClaudeText.where(t, now));
                ttokens.setText(counted(c) ? ClaudeText.tokens(t.tokens[period]) : "—"); tcached.setText(counted(c) ? ClaudeText.tokens(t.cache[period]) : "");
                tdollars.setText(!counted(c) ? "" : c.apiValue ? "$" + ClaudeText.dollars(t.cost[period]) : String.valueOf(t.replies[period]));
                tmix.shares = t.mix[period]; tmix.invalidate();
                tlines.setText(lines(t.lines[period][0], t.lines[period][1]));
            } });
        }
    }
    private void chip(TextView chip, String pit) {
        if ("need".equals(pit)) { chip.setBackground(ui.shape(Theme.WARNING, 0, 0, 6)); chip.setTextColor(Theme.ON_ACCENT); chip.setTypeface(Ui.MEDIUM); return; }
        chip.setTypeface(Ui.REGULAR);
        if ("done".equals(pit)) { chip.setBackground(ui.shape(Ui.alpha(PitArt.ACCENTS[1], 0.14f), 0, 0, 6)); chip.setTextColor(PitArt.ACCENTS[1]); return; }
        if ("stuck".equals(pit)) { chip.setBackground(ui.shape(Ui.alpha(Theme.WARNING, 0.16f), 0, 0, 6)); chip.setTextColor(Theme.WARNING); return; }
        if ("end".equals(pit) || "idle".equals(pit)) { chip.setBackground(ui.shape(0x00000000, ui.theme.controlBorder, 1, 6)); chip.setTextColor(ui.theme.muted); return; }
        chip.setBackground(ui.shape(Ui.alpha(ui.theme.accent, 0.16f), 0, 0, 6)); chip.setTextColor(ui.theme.accent);
    }

    // The side panel: the 5-hour lanes and today's four facts.
    private View sidePanel(PcState.Claude c) {
        LinearLayout side = ui.column();
        LinearLayout lanes = ui.column();
        lanes.addView(upper("Last 5 hours · laps of 10 min", 11));
        final List<PitViews.Lane> bins = new ArrayList<PitViews.Lane>();
        for (PcState.ClaudeLane lane : c.lanes) {
            LinearLayout row = ui.row();
            TextView name = trimmed(ui.text(lane.thread ? "· " + lane.project : lane.project, lane.thread ? 11 : 12, lane.thread ? ui.theme.muted : ui.theme.text));
            row.addView(name, ui.margins(ui.ll(ui.px(lane.thread ? 96 : 104), Ui.WRAP), lane.thread ? 8 : 0, 0, 0, 0));
            PitViews.Lane b = new PitViews.Lane(ui.context, ui.theme, ui.unit); b.color = PitArt.accent(lane.color); b.bins = lane.bins; bins.add(b);
            row.addView(b, ui.margins(ui.ll(0, ui.px(lane.thread ? 10 : 16), 1), 8, 0, 0, 0));
            lanes.addView(row, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, lane.thread ? 3 : 6, 0, 0));
        }
        if (c.lanes.isEmpty()) lanes.addView(ui.text("Nothing in the last 5 hours.", 12, ui.theme.muted), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 6, 0, 0));
        FrameLayout axis = new FrameLayout(ui.context);
        final TextView start = ui.text("", 11, ui.theme.muted), middle = ui.text("", 11, ui.theme.muted);
        axis.addView(start, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START)); axis.addView(middle, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.CENTER_HORIZONTAL)); axis.addView(ui.text("now", 11, ui.theme.muted), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END));
        lanes.addView(axis, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 112, 4, 0, 0));
        side.addView(tile(lanes, 12, 10), ui.ll(Ui.MATCH, Ui.WRAP));
        LinearLayout facts = ui.column();
        final TextView[] values = new TextView[4], keys = new TextView[4];
        for (int r = 0; r < 2; r++) {
            LinearLayout row = ui.row(); row.setGravity(Gravity.BOTTOM);
            for (int k = 0; k < 2; k++) {
                int i = r * 2 + k;
                LinearLayout fact = ui.row(); fact.setGravity(Gravity.BOTTOM);
                values[i] = reading("", scaled(20)); fact.addView(values[i], ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 6, 0));
                keys[i] = upper("", 11); fact.addView(keys[i], ui.margins(ui.ll(0, Ui.WRAP, 1), 0, 0, 0, 3));
                row.addView(fact, ui.ll(0, Ui.WRAP, 1));
            }
            facts.addView(row, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, r == 0 ? 0 : 4, 0, 0));
        }
        side.addView(facts, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 4, 10, 4, 0));
        fills.add(new Fill() { @Override public void run(PcState.Claude c, double now) {
            double max = 1; for (PcState.ClaudeLane l : c.lanes) for (double v : l.bins) max = Math.max(max, v);
            for (PitViews.Lane b : bins) { b.maximum = max; b.invalidate(); }
            start.setText(ClaudeText.clock(now - ClaudePit.FIVE_HOURS, dash.use24())); middle.setText(ClaudeText.clock(now - 150 * 60, dash.use24()));
            PcState.ClaudeFacts f = c.facts;
            values[0].setText(counted(c) ? ClaudeText.tokens(f.tokens) : "—"); keys[0].setText(!counted(c) ? "TODAY" : c.apiValue ? "TODAY · $" + ClaudeText.dollars(f.cost) + " API VALUE" : "TODAY · " + f.replies + " REPLIES");
            values[1].setText(ClaudeText.share(f.cacheHit())); keys[1].setText("CACHE HITS");
            values[2].setText(f.telemetry ? String.valueOf(f.tools) : "—"); keys[2].setText(f.telemetry ? "TOOLS · " + f.asked + " ASKED" : "TOOLS · NEEDS TELEMETRY");
            if (f.telemetry && (f.added > 0 || f.removed > 0)) {
                SpannableStringBuilder added = new SpannableStringBuilder(ClaudeText.added(f.added)); added.setSpan(new ForegroundColorSpan(PitArt.LIME), 0, added.length(), Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
                values[3].setText(added); keys[3].setText(ClaudeText.removed(f.removed) + " · " + ClaudeText.threads(f.linesThreads).toUpperCase(Locale.ROOT));
            } else { values[3].setText("—"); keys[3].setText("LINES"); }
        } });
        return side;
    }

    // ----- Medium -----

    private View medium(PcState.Claude c, double width, double height) {
        double head = Math.max(24, Math.ceil(15 * ui.textScale * 1.34));
        LinearLayout column = ui.column();
        column.addView(header(false, width), ui.ll(Ui.MATCH, ui.px(head)));
        LinearLayout tiles = ui.row(); tiles.setGravity(Gravity.FILL_VERTICAL);
        if (c.limitsKnown()) { tiles.addView(miniTile(true), ui.ll(0, Ui.MATCH, 1)); tiles.addView(miniTile(false), ui.margins(ui.ll(0, Ui.MATCH, 1), 10, 0, 0, 0)); }
        else { TextView message = ui.text(ClaudeText.limitsMissing(c), 13, ui.theme.muted); Ui.wrap(message, 3); tiles.addView(tile(message, 10, 8), ui.ll(0, Ui.MATCH, 1)); }
        column.addView(tiles, ui.margins(ui.ll(Ui.MATCH, ui.px(CardSizing.CLAUDE_MINI_HEIGHT)), 0, 10, 0, 0));
        List<PcState.ClaudeGroup> groups = groups(c);
        double rowHeight = CardSizing.claudeMediumRow(ui.textScale), room = height - head - 10 - CardSizing.CLAUDE_MINI_HEIGHT - 6;
        int fit = Math.max(1, (int) Math.floor(room / rowHeight));
        LinearLayout list = ui.column();
        if (groups.isEmpty()) list.addView(empty(c));
        boolean money = width >= 400;
        for (int i = 0; i < Math.min(fit, groups.size()); i++) {
            PcState.ClaudeGroup group = groups.get(i);
            final boolean isFolded = folded(group);
            list.addView(ui.divider(1, ui.theme.controlBorder), ui.ll(Ui.MATCH, Math.max(1, ui.px(1))));
            LinearLayout row = ui.row();
            final GaragePart garage = garage(group, 36);
            row.addView(garage.tap, ui.ll(ui.px(48), Ui.MATCH));
            LinearLayout text = ui.column(); text.setGravity(Gravity.CENTER_VERTICAL);
            LinearLayout first = ui.row();
            final TextView name = trimmed(ui.text(group.project, scaled(15), ui.theme.text)); name.setTypeface(Ui.MEDIUM);
            first.addView(name, ui.ll(0, Ui.WRAP, 1));
            double nameRoom = width - 48 - 6 - (scaled(64) + 10) - (money ? scaled(56) + 10 : 0);
            int shown = isFolded ? 0 : CardSizing.claudeInlineCars(nameRoom, group.threads.size());
            if (isFolded) first.addView(bubble(group.threads.size()), ui.margins(ui.ll(Ui.WRAP, ui.px(20)), 8, 0, 0, 0));
            for (int j = 0; j < shown; j++) {
                PcState.ClaudeThread t = c.thread(group.threads.get(j));
                CarPart part = car(t, group.key, 30, true);
                FrameLayout holder = new FrameLayout(ui.context); holder.addView(part.sprite, ui.frame(ui.px(30), ui.px(PitSprite.carHeight(30)), Gravity.CENTER));
                first.addView(tappable(holder, t), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), j == 0 ? 8 : 2, 0, 0, 0));
                garage.cars.add(part);
            }
            if (!isFolded && shown < group.threads.size()) first.addView(ui.caption("+" + (group.threads.size() - shown)), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 2, 0, 0, 0));
            text.addView(first, ui.ll(Ui.MATCH, Ui.WRAP));
            final TextView sub = trimmed(ui.text("", 12, ui.theme.muted)); text.addView(sub);
            row.addView(text, ui.margins(ui.ll(0, Ui.MATCH, 1), 6, 0, 0, 0));
            final TextView tokens = ui.text("", scaled(15), ui.theme.text); cell(row, tokens, scaled(64), Gravity.END);
            final TextView dollars = ui.text("", scaled(14), ui.theme.muted); if (money) cell(row, dollars, scaled(56), Gravity.END);
            list.addView(row, ui.ll(Ui.MATCH, ui.px(rowHeight)));
            final String key = group.key;
            fills.add(new Fill() { @Override public void run(PcState.Claude c, double now) {
                PcState.ClaudeGroup g = group(c, key); if (g == null) return;
                PcState.ClaudeThread asking = null; for (String id : g.threads) { PcState.ClaudeThread t = c.thread(id); if (t != null && t.running && "needs".equals(t.state)) { asking = t; break; } }
                name.setTextColor(asking != null ? Theme.WARNING : ui.theme.text);
                sub.setText(asking != null ? "Needs you · " + ClaudeText.threadTitle(asking, now, dash.use24()) : isFolded ? ClaudeText.cars(g.threads.size()) + " in the garage" : ClaudeText.groupDetail(c, g, now));
                sub.setTextColor(asking != null ? Theme.WARNING : ui.theme.muted);
                tokens.setText(counted(c) ? ClaudeText.tokens(g.tokens) : "—"); dollars.setText(!counted(c) ? "" : c.apiValue ? "$" + ClaudeText.dollars(g.cost) : ClaudeText.replies(g.replies));
                renderGarage(c, garage, g);
            } });
        }
        column.addView(list, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 6, 0, 0));
        return column;
    }
    private View bubble(int count) {
        TextView bubble = ui.text(String.valueOf(count), 12, ui.theme.page); bubble.setTypeface(Ui.MEDIUM); bubble.setGravity(Gravity.CENTER);
        bubble.setBackground(ui.shape(ui.theme.text, 0, 0, 10)); bubble.setMinWidth(ui.px(20)); bubble.setPadding(ui.px(5), 0, ui.px(5), 0);
        return bubble;
    }
    private View miniTile(final boolean fiveHour) {
        LinearLayout stack = ui.column();
        FrameLayout top = new FrameLayout(ui.context);
        top.addView(upper(fiveHour ? "5-hour" : "Week", 12), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START));
        final TextView reset = ui.text("", 12, ui.theme.muted); top.addView(reset, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END));
        stack.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
        FrameLayout middle = new FrameLayout(ui.context);
        final TextView value = reading("—", 28); middle.addView(value, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.BOTTOM));
        final TextView pace = ui.text("", 12, ui.theme.muted); middle.addView(pace, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.BOTTOM));
        stack.addView(middle, ui.ll(Ui.MATCH, Ui.WRAP));
        final PitViews.PaceBar bar = new PitViews.PaceBar(ui.context, ui.theme, ui.unit);
        stack.addView(bar, ui.ll(Ui.MATCH, ui.px(14)));
        fills.add(new Fill() { @Override public void run(PcState.Claude c, double now) {
            PcState.ClaudeWindow w = fiveHour ? c.fiveHour : c.week;
            double pct = ClaudeText.percent(w, now), elapsed = ClaudePit.elapsed(w, fiveHour ? ClaudePit.FIVE_HOURS : ClaudePit.WEEK, now), projection = ClaudePit.projection(pct, elapsed);
            if (w == null) value.setText("—"); else value.setText(big(pct, ClaudeText.warns(pct) ? Theme.WARNING : ui.theme.text, 28));
            reset.setText(w == null ? (fiveHour ? "none now" : "") : w.resetsAt <= 0 ? "" : !ClaudeText.current(w, now) ? "reset" : pct >= 100 ? "back " + ClaudeText.when(w.resetsAt, now, dash.use24()) : ClaudeText.when(w.resetsAt, now, dash.use24()));
            reset.setTextColor(pct >= 100 ? Theme.WARNING : ui.theme.muted);
            pace.setText(Double.isNaN(projection) ? "" : "pace " + Math.round(projection) + "%"); pace.setTextColor(ClaudePit.warns(projection) ? Theme.WARNING : ui.theme.muted);
            bar.value = pct / 100; bar.projection = Double.isNaN(projection) ? Double.NaN : projection / 100; bar.pace = elapsed; bar.warn = ClaudePit.warns(projection) || ClaudeText.warns(pct); bar.invalidate();
        } });
        return tile(stack, 10, 8);
    }

    // ----- Small -----

    private View small(PcState.Claude c, final double width, double height) {
        boolean meter = c.limitsKnown(), caption = CardSizing.claudeSmallMeter(height, ui.textScale);
        LinearLayout column = ui.column();
        final PcState.ClaudeSession first = c.sessions.isEmpty() ? null : c.sessions.get(0);
        PcState.ClaudeThread thread = first == null ? null : c.thread(first.id);
        LinearLayout top = ui.row();
        double size = CardSizing.claudeSmallCar(width, height - (caption ? CardSizing.CLAUDE_SMALL_METER_HEIGHT : 0));
        CarPart part = car(thread, "", size, true);
        FrameLayout holder = new FrameLayout(ui.context); holder.addView(part.sprite, ui.frame(ui.px(size), ui.px(PitSprite.carHeight(size)), Gravity.CENTER));
        top.addView(tappable(holder, thread), ui.ll(Ui.WRAP, Ui.WRAP));
        LinearLayout side = ui.column(); side.setGravity(Gravity.CENTER_VERTICAL);
        if (first == null) { TextView message = ui.text(c.connected ? ClaudeText.NO_SESSIONS : ClaudeText.NOT_CONNECTED, width < 300 ? 13 : 15, ui.theme.muted); Ui.wrap(message, 4); side.addView(message); }
        else {
            final TextView project = trimmed(ui.text("", scaled(17), ui.theme.text)); project.setTypeface(Ui.MEDIUM); side.addView(project);
            final TextView state = trimmed(ui.text("", 13, ui.theme.muted)); side.addView(state);
            final TextView more = trimmed(ui.text("", 12, ui.theme.muted)); side.addView(more);
            final String id = first.id;
            fills.add(new Fill() { @Override public void run(PcState.Claude c, double now) {
                PcState.ClaudeSession s = null; for (PcState.ClaudeSession x : c.sessions) if (x.id.equals(id)) s = x;
                if (s == null) return;
                project.setText(s.project);
                state.setText(ClaudeText.stateWords(s.state, s.since, now, true)); state.setTextColor("needs".equals(s.state) ? Theme.WARNING : ui.theme.muted);
                int working = 0, total = 0; boolean other = false;
                for (PcState.ClaudeSession x : c.sessions) if (!x.id.equals(id)) { if ("working".equals(x.state)) working++; if ("needs".equals(x.state)) other = true; }
                for (PcState.ClaudeGroup g : c.groups[period]) total += g.threads.size();
                int others = total - 1 - working;
                List<String> parts = new ArrayList<String>();
                if (other) parts.add("another needs you");
                if (working > 0) parts.add("+" + working + " working");
                if (others > 0) parts.add(others + " more " + (others == 1 ? "thread" : "threads"));
                StringBuilder text = new StringBuilder(); for (String p : parts) { if (text.length() > 0) text.append(" · "); text.append(p); }
                more.setText(text);
            } });
        }
        if (c.sample) side.addView(ui.caption("SAMPLE"));
        top.addView(side, ui.margins(ui.ll(0, Ui.WRAP, 1), 12, 0, 0, 0));
        column.addView(top, ui.ll(Ui.MATCH, 0, 1));
        if (meter && caption) {
            LinearLayout line = ui.row(); line.setGravity(Gravity.BOTTOM);
            line.addView(upper(c.fiveHour == null ? "Week" : "5-hour", 12), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 8, 4));
            final TextView value = reading("—", 28); line.addView(value, ui.ll(Ui.WRAP, Ui.WRAP));
            final TextView right = trimmed(ui.text("", 12, ui.theme.muted)); right.setGravity(Gravity.END); line.addView(right, ui.margins(ui.ll(0, Ui.WRAP, 1), 8, 0, 0, 4));
            column.addView(line, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
            final PitViews.PaceBar bar = new PitViews.PaceBar(ui.context, ui.theme, ui.unit);
            column.addView(bar, ui.ll(Ui.MATCH, ui.px(14)));
            fills.add(new Fill() { @Override public void run(PcState.Claude c, double now) {
                boolean weekly = c.fiveHour == null;
                PcState.ClaudeWindow w = weekly ? c.week : c.fiveHour;
                double pct = ClaudeText.percent(w, now), elapsed = ClaudePit.elapsed(w, weekly ? ClaudePit.WEEK : ClaudePit.FIVE_HOURS, now), projection = ClaudePit.projection(pct, elapsed);
                value.setText(w == null ? "—" : Math.round(pct) + "%"); value.setTextColor(ClaudeText.warns(pct) ? Theme.WARNING : ui.theme.text);
                List<String> parts = new ArrayList<String>();
                if (!Double.isNaN(projection)) parts.add("pace " + Math.round(projection) + "%");
                if (w != null && w.resetsAt > 0 && ClaudeText.current(w, now)) parts.add(ClaudeText.when(w.resetsAt, now, dash.use24()));
                if (ClaudeText.old(c, now) && width >= 280) parts.add("as of " + ClaudeText.clock(c.updatedAt, dash.use24()));
                StringBuilder text = new StringBuilder(); for (String p : parts) { if (text.length() > 0) text.append(" · "); text.append(p); }
                right.setText(text); right.setTextColor(ClaudePit.warns(projection) ? Theme.WARNING : ui.theme.muted);
                bar.value = pct / 100; bar.projection = Double.isNaN(projection) ? Double.NaN : projection / 100; bar.pace = elapsed; bar.warn = ClaudePit.warns(projection) || ClaudeText.warns(pct); bar.invalidate();
            } });
        } else if (caption) {
            column.addView(trimmed(ui.caption("expired".equals(c.plan) ? "Claude sign-in ended" : "signedOut".equals(c.plan) ? "Sign in for plan limits" : ClaudeText.limitsMissing(c))), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
        }
        return column;
    }

    // ----- Filling in -----

    @Override public void tick(int ticks, boolean quietMode) {
        if (!built()) return;
        PcState.Claude c = claude();
        quiet = c != null ? c.still : quietMode;
        render();
    }

    @Override public void render() {
        if (!built() || rolling != null) return;
        PcState.Claude c = claude();
        if (!shape(c).equals(builtFor)) { dash.rebuildWidget(id); return; }
        live = active() && !quiet;
        if (c == null) { stopClock(); return; }
        double now = now();
        boolean needs = c.needsYou() > 0;
        // The one state allowed to shout: an Amber border around the whole card.
        card.setBackground(ui.shape(ui.theme.surface, needs ? Theme.WARNING : ui.theme.controlBorder, needs ? 2 : 1, ui.radius));
        for (Fill fill : fills) fill.run(c, now);
        // The fuel lamp follows the plan's tightest window: the 5-hour one, or the week when the plan has none.
        double five = ClaudeText.percent(c.fiveHour != null ? c.fiveHour : c.week, now);
        for (CarPart part : cars) renderCar(c, part, part.id == null ? null : c.thread(part.id), five);
        updateClock();
    }
    private void renderCar(PcState.Claude c, CarPart part, PcState.ClaudeThread t, double five) {
        int state = t == null ? (c.connected ? PitArt.IDLE : PitArt.END) : PitArt.state(t.pit);
        int color = t == null ? PitArt.ACCENTS[0] : PitArt.accent(t.project);
        double share = t == null ? 0 : ClaudePit.share(t.rate, c.scale);
        boolean fuel = t != null && t.running && ClaudePit.fuel(five);
        PitArt.Look look = new PitArt.Look(t == null ? PitArt.SPORT : PitArt.car(t.car), fuel, state == PitArt.WORK && ClaudePit.hot(share), ClaudePit.heavy(share));
        Bitmap[] frames = PitSprite.carFrames(color, state, look, part.width);
        String key = "car|" + color + "|" + state + "|" + look.key() + "|" + part.width;
        Integer before = part.id == null ? null : seen.get(part.id);
        boolean restart = before != null && before != state && PitArt.once(state) && live;
        if (part.id != null) seen.put(part.id, state);
        part.sprite.fps = quiet ? 0 : PitArt.fps(state, share, fuel);
        part.sprite.once = PitArt.once(state);
        part.sprite.show(key, frames, PitArt.still(state), restart);
        if (!live) part.sprite.hold();
        // Idle has its engine off, dimmed; an ended car is dimmed where its row isn't.
        part.sprite.setAlpha(state == PitArt.IDLE ? 0.7f : part.dim && state == PitArt.END ? 0.6f : 1f);
    }
    private void renderGarage(PcState.Claude c, GaragePart part, PcState.ClaudeGroup group) {
        boolean closed = folded(group);
        int state = garageState(c, group);
        Bitmap[] frames = PitSprite.garageFrames(PitArt.accent(group.project), state, closed, group.threads.size(), 40);
        part.sprite.fps = quiet || !(state == PitArt.NEED || (state == PitArt.WORK && !closed)) ? 0 : 3;
        part.sprite.once = false;
        part.sprite.show("garage|" + group.project + "|" + state + "|" + closed + "|" + group.threads.size(), frames, 0, false);
        if (!live) part.sprite.hold();
        part.sprite.setAlpha(state == PitArt.END ? 0.6f : 1f);
    }
    // A garage's state: needs you (the beacon), busy, all ended (dimmed), done or quiet.
    private static int garageState(PcState.Claude c, PcState.ClaudeGroup group) {
        boolean running = false, working = false, done = false, needs = false;
        for (String id : group.threads) {
            PcState.ClaudeThread t = c.thread(id); if (t == null || !t.running) continue;
            running = true;
            if ("needs".equals(t.state)) needs = true; else if ("working".equals(t.state)) working = true; else if ("done".equals(t.state)) done = true;
        }
        return needs ? PitArt.NEED : working ? PitArt.WORK : !running ? PitArt.END : done ? PitArt.DONE : PitArt.IDLE;
    }

    // ----- The frame clock: only while the card is on screen, the app in front, and not in Quiet mode -----

    private final Runnable frameTick = new Runnable() { @Override public void run() { stepFrames(); } };
    private boolean clockOn;
    private void updateClock() {
        boolean moving = rolling != null;
        double fps = rolling != null ? 20 : 4;
        for (CarPart part : cars) if (part.sprite.moving()) { moving = true; fps = Math.max(fps, part.sprite.fps); }
        for (GaragePart part : garages) if (part.sprite.moving()) moving = true;
        if (!live || !moving || card == null) { stopClock(); return; }
        if (!clockOn) { clockOn = true; lastFrame = System.nanoTime(); card.postDelayed(frameTick, (long) (1000 / Math.min(24, fps))); }
    }
    private void stopClock() { clockOn = false; if (card != null) card.removeCallbacks(frameTick); }
    private void stepFrames() {
        clockOn = false;
        if (!built() || !active() || quiet) { live = false; stopRoll(true); return; }
        long nowNanos = System.nanoTime();
        double seconds = Math.min(0.25, (nowNanos - lastFrame) / 1e9); lastFrame = nowNanos;
        double fps = 4;
        for (CarPart part : cars) { part.sprite.step(seconds); if (part.sprite.moving()) fps = Math.max(fps, part.sprite.fps); }
        for (GaragePart part : garages) part.sprite.step(seconds);
        if (rolling != null) { fps = 20; stepRoll(seconds); }
        boolean moving = rolling != null;
        for (CarPart part : cars) moving |= part.sprite.moving();
        for (GaragePart part : garages) moving |= part.sprite.moving();
        if (moving && card != null) { clockOn = true; card.postDelayed(frameTick, (long) (1000 / Math.min(24, fps))); }
    }

    // ----- Folding: rolling in and out (Ali, 1 Nov 2026), about 1.2 s, instant in Quiet mode -----

    private void toggle(String key) {
        PcState.Claude c = claude();
        if (rolling != null || c == null) return;
        PcState.ClaudeGroup group = group(c, key);
        if (group == null) return;
        final boolean fold = !folded(group);
        final String folder = key;
        GaragePart garage = null; for (GaragePart g : garages) if (g.key.equals(key)) garage = g;
        final Runnable apply = new Runnable() { @Override public void run() {
            pending.put(folder, fold);
            dash.commandQuietly(Json.map("action", "claude.collapse", "project", folder, "collapsed", fold));
            dash.rebuildWidget(id);
        } };
        boolean motion = live && !quiet;
        if (fold && motion && garage != null && !garage.cars.isEmpty()) { rolling = null; if (startRoll(garage, false)) { rolling.done = apply; return; } }
        if (!fold && motion) rollOut = key;
        apply.run();
    }

    private static final class Roll {
        GaragePart garage; boolean out; Runnable done; double t; int color, count;
        float bayX, bayY, garageX, garageY, garageSize;
        final List<RollCar> cars = new ArrayList<RollCar>();
    }
    private static final class RollCar { CarPart part; float targetX, targetY, apronX, width; int car; float x, bottom, drawWidth, squash; String pose = ""; int z; int wheel; boolean shown; }

    // Where a view sits on the overlay, in pixels.
    private float[] on(View view) {
        int[] a = new int[2], b = new int[2]; view.getLocationInWindow(a); layer.getLocationInWindow(b);
        return new float[] { a[0] - b[0], a[1] - b[1] };
    }
    private boolean startRoll(GaragePart garage, boolean outward) {
        if (layer == null || garage.sprite.getWidth() == 0) { showGarage(garage); return false; }
        PcState.Claude c = claude();
        PcState.ClaudeGroup group = c == null ? null : group(c, garage.key);
        // Only what shows moves: the ledger's visible part on Large, the whole card otherwise.
        float top = 0, bottom = layer.getHeight();
        if (ledgerScroll != null) { float[] at = on(ledgerScroll); top = at[1]; bottom = at[1] + ledgerScroll.getHeight(); }
        float[] g = on(garage.sprite);
        if (g[1] < top - 1 || g[1] + garage.sprite.getHeight() > bottom + 1) { showGarage(garage); return false; }
        Roll roll = new Roll(); roll.garage = garage; roll.out = outward;
        roll.color = PitArt.accent(group == null ? garage.key : group.project); roll.count = group == null ? garage.cars.size() : group.threads.size();
        roll.garageX = g[0]; roll.garageY = g[1]; roll.garageSize = garage.sprite.getWidth();
        roll.bayX = g[0] + roll.garageSize * 0.5f; roll.bayY = g[1] + roll.garageSize * 0.93f;
        for (CarPart part : garage.cars) {
            if (part.sprite.getWidth() == 0) continue;
            float[] at = on(part.sprite);
            if (at[1] < top - 1 || at[1] + part.sprite.getHeight() > bottom + 1) continue;
            RollCar car = new RollCar(); car.part = part; car.width = part.sprite.getWidth();
            // The sprite's frame reaches a quarter of the car's height below it: the car's bottom is at five sixths.
            car.targetX = at[0] + car.width / 2; car.targetY = at[1] + part.sprite.getHeight() * 5 / 6f; car.apronX = car.targetX + car.width * 1.4f;
            PcState.ClaudeThread t = c == null || part.id == null ? null : c.thread(part.id);
            car.car = t == null ? PitArt.SPORT : PitArt.car(t.car);
            roll.cars.add(car);
            if (outward) part.sprite.setVisibility(View.INVISIBLE);
        }
        garage.sprite.setVisibility(View.INVISIBLE);
        layer.top = top; layer.bottom = bottom;
        rolling = roll;
        stopClock(); updateClock();
        layer.invalidate();
        return true;
    }
    private static double ease(double t) { return t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2; }
    private static double span(double t, double a, double b) { return Math.max(0, Math.min(1, (t - a) / (b - a))); }
    private static float lerp(double a, double b, double t) { return (float) (a + (b - a) * t); }
    private void stepRoll(double seconds) {
        Roll r = rolling;
        if (r == null) return;
        r.t += seconds;
        if (r.t >= ROLL) { stopRoll(true); return; }
        double t = r.t, drive = 0.6;
        int n = r.cars.size();
        double stagger = n > 1 ? Math.min(0.15, 0.3 / (n - 1)) : 0;
        int wheel = (int) Math.floor(t * 16) % 4 * 2;
        for (int i = 0; i < n; i++) {
            RollCar car = r.cars.get(i);
            int order = r.out ? i : n - 1 - i;
            double start = 0.15 + order * stagger, u = span(t, start, start + drive);
            car.shown = false;
            if (t < start) { if (!r.out) car.part.sprite.setVisibility(View.VISIBLE); continue; }
            if (!r.out) car.part.sprite.setVisibility(View.INVISIBLE);
            if (u >= 1) { if (r.out) car.part.sprite.setVisibility(View.VISIBLE); continue; }
            car.shown = true; car.wheel = wheel; car.squash = 1; car.z = 3;
            double front = car.width * 0.55;
            if (r.out) {
                if (u < 0.45) { double v = ease(u / 0.45); car.pose = "front"; car.x = lerp(r.bayX, car.apronX, v); car.bottom = lerp(r.bayY, car.targetY, v); car.drawWidth = (float) (front * (0.4 + 0.6 * v)); car.z = v < 0.3 ? 1 : 3; }
                else if (u < 0.6) { car.pose = "turn"; car.x = car.apronX; car.bottom = car.targetY; car.drawWidth = car.width; car.squash = 0.45f; }
                else { double v = ease(span(u, 0.6, 1)); car.pose = "side"; car.x = lerp(car.apronX, car.targetX, v); car.bottom = car.targetY; car.drawWidth = car.width; }
            } else {
                if (u < 0.4) { double v = ease(u / 0.4); car.pose = "reverse"; car.x = lerp(car.targetX, car.apronX, v); car.bottom = car.targetY; car.drawWidth = car.width; }
                else if (u < 0.52) { car.pose = "turnback"; car.x = car.apronX; car.bottom = car.targetY; car.drawWidth = car.width; car.squash = 0.45f; }
                else { double v = ease(span(u, 0.52, 1)); car.pose = v < 0.15 ? "brake" : "rear"; car.x = lerp(car.apronX, r.bayX, v); car.bottom = lerp(car.targetY, r.bayY, v); car.drawWidth = (float) (front * (1 - 0.6 * v)); car.z = v > 0.6 ? 1 : 3; }
            }
        }
        layer.invalidate();
    }
    private void stopRoll(boolean finished) {
        Roll r = rolling;
        if (r == null) return;
        rolling = null;
        if (layer != null) layer.invalidate();
        showGarage(r.garage);
        if (finished && r.done != null) r.done.run();
        else if (finished) render();
    }
    private static void showGarage(GaragePart garage) { garage.sprite.setVisibility(View.VISIBLE); for (CarPart car : garage.cars) car.sprite.setVisibility(View.VISIBLE); }

    // The roll's overlay: the bay behind the cars, the cars inside it, the garage's walls and shutter in front of them,
    // then the cars outside. Drawn with filtering off, like the sprites.
    private final class RollView extends View {
        float top, bottom;
        private final Paint pixels = new Paint();
        RollView(Context context) { super(context); pixels.setFilterBitmap(false); setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO); }
        private void blit(Canvas canvas, Bitmap bitmap, float x, float y, float w, float h) { canvas.drawBitmap(bitmap, new Rect(0, 0, bitmap.getWidth(), bitmap.getHeight()), new RectF(x, y, x + w, y + h), pixels); }
        private Bitmap overlay(String key, List<com.alialyami.widgetscreen.core.PitArt.Op> ops, float width, double boxW, double boxH) {
            int px = PitArt.pixelSize(width / ui.unit), w = Math.max(4, Math.round(width / ui.unit / px)), h = Math.max(4, (int) Math.round(width / ui.unit * boxH / boxW / px));
            return PitSprite.frame("roll|" + key + "|" + w, ops, 0, 0, boxW, boxH, w, h);
        }
        @Override protected void onDraw(Canvas canvas) {
            Roll r = rolling;
            if (r == null) return;
            canvas.save(); canvas.clipRect(0, top, getWidth(), bottom);
            double t = r.t; int k; boolean sign = false;
            if (r.out) k = t < 0.15 ? (int) Math.round(8 * (1 - t / 0.15)) : t > 1.05 ? (int) Math.round(4 * span(t, 1.05, 1.2)) : 0;
            else { k = t < 0.15 ? (int) Math.round(4 * (1 - t / 0.15)) : t > 1.05 ? (int) Math.round(8 * span(t, 1.05, 1.2)) : 0; sign = t > 1.15; }
            blit(canvas, overlay("back|" + r.color, PitArt.garageBack(r.color), r.garageSize, 100, 100), r.garageX, r.garageY, r.garageSize, r.garageSize);
            for (int pass = 0; pass < 2; pass++) {
                if (pass == 1) blit(canvas, overlay("front|" + r.color + "|" + k + "|" + (sign ? r.count : 0), PitArt.garageFront(r.color, k, sign ? r.count : 0, !sign), r.garageSize, 100, 100), r.garageX, r.garageY, r.garageSize, r.garageSize);
                for (RollCar car : r.cars) {
                    if (!car.shown || (pass == 0) != (car.z == 1)) continue;
                    Bitmap bitmap; float h;
                    if (car.pose.equals("front") || car.pose.equals("rear") || car.pose.equals("brake")) {
                        bitmap = overlay(car.pose + "|" + r.color, car.pose.equals("front") ? PitArt.front(r.color, true) : PitArt.rear(r.color, car.pose.equals("brake")), car.width * 0.55f, 80, 56);
                        h = car.drawWidth * 56 / 80;
                        float w = car.drawWidth * car.squash;
                        blit(canvas, bitmap, car.x - w / 2, car.bottom - h, w, h);
                    } else {
                        boolean reverse = car.pose.equals("reverse") || car.pose.equals("turnback"), moving = car.pose.equals("side") || car.pose.equals("reverse");
                        PitArt.Look look = new PitArt.Look(); look.car = car.car; look.lights = !reverse; look.reverse = reverse;
                        Bitmap[] frames = PitSprite.carFrames(r.color, moving ? PitArt.WORK : PitArt.IDLE, look, car.width / ui.unit);
                        bitmap = frames[moving ? car.wheel : 0];
                        h = (float) (car.drawWidth * 60 / 140 * 1.5);
                        float w = car.drawWidth * car.squash;
                        // The frame's bottom quarter-height is below the car.
                        blit(canvas, bitmap, car.x - w / 2, car.bottom - h * 5 / 6f, w, h);
                    }
                }
            }
            canvas.restore();
        }
    }
}
