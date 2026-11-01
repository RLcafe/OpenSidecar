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
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.views.CritterView;
import com.alialyami.widgetscreen.views.ProgressLine;

import java.util.ArrayList;
import java.util.List;

// Claude Code on the PC, in the same three designs as the Windows ClaudeCard (CardSizing.claude;
// docs/design/claude-watcher.md): Small is the loudest critter with its project and state over the 5-hour meter;
// Medium a lineup of critters with usage by project beside or under it, over the two plan meters; Large a row per
// session beside a panel with the meters, a period switch and usage by project. Usage shows tokens and what they
// would cost at API prices. The PC leaves out the projects unticked in its Settings → Claude. When a session needs you the card's border turns Amber. Tapping a session brings its window forward on the
// PC. The period switch is the phone's own.
public final class ClaudeWidget extends Widget {
    private static final String[] PERIOD_DESCRIPTIONS = { "the 5-hour window", "today", "the week" };
    private int design, period;
    private String builtFor = "";
    private FrameLayout card;
    private TextView heading, flag, more, source, smallPercent;
    private ProgressLine smallMeter;
    private final List<TextView> extras = new ArrayList<TextView>();
    private Meter five, week;
    private boolean phase, quiet;
    private final List<Part> parts = new ArrayList<Part>();
    private final List<ProjectRow> projects = new ArrayList<ProjectRow>();

    private static final class Part { CritterView critter; TextView title, state, detail, tokens, cost; String id; }
    private static final class Meter { TextView percent, reset; CritterView.Segments bar; }
    private static final class ProjectRow { TextView name, tokens; ProgressLine share; }

    public ClaudeWidget(Dashboard dash) { super("Claude", dash); }

    @Override public String part() { return "claude"; }

