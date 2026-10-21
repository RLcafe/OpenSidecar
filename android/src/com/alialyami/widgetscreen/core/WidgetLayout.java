package com.alialyami.widgetscreen.core;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

// The free widget canvas from the Windows app (WidgetLayout.cs): placements in design units, minimum sizes and an
// optional 8-unit snap. The canvas is shaped like the screen (DashboardFit) and layouts are kept per canvas size; the
// 1856 × 576 canvas of a 1920 × 720 screen is the reference (docs/design/any-screen-size.md).
public final class WidgetLayout {
    public static final double REFERENCE_WIDTH = 1856, REFERENCE_HEIGHT = 576;
    private static final double GAP = 16;
    // 80% of the reference canvas, less what rounding the canvas down to the snap grid can take off.
    private static final double MINIMUM_SHRINK = 0.78;
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

    // On a canvas smaller than the reference (80% at least, see DashboardFit) minimum sizes shrink with it, so widgets
    // keep their places and proportions and only free space inside the cards gets smaller. Upright canvases compare
    // with the upright reference canvas (656 × 1712).
    public static double[] compact(double canvasWidth, double canvasHeight) {
        boolean upright = canvasHeight > canvasWidth;
        double width = upright ? 720 - DashboardFit.MARGIN_X : REFERENCE_WIDTH;
        double height = upright ? 1920 - DashboardFit.MARGIN_Y - DashboardFit.STACKED_HEADER - DashboardFit.FOOTER : REFERENCE_HEIGHT;
        return new double[] { Math.max(MINIMUM_SHRINK, Math.min(1, canvasWidth / width)), Math.max(MINIMUM_SHRINK, Math.min(1, canvasHeight / height)) };
    }

    public static double[] minimum(String id, double canvasWidth, double canvasHeight) {
        double[] m = minimum(id), k = compact(canvasWidth, canvasHeight);
        return new double[] { Math.min(canvasWidth, Math.round(m[0] * k[0])), Math.min(canvasHeight, Math.round(m[1] * k[1])) };
    }

    // Layouts are kept per canvas size, rounded down to the 8-unit snap grid.
    public static double key(double size) { return Math.max(8, Math.floor(size / 8) * 8); }
    public static boolean isReference(double canvasWidth, double canvasHeight) { return key(canvasWidth) == REFERENCE_WIDTH && key(canvasHeight) == REFERENCE_HEIGHT; }

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

