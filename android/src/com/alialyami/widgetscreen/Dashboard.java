package com.alialyami.widgetscreen;

import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.util.DisplayMetrics;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.alialyami.widgetscreen.core.AppSettings;
import com.alialyami.widgetscreen.core.DashboardFit;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.core.WidgetLayout;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Images;
import com.alialyami.widgetscreen.integrations.Pc;
import com.alialyami.widgetscreen.views.CheckBox;
import com.alialyami.widgetscreen.views.GlyphView;
import com.alialyami.widgetscreen.views.ScaledHost;
import com.alialyami.widgetscreen.widgets.CalendarWidget;
import com.alialyami.widgetscreen.widgets.ClipboardWidget;
import com.alialyami.widgetscreen.widgets.ClocksWidget;
import com.alialyami.widgetscreen.views.CritterView;
import com.alialyami.widgetscreen.widgets.ClaudeWidget;
import com.alialyami.widgetscreen.widgets.FocusWidget;
import com.alialyami.widgetscreen.widgets.MacrosWidget;
import com.alialyami.widgetscreen.widgets.MediaWidget;
import com.alialyami.widgetscreen.widgets.NotesWidget;
import com.alialyami.widgetscreen.widgets.PrayerWidget;
import com.alialyami.widgetscreen.widgets.QuickLaunchWidget;
import com.alialyami.widgetscreen.widgets.MixerWidget;
import com.alialyami.widgetscreen.widgets.SoundboardWidget;
import com.alialyami.widgetscreen.widgets.SpotifyWidget;
import com.alialyami.widgetscreen.widgets.StorageWidget;
import com.alialyami.widgetscreen.widgets.SystemWidget;
import com.alialyami.widgetscreen.widgets.WeatherWidget;
import com.alialyami.widgetscreen.widgets.Widget;

import java.io.File;
import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

// The phone remote's DashboardController: builds the dashboard, runs the single 1 s heartbeat, asks the PC for
// the state of the cards on screen and sends their commands, and owns the phone's settings and layout editor.
// Sideways screens and upright tablets get the dashboard at the 1920 × 720 scale on a canvas shaped like the screen
// (DashboardFit); upright phones get the same cards in a scrolling list.
public final class Dashboard {
    private static final double LIST_GAP = 16;
    public final MainActivity activity;
    public AppSettings settings;
    public final Pc pc;
    public final Images images = new Images();
    public final LayoutEditor editor = new LayoutEditor();
    private final File settingsFile;
    private final Map<String, Widget> widgets = new LinkedHashMap<String, Widget>();

    private FrameLayout root;
    private View dashboardView;
    private FrameLayout logicalRoot;
    private WidgetCanvas canvas;
    private Ui ui;
    private DashboardFit fit;
    private float scale = 1;
    // The version of the PC layout the dashboard shows; empty asks the PC for its layout on the next reading.
    private String mirroredVersion = "";
    private boolean editAfterMirror;
    private View pageSwitcher, hintRow;
    private TextView pageName, pageCount;
    private View previousPage, nextPage;
    private boolean listMode, started, refreshNow, offlineShown, askedToPair;
    private int builtWidth = -1, builtHeight = -1;
    private TextView clockTime, clockPeriod, clockDay, clockDate, statusLine, displayStatus, quietButton, undoButton, redoButton, arrangeButton;
    private View clockPeriodView, mainActions, editToolbar, widgetsPanel;
    private CheckBox snapChoice;
    private SettingsPanel settingsPanel;
    private AppSettings appearance;
    private int ticks, statusClearsAt = Integer.MAX_VALUE;
    private String statusText = "";

    public Dashboard(MainActivity activity) {
        this.activity = activity;
        settingsFile = new File(activity.getFilesDir(), "settings.xml");
        String[] warning = new String[1];
        settings = AppSettings.load(settingsFile, warning);
        pc = new Pc(activity);
        widgets.put("Focus", new FocusWidget(this));
        widgets.put("Media", new MediaWidget(this));
        widgets.put("Shortcuts", new QuickLaunchWidget(this));
        widgets.put("Mixer", new MixerWidget(this));
        widgets.put("System", new SystemWidget(this));
        widgets.put("Weather", new WeatherWidget(this));
        widgets.put("Spotify", new SpotifyWidget(this));
        widgets.put("Soundboard", new SoundboardWidget(this));
        widgets.put("Storage", new StorageWidget(this));
        widgets.put("Clocks", new ClocksWidget(this));
        widgets.put("Prayer", new PrayerWidget(this));
        widgets.put("Notes", new NotesWidget(this));
        widgets.put("Claude", new ClaudeWidget(this));
        widgets.put("Clipboard", new ClipboardWidget(this));
        widgets.put("Macros", new MacrosWidget(this));
        widgets.put("Calendar", new CalendarWidget(this));
        editor.apply(settings);
        editor.setListener(new LayoutEditor.Listener() { @Override public void changed(boolean structure) { editorChanged(structure); } });
        if (warning[0] != null) say(warning[0], true);
        else if (pc.startupWarning != null) say(pc.startupWarning, true);
    }

    public void attach(FrameLayout root) {
        this.root = root;
        root.addOnLayoutChangeListener(new View.OnLayoutChangeListener() {
            @Override public void onLayoutChange(View v, int l, int t, int r, int b, int ol, int ot, int or, int ob) {
                int width = r - l - v.getPaddingLeft() - v.getPaddingRight(), height = b - t - v.getPaddingTop() - v.getPaddingBottom();
                if (width == builtWidth && height == builtHeight) return;
                // The on-screen keyboard shrinks the window on older Android; don't rebuild under a text field.
                View focused = activity.getCurrentFocus();
                if (width == builtWidth && focused instanceof android.widget.EditText) return;
                Async.MAIN.post(new Runnable() { @Override public void run() { rebuild(); } });
            }
        });
    }

    public Ui ui() { return ui; }
    public boolean listMode() { return listMode; }
    public boolean editing() { return editor.editing(); }

    // Whether a widget is on screen and should poll: the app is in front, the widget is shown, and Spotify and
    // the soundboard pause while the layout is edited (as on Windows).
    public boolean isActive(String id) {
        Widget widget = widgets.get(id);
        return started && widget != null && widget.built() && editor.isVisible(id) && !((id.equals("Spotify") || id.equals("Soundboard")) && editor.editing());
    }

    // ----- The PC -----

    public PcState state() { return pc.state; }
    public boolean online() { return pc.online(); }
    public boolean quiet() { return pc.state != null && pc.state.quiet; }
    // The PC's clock choice, or the last one it sent while it's away, so the clock doesn't flip formats offline.
    public boolean use24() { return pc.state != null && pc.online() ? pc.state.use24 : settings.use24; }
    public String offlineText() {
        if (!pc.paired()) return "Pair this phone with your PC in Settings → PC.";
        if (pc.unpaired) return "The PC no longer knows this phone. Pair it again in Settings → PC.";
        if (pc.problem != null) return "Can't reach " + pc.name() + ".";
        return "Connecting to " + pc.name() + "…";
    }