    private PcState.Claude claude() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.claude; }
    private static double now() { return System.currentTimeMillis() / 1000.0; }

    // What decides the design's parts: which sessions, and which areas have something to show.
    private String shape(PcState.Claude c) {
        if (c == null) return "-";
        StringBuilder ids = new StringBuilder();
        for (PcState.ClaudeSession s : c.sessions) ids.append(s.id).append(':').append(s.canFocus).append(',');
        return c.connected + "|" + c.sample + "|" + ids + "|" + c.limitsKnown() + c.noPlan + c.plan + "|" + c.extra.size() + "|" + c.logsFailed + "|" + c.usage[period].size() + "|" + period;
    }

    @Override protected View create() {
        PcState.Claude c = claude();
        builtFor = shape(c);
        parts.clear(); projects.clear(); extras.clear(); heading = flag = more = source = smallPercent = null; smallMeter = null; five = week = null;
        design = CardSizing.claude(width, height);
        double bodyWidth = Math.max(1, width - 48), bodyHeight = Math.max(1, height - 48);
        View content = c == null ? ui.caption("Waiting for the PC…") : design == CardSizing.SMALL ? small(c, bodyWidth, bodyHeight) : design == CardSizing.MEDIUM ? medium(c, bodyWidth, bodyHeight) : large(c, bodyWidth, bodyHeight);
        card = ui.card(content, 24);
        return card;
    }

    private CritterView critter() { CritterView view = new CritterView(ui.context, ui.theme); view.setPhase(phase, quiet); return view; }
    private TextView wrapped(String text, float size, double maxHeight) {
        TextView view = ui.text(text, size, ui.theme.muted);
        Ui.wrap(view, Math.max(1, (int) Math.floor(maxHeight / (size * 1.25))));
        return view;
    }
    // A tappable session: its whole area brings the session's window forward on the PC, when one is known.
    private View tappable(View child, PcState.ClaudeSession session) {
        if (session == null || !session.canFocus) return child;
        final String id = session.id;
        child.setClickable(true);
        child.setBackground(ui.pressable(0x00000000, 0, 0, 12));
        child.setContentDescription(session.shown() + ". Tap to show its window on the PC.");
        child.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "claude.focus", "id", id)); } }));
        return child;
    }
    private View headingRow() {
        FrameLayout row = new FrameLayout(ui.context);
        heading = ui.label("CLAUDE CODE");
        row.addView(heading, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        flag = ui.caption("");
        row.addView(flag, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        return row;
    }

    // Small: the most urgent session's critter at the left, its project and state at the right, the 5-hour meter below.
    private View small(PcState.Claude c, double width, double height) {
        // The meter's percentage needs a caption line; the shortest cards keep the bar alone.
        boolean meter = c.limitsKnown(), caption = CardSizing.claudeSmallPercent(height, ui.textScale), percent = meter && caption;
        double room = height - (percent ? 8 + Math.ceil(18 * ui.textScale) : meter ? 16 : caption ? Math.ceil(18 * ui.textScale) + 6 : 0), size = CardSizing.claudeSmallCritter(width, room);
        LinearLayout column = ui.column();
        LinearLayout row = ui.row();
        PcState.ClaudeSession first = c.sessions.isEmpty() ? null : c.sessions.get(0);
        Part part = new Part(); part.id = first == null ? null : first.id; part.critter = critter(); parts.add(part);
        FrameLayout holder = new FrameLayout(ui.context); holder.addView(part.critter, new FrameLayout.LayoutParams(ui.px(size), ui.px(size)));
        row.addView(tappable(holder, first), ui.ll(Ui.WRAP, Ui.WRAP));
        LinearLayout side = ui.column(); side.setGravity(Gravity.CENTER_VERTICAL);
        if (first == null) side.addView(wrapped(c.connected ? ClaudeText.NO_SESSIONS : ClaudeText.NOT_CONNECTED, width < 300 ? 13 : ui.small(), room - (c.sample ? 22 : 0)), ui.ll(Ui.MATCH, Ui.WRAP));
        else {
            part.title = room < 150 ? ui.body("") : ui.heading(""); side.addView(part.title, ui.ll(Ui.MATCH, Ui.WRAP));
            part.state = ui.smallText(""); side.addView(part.state, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
            if (c.sessions.size() > 1) { more = ui.caption(""); side.addView(more, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0)); }
        }
        if (c.sample) side.addView(ui.caption("SAMPLE"), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        row.addView(side, ui.margins(ui.ll(0, Ui.WRAP, 1), 14, 0, 0, 0));
        column.addView(row, ui.ll(Ui.MATCH, 0, 1));
        if (meter) {
            // The 5-hour meter along the bottom, with its percentage at the right.
            LinearLayout line = ui.row(); line.setGravity(Gravity.CENTER_VERTICAL);
            smallMeter = new ProgressLine(ui.context, ui.theme.accent, ui.theme.track);
            line.addView(smallMeter, ui.ll(0, ui.px(6), 1));
            if (percent) { smallPercent = ui.caption(""); line.addView(smallPercent, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 8, 0, 0, 0)); }
            column.addView(line, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, percent ? 8 : 10, 0, 0));
        } else if (caption) {
            column.addView(ui.caption("expired".equals(c.plan) ? "Claude sign-in ended" : "signedOut".equals(c.plan) ? "Sign in for plan limits" : ""), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
        }
        return column;
    }

    // Medium: the heading, a lineup of critters with usage by project beside it on a wide card (under it on a tall
    // narrow one), and the two plan meters in a strip: the plan and the projects are what this card is for.
    private View medium(PcState.Claude c, double width, double height) {
        double stripHeight = CardSizing.claudeStrip(ui.textScale) + 12, room = height - 36 - stripHeight;
        LinearLayout column = ui.column();
        column.addView(headingRow(), ui.ll(Ui.MATCH, ui.px(28)));
        List<PcState.ClaudeProject> usage = c.usage[period];
        boolean beside = CardSizing.claudeMediumBeside(width);
        int projectRows = c.logsFailed ? 0 : CardSizing.claudeMediumProjects(room, ui.textScale, beside, !c.sessions.isEmpty());
        String label = "BY PROJECT · " + ClaudeText.PERIOD_NAMES[period].toUpperCase(java.util.Locale.ROOT);
        if (projectRows > 0 && beside) {
            double side = CardSizing.claudeMediumColumn(width);
            LinearLayout body = ui.row(); body.setGravity(Gravity.CENTER_VERTICAL);
            body.addView(lineup(c, width - side - 16, room), ui.ll(0, Ui.WRAP, 1));
            body.addView(projectRows(c, projectRows, usage, label), ui.margins(ui.ll(ui.px(side), Ui.WRAP), 16, 0, 0, 0));
            column.addView(body, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        } else {
            double rowHeight = projectRows > 0 ? room - 26 - projectRows * CardSizing.claudeProjectRow(ui.textScale) : room;
            column.addView(lineup(c, width, rowHeight), ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
            if (projectRows > 0) column.addView(projectRows(c, projectRows, usage, label), ui.ll(Ui.MATCH, Ui.WRAP));
        }
        FrameLayout tile = new FrameLayout(ui.context);
        tile.setBackground(ui.shape(ui.theme.raised, 0, 0, 14));
        tile.setPadding(ui.px(14), ui.px(10), ui.px(14), ui.px(10));
        tile.addView(meters(c, false), new FrameLayout.LayoutParams(Ui.MATCH, Ui.WRAP));
        column.addView(tile, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 12, 0, 0));
        return column;
    }

    // The Medium lineup: a critter per session with its project (and state, when there's room), or a dozing one.
    private View lineup(PcState.Claude c, double width, double rowHeight) {
        if (c.sessions.isEmpty()) return empty(c, Math.min(96, Math.max(40, rowHeight - 8)), rowHeight);
        int columns = CardSizing.claudeLineup(width), count = c.sessions.size(), slots = Math.min(count, columns);
        int shown = count <= columns ? count : columns - 1;
        double columnWidth = width / slots;
        boolean state = CardSizing.claudeLineupState(rowHeight, ui.textScale);
        double size = CardSizing.claudeLineupCritter(rowHeight, columnWidth, ui.textScale, state);
        LinearLayout lineup = ui.row(); lineup.setGravity(Gravity.CENTER_VERTICAL);
        for (int i = 0; i < shown; i++) {
            PcState.ClaudeSession session = c.sessions.get(i);
            Part part = new Part(); part.id = session.id; part.critter = critter(); parts.add(part);
            LinearLayout stack = ui.column(); stack.setGravity(Gravity.CENTER_HORIZONTAL);
            stack.addView(part.critter, ui.ll(ui.px(size), ui.px(size)));
            part.title = ui.text("", ui.small(), ui.theme.text); part.title.setGravity(Gravity.CENTER); part.title.setMaxWidth(ui.px(Math.max(40, columnWidth - 8)));
            stack.addView(part.title, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 6, 0, 0));
            if (state) { part.state = ui.caption(""); part.state.setGravity(Gravity.CENTER); part.state.setMaxWidth(ui.px(Math.max(40, columnWidth - 8))); stack.addView(part.state, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 2, 0, 0)); }
            lineup.addView(tappable(stack, session), ui.ll(0, Ui.WRAP, 1));
        }
        if (shown < count) {
            LinearLayout others = ui.column(); others.setGravity(Gravity.CENTER);
            more = Ui.tabular(ui.text("", 28, ui.theme.muted)); more.setTypeface(Ui.LIGHT);
            others.addView(more, ui.ll(Ui.WRAP, Ui.WRAP)); others.addView(ui.caption("more"), ui.ll(Ui.WRAP, Ui.WRAP));
            lineup.addView(others, ui.ll(0, Ui.WRAP, 1));
        }
        return lineup;
    }

    // Usage by project: name, then tokens and the cost at API prices at the right, over a thin share bar.
    private View projectRows(PcState.Claude c, int rows, List<PcState.ClaudeProject> usage, String label) {
        LinearLayout panel = ui.column();
        panel.addView(ui.label(label), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 6));
        if (usage.isEmpty()) { panel.addView(ui.caption(c.logsRead ? "No usage in this period." : "Reading Claude Code's logs…"), ui.ll(Ui.MATCH, Ui.WRAP)); return panel; }
        for (int i = 0; i < Math.min(rows, usage.size()); i++) {
            ProjectRow row = new ProjectRow();
            LinearLayout top = ui.row();
            row.name = ui.text("", ui.small(), ui.theme.text); Ui.wrap(row.name, 1); top.addView(row.name, ui.ll(0, Ui.WRAP, 1));
            row.tokens = ui.smallText(""); top.addView(row.tokens, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 8, 0, 0, 0));
            panel.addView(top, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, i == 0 ? 0 : 6, 0, 0));
            row.share = new ProgressLine(ui.context, ui.theme.accent, ui.theme.track);
            panel.addView(row.share, ui.margins(ui.ll(Ui.MATCH, ui.px(4)), 0, 3, 0, 0));
            projects.add(row);
        }
        return panel;
    }

    // A dozing critter and what to do next, for no sessions or no connection.
    private View empty(PcState.Claude c, double size, double room) {
        LinearLayout row = ui.row();
        Part part = new Part(); part.critter = critter(); parts.add(part);
        row.addView(part.critter, ui.ll(ui.px(size), ui.px(size)));
        row.addView(wrapped(c.connected ? ClaudeText.NO_SESSIONS : ClaudeText.NOT_CONNECTED, ui.small(), room), ui.margins(ui.ll(0, Ui.WRAP, 1), 16, 0, 0, 0));
        return row;
    }

    // The two plan meters: side by side in the Medium strip, stacked and bigger in the Large panel.
    private View meters(PcState.Claude c, boolean stacked) {
        if (!c.limitsKnown()) return wrapped(ClaudeText.limitsMissing(c), ui.small(), stacked ? 80 : CardSizing.claudeStrip(ui.textScale) - 20);
        five = new Meter(); week = new Meter();
        View a = meter(five, "5-HOUR", stacked), b = meter(week, "WEEK", stacked);
        LinearLayout box = stacked ? ui.column() : ui.row();
        if (stacked) { box.addView(a, ui.ll(Ui.MATCH, Ui.WRAP)); box.addView(b, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 14, 0, 0)); }
        else { box.setGravity(Gravity.TOP); box.addView(a, ui.ll(0, Ui.WRAP, 1)); box.addView(b, ui.margins(ui.ll(0, Ui.WRAP, 1), 16, 0, 0, 0)); }
        return box;
    }
    private View meter(Meter meter, String label, boolean big) {
        LinearLayout stack = ui.column();
        FrameLayout top = new FrameLayout(ui.context);
        top.addView(ui.label(label), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        meter.percent = big ? Ui.tabular(ui.text("—", 30, ui.theme.text)) : ui.body("—");
        if (big) meter.percent.setTypeface(Ui.LIGHT);
        top.addView(meter.percent, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.BOTTOM));
        stack.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
        meter.bar = new CritterView.Segments(ui.context, ui.theme.accent, ui.theme.track, ui.fpx(4));
        stack.addView(meter.bar, ui.margins(ui.ll(Ui.MATCH, ui.px(big ? 14 : 10)), 0, 6, 0, 0));
        meter.reset = ui.caption(""); stack.addView(meter.reset, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        return stack;
    }

    // Large: a row per session at the left, and a panel with the meters, the period switch and usage by project.
    private View large(PcState.Claude c, double width, double height) {
        LinearLayout column = ui.column();
        column.addView(headingRow(), ui.ll(Ui.MATCH, ui.px(28)));
        double panelWidth = CardSizing.claudePanel(width), leftWidth = width - panelWidth - 16, bodyHeight = height - 40;
        LinearLayout body = ui.row(); body.setGravity(Gravity.TOP);
        if (c.sessions.isEmpty()) body.addView(empty(c, Math.min(96, leftWidth * 0.3), bodyHeight), ui.ll(0, Ui.WRAP, 1));
        else {
            double rowHeight = CardSizing.claudeRowHeight(ui.textScale);
            int capacity = CardSizing.claudeRows(bodyHeight, ui.textScale), count = c.sessions.size();
            int shown = count <= capacity ? count : Math.max(1, capacity - 1);
            LinearLayout list = ui.column();
            for (int i = 0; i < shown; i++) {
                PcState.ClaudeSession session = c.sessions.get(i);
                Part part = new Part(); part.id = session.id; part.critter = critter(); parts.add(part);
                LinearLayout row = ui.row();
                row.addView(part.critter, ui.margins(ui.ll(ui.px(56), ui.px(56)), 0, 0, 12, 0));
                LinearLayout text = ui.column();
                part.title = ui.body(""); text.addView(part.title, ui.ll(Ui.MATCH, Ui.WRAP));
                LinearLayout line = ui.row();
                part.state = ui.smallText(""); line.addView(part.state, ui.ll(Ui.WRAP, Ui.WRAP));
                part.detail = ui.caption(""); line.addView(part.detail, ui.margins(ui.ll(0, Ui.WRAP, 1), 10, 0, 0, 0));
                text.addView(line, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 3, 0, 0));
                row.addView(text, ui.ll(0, Ui.WRAP, 1));
                LinearLayout numbers = ui.column(); numbers.setGravity(Gravity.END);
                part.tokens = Ui.tabular(ui.text("", 22, ui.theme.text)); part.tokens.setTypeface(Ui.LIGHT); numbers.addView(part.tokens, ui.ll(Ui.WRAP, Ui.WRAP));
                part.cost = ui.caption(""); numbers.addView(part.cost, ui.ll(Ui.WRAP, Ui.WRAP));
                row.addView(numbers, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, 0, 0));
                list.addView(tappable(row, session), ui.margins(ui.ll(Ui.MATCH, ui.px(rowHeight)), 0, i == 0 ? 0 : 8, 0, 0));
            }
            if (shown < count) { more = ui.caption(""); list.addView(more, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 10, 0, 0)); }
            body.addView(list, ui.ll(0, Ui.WRAP, 1));
        }
        LinearLayout panel = ui.column();
        panel.setBackground(ui.shape(ui.theme.raised, 0, 0, 16));
        panel.setPadding(ui.px(16), ui.px(16), ui.px(16), ui.px(16));
        panel.addView(meters(c, true), ui.ll(Ui.MATCH, Ui.WRAP));
        // Narrower weekly windows (Opus, Sonnet, a model's own) as one line each, when the panel has room.
        int extraRows = c.limitsKnown() ? Math.min(c.extra.size(), CardSizing.claudeExtras(bodyHeight - 32, ui.textScale)) : 0;
        for (int i = 0; i < extraRows; i++) {
            FrameLayout line = new FrameLayout(ui.context);
            line.addView(ui.caption(c.extra.get(i).name.toUpperCase(java.util.Locale.ROOT)), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
            TextView pct = ui.smallText(""); line.addView(pct, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
            extras.add(pct);
            panel.addView(line, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, i == 0 ? 10 : 4, 0, 0));
        }
        LinearLayout periods = ui.row();
        for (int i = 0; i < 3; i++) {
            final int choice = i;
            TextView button = ui.button(ClaudeText.PERIOD_NAMES[i], new View.OnClickListener() { @Override public void onClick(View v) { period = choice; dash.rebuildWidget(id); } });
            button.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(ui.small()));
            button.setPadding(ui.px(4), ui.px(4), ui.px(4), ui.px(4));
            ui.select(button, i == period);
            button.setContentDescription("Usage for " + PERIOD_DESCRIPTIONS[i] + (i == period ? ", selected" : ""));
            periods.addView(button, ui.margins(ui.ll(0, ui.px(48), 1), i == 0 ? 0 : 3, 0, i == 2 ? 0 : 3, 0));
        }
        panel.addView(periods, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 14, 0, 0));
        int rows = CardSizing.claudeProjects(bodyHeight - 32, ui.textScale, extraRows);
        if (c.logsFailed) panel.addView(wrapped(ClaudeText.NO_LOGS, ui.small(), 60), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 16, 0, 0));
        else if (rows > 0) panel.addView(projectRows(c, rows, c.usage[period], "BY PROJECT"), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 12, 0, 0));
        source = ui.caption(""); panel.addView(source, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 12, 0, 0));
        body.addView(panel, ui.margins(ui.ll(ui.px(panelWidth), Ui.MATCH), 16, 0, 0, 0));
        column.addView(body, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 12, 0, 0));
        return column;
    }

    // The heartbeat: critters' pupils and hops, and the "2 min" counts.
    @Override public void tick(int ticks, boolean quietMode) {
        if (!built()) return;
        phase = ticks % 2 == 0 && !quietMode; quiet = quietMode;
        for (Part part : parts) part.critter.setPhase(phase, quiet);
        render();
    }

    // Fills the current design from the PC's last answer; new sessions or areas rebuild the card.
    @Override public void render() {
        if (!built()) return;
        PcState.Claude c = claude();
        if (!shape(c).equals(builtFor)) { dash.rebuildWidget(id); return; }
        if (c == null) return;
        double now = now();
        boolean use24 = dash.use24(), needs = c.needsYou() > 0;
        // The one state allowed to shout: an Amber border around the whole card.
        card.setBackground(ui.shape(ui.theme.surface, needs ? Theme.WARNING : ui.theme.controlBorder, needs ? 2 : 1, ui.radius));
        if (heading != null) { heading.setText(ClaudeText.heading(c)); heading.setTextColor(needs ? Theme.WARNING : ui.theme.muted); }
        if (flag != null) flag.setText(ClaudeText.flag(c, now, use24, design == CardSizing.MEDIUM));
        int named = 0;
        for (Part part : parts) {
            PcState.ClaudeSession session = null;
            if (part.id != null) for (PcState.ClaudeSession s : c.sessions) if (s.id.equals(part.id)) session = s;
            if (session == null) { part.critter.set("ready", c.connected ? "dozing" : ""); continue; }
            named++;
            part.critter.set(session.state, session.project);
            boolean asks = "needs".equals(session.state);
            if (part.title != null) part.title.setText(design == CardSizing.LARGE ? session.shown() : session.project);
            if (part.state != null) {
                part.state.setText(design == CardSizing.MEDIUM ? ClaudeText.state(session.state) : ClaudeText.stateWords(session.state, session.since, now, design == CardSizing.LARGE));
                part.state.setTextColor(asks ? Theme.WARNING : ui.theme.muted);
            }
            if (part.detail != null) {
                String detail = session.project + (session.model.isEmpty() || session.model.equals(session.project) ? "" : " · " + session.model) + (session.where.isEmpty() ? "" : " · " + session.where);
                part.detail.setText(asks && !session.message.trim().isEmpty() ? ClaudeText.ask(session.message) : detail);
                part.detail.setTextColor(asks ? Theme.WARNING : ui.theme.muted);
            }
            boolean counted = c.logsRead && !c.logsFailed;
            if (part.tokens != null) part.tokens.setText(counted ? ClaudeText.tokens(session.tokens[period]) : "—");
            if (part.cost != null) part.cost.setText(counted ? ClaudeText.money(session.cost[period]) : "");
        }
        if (more != null) {
            int hidden = c.sessions.size() - named;
            boolean hiddenNeeds = false;
            for (int i = named; i < c.sessions.size(); i++) if ("needs".equals(c.sessions.get(i).state)) hiddenNeeds = true;
            more.setText(design == CardSizing.MEDIUM ? "+" + hidden : "+" + hidden + " more" + (hiddenNeeds ? " · one needs you" : ""));
        }
        if (smallMeter != null) {
            double pct = ClaudeText.percent(c.fiveHour, now);
            smallMeter.setValue(pct / 100); smallMeter.setFill(ClaudeText.warns(pct) ? Theme.WARNING : ui.theme.accent);
            if (smallPercent != null) {
                smallPercent.setText("5h " + Math.round(pct) + "%" + (ClaudeText.old(c, now) && width >= 340 ? " · as of " + ClaudeText.clock(c.updatedAt, use24) : ""));
                smallPercent.setTextColor(ClaudeText.warns(pct) ? Theme.WARNING : ui.theme.muted);
            }
        }
        for (int i = 0; i < extras.size() && i < c.extra.size(); i++) {
            double pct = ClaudeText.percent(c.extra.get(i), now);
            extras.get(i).setText(Math.round(pct) + "%"); extras.get(i).setTextColor(ClaudeText.warns(pct) ? Theme.WARNING : ui.theme.text);
        }
        renderMeter(five, c.fiveHour, now, use24);
        renderMeter(week, c.week, now, use24);
        List<PcState.ClaudeProject> usage = c.usage[period];
        long biggest = 1; for (PcState.ClaudeProject p : usage) biggest = Math.max(biggest, p.tokens);
        for (int i = 0; i < projects.size() && i < usage.size(); i++) {
            projects.get(i).name.setText(usage.get(i).project); projects.get(i).tokens.setText(ClaudeText.usage(usage.get(i).tokens, usage.get(i).cost));
            projects.get(i).share.setValue((double) usage.get(i).tokens / biggest);
        }
        if (source != null) source.setText(ClaudeText.source(c, use24));
    }

    private void renderMeter(Meter meter, PcState.ClaudeWindow window, double now, boolean use24) {
        if (meter == null) return;
        double pct = ClaudeText.percent(window, now);
        boolean warns = ClaudeText.warns(pct);
        meter.percent.setText(window == null ? "—" : Math.round(pct) + "%");
        meter.percent.setTextColor(warns ? Theme.WARNING : ui.theme.text);
        meter.bar.set(ClaudeText.lit(pct), warns ? Theme.WARNING : ui.theme.accent);
        meter.reset.setText(ClaudeText.reset(window, now, use24));
        meter.reset.setTextColor(window != null && pct >= 100 ? Theme.WARNING : ui.theme.muted);
    }
}
