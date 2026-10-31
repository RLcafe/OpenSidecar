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
        Placement[] best = null, nearest = null;
        double distance = Double.MAX_VALUE, nearestDistance = Double.MAX_VALUE;
        for (Map.Entry<String, Placement[]> entry : saved.entrySet()) {
            double[] size = parseKey(entry.getKey());
            if (size == null || entry.getValue() == null) continue;
            Placement[] source = normalize(entry.getValue(), size[0], size[1]);
            double d = Math.abs(Math.log(size[0] / size[1] / (canvasWidth / canvasHeight)));
            if (d < nearestDistance) { nearestDistance = d; nearest = source; }
            Placement[] stretched = stretch(source, size[0], size[1], canvasWidth, canvasHeight);
            if (fits(stretched, canvasWidth, canvasHeight) && d < distance) { distance = d; best = stretched; }
        }
        if (best != null) return normalize(best, canvasWidth, canvasHeight);
        if (nearest == null) return defaults(canvasWidth, canvasHeight);
        // Nothing stretches well: the page's shown widgets, arranged afresh for this canvas.
        List<String> shown = readingOrder(nearest);
        if (shown.equals(java.util.Arrays.asList(DEFAULT_SHOWN)) && isReference(canvasWidth, canvasHeight)) return defaults(canvasWidth, canvasHeight);
        return normalize(arrange(canvasWidth, canvasHeight, shown), canvasWidth, canvasHeight);
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

    // ----- Arrangements for any canvas (the same rule as WidgetLayout.cs) -----

    public static final String[] DEFAULT_SHOWN = { "Focus", "Media", "Shortcuts", "Mixer", "Spotify" };

    // The size each card is happiest at, from the reference default; arrangements stay close to these.
    public static double[] preferred(String id) {
        switch (id) {
            case "Focus": return new double[] { 320, 400 };
            case "Mixer": return new double[] { 456, 576 };
            case "Spotify": return new double[] { 560, 576 };
            case "Shortcuts": case "Weather": case "Soundboard": return new double[] { 472, 250 };
            default: return new double[] { 472, 310 };
        }
    }

    // Shown widgets left to right, then top to bottom: the order of the phone's list and of new arrangements.
    public static List<String> readingOrder(Placement[] layout) {
        List<Placement> shown = new ArrayList<Placement>();
        for (Placement p : layout) if (p != null && p.visible) shown.add(p);
        java.util.Collections.sort(shown, new java.util.Comparator<Placement>() {
            @Override public int compare(Placement a, Placement b) { return a.x != b.x ? Double.compare(a.x, b.x) : Double.compare(a.y, b.y); }
        });
        List<String> ids = new ArrayList<String>();
        for (Placement p : shown) ids.add(p.id);
        return ids;
    }

    // Arranges the shown widgets, in order, as columns of stacked cards or as rows of cards side by side, and picks
    // the split that fits the canvas and keeps every card closest to its preferred size (growing counts half as much
    // as shrinking). Hidden widgets wait in the top left corner at their minimum size.
    public static Placement[] arrange(double canvasWidth, double canvasHeight, List<String> wanted) {
        List<String> shown = new ArrayList<String>();
        for (String id : wanted) if (java.util.Arrays.asList(IDS).contains(id) && !shown.contains(id)) shown.add(id);
        Map<String, Box> best = null;
        double bestScore = Double.MAX_VALUE;
        int n = shown.size();
        for (int across = 0; across < 2 && n > 0; across++)
            for (int mask = 0; mask < 1 << Math.max(0, n - 1); mask++) {
                List<List<String>> groups = new ArrayList<List<String>>();
                groups.add(new ArrayList<String>());
                for (int i = 0; i < n; i++) {
                    groups.get(groups.size() - 1).add(shown.get(i));
                    if (i < n - 1 && (mask & 1 << i) != 0) groups.add(new ArrayList<String>());
                }
                double[] overflow = new double[1];
                Map<String, Box> boxes = layGroups(groups, across == 1, canvasWidth, canvasHeight, overflow);
                double score = 0;
                if (overflow[0] > 0) score = 1000 + overflow[0];
                else for (Map.Entry<String, Box> e : boxes.entrySet()) {
                    double[] pref = preferred(e.getKey());
                    score += strain(e.getValue().width, pref[0]) + strain(e.getValue().height, pref[1]);
                }
                if (score < bestScore - 1e-9) { bestScore = score; best = boxes; }
            }
        Placement[] result = new Placement[IDS.length];
        for (int i = 0; i < IDS.length; i++) {
            Box b = best == null ? null : best.get(IDS[i]);
            double[] min = minimum(IDS[i], canvasWidth, canvasHeight);
            result[i] = b == null ? new Placement(IDS[i], 0, 0, min[0], min[1], false) : new Placement(IDS[i], b.x, b.y, b.width, b.height, true);
        }
        return result;
    }

    public static Placement[] arrange(double canvasWidth, double canvasHeight) { return arrange(canvasWidth, canvasHeight, java.util.Arrays.asList(DEFAULT_SHOWN)); }

    private static double strain(double actual, double preferred) { double r = actual / preferred; return r >= 1 ? 0.5 * Math.log(r) : -Math.log(Math.max(r, 1e-6)); }

    // Columns (across false): groups side by side, each group's cards stacked. Rows (across true): groups stacked, each
    // group's cards side by side.
    private static Map<String, Box> layGroups(List<List<String>> groups, boolean across, double canvasWidth, double canvasHeight, double[] overflow) {
        int o = across ? 1 : 0, in = across ? 0 : 1;
        double outerLength = across ? canvasHeight : canvasWidth, innerLength = across ? canvasWidth : canvasHeight;
        int count = groups.size();
        double[] outerMin = new double[count], outerWeight = new double[count];
        double outerSum = GAP * (count - 1);
        overflow[0] = 0;
        for (int i = 0; i < count; i++) {
            double innerSum = GAP * (groups.get(i).size() - 1);
            for (String id : groups.get(i)) {
                double[] min = minimum(id, canvasWidth, canvasHeight);
                outerMin[i] = Math.max(outerMin[i], min[o]); outerWeight[i] = Math.max(outerWeight[i], preferred(id)[o]);
                innerSum += min[in];
            }
            outerSum += outerMin[i];
            overflow[0] += Math.max(0, innerSum - innerLength);
        }
        overflow[0] += Math.max(0, outerSum - outerLength);
        Map<String, Box> result = new HashMap<String, Box>();
        double[][] outerEdges = edges(outerLength, distribute(outerLength - GAP * (count - 1), outerWeight, outerMin));
        for (int i = 0; i < count; i++) {
            List<String> g = groups.get(i);
            double[] weights = new double[g.size()], mins = new double[g.size()];
            for (int j = 0; j < g.size(); j++) { weights[j] = preferred(g.get(j))[in]; mins[j] = minimum(g.get(j), canvasWidth, canvasHeight)[in]; }
            double[][] innerEdges = edges(innerLength, distribute(innerLength - GAP * (g.size() - 1), weights, mins));
            for (int j = 0; j < g.size(); j++) {
                double os = outerEdges[i][0], ol = outerEdges[i][1] - os, ns = innerEdges[j][0], nl = innerEdges[j][1] - ns;
                result.put(g.get(j), across ? new Box(ns, os, nl, ol) : new Box(os, ns, ol, nl));
            }
        }
        return result;
    }

    // Whole-unit edges for sizes laid one after another with gaps; the last ends at the end unless it overflows.
    private static double[][] edges(double end, double[] sizes) {
        double[][] result = new double[sizes.length][];
        double position = 0;
        for (int i = 0; i < sizes.length; i++) {
            double from = Math.round(position), to = Math.round(position + sizes[i]);
            if (i == sizes.length - 1 && to <= end + 0.5) to = end;
            result[i] = new double[] { from, to };
            position += sizes[i] + GAP;
        }
        return result;
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

    // Shown widgets in the order arrange keeps them (docs/design/organize-widgets.md): on a wide canvas column by
    // column, left to right and each column top to bottom; on an upright one row by row. A widget whose centre falls
    // inside the span of the first widget of the current column (or row) joins it. The same rule as WidgetLayout.cs.
    public static List<String> arrangeOrder(List<Placement> shown, double canvasWidth, double canvasHeight) {
        final boolean rows = canvasHeight > canvasWidth;
        List<Placement> sorted = new ArrayList<Placement>();
        for (Placement p : shown) if (p != null) sorted.add(p);
        java.util.Collections.sort(sorted, new java.util.Comparator<Placement>() {
            @Override public int compare(Placement a, Placement b) { return Double.compare(low(a, rows) + high(a, rows), low(b, rows) + high(b, rows)); }
        });
        List<List<Placement>> groups = new ArrayList<List<Placement>>();
        double start = 0, end = 0;
        for (Placement p : sorted) {
            double middle = (low(p, rows) + high(p, rows)) / 2;
            if (!groups.isEmpty() && middle >= start && middle <= end) groups.get(groups.size() - 1).add(p);
            else { List<Placement> g = new ArrayList<Placement>(); g.add(p); groups.add(g); start = low(p, rows); end = high(p, rows); }
        }
        List<String> ids = new ArrayList<String>();
        for (List<Placement> g : groups) {
            java.util.Collections.sort(g, new java.util.Comparator<Placement>() {
                @Override public int compare(Placement a, Placement b) { return Double.compare(rows ? a.x : a.y, rows ? b.x : b.y); }
            });
            for (Placement p : g) ids.add(p.id);
        }
        return ids;
    }

    private static double low(Placement p, boolean rows) { return rows ? p.y : p.x; }
    private static double high(Placement p, boolean rows) { return rows ? p.y + p.height : p.x + p.width; }

    // The layout with its shown widgets arranged neatly in their current order (arrange). Hidden widgets keep their
    // places, and layers reset because nothing overlaps any more. Widgets too many to fit at their minimums keep them
    // and stay on the canvas, overlapping.
    public static Placement[] tidy(Placement[] layout, List<String> shown, double canvasWidth, double canvasHeight) {
        Placement[] arranged = arrange(canvasWidth, canvasHeight, shown), result = copy(layout);
        for (Placement q : result) {
            if (!shown.contains(q.id)) continue;
            Box r = find(arranged, q.id).bounds();
            q.width = Math.min(canvasWidth, r.width); q.height = Math.min(canvasHeight, r.height);
            q.x = Math.max(0, Math.min(canvasWidth - q.width, r.x)); q.y = Math.max(0, Math.min(canvasHeight - q.height, r.y));
            q.layer = 0;
        }
        return result;
    }

    // ----- Snapping to other widgets (docs/design/organize-widgets.md) -----

    // How close, in canvas units, an edge has to come to a target to snap to it.
    public static final double ALIGN_DISTANCE = 12;

    // A guide line drawn while an edge sits on a target: vertical lines at x = at from y = from to to, horizontal lines
    // at y = at from x = from to to.
    public static final class Guide {
        public final boolean vertical;
        public final double at, from, to;
        public Guide(boolean vertical, double at, double from, double to) { this.vertical = vertical; this.at = at; this.from = from; this.to = to; }
    }

    // Where an edge can snap along one axis: a leading edge (kind 0, left or top) to the canvas edge, another widget's
    // leading edge or one gap after its trailing edge; a trailing edge (kind 1) likewise; a centre (kind 2) to another
    // widget's centre. others holds {low, high} spans along the axis.
    private static List<Double> targets(int kind, List<double[]> others, double length) {
        List<Double> result = new ArrayList<Double>();
        if (kind == 0) result.add(0.0);
        if (kind == 1) result.add(length);
        for (double[] o : others) {
            if (kind == 0) { result.add(o[0]); result.add(o[1] + GAP); }
            else if (kind == 1) { result.add(o[1]); result.add(o[0] - GAP); }
            else result.add((o[0] + o[1]) / 2);
        }
        return result;
    }

    // The shift that puts the nearest of the given edges on a target within ALIGN_DISTANCE, or null.
    private static Double shift(double[] edges, int[] kinds, List<double[]> others, double length) {
        Double best = null;
        for (int i = 0; i < edges.length; i++)
            for (double target : targets(kinds[i], others, length)) {
                double s = target - edges[i];
                if (Math.abs(s) <= ALIGN_DISTANCE && (best == null || Math.abs(s) < Math.abs(best))) best = s;
            }
        return best;
    }

    // transform with snapping on: the edges that move snap to other shown widgets (lined up with an edge, one gap
    // beside it, or centred on it, for moves) and to the canvas edges when they come within ALIGN_DISTANCE; an axis
    // that finds no target follows the 8-unit grid. guides gets the lines to draw for the result.
    public static Box align(String id, Box start, String corner, double dx, double dy, List<Box> others, double canvasWidth, double canvasHeight, List<Guide> guides) {
        guides.clear();
        Box free = transform(id, start, corner, dx, dy, false, canvasWidth, canvasHeight), grid = transform(id, start, corner, dx, dy, true, canvasWidth, canvasHeight);
        if (free.equals(start)) return start;
        List<double[]> xs = new ArrayList<double[]>(), ys = new ArrayList<double[]>();
        for (Box o : others) { xs.add(new double[] { o.x, o.right() }); ys.add(new double[] { o.y, o.bottom() }); }
        boolean move = corner == null || corner.isEmpty();
        Box result;
        if (move) {
            Double sx = shift(new double[] { free.x, free.right(), free.x + free.width / 2 }, new int[] { 0, 1, 2 }, xs, canvasWidth);
            Double sy = shift(new double[] { free.y, free.bottom(), free.y + free.height / 2 }, new int[] { 0, 1, 2 }, ys, canvasHeight);
            double x = sx == null ? grid.x : free.x + sx, y = sy == null ? grid.y : free.y + sy;
            result = new Box(Math.max(0, Math.min(canvasWidth - free.width, x)), Math.max(0, Math.min(canvasHeight - free.height, y)), free.width, free.height);
        } else {
            double[] min = minimum(id, canvasWidth, canvasHeight);
            double left = grid.x, right = grid.right(), top = grid.y, bottom = grid.bottom();
            Double s;
            if (corner.contains("W") && (s = shift(new double[] { free.x }, new int[] { 0 }, xs, canvasWidth)) != null) left = free.x + s;
            if (corner.contains("E") && (s = shift(new double[] { free.right() }, new int[] { 1 }, xs, canvasWidth)) != null) right = free.right() + s;
            if (corner.contains("N") && (s = shift(new double[] { free.y }, new int[] { 0 }, ys, canvasHeight)) != null) top = free.y + s;
            if (corner.contains("S") && (s = shift(new double[] { free.bottom() }, new int[] { 1 }, ys, canvasHeight)) != null) bottom = free.bottom() + s;
            if (corner.contains("W")) left = Math.max(0, Math.min(right - min[0], left));
            if (corner.contains("E")) right = Math.min(canvasWidth, Math.max(left + min[0], right));
            if (corner.contains("N")) top = Math.max(0, Math.min(bottom - min[1], top));
            if (corner.contains("S")) bottom = Math.min(canvasHeight, Math.max(top + min[1], bottom));
            result = new Box(left, top, right - left, bottom - top);
        }
        for (Box o : others) {
            double fromY = Math.min(o.y, result.y), toY = Math.max(o.bottom(), result.bottom()), fromX = Math.min(o.x, result.x), toX = Math.max(o.right(), result.right());
            if (move || corner.contains("W")) {
                if (on(result.x, o.x) || on(result.x, o.right())) addGuide(guides, new Guide(true, result.x, fromY, toY));
                if (on(result.x, o.right() + GAP)) addGuide(guides, new Guide(true, o.right() + GAP / 2, fromY, toY));
            }
            if (move || corner.contains("E")) {
                if (on(result.right(), o.right()) || on(result.right(), o.x)) addGuide(guides, new Guide(true, result.right(), fromY, toY));
                if (on(result.right(), o.x - GAP)) addGuide(guides, new Guide(true, o.x - GAP / 2, fromY, toY));
            }
            if (move || corner.contains("N")) {
                if (on(result.y, o.y) || on(result.y, o.bottom())) addGuide(guides, new Guide(false, result.y, fromX, toX));
                if (on(result.y, o.bottom() + GAP)) addGuide(guides, new Guide(false, o.bottom() + GAP / 2, fromX, toX));
            }
            if (move || corner.contains("S")) {
                if (on(result.bottom(), o.bottom()) || on(result.bottom(), o.y)) addGuide(guides, new Guide(false, result.bottom(), fromX, toX));
                if (on(result.bottom(), o.y - GAP)) addGuide(guides, new Guide(false, o.y - GAP / 2, fromX, toX));
            }
            if (move && on(result.x + result.width / 2, o.x + o.width / 2)) addGuide(guides, new Guide(true, result.x + result.width / 2, fromY, toY));
            if (move && on(result.y + result.height / 2, o.y + o.height / 2)) addGuide(guides, new Guide(false, result.y + result.height / 2, fromX, toX));
        }
        return result;
    }

    private static boolean on(double a, double b) { return Math.abs(a - b) < 0.5; }

    // One line per position: guides that share a line merge their spans.
    private static void addGuide(List<Guide> guides, Guide guide) {
        for (int i = 0; i < guides.size(); i++) {
            Guide g = guides.get(i);
            if (g.vertical == guide.vertical && Math.abs(g.at - guide.at) < 0.5) { guides.set(i, new Guide(g.vertical, g.at, Math.min(g.from, guide.from), Math.max(g.to, guide.to))); return; }
        }
        guides.add(guide);
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
