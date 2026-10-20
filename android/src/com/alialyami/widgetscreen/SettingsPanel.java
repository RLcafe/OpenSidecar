package com.alialyami.widgetscreen;

import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.provider.Settings;
import android.text.Editable;
import android.text.TextWatcher;
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
import com.alialyami.widgetscreen.core.Deck;
import com.alialyami.widgetscreen.core.Spotify;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.core.Weather;
import com.alialyami.widgetscreen.core.WidgetLayout;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Http;
import com.alialyami.widgetscreen.integrations.Launcher;
import com.alialyami.widgetscreen.integrations.SpotifyService;
import com.alialyami.widgetscreen.views.CheckBox;
import com.alialyami.widgetscreen.views.FlowLayout;
import com.alialyami.widgetscreen.views.GlyphView;
import com.alialyami.widgetscreen.views.TouchSlider;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

// Settings as an overlay on the dashboard, like the Windows panel (1440 × 672 at most, full screen on
// phones). Tabs: Theme, Display, Widgets, Weather, Spotify, Quick Launch. Changes are a draft: appearance
// previews live, Save keeps everything, Cancel restores. Spotify account actions apply right away.
public final class SettingsPanel {
    public static final String[] TABS = { "Theme", "Display", "Widgets", "Weather", "Spotify", "Quick Launch" };

