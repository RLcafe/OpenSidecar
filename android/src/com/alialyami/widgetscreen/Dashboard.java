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
import com.alialyami.widgetscreen.widgets.FocusWidget;
import com.alialyami.widgetscreen.widgets.MediaWidget;
import com.alialyami.widgetscreen.widgets.QuickLaunchWidget;
import com.alialyami.widgetscreen.widgets.MixerWidget;
import com.alialyami.widgetscreen.widgets.SoundboardWidget;
import com.alialyami.widgetscreen.widgets.SpotifyWidget;
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
// Wide screens get the 1920 × 720 dashboard scaled to fit (its widget canvas stretched to the screen); phones get
// the same cards in a scrolling list.
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
    private float scale = 1;
    private boolean listMode, started, refreshNow, offlineShown, askedToPair;
    private int builtWidth = -1, builtHeight = -1;
    private TextView clockTime, clockPeriod, clockDay, clockDate, statusLine, displayStatus, quietButton, undoButton, redoButton;
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
    public String offlineText() {
        if (!pc.paired()) return "Pair this phone with your PC in Settings → PC.";
        if (pc.unpaired) return "The PC no longer knows this phone. Pair it again in Settings → PC.";
        if (pc.problem != null) return "Can't reach " + pc.name() + ".";
        return "Connecting to " + pc.name() + "…";
    }

    // Asks the PC for the cards on screen; hidden cards aren't read on the PC either.
    private void poll() {
        List<String> parts = new ArrayList<String>();
        for (Widget widget : widgets.values()) if (isActive(widget.id) && !parts.contains(widget.part())) parts.add(widget.part());
        pc.poll(parts, new Runnable() { @Override public void run() { stateChanged(); } });
    }

    public void refreshSoon() { refreshNow = true; Async.MAIN.postDelayed(new Runnable() { @Override public void run() { if (started && refreshNow) { refreshNow = false; poll(); } } }, 250); }

    private void stateChanged() {
        if (pc.problem != null) { say(pc.problem, true); offlineShown = true; }
        else if (offlineShown) { offlineShown = false; say("Connected to " + pc.name() + "."); }
        updateClock(); updateQuiet();
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
        if (pc.paired()) poll();
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
        if (pc.paired() && ticks % (quiet() ? 5 : 2) == 0) poll();
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
        float density = metrics.density, fit = Math.min(width / 1920f, height / 720f);
        listMode = settings.layoutMode.equals("List") || (settings.layoutMode.equals("Auto") && !(width >= height && fit / density >= 0.6f));
        if (listMode && editor.editing()) editor.cancel();
        if (dashboardView != null) root.removeView(dashboardView);
        root.setBackgroundColor(theme.page);
        widgetsPanel = null;
        if (!listMode) {
            scale = fit;
            int logicalWidth = (int) Math.floor(width / scale), logicalHeight = (int) Math.floor(height / scale);
            dashboardView = new ScaledHost(activity, buildDashboard(logicalWidth, logicalHeight), scale, logicalWidth, logicalHeight, false);
        } else {
            scale = density * Math.max(0.85f, Math.min(1f, width / density / 560f));
            int logicalWidth = (int) Math.floor(width / scale);
            ScrollView scroll = new ScrollView(activity);
            scroll.setFillViewport(true);
            scroll.setVerticalScrollBarEnabled(false);
            scroll.addView(new ScaledHost(activity, buildList(logicalWidth), scale, logicalWidth, 0, true));
            dashboardView = scroll;
        }
        root.addView(dashboardView, 0, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        updateClock();
        updateQuiet();
        updateEditorChrome();
        if (settingsPanel != null) settingsPanel.dashboardRebuilt();
        else askToPairIfNeeded();
        // New cards ask the PC for their readings right away instead of waiting for the next beat.
        if (pc.paired() && started) refreshSoon();
    }

    private View buildHeader(boolean stacked) {
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

        if (stacked) {
            LinearLayout header = ui.column();
            header.addView(clock, ui.ll(Ui.WRAP, Ui.WRAP));
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
        header.addView(actions, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.TOP));
        editToolbar = buildEditToolbar();
        FrameLayout.LayoutParams toolbarParams = ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.TOP);
        header.addView(editToolbar, toolbarParams);
        return header;
    }

    private View buildEditToolbar() {
        LinearLayout toolbar = ui.row();
        LinearLayout title = ui.column();
        TextView heading = ui.text("Edit layout", 25, ui.theme.text);
        heading.setTypeface(Ui.MEDIUM);
        title.addView(heading, ui.ll(Ui.WRAP, Ui.WRAP));
        title.addView(ui.smallText("Drag to move or resize"), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 2, 0, 0));
        toolbar.addView(title, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 32, 0));
        undoButton = ui.button("Undo", new View.OnClickListener() { @Override public void onClick(View v) { editor.undo(); } });
        toolbar.addView(undoButton, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 8, 0));
        redoButton = ui.button("Redo", new View.OnClickListener() { @Override public void onClick(View v) { editor.redo(); } });
        toolbar.addView(redoButton, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 8, 0));
        toolbar.addView(ui.button("Reset layout", new View.OnClickListener() { @Override public void onClick(View v) { editor.reset(); } }), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 18, 0));
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
        column.addView(buildHeader(false), ui.ll(Ui.MATCH, ui.px(72)));
        canvas = new WidgetCanvas(activity, ui, editor);
        double canvasWidth = logicalWidth - 64, canvasHeight = logicalHeight - 20 - 16 - 72 - 36;
        canvas.setFactors(canvasWidth / WidgetLayout.WIDTH, canvasHeight / WidgetLayout.HEIGHT, scale);
        for (String id : WidgetLayout.IDS) if (editor.isVisible(id)) attachCard(id);
        column.addView(canvas, ui.ll(Ui.MATCH, 0, 1));
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
        canvas.setCard(id, widgets.get(id).build(ui, box.width * canvas.fx(), box.height * canvas.fy()));
    }

    private static double listHeight(String id) {
        switch (id) {
            case "Focus": return 400;
            case "Media": return 330;
            case "Shortcuts": return 290;
            case "Mixer": return 560;
            case "System": return 330;
            case "Weather": return 270;
            default: return 640;
        }
    }

    // Phones: the same cards stacked in reading order (left to right, then top to bottom on the canvas).
    private View buildList(int logicalWidth) {
        logicalRoot = new FrameLayout(activity);
        canvas = null;
        LinearLayout column = ui.column();
        column.setPadding(ui.px(LIST_GAP), ui.px(20), ui.px(LIST_GAP), ui.px(LIST_GAP));
        column.addView(buildHeader(true), ui.ll(Ui.MATCH, Ui.WRAP));
        statusLine = Ui.wrap(ui.smallText(statusText), 3);
        column.addView(statusLine, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 8, 0, 0));
        displayStatus = null;
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

    private void updateClock() {
        if (clockTime == null) return;
        LocalDateTime now = LocalDateTime.now();
        boolean use24 = pc.state != null && pc.state.use24;
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
        editor.begin();
    }

    private void cancelEdit() { closeWidgetsPanel(); editor.cancel(); say("Layout changes discarded."); }

    private void saveEdit() {
        closeWidgetsPanel();
        settings.widgetLayout = editor.export();
        settings.snapWidgets = editor.snap();
        editor.accept();
        persist();
        say(editor.hasOverlaps() ? "Layout saved. Some widgets overlap." : "Layout saved.");
    }

    public void onBack(Runnable fallback) {
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
                if (visible && !(canvas.hasCard(id) && widgets.get(id).builtAt(box.width * canvas.fx(), box.height * canvas.fy()))) attachCard(id);
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

    // Show or hide each widget while editing: the Windows editor's widget list and "Show widget" box.
    private void openWidgetsPanel() {
        if (logicalRoot == null) return;
        FrameLayout scrim = new FrameLayout(activity);
        scrim.setClickable(true);
        scrim.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { closeWidgetsPanel(); } });
        LinearLayout panel = ui.column();
        panel.setBackground(ui.shape(ui.theme.raised, ui.theme.controlBorder, 1, 16));
        panel.setPadding(ui.px(16), ui.px(12), ui.px(16), ui.px(12));
        panel.setClickable(true);
        panel.setElevation(0);
        panel.setTag("panel");
        FrameLayout.LayoutParams params = ui.frame(ui.px(380), Ui.WRAP, Gravity.END | Gravity.TOP);
        params.topMargin = ui.px(20 + 72); params.rightMargin = ui.px(32 + 300);
        scrim.addView(panel, params);
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
            panel.addView(box, ui.ll(Ui.MATCH, Ui.WRAP));
        }
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
        if (pc.paired()) poll();
    }

    // First launch, or the PC forgot this phone: open Settings → PC once so pairing is the obvious next step.
    public void askToPairIfNeeded() {
        if (askedToPair || settingsPanel != null || editor.editing()) return;
        if (!pc.paired()) { askedToPair = true; openSettings("PC"); }
    }
}
