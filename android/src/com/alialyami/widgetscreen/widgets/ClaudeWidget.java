package com.alialyami.widgetscreen.widgets;

import android.animation.Animator;
import android.animation.AnimatorListenerAdapter;
import android.animation.ValueAnimator;
import android.view.Gravity;
import android.view.View;
import android.view.animation.AccelerateInterpolator;
import android.view.animation.OvershootInterpolator;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.ClaudeText;
import com.alialyami.widgetscreen.core.Critter;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.views.CritterView;
import com.alialyami.widgetscreen.views.ProgressLine;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

// Claude Code on the PC, in the same three designs as the Windows ClaudeCard (CardSizing.claude;
// docs/design/claude-watcher.md): Small is the loudest running session's critter with its project and state over the
// 5-hour meter. Medium and Large show each project as a big critter with its threads as baby critters: Medium a row of
// projects with up to three babies each over the plan meters, Large a column per project (its babies in a grid that
// scrolls when there are many) under a strip with the meters and the period switch. Tapping a project critter folds
// its babies into it (they're eaten) or lets them out (they pop back out), shared with the PC through claude.collapse;
// Quiet mode skips the motion. Tapping a running baby brings its window forward on the PC. The period switch is the
// phone's own and starts at the PC's.
public final class ClaudeWidget extends Widget {
    private static final String[] PERIOD_DESCRIPTIONS = { "the 5-hour window", "today", "the week" };
    private static final long MOTION = 350;
    private int design, period = -1;
    private String builtFor = "";
    private FrameLayout card;
    private TextView heading, flag, more, smallPercent;
    private ProgressLine smallMeter;
    private final List<TextView> extras = new ArrayList<TextView>();
    private Meter five, week;
    private boolean phase, quiet, animating;
    private String spitKey;
    // Folds the phone asked for, until the PC's answer says the same.
    private final Map<String, Boolean> pending = new HashMap<String, Boolean>();
    private final List<Part> parts = new ArrayList<Part>();
    private final List<Head> heads = new ArrayList<Head>();

    private static final class Part { CritterView critter; TextView title, state, tokens; View cell; String id; }
    private static final class Head { String key; CritterView critter; TextView name, detail; final List<Part> babies = new ArrayList<Part>(); }
    private static final class Meter { TextView percent, reset; CritterView.Segments bar; }

    public ClaudeWidget(Dashboard dash) { super("Claude", dash); }

    @Override public String part() { return "claude"; }