    private final Dashboard dash;
    private final FrameLayout overlay;
    private final AppSettings draft;
    private Ui ui;
    private String tab;
    private boolean wide, locationChanged;
    private TextView status, spotifyStatus;
    private LinearLayout content;
    private ScrollView scroll;
    private String spotifyMessage, weatherQuery = "", appFilter = "", statusText = "";
    private List<Weather.Location> weatherResults;
    private boolean weatherBusy;
    private List<Launcher.App> apps;
    private int selectedShortcut;
    private EditText clientId;

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
            case "Weather": buildWeather(); break;
            case "Spotify": buildSpotify(); break;
            case "Quick Launch": buildQuickLaunch(); break;
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
        TouchSlider scale = new TouchSlider(dash.activity, ui).range(90, 115, 5);
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
        add(right, ui.check("Use a 24-hour clock", draft.use24HourClock, new CheckBox.Listener() { @Override public void changed(CheckBox box, boolean checked) { draft.use24HourClock = checked; } }), 8);
    }

    // ----- Display -----

    private void buildDisplay() {
        LinearLayout[] c = columns();
        LinearLayout left = c[0], right = c[1];
        add(left, heading("Display"), 0);
        add(left, note("How the dashboard uses this screen."), 10);
        add(left, ui.check("Keep the screen on while Widget Screen is open", draft.keepScreenOn, new CheckBox.Listener() { @Override public void changed(CheckBox box, boolean checked) { draft.keepScreenOn = checked; } }), 12);
        add(left, ui.check("Hide the status and navigation bars", draft.hideSystemBars, new CheckBox.Listener() { @Override public void changed(CheckBox box, boolean checked) { draft.hideSystemBars = checked; } }), 4);
        add(left, note("Swipe in from the edge to show them for a moment."), 4);

        add(right, heading("Layout"), 0);
        add(right, choices(AppSettings.LAYOUT_MODES, new String[] { "Automatic", "Dashboard", "List" }, draft.layoutMode, new Choice() { @Override public void chose(String value) { draft.layoutMode = value; build(); } }), 12);
        add(right, note("Automatic shows the full dashboard on wide screens, such as a tablet held sideways, and a scrolling list on phones. Dashboard keeps the 1920 × 720 arrangement on any screen; List stacks the widgets."), 10);
    }

    // ----- Widgets -----

    private void buildWidgets() {
        LinearLayout[] c = columns();
        LinearLayout left = c[0], right = c[1];
        add(left, heading("Layout"), 0);
        add(left, note("Arrange widgets right on the dashboard: drag to move, stretch a corner to resize, and show or hide each one. Undo, snapping and Reset are in the editor."), 10);
        TextView edit = ui.button("Edit layout", new View.OnClickListener() { @Override public void onClick(View v) { dash.editLayoutFromSettings(); } });
        Ui.enable(edit, !dash.listMode());
        left.addView(edit, ui.margins(ui.ll(Ui.WRAP, ui.px(52)), 0, 14, 0, 0));
        add(left, note(dash.listMode() ? "The editor needs the dashboard layout. Turn the device sideways, or choose Dashboard under Display." : "Opening the editor closes Settings without saving other changes here."), 8);
        add(left, ui.smallText("Show on the dashboard"), 20);
        if (draft.widgetLayout == null) draft.widgetLayout = WidgetLayout.normalize(dash.settings.layout());
        for (final String id : WidgetLayout.IDS) {
            add(left, ui.check(WidgetLayout.title(id), WidgetLayout.find(draft.widgetLayout, id).visible, new CheckBox.Listener() {
                @Override public void changed(CheckBox box, boolean checked) { WidgetLayout.find(draft.widgetLayout, id).visible = checked; }
            }), 0);
        }

        add(right, heading("System stats"), 0);
        add(right, note("Choose the readings the System stats widget shows."), 10);
        add(right, ui.check("Battery level", draft.showBattery, new CheckBox.Listener() { @Override public void changed(CheckBox b, boolean v) { draft.showBattery = v; } }), 8);
        add(right, ui.check("Memory (RAM) usage", draft.showRam, new CheckBox.Listener() { @Override public void changed(CheckBox b, boolean v) { draft.showRam = v; } }), 0);
        add(right, ui.check("Storage used", draft.showStorage, new CheckBox.Listener() { @Override public void changed(CheckBox b, boolean v) { draft.showStorage = v; } }), 0);
        add(right, ui.check("Network speed", draft.showNetwork, new CheckBox.Listener() { @Override public void changed(CheckBox b, boolean v) { draft.showNetwork = v; } }), 0);
        add(right, ui.check("Time since restart", draft.showUptime, new CheckBox.Listener() { @Override public void changed(CheckBox b, boolean v) { draft.showUptime = v; } }), 0);
        add(right, note("Hidden readings stop updating. Turn them all off to hide the widget. Android doesn't let apps read CPU or GPU usage."), 8);

        add(right, heading("Now Playing"), 24);
        boolean allowed = dash.media.hasAccess();
        add(right, note("Now Playing reads and controls what's playing through Android's notification access. " + (allowed ? "Access is allowed." : "Access isn't allowed yet.")), 10);
        TextView access = ui.button(allowed ? "Notification access settings" : "Allow notification access", new View.OnClickListener() {
            @Override public void onClick(View v) {
                try { dash.activity.startActivity(new Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)); }
                catch (Exception e) { setStatus("Open Android Settings → Notifications → Device & app notifications, then allow Widget Screen."); }
            }
        });
        right.addView(access, ui.margins(ui.ll(Ui.WRAP, ui.px(52)), 0, 12, 0, 0));
    }

    // Coming back from Android's notification access screen updates the Widgets tab.
    public void resumed() { if (tab.equals("Widgets") || tab.equals("Spotify")) build(); }

    // ----- Weather -----

    private void buildWeather() {
        LinearLayout column = ui.column();
        content.addView(column, ui.ll(wide ? ui.px(760) : Ui.MATCH, Ui.WRAP));
        add(column, heading("Weather"), 0);
        add(column, note("Show current conditions for one city. The Weather widget starts hidden; turn it on under Widgets or in Edit layout."), 10);
        add(column, ui.smallText("City"), 16);
        LinearLayout searchRow = ui.row();
        final EditText city = ui.input(weatherQuery.isEmpty() ? draft.weatherLocation : weatherQuery, "City name");
        city.setImeOptions(EditorInfo.IME_ACTION_SEARCH);
        city.setContentDescription("City");
        city.setOnEditorActionListener(new TextView.OnEditorActionListener() {
            @Override public boolean onEditorAction(TextView v, int action, KeyEvent event) {
                if (action == EditorInfo.IME_ACTION_SEARCH || (event != null && event.getKeyCode() == KeyEvent.KEYCODE_ENTER)) { searchCity(city.getText().toString()); return true; }
                return false;
            }
        });
        searchRow.addView(city, ui.ll(0, Ui.WRAP, 1));
        TextView search = ui.button(weatherBusy ? "Searching…" : "Search", new View.OnClickListener() { @Override public void onClick(View v) { searchCity(city.getText().toString()); } });
        Ui.enable(search, !weatherBusy);
        searchRow.addView(search, ui.margins(ui.ll(Ui.WRAP, ui.px(52)), 8, 0, 0, 0));
        add(column, searchRow, 6);
        if (weatherResults != null) {
            if (weatherResults.isEmpty()) add(column, note("No places found. Check the spelling or try a nearby city."), 10);
            for (final Weather.Location location : weatherResults) {
                TextView result = ui.button(location.name, new View.OnClickListener() {
                    @Override public void onClick(View v) {
                        draft.weatherLocation = location.name; draft.latitude = location.latitude; draft.longitude = location.longitude;
                        locationChanged = true; weatherResults = null; weatherQuery = "";
                        build(); setStatus("Weather will show " + location.name + ". Save to keep it.");
                    }
                });
                result.setGravity(Gravity.CENTER_VERTICAL | Gravity.START);
                add(column, result, 6);
            }
        }
        add(column, ui.smallText(draft.weatherLocation.isEmpty() ? "Weather is off." : "Showing: " + draft.weatherLocation), 18);
        TextView off = ui.button("Turn weather off", new View.OnClickListener() {
            @Override public void onClick(View v) { draft.weatherLocation = ""; draft.latitude = draft.longitude = 0; locationChanged = true; build(); setStatus("Weather is off. Save to keep it."); }
        });
        Ui.enable(off, !draft.weatherLocation.isEmpty());
        column.addView(off, ui.margins(ui.ll(Ui.WRAP, ui.px(52)), 0, 10, 0, 0));
        add(column, note("Search sends the city name to Open-Meteo. No account is needed. Once saved, weather updates every 15 minutes."), 14);
    }

    private void searchCity(final String query) {
        weatherQuery = query;
        final String url;
        try { url = Weather.searchUrl(query); }
        catch (IllegalStateException e) { setStatus(e.getMessage()); return; }
        weatherBusy = true; build();
        Async.run(new Async.Work<List<Weather.Location>>() {
            @Override public List<Weather.Location> run() throws Exception { return Weather.parseLocations(Http.getText(url)); }
        }, new Async.Done<List<Weather.Location>>() {
            @Override public void ok(List<Weather.Location> found) { weatherBusy = false; weatherResults = found; if (tab.equals("Weather")) build(); }
        }, new Async.Failed() {
            @Override public void error(Exception error) { weatherBusy = false; if (tab.equals("Weather")) { build(); setStatus("Can't reach the weather service. Check your connection and try again."); } }
        });
    }

    // ----- Spotify -----

    private String accountStatus() {
        SpotifyService spotify = dash.spotify;
        if (spotify.signingIn() && !spotify.connected()) return "Finish signing in in your browser, then return here.";
        if (spotify.startupWarning != null) return spotify.startupWarning;
        return spotify.connected() ? spotify.canControl() ? "Connected. Your login is saved on this device." : "Connected. Reconnect once to enable playback." : "Not connected. Save your Client ID, then connect.";
    }

    public void spotifyStatus(String message) { spotifyMessage = message; if (spotifyStatus != null) spotifyStatus.setText(message); }
    public void spotifyChanged() { if (tab.equals("Spotify")) build(); }

    private String saveClientId() {
        String value = clientId.getText().toString().trim();
        if (!Spotify.validClientId(value)) throw new IllegalStateException("Paste the 32-character Client ID from your Spotify Developer app.");
        dash.saveSpotifyClientId(value);
        draft.spotifyClientId = value;
        return value;
    }

    private void buildSpotify() {
        final SpotifyService spotify = dash.spotify;
        LinearLayout[] c = columns();
        LinearLayout left = c[0], right = c[1];
        add(left, heading("Connection"), 0);
        spotifyStatus = Ui.wrap(ui.body(spotifyMessage != null ? spotifyMessage : accountStatus()), 4);
        add(left, spotifyStatus, 10);
        add(left, ui.smallText("Client ID from your Spotify developer app"), 16);
        String saved = dash.settings.spotifyClientId.isEmpty() ? spotify.clientId() : dash.settings.spotifyClientId;
        clientId = ui.input(saved, "32 characters");
        clientId.setContentDescription("Spotify Client ID");
        add(left, clientId, 6);
        boolean signingIn = spotify.signingIn() && !spotify.connected();
        FlowLayout actions = new FlowLayout(dash.activity, ui.px(8));
        actions.addView(ui.button("Save Client ID", new View.OnClickListener() {
            @Override public void onClick(View v) { try { saveClientId(); spotifyStatus("Client ID saved on this device."); } catch (IllegalStateException e) { spotifyStatus(e.getMessage()); } }
        }));
        actions.addView(ui.accentButton(spotify.connected() ? "Reconnect Spotify" : "Connect Spotify", new View.OnClickListener() {
            @Override public void onClick(View v) {
                try {
                    String url = spotify.beginSignIn(saveClientId());
                    spotifyMessage = "Finish signing in in your browser, then return here.";
                    Launcher.openUrl(dash.activity, url);
                    build();
                } catch (IllegalStateException e) { spotifyStatus(e.getMessage()); }
            }
        }));
        TextView disconnect = ui.button("Disconnect", new View.OnClickListener() {
            @Override public void onClick(View v) {
                try { spotify.disconnect(); spotifyMessage = "Disconnected. Your Client ID is still saved."; dash.spotifyAccountChanged(); }
                catch (Exception e) { spotifyStatus("Could not clear the saved connection. Try again."); }
            }
        });
        Ui.enable(disconnect, spotify.connected());
        actions.addView(disconnect);
        if (signingIn) actions.addView(ui.button("Cancel sign-in", new View.OnClickListener() {
            @Override public void onClick(View v) { spotify.cancelSignIn(); spotifyMessage = "Sign-in cancelled. Your Client ID is saved; try connecting again."; build(); }
        }));
        for (int i = 0; i < actions.getChildCount(); i++) ((TextView) actions.getChildAt(i)).setMinHeight(ui.px(52));
        add(left, actions, 10);
        add(left, note("Account changes take effect right away; you don't need to save. Disconnecting keeps your Client ID. Your sign-in is encrypted on this device."), 12);

        add(right, heading("First-time setup"), 0);
        add(right, note("1.  Create a Web API app in Spotify for Developers and add your Spotify account to its users."), 10);
        TextView developer = ui.button("Open Developer Dashboard", new View.OnClickListener() {
            @Override public void onClick(View v) { try { Launcher.openUrl(dash.activity, "https://developer.spotify.com/dashboard"); } catch (Exception e) { spotifyStatus("Could not open your browser."); } }
        });
        right.addView(developer, ui.margins(ui.ll(Ui.WRAP, ui.px(52)), 0, 8, 0, 0));
        add(right, note("2.  Add this exact redirect address to the app:"), 14);
        LinearLayout redirect = ui.row();
        TextView address = ui.text(Spotify.REDIRECT_URI, ui.body(), ui.theme.accent);
        address.setTextIsSelectable(true);
        redirect.addView(address, ui.ll(0, Ui.WRAP, 1));
        redirect.addView(ui.button("Copy", new View.OnClickListener() {
            @Override public void onClick(View v) {
                ClipboardManager clipboard = (ClipboardManager) dash.activity.getSystemService(Context.CLIPBOARD_SERVICE);
                if (clipboard != null) { clipboard.setPrimaryClip(ClipData.newPlainText("Redirect address", Spotify.REDIRECT_URI)); setStatus("Redirect address copied."); }
            }
        }), ui.ll(Ui.WRAP, ui.px(52)));
        add(right, redirect, 6);
        add(right, note("Using Widget Screen on Windows too? Add this next to the Windows address in the same app and reuse its Client ID."), 6);
        add(right, note("3.  Paste the app's Client ID on the left and tap Connect Spotify. No client secret is needed."), 14);
        add(right, note("Your browser asks you to allow access to your current song, recent albums, playlists, likes and playback. Playing songs from the widget needs Spotify Premium."), 12);
    }

    // ----- Quick Launch -----

    private void buildQuickLaunch() {
        final List<Deck.Shortcut> shortcuts = draft.shortcuts;
        selectedShortcut = Math.max(-1, Math.min(shortcuts.size() - 1, selectedShortcut));
        LinearLayout[] c = columns();
        LinearLayout left = c[0], right = c[1];
        LinearLayout titleRow = ui.row();
        titleRow.addView(heading("Your buttons"), ui.ll(0, Ui.WRAP, 1));
        titleRow.addView(ui.button("+ Add", new View.OnClickListener() {
            @Override public void onClick(View v) { shortcuts.add(new Deck.Shortcut("New button", "App", "", "app")); selectedShortcut = shortcuts.size() - 1; build(); }
        }), ui.ll(Ui.WRAP, ui.px(52)));
        add(left, titleRow, 0);
        if (shortcuts.isEmpty()) add(left, note("No buttons. The widget shows Add a button until you add one."), 10);
        for (int i = 0; i < shortcuts.size(); i++) {
            final int index = i;
            Deck.Shortcut s = shortcuts.get(i);
            LinearLayout row = ui.row();
            row.setPadding(ui.px(12), ui.px(8), ui.px(12), ui.px(8));
            row.setMinimumHeight(ui.px(60));
            ui.select(row, i == selectedShortcut);
            row.addView(new GlyphView(dash.activity, s.glyph, ui.theme.accent, ui.fpx(22)), ui.ll(ui.px(28), ui.px(28)));
            LinearLayout text = ui.column();
            text.addView(ui.body(s.label), ui.ll(Ui.MATCH, Ui.WRAP));
            text.addView(ui.smallText(Deck.describe(s, "App".equals(s.action) && Deck.isPackageName(s.target) ? dash.launcher.appName(s.target) : null)), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 3, 0, 0));
            row.addView(text, ui.margins(ui.ll(0, Ui.WRAP, 1), 12, 0, 0, 0));
            row.setClickable(true);
            row.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { selectedShortcut = index; build(); } });
            add(left, row, 6);
        }
        if (selectedShortcut >= 0) {
            LinearLayout order = ui.row();
            order.addView(ui.iconButton("up", "Move up", 20, true, new View.OnClickListener() { @Override public void onClick(View v) { move(-1); } }), ui.margins(ui.ll(ui.px(56), ui.px(52)), 0, 0, 8, 0));
            order.addView(ui.iconButton("down", "Move down", 20, true, new View.OnClickListener() { @Override public void onClick(View v) { move(1); } }), ui.margins(ui.ll(ui.px(56), ui.px(52)), 0, 0, 8, 0));
            order.addView(ui.button("Remove", new View.OnClickListener() {
                @Override public void onClick(View v) { shortcuts.remove(selectedShortcut); selectedShortcut = Math.min(selectedShortcut, shortcuts.size() - 1); build(); }
            }), ui.ll(Ui.WRAP, ui.px(52)));
            add(left, order, 10);
        }
        add(left, note("Extra buttons continue on more pages in the widget."), 10);

        if (selectedShortcut < 0) { add(right, note("Tap + Add, then choose what the new button does."), 0); return; }
        final Deck.Shortcut s = shortcuts.get(selectedShortcut);
        add(right, ui.smallText("Button name"), 0);
        final EditText name = ui.input(s.label, "Name");
        name.setContentDescription("Button name");
        name.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence t, int a, int b, int d) { }
            @Override public void onTextChanged(CharSequence t, int a, int b, int d) { }
            @Override public void afterTextChanged(Editable e) { s.label = e.toString(); }
        });
        add(right, name, 6);
        add(right, ui.smallText("Icon"), 14);
        FlowLayout icons = new FlowLayout(dash.activity, ui.px(6));
        for (final Deck.Option icon : Deck.ICONS) {
            GlyphView button = ui.iconButton(icon.id, icon.name, 22, true, new View.OnClickListener() { @Override public void onClick(View v) { s.glyph = icon.id; build(); } });
            button.setMinimumWidth(ui.px(52)); button.setMinimumHeight(ui.px(52));
            ui.select(button, icon.id.equals(s.glyph));
            icons.addView(button);
        }
        add(right, icons, 6);
        add(right, ui.smallText("Action"), 14);
        String[] typeIds = new String[Deck.TYPES.length], typeNames = new String[Deck.TYPES.length];
        for (int i = 0; i < Deck.TYPES.length; i++) { typeIds[i] = Deck.TYPES[i].id; typeNames[i] = Deck.TYPES[i].name; }
        add(right, choices(typeIds, typeNames, s.action, new Choice() {
            @Override public void chose(String value) { if (!value.equals(s.action)) { s.action = value; s.target = value.equals("Website") ? "https://" : ""; } build(); }
        }), 6);

        if (s.action.equals("Website")) {
            add(right, ui.smallText("Website address"), 14);
            EditText url = ui.input(s.target, "https://");
            url.setContentDescription("Website address");
            url.addTextChangedListener(new TextWatcher() {
                @Override public void beforeTextChanged(CharSequence t, int a, int b, int d) { }
                @Override public void onTextChanged(CharSequence t, int a, int b, int d) { }
                @Override public void afterTextChanged(Editable e) { s.target = e.toString().trim(); }
            });
            add(right, url, 6);
        } else if (s.action.equals("Control")) {
            add(right, ui.smallText("Control"), 14);
            for (final Deck.Option control : Deck.CONTROLS) {
                TextView option = ui.button(control.name, new View.OnClickListener() { @Override public void onClick(View v) { s.target = control.id; if ("app".equals(s.glyph)) s.glyph = Deck.controlGlyph(control.id); build(); } });
                option.setGravity(Gravity.CENTER_VERTICAL | Gravity.START);
                ui.select(option, control.id.equals(s.target));
                add(right, option, 6);
            }
        } else buildAppPicker(right, s);

        add(right, ui.smallText("Or use a preset"), 18);
        FlowLayout presets = new FlowLayout(dash.activity, ui.px(8));
        for (final Deck.Shortcut preset : Deck.presets()) {
            TextView button = ui.button(preset.label, new View.OnClickListener() {
                @Override public void onClick(View v) { s.label = preset.label; s.action = preset.action; s.target = preset.target; s.glyph = preset.glyph; build(); }
            });
            button.setTextSize(TypedValue.COMPLEX_UNIT_PX, ui.fpx(ui.small()));
            presets.addView(button);
        }
        add(right, presets, 6);
        add(right, ui.check("Ask before running this button", s.confirm, new CheckBox.Listener() { @Override public void changed(CheckBox box, boolean checked) { s.confirm = checked; } }), 12);
        String error = Deck.validate(s);
        if (error != null) { TextView problem = note(error); problem.setTextColor(Theme.WARNING); add(right, problem, 6); }
    }

    private void move(int direction) {
        List<Deck.Shortcut> shortcuts = draft.shortcuts;
        int target = selectedShortcut + direction;
        if (selectedShortcut < 0 || target < 0 || target >= shortcuts.size()) return;
        shortcuts.add(target, shortcuts.remove(selectedShortcut));
        selectedShortcut = target;
        build();
    }

    private void buildAppPicker(LinearLayout right, final Deck.Shortcut s) {
        add(right, ui.smallText("Your default apps"), 14);
        FlowLayout roles = new FlowLayout(dash.activity, ui.px(8));
        for (final Deck.Option role : Deck.ROLES) {
            TextView button = ui.button(role.name, new View.OnClickListener() {
                @Override public void onClick(View v) { s.target = role.id; if ("app".equals(s.glyph)) s.glyph = Deck.roleGlyph(role.id); if (s.label.equals("New button")) s.label = role.name; build(); }
            });
            button.setTextSize(TypedValue.COMPLEX_UNIT_PX, ui.fpx(ui.small()));
            ui.select(button, role.id.equals(s.target));
            roles.addView(button);
        }
        add(right, roles, 6);
        add(right, ui.smallText("Installed app"), 14);
        final EditText filter = ui.input(appFilter, "Filter the list by name");
        filter.setContentDescription("Filter apps by name");
        add(right, filter, 6);
        final LinearLayout list = ui.column();
        add(right, list, 6);
        if (apps == null) {
            add(list, note("Loading apps…"), 0);
            Async.run(new Async.Work<List<Launcher.App>>() {
                @Override public List<Launcher.App> run() { return dash.launcher.apps(); }
            }, new Async.Done<List<Launcher.App>>() {
                @Override public void ok(List<Launcher.App> found) { apps = found; if (tab.equals("Quick Launch")) build(); }
            }, new Async.Failed() { @Override public void error(Exception e) { apps = new ArrayList<Launcher.App>(); } });
            return;
        }
        filter.addTextChangedListener(new TextWatcher() {
            @Override public void beforeTextChanged(CharSequence t, int a, int b, int d) { }
            @Override public void onTextChanged(CharSequence t, int a, int b, int d) { }
            @Override public void afterTextChanged(Editable e) { appFilter = e.toString(); fillApps(list, s); }
        });
        fillApps(list, s);
    }

    private void fillApps(LinearLayout list, final Deck.Shortcut s) {
        list.removeAllViews();
        String needle = appFilter.trim().toLowerCase(Locale.getDefault());
        int shown = 0;
        for (final Launcher.App app : apps) {
            if (!needle.isEmpty() && !app.label.toLowerCase(Locale.getDefault()).contains(needle)) continue;
            if (++shown > 40) { add(list, note("Type in the filter to find more apps."), 4); break; }
            TextView button = ui.button(app.label, new View.OnClickListener() {
                @Override public void onClick(View v) { s.target = app.packageName; if (s.label.equals("New button") || s.label.trim().isEmpty()) s.label = app.label; build(); }
            });
            button.setGravity(Gravity.CENTER_VERTICAL | Gravity.START);
            ui.select(button, app.packageName.equals(s.target));
            add(list, button, 6);
        }
        if (shown == 0) add(list, note("No apps match."), 4);
    }

    // ----- Save and cancel -----

    private void save() {
        for (int i = 0; i < draft.shortcuts.size(); i++) {
            Deck.Shortcut s = draft.shortcuts.get(i);
            String error = Deck.validate(s);
            if (error != null) {
                selectedShortcut = i; if (!tab.equals("Quick Launch")) { tab = "Quick Launch"; scroll = null; } build();
                setStatus("“" + s.label + "”: " + error);
                return;
            }
        }
        dash.applySettings(draft, locationChanged);
        dash.settingsClosed();
    }

    public void cancel() { dash.settingsClosed(); }

    // The dashboard behind was rebuilt (rotation, appearance preview): rebuild the panel for the new size
    // and look, keeping the draft and the open tab.
    public void dashboardRebuilt() { build(); overlay.bringToFront(); }
}