    // The default arrangement for a canvas: the Windows default on the reference canvas, a template elsewhere.
    public static Placement[] defaults(double canvasWidth, double canvasHeight) {
        canvasWidth = key(canvasWidth); canvasHeight = key(canvasHeight);
        if (isReference(canvasWidth, canvasHeight)) return normalize(defaults());
        return normalize(arrange(canvasWidth, canvasHeight), canvasWidth, canvasHeight);
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

    public static Placement[] normalize(Placement[] layout) { return normalize(layout, defaults(), REFERENCE_WIDTH, REFERENCE_HEIGHT); }

    public static Placement[] normalize(Placement[] layout, double canvasWidth, double canvasHeight) {
        canvasWidth = key(canvasWidth); canvasHeight = key(canvasHeight);
        return isReference(canvasWidth, canvasHeight) ? normalize(layout) : normalize(layout, arrange(canvasWidth, canvasHeight), canvasWidth, canvasHeight);
    }

    // Every known widget gets exactly one placement, inside the canvas and at least its minimum size.
    private static Placement[] normalize(Placement[] layout, Placement[] defaults, double canvasWidth, double canvasHeight) {
        Placement[] result = new Placement[defaults.length];
        for (int i = 0; i < defaults.length; i++) {
            Placement fallback = defaults[i];
            Placement supplied = find(layout, fallback.id);
            Placement p = supplied == null ? fallback.copy() : supplied.copy();
            double[] min = minimum(p.id, canvasWidth, canvasHeight);
            p.width = Math.max(min[0], Math.min(canvasWidth, finite(p.width, fallback.width)));
            p.height = Math.max(min[1], Math.min(canvasHeight, finite(p.height, fallback.height)));
            p.x = Math.max(0, Math.min(canvasWidth - p.width, finite(p.x, fallback.x)));
            p.y = Math.max(0, Math.min(canvasHeight - p.height, finite(p.y, fallback.y)));
            p.layer = Math.max(0, Math.min(1000000, p.layer));
            result[i] = p;
        }
        return result;
    }

    // ----- Layouts per canvas size -----

    // The layout for a canvas: the one saved for its size; otherwise the saved layout of the closest shape that
    // stretches to it without squeezing a shown widget under its minimum; otherwise the default arrangement.
    // saved maps "WxH" canvas keys (see keyName) to layouts.
    public static Placement[] forCanvas(Map<String, Placement[]> saved, double canvasWidth, double canvasHeight) {
        canvasWidth = key(canvasWidth); canvasHeight = key(canvasHeight);
        Placement[] exact = saved.get(keyName(canvasWidth, canvasHeight));
        if (exact != null) return normalize(exact, canvasWidth, canvasHeight);
        Placement[] best = null;
        double distance = Double.MAX_VALUE;
        for (Map.Entry<String, Placement[]> entry : saved.entrySet()) {
            double[] size = parseKey(entry.getKey());
            if (size == null || entry.getValue() == null) continue;
            Placement[] stretched = stretch(normalize(entry.getValue(), size[0], size[1]), size[0], size[1], canvasWidth, canvasHeight);
            if (!fits(stretched, canvasWidth, canvasHeight)) continue;
            double d = Math.abs(Math.log(size[0] / size[1] / (canvasWidth / canvasHeight)));
            if (d < distance) { distance = d; best = stretched; }
        }
        return best != null ? normalize(best, canvasWidth, canvasHeight) : defaults(canvasWidth, canvasHeight);
    }

    public static String keyName(double canvasWidth, double canvasHeight) { return (long) key(canvasWidth) + "x" + (long) key(canvasHeight); }

    public static double[] parseKey(String name) {
        String[] parts = name == null ? new String[0] : name.split("x");
        if (parts.length != 2) return null;
        try {
            double width = Long.parseLong(parts[0]), height = Long.parseLong(parts[1]);
            return width >= 64 && height >= 64 && width <= 100000 && height <= 100000 ? new double[] { key(width), key(height) } : null;
        } catch (NumberFormatException e) { return null; }
    }

    // Scales positions and sizes from one canvas to another, rounded to whole units.
    public static Placement[] stretch(Placement[] layout, double fromWidth, double fromHeight, double toWidth, double toHeight) {
        double fx = toWidth / fromWidth, fy = toHeight / fromHeight;
        List<Placement> result = new ArrayList<Placement>();
        for (Placement p : layout) {
            if (p == null) continue;
            Placement q = p.copy();
            double left = Math.round(p.x * fx), top = Math.round(p.y * fy);
            q.x = left; q.y = top; q.width = Math.round((p.x + p.width) * fx) - left; q.height = Math.round((p.y + p.height) * fy) - top;
            result.add(q);
        }
        return result.toArray(new Placement[0]);
    }

    // Whether every shown widget keeps its minimum size on the canvas.
    public static boolean fits(Placement[] layout, double canvasWidth, double canvasHeight) {
        for (Placement p : layout) {
            if (p == null || !p.visible) continue;
            double[] min = minimum(p.id, canvasWidth, canvasHeight);
            if (p.width < min[0] - 1 || p.height < min[1] - 1) return false;
        }
        return true;
    }

    // ----- Default arrangements for any canvas (the same templates as WidgetLayout.cs) -----

    private static final class Node {
        final String id; final boolean across; final double weight; final Node[] children;
        Node(String id, boolean across, double weight, Node[] children) { this.id = id; this.across = across; this.weight = weight; this.children = children; }
    }
    private static Node leaf(String id, double weight) { return new Node(id, false, weight, null); }
    private static Node row(double weight, Node... children) { return new Node(null, true, weight, children); }
    private static Node column(double weight, Node... children) { return new Node(null, false, weight, children); }
    private static final class Template {
        final String name; final double aspect; final Node root;
        Template(String name, double aspect, Node root) { this.name = name; this.aspect = aspect; this.root = root; }
    }
    // Wide canvases get columns, squarer ones three columns or two rows of cards, upright ones rows.
    private static final Template[] TEMPLATES = {
        new Template("Columns", 3.2, row(1, leaf("Focus", 320), column(472, leaf("Media", 310), leaf("Shortcuts", 250)), leaf("Mixer", 456), leaf("Spotify", 560))),
        new Template("Three columns", 2.0, row(1, column(472, leaf("Focus", 330), leaf("Media", 310)), column(456, leaf("Mixer", 460), leaf("Shortcuts", 250)), leaf("Spotify", 560))),
        new Template("Two rows", 1.4, column(1, row(330, leaf("Focus", 320), leaf("Media", 472), leaf("Shortcuts", 472)), row(560, leaf("Mixer", 456), leaf("Spotify", 560)))),
        new Template("Tall", 0.65, column(1, row(330, leaf("Focus", 320), leaf("Media", 472)), leaf("Shortcuts", 250), row(560, leaf("Mixer", 456), leaf("Spotify", 560)))),
        new Template("Narrow", 0.4, column(1, row(330, leaf("Focus", 320), leaf("Media", 472)), leaf("Shortcuts", 250), leaf("Mixer", 480), leaf("Spotify", 560)))
    };

    private static double[] minimumOf(Node node, double canvasWidth, double canvasHeight) {
        if (node.id != null) return minimum(node.id, canvasWidth, canvasHeight);
        double along = GAP * (node.children.length - 1), across = 0;
        for (Node child : node.children) {
            double[] m = minimumOf(child, canvasWidth, canvasHeight);
            along += node.across ? m[0] : m[1]; across = Math.max(across, node.across ? m[1] : m[0]);
        }
        return node.across ? new double[] { along, across } : new double[] { across, along };
    }

    // Shares a length between children by weight; a child that would get less than its minimum gets its minimum.
    public static double[] distribute(double length, double[] weights, double[] minimums) {
        int n = weights.length;
        double[] sizes = new double[n];
        boolean[] atMinimum = new boolean[n];
        for (boolean changed = true; changed;) {
            changed = false;
            double rest = length, weight = 0;
            for (int i = 0; i < n; i++) if (atMinimum[i]) rest -= minimums[i]; else weight += weights[i];
            for (int i = 0; i < n; i++) {
                sizes[i] = atMinimum[i] ? minimums[i] : weight > 0 ? Math.max(0, rest) * weights[i] / weight : 0;
                if (!atMinimum[i] && sizes[i] < minimums[i]) { atMinimum[i] = true; changed = true; }
            }
        }
        return sizes;
    }

    private static void lay(Node node, Box area, double canvasWidth, double canvasHeight, Map<String, Box> result) {
        if (node.id != null) { result.put(node.id, area); return; }
        int n = node.children.length;
        double start = node.across ? area.x : area.y, end = start + (node.across ? area.width : area.height);
        double[] weights = new double[n], minimums = new double[n];
        for (int i = 0; i < n; i++) {
            weights[i] = node.children[i].weight;
            double[] m = minimumOf(node.children[i], canvasWidth, canvasHeight);
            minimums[i] = node.across ? m[0] : m[1];
        }
        double[] sizes = distribute(end - start - GAP * (n - 1), weights, minimums);
        double position = start;
        for (int i = 0; i < n; i++) {
            // Edges round to whole units; the last child ends exactly at the area's edge unless it overflows.
            double from = Math.round(position), to = Math.round(position + sizes[i]);
            if (i == n - 1 && to <= end + 0.5) to = end;
            lay(node.children[i], node.across ? new Box(from, area.y, to - from, area.height) : new Box(area.x, from, area.width, to - from), canvasWidth, canvasHeight, result);
            position += sizes[i] + GAP;
        }
    }

    // The template that fits the canvas and is closest to its shape; if none fits, the one that overflows least.
    public static String templateFor(double canvasWidth, double canvasHeight) { return choose(key(canvasWidth), key(canvasHeight)).name; }

    private static Template choose(double canvasWidth, double canvasHeight) {
        double aspect = canvasWidth / canvasHeight, bestScore = Double.MAX_VALUE;
        Template best = TEMPLATES[0];
        for (Template template : TEMPLATES) {
            double[] m = minimumOf(template.root, canvasWidth, canvasHeight);
            double overflow = Math.max(0, m[0] - canvasWidth) + Math.max(0, m[1] - canvasHeight);
            double score = overflow > 0 ? 1000 + overflow : Math.abs(Math.log(aspect / template.aspect));
            if (score < bestScore) { bestScore = score; best = template; }
        }
        return best;
    }

    // The default widgets (Focus, Now Playing, Quick Launch, the mixer and Spotify) arranged for the canvas. Hidden
    // widgets wait where they are most likely wanted: System stats over Now Playing, Weather and the soundboard over
    // Quick Launch.
    public static Placement[] arrange(double canvasWidth, double canvasHeight) {
        Map<String, Box> boxes = new HashMap<String, Box>();
        lay(choose(canvasWidth, canvasHeight).root, new Box(0, 0, canvasWidth, canvasHeight), canvasWidth, canvasHeight, boxes);
        boxes.put("System", boxes.get("Media")); boxes.put("Weather", boxes.get("Shortcuts")); boxes.put("Soundboard", boxes.get("Shortcuts"));
        Placement[] result = new Placement[IDS.length];
        for (int i = 0; i < IDS.length; i++) {
            Box b = boxes.get(IDS[i]);
            result[i] = new Placement(IDS[i], b.x, b.y, b.width, b.height, !IDS[i].equals("System") && !IDS[i].equals("Weather") && !IDS[i].equals("Soundboard"));
        }
        return result;
    }

    private static double snap(double value, boolean enabled) { return enabled ? Math.round(value / 8) * 8 : value; }

    public static Box transform(String id, Box start, String corner, double dx, double dy, boolean snap) {
        return transform(id, start, corner, dx, dy, snap, REFERENCE_WIDTH, REFERENCE_HEIGHT);
    }

    // Moves (corner null or empty) or resizes from one of the corners "NW", "NE", "SW", "SE".
    public static Box transform(String id, Box start, String corner, double dx, double dy, boolean snap, double canvasWidth, double canvasHeight) {
        if (Double.isNaN(dx) || Double.isInfinite(dx) || Double.isNaN(dy) || Double.isInfinite(dy)) return start;
        double[] min = minimum(id, canvasWidth, canvasHeight);
        double left = start.x, right = start.right(), top = start.y, bottom = start.bottom();
        if (corner == null || corner.isEmpty())
            return new Box(Math.max(0, Math.min(canvasWidth - start.width, snap(left + dx, snap))), Math.max(0, Math.min(canvasHeight - start.height, snap(top + dy, snap))), start.width, start.height);
        if (corner.contains("W")) left = Math.max(0, Math.min(right - min[0], snap(left + dx, snap)));
        if (corner.contains("E")) right = Math.min(canvasWidth, Math.max(left + min[0], snap(right + dx, snap)));
        if (corner.contains("N")) top = Math.max(0, Math.min(bottom - min[1], snap(top + dy, snap)));
        if (corner.contains("S")) bottom = Math.min(canvasHeight, Math.max(top + min[1], snap(bottom + dy, snap)));
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
