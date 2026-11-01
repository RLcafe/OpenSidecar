package com.alialyami.widgetscreen;

import android.text.InputType;
import android.util.DisplayMetrics;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.inputmethod.EditorInfo;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.alialyami.widgetscreen.core.AppSettings;
import com.alialyami.widgetscreen.core.PcLink;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.core.WidgetLayout;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Pc;
import com.alialyami.widgetscreen.views.CheckBox;
import com.alialyami.widgetscreen.views.FlowLayout;
import com.alialyami.widgetscreen.views.TouchSlider;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

// Settings as an overlay on the dashboard, like the Windows panel (1440 × 672 at most, full screen on
// phones). Tabs: Theme, Display and Widgets for this phone, and PC for pairing. Changes are a draft: appearance
// previews live, Save keeps everything, Cancel restores. Pairing applies right away. Everything the widgets do
// (Quick Launch buttons, weather city, Spotify, Discord) is set up in Settings on the PC.
public final class SettingsPanel {
    public static final String[] TABS = { "Theme", "Display", "Widgets", "PC" };

    private final Dashboard dash;
    private final FrameLayout overlay;
    private final AppSettings draft;
    // Widgets shown or hidden under Widgets, for the layout on screen now (each canvas shape has its own).
    private WidgetLayout.Placement[] shown;
    private Ui ui;
    private String tab;
    private boolean wide, finding, pairing;
    private TextView status;
    private LinearLayout content;
    private ScrollView scroll;
    private String statusText = "", pcAddress = "", pcCode = "", pcMessage;
    private List<PcLink.Found> found;

    public SettingsPanel(Dashboard dash, String tab) {
        this.dash = dash;
        draft = dash.settings.copy();
        this.tab = tab == null ? "Theme" : tab;
        overlay = new FrameLayout(dash.activity);
        overlay.setClickable(true);
        build();
    }

    public View view() { return overlay; }

