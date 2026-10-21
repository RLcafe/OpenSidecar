package com.alialyami.widgetscreen.core;

// The free widget canvas from the Windows app (WidgetLayout.cs): 1856 × 576 design units, minimum
// sizes, an optional 8-unit snap. Android stretches this canvas to fill the screen.
public final class WidgetLayout {
    public static final double WIDTH = 1856, HEIGHT = 576;
    // The same widgets as the Windows dashboard, which the phone mirrors.
    public static final String[] IDS = { "Focus", "Media", "Shortcuts", "Mixer", "System", "Weather", "Spotify", "Soundboard" };

    public static final class Box {
        public final double x, y, width, height;
        public Box(double x, double y, double width, double height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public double right() { return x + width; }
        public double bottom() { return y + height; }
        @Override public boolean equals(Object other) {
            if (!(other instanceof Box)) return false;
            Box box = (Box) other;
            return box.x == x && box.y == y && box.width == width && box.height == height;
        }
        @Override public int hashCode() { return Double.valueOf(x + 3 * y + 7 * width + 13 * height).hashCode(); }
        @Override public String toString() { return x + "," + y + "," + width + "," + height; }
    }

    public static final class Placement {
        public String id;
        public double x, y, width, height;
        public boolean visible = true;
        public int layer;
        public Placement() { }
        public Placement(String id, double x, double y, double width, double height, boolean visible) {
            this.id = id; this.x = x; this.y = y; this.width = width; this.height = height; this.visible = visible;
        }
        public Box bounds() { return new Box(x, y, width, height); }
        public void setBounds(Box box) { x = box.x; y = box.y; width = box.width; height = box.height; }
        public Placement copy() { Placement copy = new Placement(id, x, y, width, height, visible); copy.layer = layer; return copy; }
    }

    public static String title(String id) {
        switch (id) {
            case "Focus": return "Focus timer";
            case "Media": return "Now playing";
            case "Shortcuts": return "Quick launch";
            case "Mixer": return "Sound mixer";
            case "System": return "System stats";
            default: return id;
        }
    }

    public static double[] minimum(String id) {
        switch (id) {
            case "Focus": return new double[] { 296, 300 };
            case "Media": return new double[] { 360, 310 };
            case "Spotify": return new double[] { 560, 500 };
            case "Shortcuts": return new double[] { 420, 190 };
            case "Mixer": return new double[] { 380, 420 };
            case "System": return new double[] { 380, 310 };
            case "Soundboard": return new double[] { 360, 240 };
            default: return new double[] { 360, 220 };
        }
    }

    // The Windows default (its Spotify arrangement): Focus in the left column, Now Playing over Quick Launch,
    // the mixer, then Spotify. System stats, Weather and the Soundboard start hidden.
    public static Placement[] defaults() {
        return new Placement[] {
            new Placement("Focus", 0, 0, 320, 576, true),
            new Placement("Media", 336, 0, 472, 310, true),
            new Placement("Shortcuts", 336, 326, 472, 250, true),
            new Placement("Mixer", 824, 0, 456, 576, true),
            new Placement("System", 1384, 0, 472, 310, false),
            new Placement("Weather", 1384, 326, 472, 250, false),
            new Placement("Spotify", 1296, 0, 560, 576, true),
            new Placement("Soundboard", 336, 326, 472, 250, false)
        };
    }

    public static Placement[] copy(Placement[] layout) {
        Placement[] result = new Placement[layout.length];
        for (int i = 0; i < layout.length; i++) result[i] = layout[i].copy();
        return result;
    }

    public static Placement find(Placement[] layout, String id) {
        if (layout != null) for (Placement p : layout) if (p != null && id.equals(p.id)) return p;
        return null;
    }

    private static double finite(double value, double fallback) { return Double.isNaN(value) || Double.isInfinite(value) ? fallback : value; }

    // Every known widget gets exactly one placement, inside the canvas and at least its minimum size.
    public static Placement[] normalize(Placement[] layout) {
        Placement[] defaults = defaults();
        Placement[] result = new Placement[defaults.length];
        for (int i = 0; i < defaults.length; i++) {
            Placement fallback = defaults[i];
            Placement supplied = find(layout, fallback.id);
            Placement p = supplied == null ? fallback.copy() : supplied.copy();
            double[] min = minimum(p.id);
            p.width = Math.max(min[0], Math.min(WIDTH, finite(p.width, fallback.width)));
            p.height = Math.max(min[1], Math.min(HEIGHT, finite(p.height, fallback.height)));
            p.x = Math.max(0, Math.min(WIDTH - p.width, finite(p.x, fallback.x)));
            p.y = Math.max(0, Math.min(HEIGHT - p.height, finite(p.y, fallback.y)));
            p.layer = Math.max(0, Math.min(1000000, p.layer));
            result[i] = p;
        }
        return result;
    }

    private static double snap(double value, boolean enabled) { return enabled ? Math.round(value / 8) * 8 : value; }

    // Moves (corner null or empty) or resizes from one of the corners "NW", "NE", "SW", "SE".
    public static Box transform(String id, Box start, String corner, double dx, double dy, boolean snap) {
        if (Double.isNaN(dx) || Double.isInfinite(dx) || Double.isNaN(dy) || Double.isInfinite(dy)) return start;
        double[] min = minimum(id);
        double left = start.x, right = start.right(), top = start.y, bottom = start.bottom();
        if (corner == null || corner.isEmpty())
            return new Box(Math.max(0, Math.min(WIDTH - start.width, snap(left + dx, snap))), Math.max(0, Math.min(HEIGHT - start.height, snap(top + dy, snap))), start.width, start.height);
        if (corner.contains("W")) left = Math.max(0, Math.min(right - min[0], snap(left + dx, snap)));
        if (corner.contains("E")) right = Math.min(WIDTH, Math.max(left + min[0], snap(right + dx, snap)));
        if (corner.contains("N")) top = Math.max(0, Math.min(bottom - min[1], snap(top + dy, snap)));
        if (corner.contains("S")) bottom = Math.min(HEIGHT, Math.max(top + min[1], snap(bottom + dy, snap)));
        return new Box(left, top, right - left, bottom - top);
    }

    public static boolean same(Placement[] a, Placement[] b) {
        if (a == null || b == null || a.length != b.length) return a == b;
        for (int i = 0; i < a.length; i++) {
            Placement p = a[i], q = b[i];
            if (!p.id.equals(q.id) || !p.bounds().equals(q.bounds()) || p.visible != q.visible || p.layer != q.layer) return false;
        }
        return true;
    }
}