    private PcState.Claude claude() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.claude; }
    private static double now() { return System.currentTimeMillis() / 1000.0; }
    private boolean folded(PcState.ClaudeGroup group) { Boolean asked = pending.get(group.key); return asked != null ? asked : group.collapsed; }
    private List<PcState.ClaudeGroup> groups(PcState.Claude c) { return c.groups[period]; }

    // What decides the design's parts: which sessions, which projects and threads, and which areas have something.
    private String shape(PcState.Claude c) {
        if (c == null) return "-";
        StringBuilder ids = new StringBuilder();
        for (PcState.ClaudeSession s : c.sessions) ids.append(s.id).append(':').append(s.canFocus).append(',');
        StringBuilder projects = new StringBuilder();
        for (PcState.ClaudeGroup g : groups(c)) {
            projects.append(g.key).append(folded(g) ? "!" : "").append(':');
            for (String id : g.threads) { PcState.ClaudeThread t = c.thread(id); projects.append(id).append(t != null && t.canFocus ? "*" : "").append(','); }
            projects.append(';');
        }
        return c.connected + "|" + c.sample + "|" + ids + "|" + projects + "|" + c.limitsKnown() + c.noPlan + c.plan + "|" + c.extra.size() + "|" + c.logsFailed + "|" + period;
    }

    @Override protected View create() {
        PcState.Claude c = claude();
        if (period < 0 && c != null) period = ClaudeText.period(c.period);
        if (period < 0) period = 1;
        if (c != null) for (PcState.ClaudeGroup g : groups(c)) { Boolean asked = pending.get(g.key); if (asked != null && asked == g.collapsed) pending.remove(g.key); }
        builtFor = shape(c);
        parts.clear(); heads.clear(); extras.clear(); heading = flag = more = smallPercent = null; smallMeter = null; five = week = null;
        design = CardSizing.claude(width, height);
        double bodyWidth = Math.max(1, width - 48), bodyHeight = Math.max(1, height - 48);
        View content = c == null ? ui.caption("Waiting for the PC…") : design == CardSizing.SMALL ? small(c, bodyWidth, bodyHeight) : design == CardSizing.MEDIUM ? medium(c, bodyWidth, bodyHeight) : large(c, bodyWidth, bodyHeight);
        card = ui.card(content, 24);
        if (spitKey != null) {
            final String key = spitKey; spitKey = null;
            card.post(new Runnable() { @Override public void run() { for (Head head : heads) if (head.key.equals(key)) spit(head); } });
        }
        return card;
    }

    private CritterView critter() { CritterView view = new CritterView(ui.context, ui.theme); view.setPhase(phase, quiet); return view; }
    private TextView wrapped(String text, float size, double maxHeight) {
        TextView view = ui.text(text, size, ui.theme.muted);
        Ui.wrap(view, Math.max(1, (int) Math.floor(maxHeight / (size * 1.25))));
        return view;
    }
    // A tappable session: its whole area brings the session's window forward on the PC, when one is known.
    private View tappable(View child, final String id, boolean canFocus, String shown) {
        if (id == null || !canFocus) return child;
        child.setClickable(true);
        child.setBackground(ui.pressable(0x00000000, 0, 0, 12));
        child.setContentDescription(shown + ". Tap to show its window on the PC.");
        child.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "claude.focus", "id", id)); } }));
        return child;
    }
    // A project critter: a tap folds its babies in or lets them out.
    private View folding(View child, final PcState.ClaudeGroup group) {
        child.setClickable(true);
        child.setBackground(ui.pressable(0x00000000, 0, 0, 12));
        child.setMinimumHeight(ui.px(48)); child.setMinimumWidth(ui.px(48));
        child.setContentDescription(group.project + ", " + ClaudeText.threads(group.threads.size()) + (folded(group) ? ", folded. Tap to show them." : ". Tap to fold them in."));
        final String key = group.key;
        child.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { toggle(key); } }));
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
        row.addView(first == null ? holder : tappable(holder, first.id, first.canFocus, first.shown()), ui.ll(Ui.WRAP, Ui.WRAP));
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

    // Medium: the heading, a row of project critters with up to three babies each, and the plan meters in a strip.
    private View medium(PcState.Claude c, double width, double height) {
        double stripHeight = CardSizing.claudeStrip(ui.textScale) + 12, room = height - 36 - stripHeight;
        LinearLayout column = ui.column();
        column.addView(headingRow(), ui.ll(Ui.MATCH, ui.px(28)));
        List<PcState.ClaudeGroup> groups = groups(c);
        if (groups.isEmpty()) column.addView(empty(c, Math.min(96, Math.max(40, room - 8)), room), ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        else {
            int columns = CardSizing.claudeMediumColumns(width, groups.size());
            boolean overflow = groups.size() > columns && columns > 1;
            int shown = overflow ? columns - 1 : Math.min(columns, groups.size());
            double columnWidth = width / columns;
            boolean babies = CardSizing.claudeMediumBabies(room, ui.textScale);
            double parent = CardSizing.claudeMediumParent(room, columnWidth, ui.textScale, babies), baby = Math.max(16, Math.round(parent * 0.4));
            LinearLayout row = ui.row(); row.setGravity(Gravity.CENTER_VERTICAL);
            for (int i = 0; i < shown; i++) {
                PcState.ClaudeGroup group = groups.get(i);
                Head head = new Head(); head.key = group.key; head.critter = critter(); heads.add(head);
                LinearLayout stack = ui.column(); stack.setGravity(Gravity.CENTER_HORIZONTAL);
                FrameLayout holder = new FrameLayout(ui.context); holder.addView(head.critter, ui.frame(ui.px(parent), ui.px(parent), Gravity.CENTER));
                stack.addView(folding(holder, group), ui.ll(Ui.WRAP, Ui.WRAP));
                head.name = ui.text("", ui.small(), ui.theme.text); head.name.setGravity(Gravity.CENTER); Ui.wrap(head.name, 1); head.name.setMaxWidth(ui.px(Math.max(40, columnWidth - 8)));
                stack.addView(head.name, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 6, 0, 0));
                if (babies && !folded(group) && !group.threads.isEmpty()) {
                    int count = CardSizing.claudeMediumBabyCount(columnWidth - 8, baby, group.threads.size());
                    LinearLayout line = ui.row(); line.setGravity(Gravity.CENTER);
                    for (int j = 0; j < count; j++) line.addView(baby(c, head, c.thread(group.threads.get(j)), baby, 0, false), ui.ll(Ui.WRAP, Ui.WRAP));
                    if (count < group.threads.size()) line.addView(ui.caption("+" + (group.threads.size() - count)), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 4, 0, 0, 0));
                    stack.addView(line, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 8, 0, 0));
                }
                row.addView(stack, ui.ll(0, Ui.WRAP, 1));
            }
            if (overflow) row.addView(moreProjects(groups.subList(shown, groups.size()), c, false), ui.ll(0, Ui.WRAP, 1));
            column.addView(row, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        }
        FrameLayout tile = new FrameLayout(ui.context);
        tile.setBackground(ui.shape(ui.theme.raised, 0, 0, 14));
        tile.setPadding(ui.px(14), ui.px(10), ui.px(14), ui.px(10));
        tile.addView(meters(c), new FrameLayout.LayoutParams(Ui.MATCH, Ui.WRAP));
        column.addView(tile, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 12, 0, 0));
        return column;
    }

    // A thread's baby critter: Large gives it a cell with its title and tokens, Medium the critter alone. A running
    // baby's tap brings its window forward on the PC.
    private View baby(PcState.Claude c, Head head, PcState.ClaudeThread thread, double size, double cellWidth, boolean labelled) {
        Part part = new Part(); part.id = thread == null ? null : thread.id; part.critter = critter();
        LinearLayout stack = ui.column(); stack.setGravity(Gravity.CENTER_HORIZONTAL);
        stack.addView(part.critter, ui.margins(ui.ll(ui.px(size), ui.px(size)), labelled ? 0 : 2, 0, labelled ? 0 : 2, 0));
        if (labelled) {
            part.title = ui.text("", ui.small(), ui.theme.text); part.title.setGravity(Gravity.CENTER); Ui.wrap(part.title, 2); part.title.setMaxWidth(ui.px(Math.max(30, cellWidth - 6)));
            stack.addView(part.title, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 4, 0, 0));
            part.tokens = ui.caption(""); part.tokens.setGravity(Gravity.CENTER); Ui.wrap(part.tokens, 1); part.tokens.setMaxWidth(ui.px(Math.max(30, cellWidth - 6)));
            stack.addView(part.tokens, ui.ll(Ui.WRAP, Ui.WRAP));
        }
        String shown = thread == null ? "" : ClaudeText.threadTitle(thread, now(), dash.use24());
        part.cell = thread == null ? stack : tappable(stack, thread.id, thread.running && thread.canFocus, shown);
        if (part.cell == stack) stack.setContentDescription(shown);
        head.babies.add(part);
        return part.cell;
    }

    // The projects past the last column: how many, and the first few names.
    private View moreProjects(List<PcState.ClaudeGroup> rest, PcState.Claude c, boolean names) {
        LinearLayout others = ui.column(); others.setGravity(names ? Gravity.CENTER_HORIZONTAL | Gravity.TOP : Gravity.CENTER);
        boolean needs = false; for (PcState.ClaudeGroup g : rest) needs |= ClaudeText.groupNeeds(c, g);
        more = Ui.tabular(ui.text("+" + rest.size(), 28, needs ? Theme.WARNING : ui.theme.muted)); more.setTypeface(Ui.LIGHT);
        others.addView(more, ui.ll(Ui.WRAP, Ui.WRAP));
        others.addView(ui.caption(rest.size() == 1 ? "more project" : "more projects"), ui.ll(Ui.WRAP, Ui.WRAP));
        if (names) for (int i = 0; i < Math.min(4, rest.size()); i++) { TextView name = ui.caption(rest.get(i).project); Ui.wrap(name, 1); others.addView(name, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 4, 0, 0)); }
        return others;
    }

    // A dozing critter and what to do next, for no threads or no connection.
    private View empty(PcState.Claude c, double size, double room) {
        LinearLayout row = ui.row();
        Part part = new Part(); part.critter = critter(); parts.add(part);
        row.addView(part.critter, ui.ll(ui.px(size), ui.px(size)));
        String words = !c.connected && c.threads.isEmpty() ? ClaudeText.NOT_CONNECTED : ClaudeText.noThreads(period);
        row.addView(wrapped(words, ui.small(), room), ui.margins(ui.ll(0, Ui.WRAP, 1), 16, 0, 0, 0));
        return row;
    }

    // The two plan meters side by side, or what they need.
    private View meters(PcState.Claude c) {
        if (!c.limitsKnown()) return wrapped(ClaudeText.limitsMissing(c), ui.small(), CardSizing.claudeStrip(ui.textScale) - 20);
        five = new Meter(); week = new Meter();
        View a = meter(five, "5-HOUR"), b = meter(week, "WEEK");
        LinearLayout box = ui.row(); box.setGravity(Gravity.TOP);
        box.addView(a, ui.ll(0, Ui.WRAP, 1)); box.addView(b, ui.margins(ui.ll(0, Ui.WRAP, 1), 16, 0, 0, 0));
        return box;
    }
    private View meter(Meter meter, String label) {
        LinearLayout stack = ui.column();
        FrameLayout top = new FrameLayout(ui.context);
        top.addView(ui.label(label), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        meter.percent = ui.body("—");
        top.addView(meter.percent, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.BOTTOM));
        stack.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
        meter.bar = new CritterView.Segments(ui.context, ui.theme.accent, ui.theme.track, ui.fpx(4));
        stack.addView(meter.bar, ui.margins(ui.ll(Ui.MATCH, ui.px(10)), 0, 6, 0, 0));
        meter.reset = ui.caption(""); stack.addView(meter.reset, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        return stack;
    }

    // Large: the meter strip (with the narrower weekly windows when there's room, and the period switch), then a
    // column per project: its big critter, name and usage, and its threads as babies in a grid that scrolls.
    private View large(PcState.Claude c, double width, double height) {
        LinearLayout column = ui.column();
        column.addView(headingRow(), ui.ll(Ui.MATCH, ui.px(28)));
        double stripHeight = CardSizing.claudeStrip(ui.textScale) + 20, area = height - 28 - 12 - stripHeight - 12;
        LinearLayout strip = ui.row(); strip.setGravity(Gravity.CENTER_VERTICAL);
        strip.setBackground(ui.shape(ui.theme.raised, 0, 0, 14));
        strip.setPadding(ui.px(14), ui.px(10), ui.px(14), ui.px(10));
        strip.addView(meters(c), ui.ll(0, Ui.WRAP, 1));
        int extraRows = c.limitsKnown() && width >= 1000 ? Math.min(3, c.extra.size()) : 0;
        if (extraRows > 0) {
            LinearLayout list = ui.column();
            for (int i = 0; i < extraRows; i++) {
                FrameLayout line = new FrameLayout(ui.context);
                TextView name = ui.caption(c.extra.get(i).name.toUpperCase(java.util.Locale.ROOT)); Ui.wrap(name, 1);
                line.addView(name, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
                TextView pct = ui.caption(""); line.addView(pct, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
                extras.add(pct);
                list.addView(line, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, i == 0 ? 0 : 2, 0, 0));
            }
            strip.addView(list, ui.margins(ui.ll(ui.px(170), Ui.WRAP), 16, 0, 0, 0));
        }
        LinearLayout periods = ui.row();
        for (int i = 0; i < 3; i++) {
            final int choice = i;
            TextView button = ui.button(ClaudeText.PERIOD_NAMES[i], new View.OnClickListener() { @Override public void onClick(View v) { period = choice; dash.rebuildWidget(id); } });
            button.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(ui.small()));
            button.setPadding(ui.px(4), ui.px(4), ui.px(4), ui.px(4));
            ui.select(button, i == period);
            button.setContentDescription("Threads and usage for " + PERIOD_DESCRIPTIONS[i] + (i == period ? ", selected" : ""));
            periods.addView(button, ui.margins(ui.ll(ui.px(64), ui.px(52)), i == 0 ? 0 : 6, 0, 0, 0));
        }
        strip.addView(periods, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 16, 0, 0, 0));
        column.addView(strip, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 12, 0, 0));
        List<PcState.ClaudeGroup> groups = groups(c);
        if (groups.isEmpty()) { column.addView(empty(c, Math.min(96, Math.max(40, area * 0.5)), area), ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 12, 0, 0)); return column; }
        int columns = CardSizing.claudeColumns(width, groups.size());
        boolean overflow = groups.size() > columns && columns > 1;
        int shown = overflow ? columns - 1 : Math.min(columns, groups.size());
        double columnWidth = (width - 16 * (columns - 1)) / columns;
        LinearLayout body = ui.row(); body.setGravity(Gravity.TOP);
        for (int i = 0; i < shown; i++) body.addView(projectColumn(c, groups.get(i), columnWidth, area), ui.margins(ui.ll(0, Ui.MATCH, 1), i == 0 ? 0 : 16, 0, 0, 0));
        if (overflow) body.addView(moreProjects(groups.subList(shown, groups.size()), c, true), ui.margins(ui.ll(0, Ui.MATCH, 1), 16, 0, 0, 0));
        column.addView(body, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 12, 0, 0));
        return column;
    }

    // One project's column: the big critter (a tap folds the babies in or out), its name and usage, then its
    // babies, a row per few, scrolling inside the column when they don't all fit.
    private View projectColumn(PcState.Claude c, PcState.ClaudeGroup group, double width, double area) {
        double parent = CardSizing.claudeParent(width, area, ui.textScale), baby = Critter.baby(parent);
        LinearLayout column = ui.column(); column.setGravity(Gravity.CENTER_HORIZONTAL);
        Head head = new Head(); head.key = group.key; head.critter = critter(); heads.add(head);
        FrameLayout holder = new FrameLayout(ui.context); holder.addView(head.critter, ui.frame(ui.px(parent), ui.px(parent), Gravity.CENTER));
        column.addView(folding(holder, group), ui.ll(Ui.WRAP, Ui.WRAP));
        head.name = ui.body(""); head.name.setGravity(Gravity.CENTER); Ui.wrap(head.name, 1); head.name.setMaxWidth(ui.px(width));
        column.addView(head.name, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 6, 0, 0));
        head.detail = ui.caption(""); head.detail.setGravity(Gravity.CENTER); Ui.wrap(head.detail, 1); head.detail.setMaxWidth(ui.px(width));
        column.addView(head.detail, ui.ll(Ui.WRAP, Ui.WRAP));
        if (!folded(group) && !group.threads.isEmpty()) {
            int perRow = Math.min(group.threads.size(), CardSizing.claudeBabiesPerRow(width, ui.textScale));
            int rows = (group.threads.size() + perRow - 1) / perRow;
            double cellWidth = width / CardSizing.claudeBabiesPerRow(width, ui.textScale), cellHeight = CardSizing.claudeBabyHeight(baby, ui.textScale);
            LinearLayout grid = ui.column(); grid.setGravity(Gravity.CENTER_HORIZONTAL);
            for (int r = 0; r < rows; r++) {
                LinearLayout line = new LinearLayout(ui.context); line.setOrientation(LinearLayout.HORIZONTAL); line.setGravity(Gravity.TOP);
                for (int k = 0; k < perRow && r * perRow + k < group.threads.size(); k++)
                    line.addView(baby(c, head, c.thread(group.threads.get(r * perRow + k)), baby, cellWidth, true), ui.ll(ui.px(cellWidth), ui.px(cellHeight)));
                grid.addView(line, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, r == 0 ? 0 : 6, 0, 0));
            }
            double room = area - parent - CardSizing.claudeHeadText(ui.textScale) - 10;
            if (rows * (cellHeight + 6) - 6 > room) {
                // More babies than fit: the column's grid scrolls (inner lists may scroll; the dashboard never does).
                ScrollView scroll = new ScrollView(ui.context); scroll.setVerticalScrollBarEnabled(false); scroll.setNestedScrollingEnabled(true);
                scroll.addView(grid, new FrameLayout.LayoutParams(Ui.MATCH, Ui.WRAP));
                column.addView(scroll, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 10, 0, 0));
            } else column.addView(grid, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 10, 0, 0));
        }
        return column;
    }

    // ----- Folding: eating and spitting (~350 ms, instant in Quiet mode) -----

    private void toggle(String key) {
        PcState.Claude c = claude();
        if (animating || c == null) return;
        PcState.ClaudeGroup group = null; for (PcState.ClaudeGroup g : groups(c)) if (g.key.equals(key)) group = g;
        if (group == null) return;
        final boolean fold = !folded(group);
        Head head = null; for (Head h : heads) if (h.key.equals(key)) head = h;
        final String folder = key;
        Runnable apply = new Runnable() { @Override public void run() {
            pending.put(folder, fold);
            dash.commandQuietly(Json.map("action", "claude.collapse", "project", folder, "collapsed", fold));
            dash.rebuildWidget(id);
        } };
        if (fold && head != null && !head.babies.isEmpty() && !quiet) { eat(head, apply); return; }
        if (!fold && !quiet) spitKey = key;
        apply.run();
    }

    // Where a baby must move to sit in its parent's mouth, in its own pixels.
    private static float[] toMouth(Head head, View baby) {
        int[] a = new int[2], b = new int[2];
        head.critter.getLocationInWindow(a); baby.getLocationInWindow(b);
        return new float[] { a[0] + head.critter.mouthX() - (b[0] + baby.getWidth() / 2f), a[1] + head.critter.mouthY() - (b[1] + baby.getHeight() / 2f) };
    }
    // The mouth opens wide, holds, then closes: a gulp when eating, a puff when spitting.
    private static float mouth(float t, boolean eat) {
        float open = eat ? 120f / MOTION : 60f / MOTION, close = eat ? 270f / MOTION : 220f / MOTION;
        return t < open ? t / open : t < close ? 1 : Math.max(0, 1 - (t - close) / (1 - close));
    }

    private void eat(final Head head, final Runnable done) {
        animating = true;
        final List<float[]> targets = new ArrayList<float[]>();
        for (Part baby : head.babies) { baby.cell.setPivotX(baby.cell.getWidth() / 2f); baby.cell.setPivotY(baby.cell.getHeight() / 2f); targets.add(toMouth(head, baby.cell)); }
        final AccelerateInterpolator ease = new AccelerateInterpolator(1.5f);
        ValueAnimator motion = ValueAnimator.ofFloat(0, 1).setDuration(MOTION);
        motion.addUpdateListener(new ValueAnimator.AnimatorUpdateListener() { @Override public void onAnimationUpdate(ValueAnimator animator) {
            float t = (float) animator.getAnimatedValue(), e = ease.getInterpolation(t);
            for (int i = 0; i < head.babies.size(); i++) {
                View cell = head.babies.get(i).cell;
                cell.setScaleX(1 - 0.9f * e); cell.setScaleY(1 - 0.9f * e);
                cell.setTranslationX(targets.get(i)[0] * e); cell.setTranslationY(targets.get(i)[1] * e);
                cell.setAlpha(t < 0.7f ? 1 : Math.max(0, 1 - (t - 0.7f) / 0.3f));
            }
            head.critter.setMouth(mouth(t, true));
        } });
        motion.addListener(new AnimatorListenerAdapter() { @Override public void onAnimationEnd(Animator animator) { animating = false; head.critter.setMouth(0); done.run(); } });
        motion.start();
    }

    private void spit(final Head head) {
        if (head.babies.isEmpty() || head.critter.getWidth() == 0) return;
        final List<float[]> starts = new ArrayList<float[]>();
        for (Part baby : head.babies) { baby.cell.setPivotX(baby.cell.getWidth() / 2f); baby.cell.setPivotY(baby.cell.getHeight() / 2f); starts.add(toMouth(head, baby.cell)); }
        final OvershootInterpolator ease = new OvershootInterpolator(1.4f);
        ValueAnimator motion = ValueAnimator.ofFloat(0, 1).setDuration(MOTION);
        motion.addUpdateListener(new ValueAnimator.AnimatorUpdateListener() { @Override public void onAnimationUpdate(ValueAnimator animator) {
            float t = (float) animator.getAnimatedValue(), e = ease.getInterpolation(t);
            for (int i = 0; i < head.babies.size(); i++) {
                View cell = head.babies.get(i).cell;
                cell.setScaleX(0.1f + 0.9f * e); cell.setScaleY(0.1f + 0.9f * e);
                cell.setTranslationX(starts.get(i)[0] * (1 - e)); cell.setTranslationY(starts.get(i)[1] * (1 - e));
                cell.setAlpha(Math.min(1, t / 0.25f));
            }
            head.critter.setMouth(mouth(t, false));
        } });
        motion.addListener(new AnimatorListenerAdapter() { @Override public void onAnimationEnd(Animator animator) {
            head.critter.setMouth(0);
            for (Part baby : head.babies) { baby.cell.setScaleX(1); baby.cell.setScaleY(1); baby.cell.setTranslationX(0); baby.cell.setTranslationY(0); baby.cell.setAlpha(1); }
        } });
        motion.start();
    }

    // The heartbeat: critters' pupils and hops, and the "2 min" counts.
    @Override public void tick(int ticks, boolean quietMode) {
        if (!built()) return;
        phase = ticks % 2 == 0 && !quietMode; quiet = quietMode;
        for (Part part : parts) part.critter.setPhase(phase, quiet);
        for (Head head : heads) { head.critter.setPhase(phase, quiet); for (Part baby : head.babies) baby.critter.setPhase(phase, quiet); }
        render();
    }

    // Fills the current design from the PC's last answer; new sessions, projects or areas rebuild the card.
    @Override public void render() {
        if (!built() || animating) return;
        PcState.Claude c = claude();
        if (!shape(c).equals(builtFor)) { dash.rebuildWidget(id); return; }
        if (c == null) return;
        double now = now();
        boolean use24 = dash.use24(), needs = c.needsYou() > 0, counted = c.logsRead && !c.logsFailed;
        // The one state allowed to shout: an Amber border around the whole card.
        card.setBackground(ui.shape(ui.theme.surface, needs ? Theme.WARNING : ui.theme.controlBorder, needs ? 2 : 1, ui.radius));
        if (heading != null) { heading.setText(ClaudeText.heading(c)); heading.setTextColor(needs ? Theme.WARNING : ui.theme.muted); }
        if (flag != null) flag.setText(ClaudeText.flag(c, now, use24, true));
        for (Part part : parts) {
            PcState.ClaudeSession session = null;
            if (part.id != null) for (PcState.ClaudeSession s : c.sessions) if (s.id.equals(part.id)) session = s;
            if (session == null) { part.critter.set("ready", c.connected ? "dozing" : ""); continue; }
            part.critter.set(session.state, session.project);
            if (part.title != null) part.title.setText(session.project);
            if (part.state != null) { part.state.setText(ClaudeText.stateWords(session.state, session.since, now, false)); part.state.setTextColor("needs".equals(session.state) ? Theme.WARNING : ui.theme.muted); }
        }
        if (more != null && design == CardSizing.SMALL) {
            boolean hiddenNeeds = false;
            for (int i = 1; i < c.sessions.size(); i++) if ("needs".equals(c.sessions.get(i).state)) hiddenNeeds = true;
            more.setText("+" + (c.sessions.size() - 1) + " more" + (hiddenNeeds ? " · one needs you" : ""));
        }
        for (Head head : heads) {
            PcState.ClaudeGroup group = null; for (PcState.ClaudeGroup g : groups(c)) if (g.key.equals(head.key)) group = g;
            if (group == null) continue;
            String state = ClaudeText.groupState(c, group);
            head.critter.set(state, group.project); head.critter.setCollapsed(folded(group), group.threads.size());
            if (head.name != null) { head.name.setText(group.project); head.name.setTextColor("needs".equals(state) ? Theme.WARNING : ui.theme.text); }
            if (head.detail != null) head.detail.setText(ClaudeText.groupDetail(group, counted));
            for (Part baby : head.babies) {
                PcState.ClaudeThread thread = baby.id == null ? null : c.thread(baby.id);
                if (thread == null) continue;
                baby.critter.set(thread.running ? thread.state : "ready", group.project);
                // An ended thread sleeps, dimmed.
                baby.critter.setAlpha(thread.running ? 1f : 0.5f);
                boolean asks = thread.running && "needs".equals(thread.state);
                if (baby.title != null) { baby.title.setText(ClaudeText.threadTitle(thread, now, use24)); baby.title.setTextColor(asks ? Theme.WARNING : thread.running ? ui.theme.text : ui.theme.muted); }
                if (baby.tokens != null) baby.tokens.setText(asks ? ClaudeText.stateWords(thread.state, thread.since, now, false) : counted ? ClaudeText.tokens(thread.tokens[period]) + " tokens" : "—");
            }
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
