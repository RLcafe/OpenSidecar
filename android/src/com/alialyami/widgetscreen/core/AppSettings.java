package com.alialyami.widgetscreen.core;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
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
    // Keep the app sideways, so the phone always shows the Windows dashboard arrangement.
    public boolean landscapeOnly;
    public String layoutMode = "Auto";
    // The PC's 24-hour clock choice as last seen, so the clock keeps its format while the PC is away.
    public boolean use24;
    // The one-time tips under the header were put away with Got it.
    public boolean hintDismissed;
    // The phone mirrors the PC's layout (docs/design/any-screen-size.md). These hold the last one the PC sent, so the
    // dashboard keeps its arrangement while the PC is away: the current page's layouts per canvas size (the reference
    // 1856 × 576 one in widgetLayout), the page names, the page shown and the canvas of the PC's own screen.
    public WidgetLayout.Placement[] widgetLayout;
    public final List<String> pages = new ArrayList<String>();
    public int page;
    public double pcCanvasWidth = WidgetLayout.REFERENCE_WIDTH, pcCanvasHeight = WidgetLayout.REFERENCE_HEIGHT;
    // Layouts saved on canvases of other shapes, keyed "WxH" (docs/design/any-screen-size.md).
    public final Map<String, WidgetLayout.Placement[]> canvasLayouts = new LinkedHashMap<String, WidgetLayout.Placement[]>();

    // Every saved layout by canvas key, the reference one included.
    public Map<String, WidgetLayout.Placement[]> savedLayouts() {
        Map<String, WidgetLayout.Placement[]> all = new LinkedHashMap<String, WidgetLayout.Placement[]>();
        if (widgetLayout != null) all.put(WidgetLayout.keyName(WidgetLayout.REFERENCE_WIDTH, WidgetLayout.REFERENCE_HEIGHT), widgetLayout);
        all.putAll(canvasLayouts);
        return all;
    }

    public WidgetLayout.Placement[] layoutFor(double canvasWidth, double canvasHeight) { return WidgetLayout.forCanvas(savedLayouts(), canvasWidth, canvasHeight); }

    public boolean hasLayout(double canvasWidth, double canvasHeight) { return savedLayouts().containsKey(WidgetLayout.keyName(canvasWidth, canvasHeight)); }

    // Saves a layout for one canvas size only; other shapes keep their own. A page shows the same widgets on every
    // screen, so its layouts for other sizes take this one's shown and hidden widgets (as on the PC).
    public void store(double canvasWidth, double canvasHeight, WidgetLayout.Placement[] layout) {
        if (widgetLayout != null) widgetLayout = follow(widgetLayout, layout);
        for (Map.Entry<String, WidgetLayout.Placement[]> entry : canvasLayouts.entrySet()) entry.setValue(follow(entry.getValue(), layout));
        if (WidgetLayout.isReference(canvasWidth, canvasHeight)) widgetLayout = WidgetLayout.copy(layout);
        else canvasLayouts.put(WidgetLayout.keyName(canvasWidth, canvasHeight), WidgetLayout.copy(layout));
    }

    private static WidgetLayout.Placement[] follow(WidgetLayout.Placement[] other, WidgetLayout.Placement[] layout) {
        WidgetLayout.Placement[] result = WidgetLayout.copy(other);
        for (WidgetLayout.Placement p : result) { WidgetLayout.Placement match = WidgetLayout.find(layout, p.id); if (match != null) p.visible = match.visible; }
        return result;
    }

    // Takes the layout the PC sent: its current page's layouts replace the cached ones.
    public void mirror(PcState.Layout layout) {
        widgetLayout = null; canvasLayouts.clear();
        canvasLayouts.putAll(layout.sources());
        pages.clear(); pages.addAll(layout.pages);
        page = layout.page; snapWidgets = layout.snap;
        if (layout.canvasWidth >= 64 && layout.canvasHeight >= 64) { pcCanvasWidth = WidgetLayout.key(layout.canvasWidth); pcCanvasHeight = WidgetLayout.key(layout.canvasHeight); }
        normalize();
    }

    public String pageName() { return page >= 0 && page < pages.size() ? pages.get(page) : "Main"; }

    public AppSettings copy() { return fromProperties(toProperties()); }

    public void normalize() {
        textScale = Double.isNaN(textScale) ? 1 : Math.max(0.9, Math.min(1.15, textScale));
        if (!Theme.contains(Theme.ACCENTS, accent)) accent = "Lime";
        if (!Theme.contains(Theme.THEMES, theme)) theme = "Midnight";
        if (!Theme.contains(LAYOUT_MODES, layoutMode)) layoutMode = "Auto";
        cornerRadius = Double.isNaN(cornerRadius) || Double.isInfinite(cornerRadius) ? 20 : Math.max(0, Math.min(24, cornerRadius));
        String reference = WidgetLayout.keyName(WidgetLayout.REFERENCE_WIDTH, WidgetLayout.REFERENCE_HEIGHT);
        Map<String, WidgetLayout.Placement[]> layouts = new LinkedHashMap<String, WidgetLayout.Placement[]>();
        for (Map.Entry<String, WidgetLayout.Placement[]> entry : canvasLayouts.entrySet()) {
            double[] size = WidgetLayout.parseKey(entry.getKey());
            if (size == null || entry.getValue() == null) continue;
            String key = WidgetLayout.keyName(size[0], size[1]);
            if (key.equals(reference)) { if (widgetLayout == null) widgetLayout = entry.getValue(); continue; }
            if (!layouts.containsKey(key)) layouts.put(key, WidgetLayout.normalize(entry.getValue(), size[0], size[1]));
        }
        canvasLayouts.clear(); canvasLayouts.putAll(layouts);
        if (pages.isEmpty()) pages.add("Main");
        page = Math.max(0, Math.min(pages.size() - 1, page));
        if (WidgetLayout.parseKey(WidgetLayout.keyName(pcCanvasWidth, pcCanvasHeight)) == null) { pcCanvasWidth = WidgetLayout.REFERENCE_WIDTH; pcCanvasHeight = WidgetLayout.REFERENCE_HEIGHT; }
        if (widgetLayout != null) widgetLayout = WidgetLayout.normalize(widgetLayout);
    }

    private static void putLayout(Properties p, String prefix, WidgetLayout.Placement[] layout) {
        for (WidgetLayout.Placement w : layout)
            p.setProperty(prefix + w.id, number(w.x) + "," + number(w.y) + "," + number(w.width) + "," + number(w.height) + "," + w.visible + "," + w.layer);
    }

    private static WidgetLayout.Placement[] readLayout(Properties p, String prefix) {
        List<WidgetLayout.Placement> placements = new ArrayList<WidgetLayout.Placement>();
        for (String id : WidgetLayout.IDS) {
            String value = p.getProperty(prefix + id);
            if (value == null) continue;
            String[] parts = value.split(",");
            if (parts.length < 5) continue;
            try {
                WidgetLayout.Placement placement = new WidgetLayout.Placement(id, Double.parseDouble(parts[0]), Double.parseDouble(parts[1]), Double.parseDouble(parts[2]), Double.parseDouble(parts[3]), "true".equals(parts[4]));
                if (parts.length > 5) placement.layer = Integer.parseInt(parts[5]);
                placements.add(placement);
            } catch (NumberFormatException ignored) { }
        }
        return placements.isEmpty() ? null : placements.toArray(new WidgetLayout.Placement[0]);
    }

    private static String number(double value) { return String.format(Locale.ROOT, "%s", value); }

    public Properties toProperties() {
        Properties p = new Properties();
        p.setProperty("Version", Integer.toString(version));
        p.setProperty("Accent", accent); p.setProperty("Theme", theme);
        p.setProperty("CornerRadius", number(cornerRadius)); p.setProperty("TextScale", number(textScale));
        p.setProperty("SnapWidgets", Boolean.toString(snapWidgets));
        p.setProperty("KeepScreenOn", Boolean.toString(keepScreenOn)); p.setProperty("HideSystemBars", Boolean.toString(hideSystemBars));
        p.setProperty("LandscapeOnly", Boolean.toString(landscapeOnly));
        p.setProperty("LayoutMode", layoutMode);
        p.setProperty("Use24Hour", Boolean.toString(use24));
        p.setProperty("HintDismissed", Boolean.toString(hintDismissed));
        // The reference layout keeps its old keys (Layout.Focus); other canvases add theirs (Layout.1480x552.Focus).
        if (widgetLayout != null) putLayout(p, "Layout.", widgetLayout);
        for (int i = 0; i < pages.size(); i++) p.setProperty("Page." + i, pages.get(i));
        p.setProperty("CurrentPage", Integer.toString(page));
        p.setProperty("PcCanvas", WidgetLayout.keyName(pcCanvasWidth, pcCanvasHeight));
        for (Map.Entry<String, WidgetLayout.Placement[]> entry : canvasLayouts.entrySet()) putLayout(p, "Layout." + entry.getKey() + ".", entry.getValue());
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
        s.landscapeOnly = readBool(p, "LandscapeOnly", false);
        s.layoutMode = p.getProperty("LayoutMode", s.layoutMode);
        s.use24 = readBool(p, "Use24Hour", false);
        s.hintDismissed = readBool(p, "HintDismissed", false);
        s.widgetLayout = readLayout(p, "Layout.");
        for (int i = 0; p.getProperty("Page." + i) != null && i < 8; i++) s.pages.add(p.getProperty("Page." + i));
        s.page = (int) readNumber(p, "CurrentPage", 0);
        double[] pcCanvas = WidgetLayout.parseKey(p.getProperty("PcCanvas"));
        if (pcCanvas != null) { s.pcCanvasWidth = pcCanvas[0]; s.pcCanvasHeight = pcCanvas[1]; }
        for (String name : p.stringPropertyNames()) {
            String[] parts = name.split("\\.");
            if (parts.length != 3 || !parts[0].equals("Layout") || WidgetLayout.parseKey(parts[1]) == null || s.canvasLayouts.containsKey(parts[1])) continue;
            WidgetLayout.Placement[] layout = readLayout(p, "Layout." + parts[1] + ".");
            if (layout != null) s.canvasLayouts.put(parts[1], layout);
        }
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