    // Rebuilds the whole panel from the draft. Within a tab the scroll position is kept, so choosing an
    // icon or an app doesn't jump back to the top.
    private void build() {
        final int keepScroll = scroll == null ? 0 : scroll.getScrollY();
        DisplayMetrics metrics = dash.activity.getResources().getDisplayMetrics();
        int width = Math.max(1, dash.activity.getWindow().getDecorView().getWidth()), height = Math.max(1, dash.activity.getWindow().getDecorView().getHeight());
        if (width <= 1) { width = metrics.widthPixels; height = metrics.heightPixels; }
        float widthDp = width / metrics.density, heightDp = height / metrics.density;
        float unit = metrics.density * Math.max(0.92f, Math.min(1f, Math.min(widthDp / 1000f, heightDp / 640f)));
        ui = new Ui(dash.activity, Theme.of(draft.theme, draft.accent), draft, unit);
        overlay.removeAllViews();
        overlay.setBackgroundColor(Ui.alpha(ui.theme.page, 0.91f));

        int panelWidth = Math.min(width, ui.px(1440)), panelHeight = Math.min(height, ui.px(672));
        boolean full = panelWidth >= width - ui.px(48) || panelHeight >= height - ui.px(48);
        wide = panelWidth >= ui.px(980);
        LinearLayout panel = ui.column();
        panel.setBackground(full ? ui.shape(ui.theme.surface, 0, 0, 0) : ui.shape(ui.theme.surface, ui.theme.controlBorder, 1, ui.radius));
        panel.setPadding(ui.px(28), ui.px(full ? 20 : 28), ui.px(28), ui.px(full ? 12 : 20));
        panel.setClickable(true);

        LinearLayout header = ui.row();
        TextView title = ui.text("Settings", 28, ui.theme.text);
        title.setTypeface(Ui.MEDIUM);
        header.addView(title, ui.ll(0, Ui.WRAP, 1));
        header.addView(ui.iconButton("close", "Cancel settings", 22, true, new View.OnClickListener() { @Override public void onClick(View v) { cancel(); } }), ui.ll(ui.px(52), ui.px(52)));
        panel.addView(header, ui.ll(Ui.MATCH, Ui.WRAP));

        HorizontalScrollView tabScroll = new HorizontalScrollView(dash.activity);
        tabScroll.setHorizontalScrollBarEnabled(false);
        LinearLayout tabs = ui.row();
        for (final String name : TABS) {
            TextView button = ui.text(name, 18, name.equals(tab) ? ui.theme.text : ui.theme.muted);
            button.setGravity(Gravity.CENTER);
            button.setPadding(ui.px(24), ui.px(14), ui.px(24), ui.px(14));
            button.setMinHeight(ui.px(52));
            button.setBackground(name.equals(tab) ? ui.pressable(ui.theme.raised, ui.theme.accent, 1, 10) : ui.pressable(0x00000000, 0, 0, 10));
            button.setClickable(true);
            button.setContentDescription(name + (name.equals(tab) ? ", selected" : ""));
            button.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { selectTab(name); } });
            tabs.addView(button, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 8, 0));
        }
        tabScroll.addView(tabs);
        panel.addView(tabScroll, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 12, 0, 8));

        scroll = new ScrollView(dash.activity);
        scroll.setFillViewport(false);
        content = ui.column();
        content.setPadding(0, ui.px(12), 0, ui.px(12));
        scroll.addView(content, new FrameLayout.LayoutParams(Ui.MATCH, Ui.WRAP));
        panel.addView(scroll, ui.ll(Ui.MATCH, 0, 1));

        LinearLayout footer = ui.row();
        status = Ui.wrap(ui.smallText(statusText), 2);
        footer.addView(status, ui.margins(ui.ll(0, Ui.WRAP, 1), 0, 0, 12, 0));
        footer.addView(ui.button("Cancel", new View.OnClickListener() { @Override public void onClick(View v) { cancel(); } }), ui.margins(ui.ll(Ui.WRAP, ui.px(52)), 0, 0, 10, 0));
        footer.addView(ui.accentButton("Save changes", new View.OnClickListener() { @Override public void onClick(View v) { save(); } }), ui.ll(Ui.WRAP, ui.px(52)));
        panel.addView(footer, ui.margins(ui.ll(Ui.MATCH, ui.px(64)), 0, 4, 0, 0));

        overlay.addView(panel, new FrameLayout.LayoutParams(full ? Ui.MATCH : panelWidth, full ? Ui.MATCH : panelHeight, Gravity.CENTER));
        buildTab();
        if (keepScroll > 0) scroll.post(new Runnable() { @Override public void run() { scroll.scrollTo(0, keepScroll); } });
    }

    public void selectTab(String name) {
        if (name == null || name.equals(tab)) return;
        tab = name;
        scroll = null;
        build();
    }

    private void setStatus(String text) { statusText = text == null ? "" : text; status.setText(statusText); }

    // ----- shared pieces -----

    private TextView heading(String text) { TextView view = ui.text(text, 24, ui.theme.text); return view; }
    private TextView note(String text) { return Ui.wrap(ui.smallText(text), 6); }

    private void add(LinearLayout column, View view, double top) { column.addView(view, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, top, 0, 0)); }

    private LinearLayout[] columns() {
        LinearLayout left = ui.column(), right = ui.column();
        if (wide) {
            LinearLayout row = ui.row();
            row.setGravity(Gravity.TOP);
            row.addView(left, ui.ll(ui.px(570), Ui.WRAP));
            row.addView(new View(dash.activity), ui.ll(ui.px(32), 1));
            row.addView(right, ui.ll(0, Ui.WRAP, 1));
            content.addView(row, ui.ll(Ui.MATCH, Ui.WRAP));
        } else {
            content.addView(left, ui.ll(Ui.MATCH, Ui.WRAP));
            content.addView(right, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 28, 0, 0));
        }
        return new LinearLayout[] { left, right };
    }

    private interface Choice { void chose(String value); }

    // Segmented choice buttons with the shared selected look (instead of Windows dropdowns).
    private FlowLayout choices(String[] values, String[] labels, String selected, final Choice choice) {
        FlowLayout flow = new FlowLayout(dash.activity, ui.px(8));
        for (int i = 0; i < values.length; i++) {
            final String value = values[i];
            TextView button = ui.button(labels == null ? value : labels[i], new View.OnClickListener() { @Override public void onClick(View v) { choice.chose(value); } });
            button.setMinHeight(ui.px(52));
            ui.select(button, value.equals(selected));
            button.setContentDescription((labels == null ? value : labels[i]) + (value.equals(selected) ? ", selected" : ""));
            flow.addView(button);
        }
        return flow;
    }

    private View valueRow(String label, String value) {
        FrameLayout row = new FrameLayout(dash.activity);
        row.addView(ui.smallText(label), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START));
        row.addView(ui.smallText(value), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END));
        return row;
    }

    private void buildTab() {
        content.removeAllViews();
        switch (tab) {
            case "Display": buildDisplay(); break;
            case "Widgets": buildWidgets(); break;
            case "PC": buildPc(); break;
            default: buildTheme(); break;
        }
    }

    // ----- Theme -----

    // The dashboard rebuilds with the draft's look, then this panel rebuilds too (dashboardRebuilt).
    private void previewAppearance() { dash.preview(draft); }

    private void buildTheme() {
        LinearLayout[] c = columns();
        LinearLayout left = c[0], right = c[1];
        add(left, heading("Appearance"), 0);
        add(left, note("Changes preview on the dashboard. Save to keep them."), 10);
        add(left, ui.smallText("Theme"), 16);
        add(left, choices(Theme.THEMES, null, draft.theme, new Choice() { @Override public void chose(String value) { draft.theme = value; previewAppearance(); } }), 6);
        add(left, ui.smallText("Accent color"), 14);
        add(left, choices(Theme.ACCENTS, null, draft.accent, new Choice() { @Override public void chose(String value) { draft.accent = value; previewAppearance(); } }), 6);
        final View cornersRow = valueRow("Widget corners", Math.round(draft.cornerRadius) + "");
        add(left, cornersRow, 18);
        TouchSlider corners = new TouchSlider(dash.activity, ui).range(0, 24, 4);
        corners.setValue((float) draft.cornerRadius);
        corners.setContentDescription("Widget corners");
        corners.setListener(new TouchSlider.Listener() {
            @Override public void changed(TouchSlider s, float value, boolean done) {
                draft.cornerRadius = value;
                ((TextView) ((FrameLayout) cornersRow).getChildAt(1)).setText(Math.round(value) + "");
                if (done) previewAppearance();
            }
        });
        add(left, corners, 4);

        add(right, heading("Readability"), 0);
        add(right, note("Size text for your screen and how far you sit from it."), 10);
        final View scaleRow = valueRow("Text size", Math.round(draft.textScale * 100) + "%");
        add(right, scaleRow, 18);
        TouchSlider scale = new TouchSlider(dash.activity, ui).range(90, 140, 5);
        scale.setValue((float) (draft.textScale * 100));
        scale.setContentDescription("Text size");
        scale.setListener(new TouchSlider.Listener() {
            @Override public void changed(TouchSlider s, float value, boolean done) {
                draft.textScale = value / 100.0;
                ((TextView) ((FrameLayout) scaleRow).getChildAt(1)).setText(Math.round(value) + "%");
                if (done) previewAppearance();
            }
        });
        add(right, scale, 4);
        add(right, note("Above 115% some cards show less at once. The 24-hour clock follows the PC's setting."), 12);
    }

    // ----- Display -----

    private void buildDisplay() {
        LinearLayout[] c = columns();
        LinearLayout left = c[0], right = c[1];
        add(left, heading("Display"), 0);
        add(left, note("How the dashboard uses this screen."), 10);
        add(left, ui.smallText("Keep the screen on"), 12);
        add(left, choices(AppSettings.KEEP_SCREEN_MODES, new String[] { "Always", "While charging", "Never" }, draft.keepScreenOn, new Choice() { @Override public void chose(String value) { draft.keepScreenOn = value; build(); } }), 6);
        add(left, note("While charging suits a phone on a stand: the screen stays on when it's plugged in and goes to sleep as usual in your hand."), 4);
        add(left, ui.check("Hide the status and navigation bars", draft.hideSystemBars, new CheckBox.Listener() { @Override public void changed(CheckBox box, boolean checked) { draft.hideSystemBars = checked; } }), 4);
        add(left, note("Swipe in from the edge to show them for a moment."), 4);
        add(left, ui.check("Keep the app sideways", draft.landscapeOnly, new CheckBox.Listener() { @Override public void changed(CheckBox box, boolean checked) { draft.landscapeOnly = checked; } }), 12);
        add(left, note("Sideways, the phone shows the dashboard like the PC does, shaped to fit this screen."), 4);

        add(right, heading("Layout"), 0);
        add(right, choices(AppSettings.LAYOUT_MODES, new String[] { "Automatic", "Dashboard", "List" }, draft.layoutMode, new Choice() { @Override public void chose(String value) { draft.layoutMode = value; build(); } }), 12);
        add(right, note("Automatic shows the dashboard when the screen is sideways or on an upright tablet, and a scrolling list on an upright phone. Dashboard always uses the dashboard; List always stacks the widgets. Each screen shape keeps its own layout."), 10);
    }

    // ----- Widgets -----

    private void buildWidgets() {
        LinearLayout[] c = columns();
        LinearLayout left = c[0], right = c[1];
        add(left, heading("Layout"), 0);
        add(left, note("Arrange widgets right on the dashboard: drag to move, stretch a corner to resize, and show or hide each one. Undo, snapping to other widgets, Arrange and Reset are in the editor."), 10);
        TextView edit = ui.button("Edit layout", new View.OnClickListener() { @Override public void onClick(View v) { dash.editLayoutFromSettings(); } });
        Ui.enable(edit, !dash.listMode());
        left.addView(edit, ui.margins(ui.ll(Ui.WRAP, ui.px(52)), 0, 14, 0, 0));
        add(left, note(dash.listMode() ? "The editor needs the dashboard layout. Turn the device sideways, or choose Dashboard under Display." : "Opening the editor closes Settings without saving other changes here."), 8);
        add(left, ui.smallText("Show on the dashboard"), 20);
        if (shown == null) shown = dash.editor.export();
        for (final String id : WidgetLayout.IDS) {
            add(left, ui.check(WidgetLayout.title(id), WidgetLayout.find(shown, id).visible, new CheckBox.Listener() {
                @Override public void changed(CheckBox box, boolean checked) { WidgetLayout.find(shown, id).visible = checked; }
            }), 0);
        }

        add(right, heading("On the PC"), 0);
        add(right, note("The cards show and control your PC. Quick Launch buttons, System stats readings, the weather city, the world clocks' cities, prayer times, calendars, macros, the games' stores, Spotify and Discord are set up in Settings on the PC, and the phone follows."), 10);
    }

    // ----- PC -----

    public void pcChanged() { if (tab.equals("PC")) refreshPcStatus(); }

    private TextView pcStatus;

    private String pcStatusText() {
        Pc pc = dash.pc;
        if (pcMessage != null) return pcMessage;
        if (!pc.paired()) return "Not paired yet. Follow the steps to connect this phone to your PC.";
        if (pc.online()) return "Connected to " + pc.name() + " (" + pc.address() + ").";
        return dash.offlineText();
    }

    private void refreshPcStatus() { if (pcStatus != null) pcStatus.setText(pcStatusText()); }

    private void buildPc() {
        final Pc pc = dash.pc;
        LinearLayout[] c = columns();
        LinearLayout left = c[0], right = c[1];
        add(left, heading("Your PC"), 0);
        pcStatus = Ui.wrap(ui.body(pcStatusText()), 4);
        add(left, pcStatus, 10);
        if (pc.paired()) {
            TextView forget = ui.button("Forget this PC", new View.OnClickListener() {
                @Override public void onClick(View v) {
                    try { pc.forget(); pcMessage = "Forgotten. Pair again to control a PC."; dash.pcChanged(); }
                    catch (Exception e) { pcMessage = "Couldn't forget the PC. Try again."; }
                    build();
                }
            });
            left.addView(forget, ui.margins(ui.ll(Ui.WRAP, ui.px(52)), 0, 10, 0, 0));
        }
        add(left, ui.smallText(pc.paired() ? "Pair again, or with another PC" : "Pair with your PC"), 22);
        TextView find = ui.button(finding ? "Looking…" : "Find PCs on this Wi-Fi", new View.OnClickListener() { @Override public void onClick(View v) { findPcs(); } });
        Ui.enable(find, !finding);
        left.addView(find, ui.margins(ui.ll(Ui.WRAP, ui.px(52)), 0, 8, 0, 0));
        if (found != null) {
            if (found.isEmpty()) add(left, note("No PC answered. Check that Widget Screen is open on the PC with phone control on, or type its address below."), 8);
            for (final PcLink.Found pcFound : found) {
                TextView option = ui.button(pcFound.name + "  ·  " + pcFound.host, new View.OnClickListener() {
                    @Override public void onClick(View v) { pcAddress = pcFound.host + (pcFound.port == PcLink.PORT ? "" : ":" + pcFound.port); build(); }
                });
                option.setGravity(Gravity.CENTER_VERTICAL | Gravity.START);
                ui.select(option, pcAddress.equals(pcFound.host) || pcAddress.equals(pcFound.host + ":" + pcFound.port));
                add(left, option, 6);
            }
        }
        add(left, ui.smallText("PC address"), 16);
        final EditText address = ui.input(pcAddress.isEmpty() ? pc.address() : pcAddress, "For example 192.168.1.20");
        address.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        address.setContentDescription("PC address");
        add(left, address, 6);
        add(left, ui.smallText("Pairing code from the PC"), 14);
        final EditText code = ui.input(pcCode, "6 digits");
        code.setInputType(InputType.TYPE_CLASS_NUMBER);
        code.setContentDescription("Pairing code");
        add(left, code, 6);
        TextView pair = ui.accentButton(pairing ? "Pairing…" : "Pair", new View.OnClickListener() {
            @Override public void onClick(View v) { pair(address.getText().toString(), code.getText().toString()); }
        });
        Ui.enable(pair, !pairing);
        left.addView(pair, ui.margins(ui.ll(Ui.WRAP, ui.px(52)), 0, 12, 0, 0));

        add(right, heading("How to pair"), 0);
        add(right, note("1.  On the PC, open Widget Screen, then Settings → Phone, and turn on Let my phone control this PC."), 10);
        add(right, note("2.  Tap Pair a phone on the PC. A six-digit code appears, with the PC's address."), 8);
        add(right, note("3.  Here, tap Find PCs on this Wi-Fi and pick your PC, or type its address. Enter the code and tap Pair."), 8);
        add(right, note("The phone and PC must be on the same Wi-Fi. If the phone can't find or reach the PC, allow Widget Screen on private networks when Windows asks, or in Windows Firewall."), 14);
        add(right, note("Pairing applies right away; you don't need to save."), 8);
    }

    private void findPcs() {
        finding = true; build();
        dash.pc.discover(new com.alialyami.widgetscreen.integrations.Async.Done<List<PcLink.Found>>() {
            @Override public void ok(List<PcLink.Found> result) {
                finding = false; found = result;
                if (result.size() == 1) pcAddress = result.get(0).host + (result.get(0).port == PcLink.PORT ? "" : ":" + result.get(0).port);
                if (tab.equals("PC")) build();
            }
        });
    }

    private void pair(String addressText, String codeText) {
        pcAddress = addressText.trim(); pcCode = codeText.trim();
        String[] parsed = PcLink.parseAddress(pcAddress);
        if (parsed == null) { pcMessage = "Type the PC's address as shown in Settings → Phone on the PC, for example 192.168.1.20."; build(); return; }
        if (!pcCode.replace(" ", "").matches("[0-9]{6}")) { pcMessage = "Enter the six-digit code shown on the PC."; build(); return; }
        pairing = true; pcMessage = "Pairing with the PC…"; build();
        dash.pc.pair(parsed[0], Integer.parseInt(parsed[1]), pcCode, Dashboard.deviceName(), new com.alialyami.widgetscreen.integrations.Async.Done<String>() {
            @Override public void ok(String name) { pairing = false; pcCode = ""; pcMessage = "Paired with " + name + ". The dashboard now shows and controls it."; dash.pcChanged(); build(); }
        }, new com.alialyami.widgetscreen.integrations.Async.Failed() {
            @Override public void error(Exception error) { pairing = false; pcMessage = Pc.message(error); build(); }
        });
    }

    // ----- Save and cancel -----

    private void save() {
        dash.applySettings(draft);
        // The widgets shown belong to the PC's page: a change is saved there, for every screen.
        if (shown != null) {
            WidgetLayout.Placement[] before = dash.editor.export();
            for (WidgetLayout.Placement p : shown)
                if (WidgetLayout.find(before, p.id).visible != p.visible) { dash.saveLayout(shown, dash.editor.canvasWidth(), dash.editor.canvasHeight(), "Widgets saved on the PC."); break; }
        }
        dash.settingsClosed();
    }

    public void cancel() { dash.settingsClosed(); }

    // The dashboard behind was rebuilt (rotation, appearance preview): rebuild the panel for the new size
    // and look, keeping the draft and the open tab.
    public void dashboardRebuilt() { build(); overlay.bringToFront(); }
}