    // Asks the PC for the cards on screen; hidden cards aren't read on the PC either. The layout section is only asked
    // for when the PC's version moved, and never while this phone is editing (the edit saves and reads it again).
    // Sections a card wants beyond its own while something is open in it (Now Playing's volume panel asks for audio).
    private final java.util.Set<String> extraParts = new java.util.LinkedHashSet<String>();
    public void wantPart(String part, boolean want) { if (want ? extraParts.add(part) : extraParts.remove(part)) if (want) refreshSoon(); }

    private void poll(boolean force) {
        List<String> parts = new ArrayList<String>();
        for (Widget widget : widgets.values()) if (isActive(widget.id) && !parts.contains(widget.part())) parts.add(widget.part());
        for (String part : extraParts) if (!parts.contains(part)) parts.add(part);
        if (!editor.editing() && (mirroredVersion.isEmpty() || (pc.state != null && !pc.state.layoutVersion.equals(mirroredVersion)))) parts.add("layout");
        pc.poll(parts, new Runnable() { @Override public void run() { stateChanged(); } }, force);
    }

    public void refreshSoon() { refreshNow = true; Async.MAIN.postDelayed(new Runnable() { @Override public void run() { if (started && refreshNow) { refreshNow = false; poll(true); } } }, 250); }

    private void stateChanged() {
        if (pc.problem != null) {
            // Right after pairing, the usual cause is the PC's firewall; say so once, where the user is looking.
            if (pc.justPaired() && pc.failures() == 1 && !pc.unpaired) say("Paired, but " + pc.name() + " isn't answering. On the PC, allow Widget Screen on private networks when Windows Firewall asks, or in Windows Security → Firewall → Allow an app.", true);
            else if (offlineCard == null) say(pc.problem, true);
            offlineShown = true;
        }
        else if (offlineShown) { offlineShown = false; say("Connected to " + pc.name() + "."); }
        updateOfflineCard();
        if (pc.state != null && pc.problem == null && settings.use24 != pc.state.use24) { settings.use24 = pc.state.use24; persist(); }
        updateClock(); updateQuiet();
        if (pc.state != null && pc.state.layout != null) mirror(pc.state.layout, pc.state.layoutVersion);
        updateClaudePill();
        for (Widget widget : widgets.values()) if (widget.built()) {
            try { widget.stateChanged(); }
            catch (Exception e) { /* One failing widget never stops the others. */ }
        }
        if (settingsPanel != null) settingsPanel.pcChanged();
    }

    // Sends a command to the PC; any problem shows in the status line, and the cards refresh right after.
    public void command(Map<String, Object> body) { command(body, null); }
    public void command(Map<String, Object> body, final String done) {
        pc.command(body, new Async.Done<String>() {
            @Override public void ok(String message) { if (message != null && !message.isEmpty()) say(message); else if (done != null) say(done); refreshSoon(); }
        }, new Async.Failed() {
            @Override public void error(Exception error) { say(Pc.message(error)); refreshSoon(); }
        });
    }

    // A slider move: the PC applies it and the next poll shows it; no extra reading, so rows don't flicker mid-drag.
    public void commandQuietly(Map<String, Object> body) {
        pc.command(body, new Async.Done<String>() { @Override public void ok(String message) { } }, new Async.Failed() {
            @Override public void error(Exception error) { say(Pc.message(error)); refreshSoon(); }
        });
    }

    public void openUrl(String url) {
        activity.startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
    }

    // A card whose arrangement depends on the PC (System stats rows) is built again when that changes.
    public void rebuildWidget(final String id) {
        Async.MAIN.post(new Runnable() {
            @Override public void run() { if (canvas != null && canvas.hasCard(id)) attachCard(id); else if (listMode) rebuild(); }
        });
    }

    public static String deviceName() {
        String model = Build.MODEL == null ? "Phone" : Build.MODEL;
        String maker = Build.MANUFACTURER == null ? "" : Build.MANUFACTURER;
        return maker.isEmpty() || model.toLowerCase(java.util.Locale.ROOT).startsWith(maker.toLowerCase(java.util.Locale.ROOT)) ? model : maker.substring(0, 1).toUpperCase(java.util.Locale.ROOT) + maker.substring(1) + " " + model;
    }

    public void onStart() {
        started = true;
        Async.MAIN.removeCallbacks(heartbeat);
        Async.MAIN.post(heartbeat);
        for (Widget widget : widgets.values()) if (widget.built()) widget.render();
        if (pc.paired()) poll(true);
    }

    public void onStop() {
        started = false;
        Async.MAIN.removeCallbacks(heartbeat);
    }

    private final Runnable heartbeat = new Runnable() {
        @Override public void run() {
            if (!started) return;
            tick();
            Async.MAIN.postDelayed(this, 1000);
        }
    };

    private void tick() {
        ticks++;
        updateClock();
        if (ticks >= statusClearsAt && !editor.editing()) { setStatus(""); statusClearsAt = Integer.MAX_VALUE; }
        // The PC is asked every 2 s, or every 5 s in its Quiet mode, like the Windows refresh cadence.
        if (pc.paired() && ticks % (quiet() ? 5 : 2) == 0) poll(false);
        if (claudePill != null && claudePill.getVisibility() == View.VISIBLE) updateClaudePill();
        for (Widget widget : widgets.values()) if (widget.built()) {
            try { widget.tick(ticks, quiet()); }
            catch (Exception e) { /* One failing widget never stops the others. */ }
        }
    }

    // Status messages clear after about ten seconds on the heartbeat. A sticky one stays until replaced.
    public void say(String text) { say(text, false); }
    public void say(String text, boolean sticky) { setStatus(text); statusClearsAt = sticky ? Integer.MAX_VALUE : ticks + 10; }
    private void setStatus(String text) { statusText = text == null ? "" : text; if (statusLine != null) statusLine.setText(statusText); }

    public AppSettings look() { return appearance != null ? appearance : settings; }

    // Live preview from Settings; null restores the saved appearance.
    public void preview(AppSettings draft) { appearance = draft; rebuild(); }

