package com.alialyami.widgetscreen.core;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Properties;

// The phone's own preferences: how the dashboard looks on this screen and which widgets it shows. Everything
// the widgets show and do (Quick Launch buttons, weather city, Spotify, Discord, Quiet mode, the 24-hour clock)
// comes from the PC. Stored as Java properties XML (settings.xml in the app's private folder), written atomically.
public final class AppSettings {
    public static final String[] LAYOUT_MODES = { "Auto", "Dashboard", "List" };

    public int version = 2;
    public String accent = "Lime", theme = "Midnight";
    public double cornerRadius = 20, textScale = 1;
    public boolean snapWidgets = true;
    // Display: keep the screen awake while the dashboard is open, hide the status and navigation bars,
    // and whether to use the stretched dashboard canvas or a scrolling list (Auto picks by screen size).
    public boolean keepScreenOn = true, hideSystemBars = true;
    public String layoutMode = "Auto";
    // Null until the layout is first saved; the default arrangement is used until then.
    public WidgetLayout.Placement[] widgetLayout;

    public WidgetLayout.Placement[] layout() { return widgetLayout == null ? WidgetLayout.defaults() : widgetLayout; }

    public AppSettings copy() { return fromProperties(toProperties()); }

    public void normalize() {
        textScale = Double.isNaN(textScale) ? 1 : Math.max(0.9, Math.min(1.15, textScale));
        if (!Theme.contains(Theme.ACCENTS, accent)) accent = "Lime";
        if (!Theme.contains(Theme.THEMES, theme)) theme = "Midnight";
        if (!Theme.contains(LAYOUT_MODES, layoutMode)) layoutMode = "Auto";
        cornerRadius = Double.isNaN(cornerRadius) || Double.isInfinite(cornerRadius) ? 20 : Math.max(0, Math.min(24, cornerRadius));
        if (widgetLayout != null) widgetLayout = WidgetLayout.normalize(widgetLayout);
    }

    private static String number(double value) { return String.format(Locale.ROOT, "%s", value); }

    public Properties toProperties() {
        Properties p = new Properties();
        p.setProperty("Version", Integer.toString(version));
        p.setProperty("Accent", accent); p.setProperty("Theme", theme);
        p.setProperty("CornerRadius", number(cornerRadius)); p.setProperty("TextScale", number(textScale));
        p.setProperty("SnapWidgets", Boolean.toString(snapWidgets));
        p.setProperty("KeepScreenOn", Boolean.toString(keepScreenOn)); p.setProperty("HideSystemBars", Boolean.toString(hideSystemBars));
        p.setProperty("LayoutMode", layoutMode);
        if (widgetLayout != null) for (WidgetLayout.Placement w : widgetLayout)
            p.setProperty("Layout." + w.id, number(w.x) + "," + number(w.y) + "," + number(w.width) + "," + number(w.height) + "," + w.visible + "," + w.layer);
        return p;
    }

    private static double readNumber(Properties p, String key, double fallback) {
        try { String value = p.getProperty(key); return value == null ? fallback : Double.parseDouble(value.trim()); }
        catch (NumberFormatException e) { return fallback; }
    }
    private static boolean readBool(Properties p, String key, boolean fallback) {
        String value = p.getProperty(key);
        return value == null ? fallback : "true".equalsIgnoreCase(value.trim());
    }

    public static AppSettings fromProperties(Properties p) {
        AppSettings s = new AppSettings();
        s.accent = p.getProperty("Accent", s.accent); s.theme = p.getProperty("Theme", s.theme);
        s.cornerRadius = readNumber(p, "CornerRadius", s.cornerRadius); s.textScale = readNumber(p, "TextScale", s.textScale);
        s.snapWidgets = readBool(p, "SnapWidgets", s.snapWidgets);
        s.keepScreenOn = readBool(p, "KeepScreenOn", s.keepScreenOn); s.hideSystemBars = readBool(p, "HideSystemBars", s.hideSystemBars);
        s.layoutMode = p.getProperty("LayoutMode", s.layoutMode);
        List<WidgetLayout.Placement> placements = new ArrayList<WidgetLayout.Placement>();
        for (String id : WidgetLayout.IDS) {
            String value = p.getProperty("Layout." + id);
            if (value == null) continue;
            String[] parts = value.split(",");
            if (parts.length < 5) continue;
            try {
                WidgetLayout.Placement placement = new WidgetLayout.Placement(id, Double.parseDouble(parts[0]), Double.parseDouble(parts[1]), Double.parseDouble(parts[2]), Double.parseDouble(parts[3]), "true".equals(parts[4]));
                if (parts.length > 5) placement.layer = Integer.parseInt(parts[5]);
                placements.add(placement);
            } catch (NumberFormatException ignored) { }
        }
        if (!placements.isEmpty()) s.widgetLayout = placements.toArray(new WidgetLayout.Placement[0]);
        s.normalize();
        return s;
    }

    // Load never throws for a bad file: defaults are used and the original stays until the next save.
    public static AppSettings load(File file, String[] warning) {
        if (!file.exists()) return new AppSettings();
        InputStream in = null;
        try {
            in = new FileInputStream(file);
            Properties p = new Properties();
            p.loadFromXML(in);
            return fromProperties(p);
        } catch (Exception e) {
            if (warning != null && warning.length > 0) warning[0] = "Settings could not be read. Defaults are in use; the original file is kept until you save.";
            return new AppSettings();
        } finally {
            if (in != null) try { in.close(); } catch (IOException ignored) { }
        }
    }

    public static void save(File file, AppSettings settings) throws IOException {
        settings.normalize();
        File folder = file.getAbsoluteFile().getParentFile();
        if (folder != null && !folder.exists() && !folder.mkdirs()) throw new IOException("Could not create the settings folder.");
        File temporary = new File(file.getPath() + ".tmp");
        FileOutputStream out = new FileOutputStream(temporary);
        try {
            settings.toProperties().storeToXML(out, "Widget Screen settings", "UTF-8");
            out.flush(); out.getFD().sync();
        } finally { out.close(); }
        Files.move(temporary.toPath(), file.toPath(), StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
    }
}