    public void rebuild() {
        if (root == null) return;
        int width = root.getWidth() - root.getPaddingLeft() - root.getPaddingRight(), height = root.getHeight() - root.getPaddingTop() - root.getPaddingBottom();
        if (width <= 0 || height <= 0) return;
        builtWidth = width; builtHeight = height;
        for (Widget widget : widgets.values()) widget.release();
        AppSettings look = look();
        Theme theme = Theme.of(look.theme, look.accent);
        ui = new Ui(activity, theme, look, 1f);
        activity.applyWindow(settings, theme);
        DisplayMetrics metrics = activity.getResources().getDisplayMetrics();
        fit = DashboardFit.choose(width, height, metrics.density, settings.layoutMode);
        listMode = fit.list;
        if (listMode && editor.editing()) editor.cancel();
        // Each canvas shape has its own layout; the list shows the PC screen's layout in reading order.
        boolean discarded = listMode ? editor.setCanvas(settings.pcCanvasWidth, settings.pcCanvasHeight) : editor.setCanvas(fit.canvasWidth(), fit.canvasHeight());
        if (discarded) say("The screen changed shape, so the layout changes were discarded.");
        if (dashboardView != null) root.removeView(dashboardView);
        root.setBackgroundColor(theme.page);
        widgetsPanel = null; offlineCard = null;
        scale = (float) fit.scale;
        if (!listMode) {
            int logicalWidth = (int) fit.logicalWidth, logicalHeight = (int) fit.logicalHeight;
            dashboardView = new ScaledHost(activity, buildDashboard(logicalWidth, logicalHeight), scale, logicalWidth, logicalHeight, false);
        } else {
            int logicalWidth = (int) fit.logicalWidth;
            ScrollView scroll = new ScrollView(activity);
            scroll.setFillViewport(true);
            scroll.setVerticalScrollBarEnabled(false);
            scroll.addView(new ScaledHost(activity, buildList(logicalWidth), scale, logicalWidth, 0, true));
            dashboardView = scroll;
        }
        root.addView(dashboardView, 0, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        updateClock();
        updateQuiet();
        updatePages();
        updateEditorChrome();
        if (settingsPanel != null) settingsPanel.dashboardRebuilt();
        else askToPairIfNeeded();
        // New cards ask the PC for their readings right away instead of waiting for the next beat.
        if (pc.paired() && started) refreshSoon();
    }

    // stacked: the phone list's header (clock over the buttons, no editor). twoRows: an upright dashboard's header, the
    // buttons or the edit toolbar on a second row under the clock. narrow: the toolbar drops its title.
    private View buildHeader(boolean stacked, boolean twoRows, boolean narrow) {
        LinearLayout clock = ui.row();
        clock.setGravity(Gravity.CENTER_VERTICAL);
        clock.setContentDescription("Local time and date");
        clockTime = Ui.tabular(ui.text("00:00", ui.headerClock(), ui.theme.text));
        clockTime.setTypeface(Ui.LIGHT);
        clock.addView(clockTime, ui.ll(Ui.WRAP, Ui.WRAP));
        clockPeriod = ui.text("AM", ui.body(), ui.theme.clockMuted);
        LinearLayout.LayoutParams period = ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 8, 0, 0, 14);
        period.gravity = Gravity.BOTTOM;
        clock.addView(clockPeriod, period);
        clockPeriodView = clockPeriod;
        View divider = new View(activity);
        divider.setBackgroundColor(ui.theme.controlBorder);
        clock.addView(divider, ui.margins(ui.ll(ui.px(2), ui.px(44)), 22, 0, 22, 0));
        LinearLayout date = ui.column();
        clockDay = ui.label("THURSDAY");
        clockDay.setTextColor(ui.theme.accent);
        date.addView(clockDay, ui.ll(Ui.WRAP, Ui.WRAP));
        clockDate = ui.heading("October 8");
        date.addView(clockDate, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 1, 0, 0));
        clock.addView(date, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 4, 0, 0));

        LinearLayout actions = ui.row();
        if (!stacked) actions.addView(ui.button("Edit layout", new View.OnClickListener() { @Override public void onClick(View v) { beginEdit(); } }), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 10, 0));
        quietButton = ui.button("Quiet mode", new View.OnClickListener() { @Override public void onClick(View v) { toggleQuiet(); } });
        actions.addView(quietButton, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 10, 0));
        LinearLayout settingsButton = ui.row();
        settingsButton.setBackground(ui.pressable(ui.theme.raised, ui.theme.controlBorder, 1, 12));
        settingsButton.setPadding(ui.px(16), ui.px(8), ui.px(16), ui.px(8));
        settingsButton.setMinimumHeight(ui.px(48));
        settingsButton.addView(new GlyphView(activity, "settings", ui.theme.text, ui.fpx(20)), ui.margins(ui.ll(ui.px(22), ui.px(22)), 0, 0, 10, 0));
        settingsButton.addView(ui.body("Settings"), ui.ll(Ui.WRAP, Ui.WRAP));
        settingsButton.setClickable(true);
        settingsButton.setContentDescription("Settings");
        settingsButton.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { openSettings(null); } });
        actions.addView(settingsButton, ui.ll(Ui.WRAP, Ui.WRAP));
        mainActions = actions;
        pageSwitcher = buildPageSwitcher();
        hintRow = buildHint();

        if (stacked) {
            LinearLayout header = ui.column();
            header.addView(clock, ui.ll(Ui.WRAP, Ui.WRAP));
            header.addView(pageSwitcher, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 12, 0, 0));
            header.addView(hintRow, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 12, 0, 0));
            LinearLayout.LayoutParams params = ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 12, 0, 0);
            params.gravity = Gravity.END;
            header.addView(actions, params);
            editToolbar = null;
            return header;
        }
        FrameLayout header = new FrameLayout(activity);
        FrameLayout.LayoutParams clockParams = ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.TOP);
        clockParams.topMargin = ui.px(-8);
        header.addView(clock, clockParams);
        // Beside the clock in the middle of the header, or under the clock when the header has two rows.
        FrameLayout.LayoutParams pageParams = ui.frame(Ui.WRAP, Ui.WRAP, twoRows ? Gravity.START | Gravity.BOTTOM : Gravity.CENTER_HORIZONTAL | Gravity.TOP);
        if (twoRows) pageParams.bottomMargin = ui.px(16);
        header.addView(pageSwitcher, pageParams);
        // The tips take the page switcher's spot while there is one page; a two-row header has no room for them.
        FrameLayout.LayoutParams hintParams = ui.frame(Ui.WRAP, Ui.WRAP, Gravity.CENTER_HORIZONTAL | Gravity.TOP);
        hintParams.topMargin = ui.px(4);
        header.addView(hintRow, hintParams);
        if (twoRows) hintRow.setVisibility(View.GONE);
        int place = Gravity.END | (twoRows ? Gravity.BOTTOM : Gravity.TOP);
        FrameLayout.LayoutParams actionParams = ui.frame(Ui.WRAP, Ui.WRAP, place);
        if (twoRows) actionParams.bottomMargin = ui.px(16);
        header.addView(actions, actionParams);
        editToolbar = buildEditToolbar(narrow);
        FrameLayout.LayoutParams toolbarParams = ui.frame(Ui.WRAP, Ui.WRAP, place);
        if (twoRows) toolbarParams.bottomMargin = ui.px(16);
        header.addView(editToolbar, toolbarParams);
        return header;
    }

    // A one-time hint for what the dashboard doesn't show by itself; Got it puts it away for good (as on the PC).
    private View buildHint() {
        LinearLayout row = ui.row();
        row.setContentDescription("Tips");
        TextView text = ui.smallText(listMode ? "Edit layout on the PC or a sideways phone  ·  Settings → PC pairs this phone" : "Swipe sideways to switch pages  ·  Edits here are saved on the PC");
        text.setSingleLine(true); text.setEllipsize(android.text.TextUtils.TruncateAt.END);
        text.setMaxWidth(ui.px(560));
        row.addView(text, ui.ll(Ui.WRAP, Ui.WRAP));
        TextView gotIt = ui.button("Got it", new View.OnClickListener() { @Override public void onClick(View v) { settings.hintDismissed = true; persist(); updateHint(); } });
        gotIt.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(ui.small()));
        gotIt.setPadding(ui.px(12), ui.px(4), ui.px(12), ui.px(4));
        gotIt.setContentDescription("Dismiss tips");
        row.addView(gotIt, ui.margins(ui.ll(Ui.WRAP, ui.px(48)), 14, 0, 0, 0));
        return row;
    }

    private void updateHint() {
        if (hintRow == null) return;
        boolean twoRows = !listMode && fit != null && fit.stacked;
        boolean show = !settings.hintDismissed && !editor.editing() && settings.pages.size() == 1 && !twoRows;
        hintRow.setVisibility(show ? View.VISIBLE : View.GONE);
    }

    // Layout pages, as on the PC: previous, the page's name and position, next. Shown when the PC has more than one page.
    private View buildPageSwitcher() {
        LinearLayout row = ui.row();
        row.setGravity(Gravity.CENTER_VERTICAL);
        row.setContentDescription("Layout pages");
        previousPage = ui.iconButton("chevron-left", "Previous page", 22, true, new View.OnClickListener() { @Override public void onClick(View v) { showPage(settings.page - 1); } });
        row.addView(previousPage, ui.ll(ui.px(48), ui.px(48)));
        LinearLayout label = ui.column();
        label.setGravity(Gravity.CENTER_HORIZONTAL);
        pageName = ui.body(settings.pageName());
        pageName.setMaxWidth(ui.px(260)); pageName.setSingleLine(true); pageName.setEllipsize(android.text.TextUtils.TruncateAt.END);
        label.addView(pageName, ui.ll(Ui.WRAP, Ui.WRAP));
        pageCount = ui.caption("");
        label.addView(pageCount, ui.ll(Ui.WRAP, Ui.WRAP));
        label.setMinimumWidth(ui.px(120));
        row.addView(label, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 14, 0, 14, 0));
        nextPage = ui.iconButton("chevron-right", "Next page", 22, true, new View.OnClickListener() { @Override public void onClick(View v) { showPage(settings.page + 1); } });
        row.addView(nextPage, ui.ll(ui.px(48), ui.px(48)));
        LinearLayout pill = ui.row();
        pill.setBackground(ui.pressable(ui.theme.raised, Theme.WARNING, 2, 12));
        pill.setPadding(ui.px(10), ui.px(4), ui.px(16), ui.px(4));
        pill.setMinimumHeight(ui.px(48));
        claudePillCritter = new CritterView(activity, ui.theme);
        claudePillCritter.set("needs", "pill");
        pill.addView(claudePillCritter, ui.ll(ui.px(32), ui.px(32)));
        TextView words = ui.text("Claude needs you", ui.small(), Theme.WARNING);
        words.setTypeface(Ui.MEDIUM);
        pill.addView(words, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 8, 0, 0, 0));
        pill.setClickable(true);
        pill.setContentDescription("Claude needs you. Show the Claude page.");
        pill.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { if (pc.state != null && pc.state.claudePage >= 0) showPage(pc.state.claudePage); } }));
        pill.setVisibility(View.GONE);
        claudePill = pill;
        row.addView(pill, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 20, 0, 0, 0));
        return row;
    }

    // The header pill (as on the PC): a Claude Code session needs you while the Claude card is on another page. A
    // session that newly needs you taps the phone once while the app is open.
    private View claudePill;
    private CritterView claudePillCritter;
    private int claudeNeeds = -1;
    private void updateClaudePill() {
        PcState state = pc.state;
        int needs = state != null && pc.online() ? state.claudeNeeds : 0;
        if (claudeNeeds >= 0 && needs > claudeNeeds && root != null) {
            try { root.performHapticFeedback(android.view.HapticFeedbackConstants.CONTEXT_CLICK); } catch (Exception ignored) { }
        }
        claudeNeeds = needs;
        if (claudePill == null) return;
        boolean show = needs > 0 && state.claudePage >= 0 && !editor.isVisible("Claude") && !editor.editing();
        claudePill.setVisibility(show ? View.VISIBLE : View.GONE);
        if (claudePillCritter != null) claudePillCritter.setPhase(ticks % 2 == 0 && !quiet(), quiet());
    }

    private void updatePages() {
        if (pageSwitcher == null) return;
        int count = settings.pages.size();
        pageSwitcher.setVisibility(count > 1 && !editor.editing() ? View.VISIBLE : View.GONE);
        pageName.setText(settings.pageName());
        pageCount.setText((settings.page + 1) + " of " + count);
        Ui.enable(previousPage, settings.page > 0);
        Ui.enable(nextPage, settings.page < count - 1);
        updateHint();
        updateClaudePill();
    }

    // The page shown is the PC's: switching here switches the PC, and the PC's layout for that page follows.
    public void showPage(int index) {
        if (editor.editing() || index < 0 || index >= settings.pages.size() || index == settings.page) return;
        layoutCommand(Json.map("action", "page.show", "index", index, "page", settings.pages.get(index)), null);
    }

    // Page and layout changes go to the PC; the phone then reads the PC's layout again.
    private void layoutCommand(Map<String, Object> body, final String done) {
        pc.command(body, new Async.Done<String>() {
            @Override public void ok(String message) { if (message != null && !message.isEmpty()) say(message); else if (done != null) say(done); mirroredVersion = ""; refreshSoon(); }
        }, new Async.Failed() {
            @Override public void error(Exception error) { say(Pc.message(error), true); mirroredVersion = ""; refreshSoon(); }
        });
    }

    // Saves a layout for this phone's canvas on the PC's current page (Edit layout, Settings → Widgets).
    public void saveLayout(WidgetLayout.Placement[] layout, double canvasWidth, double canvasHeight, String done) {
        settings.store(canvasWidth, canvasHeight, layout);
        persist();
        layoutCommand(Json.map("action", "layout.save", "index", settings.page, "page", settings.pageName(), "width", canvasWidth, "height", canvasHeight, "widgets", PcState.json(layout)), done);
    }

    // The PC sent its layout: show it, unless an edit is open (the edit saves to the PC and the layout is read again).
    private void mirror(PcState.Layout layout, String version) {
        if (version.equals(mirroredVersion) || editor.editing()) return;
        mirroredVersion = version;
        settings.mirror(layout);
        persist();
        editor.apply(settings);
        if (listMode) rebuild();
        else { editorChanged(true); updatePages(); }
        if (editAfterMirror) { editAfterMirror = false; beginEdit(); }
    }

    // On narrow (phone) dashboards the toolbar drops its title so it doesn't run into the clock.
    private View buildEditToolbar(boolean narrow) {
        LinearLayout toolbar = ui.row();
        LinearLayout title = ui.column();
        TextView heading = ui.text("Edit layout", 25, ui.theme.text);
        heading.setTypeface(Ui.MEDIUM);
        title.addView(heading, ui.ll(Ui.WRAP, Ui.WRAP));
        title.addView(ui.smallText("Drag to move or resize"), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 2, 0, 0));
        if (!narrow) toolbar.addView(title, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 32, 0));
        undoButton = ui.button("Undo", new View.OnClickListener() { @Override public void onClick(View v) { editor.undo(); } });
        toolbar.addView(undoButton, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 8, 0));
        redoButton = ui.button("Redo", new View.OnClickListener() { @Override public void onClick(View v) { editor.redo(); } });
        toolbar.addView(redoButton, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 8, 0));
        toolbar.addView(ui.button("Reset layout", new View.OnClickListener() { @Override public void onClick(View v) { editor.reset(); } }), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 8, 0));
        arrangeButton = ui.button("Arrange", new View.OnClickListener() { @Override public void onClick(View v) { if (!editor.arrange()) say("The widgets are already arranged."); } });
        arrangeButton.setContentDescription("Arrange widgets: line up the shown widgets neatly in their current order");
        toolbar.addView(arrangeButton, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 18, 0));
        snapChoice = ui.check("Snap", editor.snap(), new CheckBox.Listener() { @Override public void changed(CheckBox box, boolean checked) { editor.setSnap(checked); } });
        toolbar.addView(snapChoice, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 18, 0));
        toolbar.addView(ui.button("Widgets", new View.OnClickListener() { @Override public void onClick(View v) { toggleWidgetsPanel(); } }), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 32, 0));
        toolbar.addView(ui.button("Cancel", new View.OnClickListener() { @Override public void onClick(View v) { cancelEdit(); } }), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 8, 0));
        toolbar.addView(ui.accentButton("Save layout", new View.OnClickListener() { @Override public void onClick(View v) { saveEdit(); } }), ui.ll(Ui.WRAP, Ui.WRAP));
        return toolbar;
    }

    private View buildDashboard(int logicalWidth, int logicalHeight) {
        logicalRoot = new FrameLayout(activity);
        LinearLayout column = ui.column();
        column.setPadding(ui.px(32), ui.px(20), ui.px(32), ui.px(16));
        column.addView(buildHeader(false, fit.stacked, logicalWidth < 1800), ui.ll(Ui.MATCH, ui.px(fit.headerHeight())));
        canvas = new WidgetCanvas(activity, ui, editor);
        canvas.setScreenScale(scale);
        canvas.setSwipe(new WidgetCanvas.Swipe() { @Override public void swiped(int direction) { showPage(settings.page + direction); } });
        for (String id : WidgetLayout.IDS) if (editor.isVisible(id)) attachCard(id);
        column.addView(canvas, ui.ll(ui.px(fit.canvasWidth()), ui.px(fit.canvasHeight())));
        column.addView(new View(activity), ui.ll(Ui.MATCH, 0, 1));
        FrameLayout footer = new FrameLayout(activity);
        statusLine = ui.smallText(statusText);
        footer.addView(statusLine, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.BOTTOM));
        statusLine.setMaxWidth(ui.px(logicalWidth - 64 - 420));
        displayStatus = ui.caption("");
        footer.addView(displayStatus, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.BOTTOM));
        column.addView(footer, ui.ll(Ui.MATCH, ui.px(36)));
        logicalRoot.addView(column, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        return logicalRoot;
    }

    private void attachCard(String id) {
        WidgetLayout.Box box = editor.bounds(id);
        canvas.setCard(id, widgets.get(id).build(ui, box.width, box.height));
    }

    private static double listHeight(String id) {
        switch (id) {
            case "Focus": return 400;
            case "Media": return 330;
            case "Shortcuts": return 290;
            case "Mixer": return 560;
            case "System": return 330;
            case "Weather": return 270;
            case "Storage": return 300;
            case "Clocks": return 280;
            case "Prayer": return 300;
            case "Notes": return 420;
            case "Claude": return 330;
            case "Clipboard": return 400;
            case "Macros": return 290;
            case "Calendar": return 420;
            default: return 640;
        }
    }

    // Phones: the same cards stacked in reading order (left to right, then top to bottom on the canvas).
    private View buildList(int logicalWidth) {
        logicalRoot = new FrameLayout(activity);
        canvas = null;
        LinearLayout column = ui.column();
        column.setPadding(ui.px(LIST_GAP), ui.px(20), ui.px(LIST_GAP), ui.px(LIST_GAP));
        column.addView(buildHeader(true, true, true), ui.ll(Ui.MATCH, Ui.WRAP));
        statusLine = Ui.wrap(ui.smallText(statusText), 3);
        column.addView(statusLine, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 8, 0, 0));
        displayStatus = null;
        // One calm card at the top while the PC is away, instead of "PC offline" on every card.
        offlineCard = buildOfflineCard();
        column.addView(offlineCard, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, LIST_GAP, 0, 0));
        updateOfflineCard();
        java.util.List<WidgetLayout.Placement> order = new java.util.ArrayList<WidgetLayout.Placement>();
        for (WidgetLayout.Placement p : editor.placements()) if (editor.isVisible(p.id)) order.add(p);
        java.util.Collections.sort(order, new java.util.Comparator<WidgetLayout.Placement>() {
            @Override public int compare(WidgetLayout.Placement a, WidgetLayout.Placement b) { return a.x != b.x ? Double.compare(a.x, b.x) : Double.compare(a.y, b.y); }
        });
        double cardWidth = logicalWidth - 2 * LIST_GAP;
        for (WidgetLayout.Placement p : order) {
            double height = listHeight(p.id);
            column.addView(widgets.get(p.id).build(ui, cardWidth, height), ui.margins(ui.ll(Ui.MATCH, ui.px(height)), 0, LIST_GAP, 0, 0));
        }
        if (order.isEmpty()) {
            TextView none = Ui.wrap(ui.smallText("Every widget is hidden. Turn some on in Settings → Widgets."), 3);
            column.addView(none, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 24, 0, 0));
        }
        logicalRoot.addView(column, new FrameLayout.LayoutParams(Ui.MATCH, Ui.WRAP));
        return logicalRoot;
    }

    // ----- Offline card (phone list) -----

    private View offlineCard;
    private TextView offlineTitle, offlineText;

    private View buildOfflineCard() {
        LinearLayout column = ui.column();
        offlineTitle = ui.heading("Can't reach the PC");
        offlineTitle.setTypeface(Ui.MEDIUM);
        column.addView(offlineTitle, ui.ll(Ui.MATCH, Ui.WRAP));
        offlineText = Ui.wrap(ui.smallText(""), 4);
        column.addView(offlineText, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 12));
        LinearLayout buttons = ui.row();
        buttons.addView(ui.button("Try now", new View.OnClickListener() { @Override public void onClick(View v) { refreshSoon(); } }), ui.margins(ui.ll(Ui.WRAP, ui.px(48)), 0, 0, 10, 0));
        buttons.addView(ui.flatButton("Settings → PC", new View.OnClickListener() { @Override public void onClick(View v) { openSettings("PC"); } }), ui.ll(Ui.WRAP, ui.px(48)));
        column.addView(buttons, ui.ll(Ui.MATCH, Ui.WRAP));
        FrameLayout card = ui.card(column, 20);
        card.setVisibility(View.GONE);
        return card;
    }

    private void updateOfflineCard() {
        if (offlineCard == null) return;
        boolean show = pc.paired() && !pc.online();
        offlineCard.setVisibility(show ? View.VISIBLE : View.GONE);
        if (!show) return;
        offlineTitle.setText(pc.unpaired ? "The PC forgot this phone" : pc.problem != null ? "Can't reach " + pc.name() : "Connecting to " + pc.name() + "…");
        int failures = pc.failures();
        offlineText.setText(pc.unpaired ? "Pair it again in Settings → PC." : pc.problem != null ? pc.problem + (failures >= 3 ? " Trying again every minute." : failures == 2 ? " Trying again in 15 s." : failures == 1 ? " Trying again in 5 s." : "") : "The first reading is on its way.");
    }

    private void updateClock() {
        if (clockTime == null) return;
        LocalDateTime now = LocalDateTime.now();
        boolean use24 = use24();
        clockTime.setText(Format.clockTime(now, use24));
        clockPeriod.setText(Format.clockPeriod(now));
        clockPeriodView.setVisibility(use24 ? View.GONE : View.VISIBLE);
        clockDay.setText(Format.clockDay(now));
        clockDate.setText(Format.clockDate(now));
    }

    // Quiet mode is a toggle: when on it takes the same selected look as the chosen Focus preset.
    private void updateQuiet() {
        if (quietButton == null) return;
        ui.select(quietButton, quiet());
        quietButton.setContentDescription(quiet() ? "Quiet mode, on" : "Quiet mode, off");
        Ui.enable(quietButton, online());
    }

    // Quiet mode belongs to the PC: it slows the PC's updates and the phone's readings alike.
    public void toggleQuiet() { command(Json.map("action", "quiet.toggle"), quiet() ? "Quiet mode off." : "Quiet mode on. Updates are slower."); }

    public void persist() {
        try { AppSettings.save(settingsFile, settings); }
        catch (Exception e) { say("Settings couldn't be saved on this device. Try again.", true); }
    }

    // ----- Layout editor -----

    private void beginEdit() {
        if (listMode || canvas == null) return;
        if (!online()) { say("Connect to " + pc.name() + " to edit its layout. The phone shows the PC's layout."); return; }
        if (pc.state != null && pc.state.layout != null && pc.state.layout.editing) { say("The layout is being edited on the PC. Try again once it's saved there."); return; }
        editor.begin();
        updatePages();
    }

    private void cancelEdit() { closeWidgetsPanel(); editor.cancel(); updatePages(); say("Layout changes discarded."); }

    private void saveEdit() {
        closeWidgetsPanel();
        // Saved for this canvas size only; other screen shapes keep their own layouts.
        settings.snapWidgets = editor.snap();
        WidgetLayout.Placement[] layout = editor.export();
        boolean overlaps = editor.hasOverlaps();
        editor.accept();
        updatePages();
        saveLayout(layout, editor.canvasWidth(), editor.canvasHeight(), overlaps ? "Layout saved on the PC. Some widgets overlap." : "Layout saved on the PC.");
    }

    // ----- Confirm sheet -----

    private View confirmSheet;

    // The one way the phone asks "are you sure": a themed sheet over the dashboard (the same look as the PC's), with
    // 52-unit buttons. Tapping outside, Cancel or Back answers no.
    public void confirm(String title, String subject, String detail, String action, final Runnable yes) {
        closeConfirm();
        if (root == null) return;
        Ui sheetUi = ui != null ? ui : new Ui(activity, Theme.of(look().theme, look().accent), look(), 1f);
        final FrameLayout scrim = new FrameLayout(activity);
        scrim.setBackgroundColor(Ui.alpha(sheetUi.theme.page, 0.7f));
        scrim.setClickable(true);
        scrim.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { closeConfirm(); } });
        LinearLayout panel = sheetUi.column();
        panel.setBackground(sheetUi.shape(sheetUi.theme.surface, sheetUi.theme.controlBorder, 1, sheetUi.radius));
        panel.setPadding(sheetUi.px(28), sheetUi.px(28), sheetUi.px(28), sheetUi.px(28));
        panel.setClickable(true);
        panel.addView(sheetUi.label(title), sheetUi.ll(Ui.MATCH, Ui.WRAP));
        TextView subjectText = Ui.wrap(sheetUi.heading(subject), 3); subjectText.setTypeface(Ui.MEDIUM);
        panel.addView(subjectText, sheetUi.margins(sheetUi.ll(Ui.MATCH, Ui.WRAP), 0, 8, 0, 0));
        if (detail != null && !detail.isEmpty()) panel.addView(Ui.wrap(sheetUi.smallText(detail), 6), sheetUi.margins(sheetUi.ll(Ui.MATCH, Ui.WRAP), 0, 8, 0, 0));
        LinearLayout buttons = sheetUi.row();
        buttons.setGravity(Gravity.END);
        TextView cancel = sheetUi.button("Cancel", new View.OnClickListener() { @Override public void onClick(View v) { closeConfirm(); } });
        cancel.setMinWidth(sheetUi.px(140));
        buttons.addView(cancel, sheetUi.margins(sheetUi.ll(Ui.WRAP, sheetUi.px(52)), 0, 0, 12, 0));
        TextView go = sheetUi.accentButton(action, new View.OnClickListener() { @Override public void onClick(View v) { closeConfirm(); yes.run(); } });
        go.setMinWidth(sheetUi.px(170));
        buttons.addView(go, sheetUi.ll(Ui.WRAP, sheetUi.px(52)));
        panel.addView(buttons, sheetUi.margins(sheetUi.ll(Ui.MATCH, Ui.WRAP), 0, 24, 0, 0));
        int width = Math.min(sheetUi.px(600), Math.max(1, root.getWidth() - root.getPaddingLeft() - root.getPaddingRight() - sheetUi.px(32)));
        scrim.addView(panel, sheetUi.frame(width, Ui.WRAP, Gravity.CENTER));
        confirmSheet = scrim;
        root.addView(scrim, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
    }

    private void closeConfirm() {
        if (confirmSheet != null && root != null) root.removeView(confirmSheet);
        confirmSheet = null;
    }

    public void onBack(Runnable fallback) {
        if (confirmSheet != null) { closeConfirm(); return; }
        if (settingsPanel != null) { settingsPanel.cancel(); return; }
        if (widgetsPanel != null) { closeWidgetsPanel(); return; }
        if (editor.editing()) { cancelEdit(); return; }
        fallback.run();
    }

    private void editorChanged(boolean structure) {
        if (canvas == null) return;
        if (structure) {
            // Build or drop cards whose visibility changed, and rebuild a resized card's content.
            for (String id : WidgetLayout.IDS) {
                boolean visible = editor.isVisible(id);
                WidgetLayout.Box box = editor.bounds(id);
                if (visible && !(canvas.hasCard(id) && widgets.get(id).builtAt(box.width, box.height))) attachCard(id);
                else if (!visible && canvas.hasCard(id)) { widgets.get(id).release(); canvas.removeCard(id); }
            }
        }
        canvas.sync();
        updateEditorChrome();
    }

    private void updateEditorChrome() {
        boolean editing = editor.editing() && !listMode;
        if (mainActions != null) mainActions.setVisibility(editing ? View.GONE : View.VISIBLE);
        if (editToolbar != null) editToolbar.setVisibility(editing ? View.VISIBLE : View.GONE);
        if (!editing) { if (displayStatus != null) displayStatus.setText(""); return; }
        Ui.enable(undoButton, editor.canUndo() && !editor.gestureActive());
        Ui.enable(redoButton, editor.canRedo() && !editor.gestureActive());
        Ui.enable(arrangeButton, !editor.gestureActive());
        snapChoice.setChecked(editor.snap(), false);
        String id = editor.selected();
        WidgetLayout.Box b = editor.bounds(id);
        String info = WidgetLayout.title(id) + " · " + Math.round(b.width) + " × " + Math.round(b.height) + " · X " + Math.round(b.x) + ", Y " + Math.round(b.y);
        if (id.equals("System") && !editor.isVisible("System") && editor.isEnabled("System")) info += " · Turn on readings in Settings to show system stats";
        else if (!editor.isEnabled(id)) info += " · Hidden · Turn it on under Widgets to bring it back";
        if (editor.hasOverlaps()) info += " · Widgets overlap";
        say(info, true);
        if (displayStatus != null) displayStatus.setText("DRAG A CARD TO MOVE · DRAG A CORNER TO RESIZE · BACK CANCELS");
        if (widgetsPanel != null) refreshWidgetsPanel();
    }

    private void toggleWidgetsPanel() { if (widgetsPanel != null) closeWidgetsPanel(); else openWidgetsPanel(); }

    private void closeWidgetsPanel() {
        if (widgetsPanel != null && logicalRoot != null) logicalRoot.removeView(widgetsPanel);
        widgetsPanel = null;
    }

    // The editor's list (the same on the PC): a check mark per widget to show or hide it (tapping a name selects it),
    // and the pages: rename the one shown, open, move or delete any, add a new one.
    private void openWidgetsPanel() {
        if (logicalRoot == null) return;
        FrameLayout scrim = new FrameLayout(activity);
        scrim.setClickable(true);
        scrim.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { closeWidgetsPanel(); } });
        ScrollView scroll = new ScrollView(activity);
        scroll.setBackground(ui.shape(ui.theme.raised, ui.theme.controlBorder, 1, 16));
        scroll.setVerticalScrollBarEnabled(false);
        LinearLayout panel = ui.column();
        panel.setPadding(ui.px(16), ui.px(12), ui.px(16), ui.px(12));
        panel.setClickable(true);
        panel.setTag("panel");
        scroll.addView(panel, new FrameLayout.LayoutParams(Ui.MATCH, Ui.WRAP));
        FrameLayout.LayoutParams params = ui.frame(ui.px(440), Ui.WRAP, Gravity.END | Gravity.TOP);
        params.topMargin = ui.px(20 + (fit == null ? DashboardFit.HEADER : fit.headerHeight())); params.rightMargin = ui.px(32 + 300);
        params.bottomMargin = ui.px(16 + DashboardFit.FOOTER);
        scrim.addView(scroll, params);
        widgetsPanel = scrim;
        logicalRoot.addView(scrim, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        refreshWidgetsPanel();
    }

    private void refreshWidgetsPanel() {
        LinearLayout panel = (LinearLayout) widgetsPanel.findViewWithTag("panel");
        panel.removeAllViews();
        panel.addView(ui.label("SHOW ON THE DASHBOARD"), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 4, 4, 0, 8));
        for (final String id : WidgetLayout.IDS) {
            CheckBox box = ui.check(WidgetLayout.title(id), editor.isEnabled(id), new CheckBox.Listener() {
                @Override public void changed(CheckBox b, boolean checked) { editor.setVisible(id, checked); if (checked) editor.select(id); }
            });
            if (id.equals(editor.selected())) box.setBackground(ui.shape(ui.theme.selected, ui.theme.accent, 1, 10));
            panel.addView(box, ui.ll(Ui.MATCH, Ui.WRAP));
        }
        // Nudge arrows: one-unit moves and resizes of the selected card, which a finger can't do on a phone (the PC
        // has them on the keyboard). 8 units a tap, with Snap on; 1 unit with it off.
        panel.addView(ui.label("NUDGE " + WidgetLayout.title(editor.selected()).toUpperCase(java.util.Locale.ROOT)), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 4, 20, 0, 8));
        final int step = editor.snap() ? 8 : 1;
        LinearLayout move = ui.row();
        move.addView(ui.smallText("Move"), ui.margins(ui.ll(ui.px(64), Ui.WRAP), 4, 0, 0, 0));
        move.addView(ui.iconButton("chevron-left", "Move left", 20, true, nudge(-step, 0, false)), ui.margins(ui.ll(ui.px(48), ui.px(48)), 0, 0, 6, 0));
        move.addView(ui.iconButton("chevron-up", "Move up", 20, true, nudge(0, -step, false)), ui.margins(ui.ll(ui.px(48), ui.px(48)), 0, 0, 6, 0));
        move.addView(ui.iconButton("chevron-down", "Move down", 20, true, nudge(0, step, false)), ui.margins(ui.ll(ui.px(48), ui.px(48)), 0, 0, 6, 0));
        move.addView(ui.iconButton("chevron-right", "Move right", 20, true, nudge(step, 0, false)), ui.ll(ui.px(48), ui.px(48)));
        panel.addView(move, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 6));
        LinearLayout size = ui.row();
        size.addView(ui.smallText("Size"), ui.margins(ui.ll(ui.px(64), Ui.WRAP), 4, 0, 0, 0));
        size.addView(ui.iconButton("chevron-left", "Narrower", 20, true, nudge(-step, 0, true)), ui.margins(ui.ll(ui.px(48), ui.px(48)), 0, 0, 6, 0));
        size.addView(ui.iconButton("chevron-up", "Shorter", 20, true, nudge(0, -step, true)), ui.margins(ui.ll(ui.px(48), ui.px(48)), 0, 0, 6, 0));
        size.addView(ui.iconButton("chevron-down", "Taller", 20, true, nudge(0, step, true)), ui.margins(ui.ll(ui.px(48), ui.px(48)), 0, 0, 6, 0));
        size.addView(ui.iconButton("chevron-right", "Wider", 20, true, nudge(step, 0, true)), ui.ll(ui.px(48), ui.px(48)));
        panel.addView(size, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 4));
        panel.addView(ui.label("PAGES"), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 4, 20, 0, 8));
        panel.addView(ui.smallText("Page actions save your layout changes first."), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 4, 0, 0, 8));
        LinearLayout rename = ui.row();
        rename.setGravity(Gravity.CENTER_VERTICAL);
        final android.widget.EditText name = ui.input(settings.pageName(), "Page name");
        name.setSingleLine(true);
        rename.addView(name, ui.ll(0, ui.px(52), 1));
        rename.addView(ui.button("Rename", new View.OnClickListener() { @Override public void onClick(View v) {
            pageAction(Json.map("action", "page.rename", "index", settings.page, "page", settings.pageName(), "name", name.getText().toString()));
        } }), ui.margins(ui.ll(Ui.WRAP, ui.px(52)), 8, 0, 0, 0));
        panel.addView(rename, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 8));
        for (int i = 0; i < settings.pages.size(); i++) {
            final int index = i;
            LinearLayout row = ui.row();
            row.setGravity(Gravity.CENTER_VERTICAL);
            TextView open = ui.button(settings.pages.get(i), new View.OnClickListener() { @Override public void onClick(View v) {
                if (index != settings.page) pageAction(Json.map("action", "page.show", "index", index, "page", settings.pages.get(index)));
            } });
            open.setGravity(Gravity.CENTER_VERTICAL | Gravity.START);
            open.setSingleLine(true); open.setEllipsize(android.text.TextUtils.TruncateAt.END);
            ui.select(open, i == settings.page);
            open.setContentDescription(settings.pages.get(i) + (i == settings.page ? ", shown" : ", open this page"));
            row.addView(open, ui.ll(0, ui.px(52), 1));
            View up = ui.iconButton("chevron-up", "Move " + settings.pages.get(i) + " up", 20, false, new View.OnClickListener() { @Override public void onClick(View v) {
                pageAction(Json.map("action", "page.move", "index", index, "page", settings.pages.get(index), "to", index - 1));
            } });
            Ui.enable(up, i > 0);
            row.addView(up, ui.margins(ui.ll(ui.px(48), ui.px(48)), 4, 0, 0, 0));
            View down = ui.iconButton("chevron-down", "Move " + settings.pages.get(i) + " down", 20, false, new View.OnClickListener() { @Override public void onClick(View v) {
                pageAction(Json.map("action", "page.move", "index", index, "page", settings.pages.get(index), "to", index + 1));
            } });
            Ui.enable(down, i < settings.pages.size() - 1);
            row.addView(down, ui.ll(ui.px(48), ui.px(48)));
            View delete = ui.iconButton("close", "Delete " + settings.pages.get(i), 18, false, new View.OnClickListener() { @Override public void onClick(View v) {
                confirmDelete(index);
            } });
            Ui.enable(delete, settings.pages.size() > 1);
            row.addView(delete, ui.ll(ui.px(48), ui.px(48)));
            panel.addView(row, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 6));
        }
        TextView add = ui.button("Add page", new View.OnClickListener() { @Override public void onClick(View v) { pageAction(Json.map("action", "page.add")); } });
        Ui.enable(add, settings.pages.size() < 8);
        panel.addView(add, ui.margins(ui.ll(Ui.WRAP, ui.px(52)), 0, 4, 0, 0));
    }

    private View.OnClickListener nudge(final int dx, final int dy, final boolean resize) {
        return new View.OnClickListener() { @Override public void onClick(View v) { editor.nudgeSelected(dx, dy, resize); } };
    }

    // Deleting a page asks first: a second tap within a few seconds confirms.
    private int deleteArmed = -1, deleteArmedAt;
    private void confirmDelete(int index) {
        if (deleteArmed == index && ticks - deleteArmedAt <= 5) { deleteArmed = -1; pageAction(Json.map("action", "page.delete", "index", index, "page", settings.pages.get(index))); return; }
        deleteArmed = index; deleteArmedAt = ticks;
        say("Tap delete again to delete " + settings.pages.get(index) + " and its layouts.", true);
    }

    // Page changes happen on the PC: unsaved layout changes are saved first, and the editor opens again on the page
    // the PC shows next.
    private void pageAction(Map<String, Object> body) {
        closeWidgetsPanel();
        if (editor.canUndo()) saveEdit(); else { editor.cancel(); updatePages(); }
        editAfterMirror = true;
        layoutCommand(body, null);
    }

    // ----- Settings -----

    public void openSettings(String tab) {
        if (editor.editing()) return;
        if (settingsPanel != null) { settingsPanel.selectTab(tab); return; }
        settingsPanel = new SettingsPanel(this, tab);
        root.addView(settingsPanel.view(), new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
    }

    public void imeInset(int bottom) { if (settingsPanel != null) settingsPanel.view().setPadding(0, 0, 0, bottom); }

    public void settingsClosed() {
        if (settingsPanel != null) root.removeView(settingsPanel.view());
        settingsPanel = null;
        if (appearance != null) { appearance = null; rebuild(); }
    }

    // Saves the Settings draft: everything except Spotify account actions, which apply right away.
    // The layout can't be edited while Settings is open, so the draft's layout only differs by the widgets
    // shown or hidden under Settings → Widgets.
    public void applySettings(AppSettings draft) {
        settings = draft;
        persist();
        editor.apply(settings);
        appearance = null;
        rebuild();
    }

    // Opening the editor from Settings closes Settings without saving its other changes.
    public void editLayoutFromSettings() {
        if (settingsPanel != null) { root.removeView(settingsPanel.view()); settingsPanel = null; }
        if (appearance != null) { appearance = null; rebuild(); }
        beginEdit();
    }

    // The PC was paired, forgotten or changed in Settings → PC: start over with fresh readings.
    public void pcChanged() {
        offlineShown = false; setStatus("");
        for (Widget widget : widgets.values()) if (widget.built()) widget.stateChanged();
        if (pc.paired()) poll(true);
    }

    // First launch, or the PC forgot this phone: open Settings → PC once so pairing is the obvious next step.
    public void askToPairIfNeeded() {
        if (askedToPair || settingsPanel != null || editor.editing()) return;
        if (!pc.paired()) { askedToPair = true; openSettings("PC"); }
    }
}
