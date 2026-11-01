package com.alialyami.widgetscreen.core;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

// The pit art (docs/design/claude-watcher-v2.html; decision 017): race cars seen from the side, front and rear, and the
// pit garage, as a list of drawing steps that views/PitRaster turns into small bitmaps with hard pixels. No faces: a
// car's status shows in its wheels, lights, smoke and how it moves. The same numbers as PitArt in the Windows
// PitArt.cs. A car is 140 × 60 units, a garage 100 × 100, a car seen from the front or rear 80 × 56.
public final class PitArt {
    // States (as ClaudePit names them) and car classes, in the PC's order.
    public static final int IDLE = 0, WORK = 1, REV = 2, NEED = 3, PIT = 4, STUCK = 5, DONE = 6, END = 7;
    public static final int GT = 0, SPORT = 1, HATCH = 2;
    public static final int AMBER = 0xFFF5C47D, LIME = 0xFFBAF28B, PAGE = 0xFF0C0E13, TEXT = 0xFFF2F4F8, MUTED = 0xFFA0AABB;
    // Project colours: Lime, Glacier and Lavender by a stable hash of the project's name; Amber is kept for "needs you".
    public static final int[] ACCENTS = { 0xFFBAF28B, 0xFF8ED8F8, 0xFFC5AEFF };

    // One drawing step: a path, ellipse, rectangle or text, or a pushed or popped transform ({a, b, c, d, e, f}).
    public static final class Op {
        public static final int PATH = 0, ELLIPSE = 1, RECT = 2, TEXT = 3, PUSH = 4, POP = 5;
        public int kind;
        public String path = "", text = "";
        public double x, y, w, h, radius, strokeWidth, size;
        public int fill, stroke;
        public boolean roundCap, evenOdd;
        public double[] matrix;
    }

    // FNV-1a over the lower-case name, as an unsigned 32-bit value (the same as the PC's PitArt.Hash).
    public static long hash(String project) {
        long hash = 2166136261L;
        for (char c : (project == null ? "" : project.trim().toLowerCase(Locale.ROOT)).toCharArray()) { hash ^= c; hash = (hash * 16777619L) & 0xFFFFFFFFL; }
        return hash;
    }
    public static int accent(String project) { return ACCENTS[(int) (hash(project) / 3 % 3)]; }
    public static int alpha(int color, double opacity) { return ((int) Math.round(Math.max(0, Math.min(1, opacity)) * ((color >>> 24) & 0xFF)) << 24) | (color & 0xFFFFFF); }

    public static int state(String name) {
        if (name == null) return IDLE;
        switch (name) { case "work": return WORK; case "rev": return REV; case "need": return NEED; case "pit": return PIT; case "stuck": return STUCK; case "done": return DONE; case "end": case "archived": return END; default: return IDLE; }
    }
    public static int car(String name) { return "gt".equals(name) ? GT : "hatch".equals(name) ? HATCH : SPORT; }

    private static final String[] BODY = {
        "M4 41 Q4 34 14 32 L40 29 Q54 18 74 17 L92 17 Q110 18 124 28 L134 31 Q138 33 138 39 L138 43 Q138 46 134 46 L8 46 Q4 46 4 41Z",
        "M6 42 Q6 36 14 34 L36 31 Q50 19 70 17 L88 17 Q104 19 118 29 L130 32 Q136 34 136 40 L136 43 Q136 46 132 46 L10 46 Q6 46 6 42Z",
        "M8 43 Q8 35 16 33 L30 31 Q40 15 60 13 L112 13 Q124 13 128 23 L130 33 Q132 37 132 41 L132 43 Q132 46 128 46 L12 46 Q8 46 8 43Z" };
    private static final String[] GLASS = { "M50 29 Q60 20 74 20 L90 20 Q104 21 114 29Z", "M46 31 Q58 21 72 20 L86 20 Q98 22 108 30Z", "M38 31 Q46 17 62 16 L108 16 Q118 16 122 25 L123 31Z" };
    private static final double[] GLASS_X = { 50, 46, 38 };
    private static final String[] PILLAR = { "M84 20 L83 29", "M80 20 L80 30", "M84 16 L84 31" };
    private static final double[] FRONT_WHEEL = { 28, 30, 32 }, REAR_WHEEL = { 114, 110, 108 }, WHEEL = { 10.5, 11, 11 }, ROOF = { 17, 17, 13 }, ROOF_X = { 84, 80, 84 };
    private static final double[][] HEAD = { { 8, 35 }, { 10, 37 }, { 11, 37 } }, TAIL = { { 134, 34 }, { 132, 35 }, { 128, 30 } };

    // What a side-view car frame shows besides its state.
    public static final class Look {
        public int car = SPORT;
        public boolean fuel, hot, fast, lights, reverse;
        public Look() { }
        public Look(int car, boolean fuel, boolean hot, boolean fast) { this.car = car; this.fuel = fuel; this.hot = hot; this.fast = fast; }
        public String key() { return car + (fuel ? "f" : "") + (hot ? "h" : "") + (fast ? "x" : "") + (lights ? "l" : "") + (reverse ? "r" : ""); }
    }

    private static String n(double v) { double r = Math.round(v * 100) / 100.0; return r == Math.rint(r) ? String.valueOf((long) r) : String.valueOf(r); }
    private static Op p(String path, int fill) { return p(path, fill, 0, 0, false); }
    private static Op p(String path, int fill, int stroke, double width) { return p(path, fill, stroke, width, false); }
    private static Op p(String path, int fill, int stroke, double width, boolean round) { Op o = new Op(); o.kind = Op.PATH; o.path = path; o.fill = fill; o.stroke = stroke; o.strokeWidth = width; o.roundCap = round; return o; }
    private static Op e(double cx, double cy, double rx, double ry, int fill) { return e(cx, cy, rx, ry, fill, 0, 0); }
    private static Op e(double cx, double cy, double rx, double ry, int fill, int stroke, double width) { Op o = new Op(); o.kind = Op.ELLIPSE; o.x = cx; o.y = cy; o.w = rx; o.h = ry; o.fill = fill; o.stroke = stroke; o.strokeWidth = width; return o; }
    private static Op c(double cx, double cy, double r, int fill) { return e(cx, cy, r, r, fill); }
    private static Op r(double x, double y, double w, double h, int fill) { return r(x, y, w, h, fill, 0); }
    private static Op r(double x, double y, double w, double h, int fill, double radius) { Op o = new Op(); o.kind = Op.RECT; o.x = x; o.y = y; o.w = w; o.h = h; o.fill = fill; o.radius = radius; return o; }
    private static Op push(double a, double b, double c, double d, double e, double f) { Op o = new Op(); o.kind = Op.PUSH; o.matrix = new double[] { a, b, c, d, e, f }; return o; }
    private static Op pop() { Op o = new Op(); o.kind = Op.POP; return o; }
    private static Op rotate(double degrees, double cx, double cy) {
        double a = degrees * Math.PI / 180, cos = Math.cos(a), sin = Math.sin(a);
        return push(cos, sin, -sin, cos, cx - cos * cx + sin * cy, cy - sin * cx - cos * cy);
    }
    private static Op text(double x, double y, String value, double size, int fill) { Op o = new Op(); o.kind = Op.TEXT; o.x = x; o.y = y; o.text = value; o.size = size; o.fill = fill; return o; }

    private static void wheel(List<Op> ops, double cx, double cy, double r, double rot, boolean hot) {
        ops.add(e(cx, cy, r, r, 0xFF1B1F27, PAGE, 1.5));
        ops.add(c(cx, cy, r * 0.64, hot ? 0xFFF5A05A : MUTED));
        if (hot) ops.add(c(cx, cy, r * 0.64, alpha(AMBER, 0.35)));
        for (int i = 0; i < 5; i++) {
            double a = i * Math.PI * 2 / 5 - Math.PI / 2 - rot;
            ops.add(p("M" + n(cx) + " " + n(cy) + "L" + n(cx + r * 0.6 * Math.cos(a)) + " " + n(cy + r * 0.6 * Math.sin(a)), 0, 0xFF2A303B, r * 0.2, true));
        }
        ops.add(c(cx, cy, r * 0.16, 0xFF353D49));
    }

    // One frame (0–7) of a car seen from the side, facing left, in a 140 × 60 box.
    public static List<Op> car(int color, int state, Look look, int f) {
        int k = look.car;
        double cy = 46, fw = FRONT_WHEEL[k], rw = REAR_WHEEL[k], wr = WHEEL[k], hx = HEAD[k][0], hy = HEAD[k][1], tx = TAIL[k][0], ty = TAIL[k][1];
        List<Op> ops = new ArrayList<Op>();
        boolean spins = state == WORK || state == STUCK;
        double rot = spins ? f * Math.PI * 2 / 5 / 8 : 0;
        // Your turn keeps its brake lights on, their glow breathing; Idle has its lights off (decision 019).
        boolean hazard = state == NEED && f < 4, flash = state == NEED && (f == 0 || f == 2), brake = state == DONE;
        List<Op> lights = new ArrayList<Op>();
        lights.add(e(hx, hy, 3.4, 2.4, state == END || state == IDLE ? 0xFF4A5361 : flash ? 0xFFFFFFFF : 0xFFDDE3EA));
        lights.add(r(tx - 2, ty - 2, 4, 4.5, brake ? 0xFFFF8A8A : 0xFF7A3434));
        lights.add(r(hx + 1, hy + 4, 5, 3, hazard ? AMBER : 0xFF5E4A2A));
        lights.add(r(tx - 4, ty + 4, 5, 3, hazard ? AMBER : 0xFF5E4A2A));
        if (flash) lights.add(p("M" + n(hx - 2) + " " + n(hy - 2) + " L-16 " + n(hy - 8) + " L-16 " + n(hy + 8) + " L" + n(hx - 2) + " " + n(hy + 2) + "Z", alpha(TEXT, 0.55)));
        if (hazard) { lights.add(c(hx + 3, hy + 5, 8, alpha(AMBER, 0.45))); lights.add(c(tx - 2, ty + 5, 8, alpha(AMBER, 0.45))); }
        // Needs you: an amber beacon on the roof, lit in every frame and flaring with the hazards.
        if (state == NEED) { if (f < 4) lights.add(c(ROOF_X[k], ROOF[k] - 4, 9, alpha(AMBER, 0.5))); lights.add(r(ROOF_X[k] - 5, ROOF[k] - 7, 10, 6, AMBER, 2)); }
        if (look.lights) { lights.add(p("M" + n(hx - 2) + " " + n(hy - 2) + " L-12 " + n(hy - 6) + " L-12 " + n(hy + 6) + " L" + n(hx - 2) + " " + n(hy + 2) + "Z", alpha(0xFFFFF6D8, 0.45))); lights.add(e(hx, hy, 3.6, 2.6, 0xFFFFFFFF)); }
        if (look.reverse) { lights.add(c(tx + 1, ty + 1, 7, alpha(0xFFFFFFFF, 0.4))); lights.add(r(tx - 1, ty - 1, 4, 4, 0xFFFFFFFF)); }
        if (brake) lights.add(c(tx, ty, f < 4 ? 8 : 6, alpha(0xFFF09A9A, f < 4 ? 0.5 : 0.4)));

        List<Op> body = new ArrayList<Op>();
        body.add(p(BODY[k], color, alpha(PAGE, 0.7), 2));
        body.add(p("M8 " + n(cy - 5) + "H" + n(tx + 2) + "V" + n(cy) + "H8Z", alpha(PAGE, 0.22)));
        body.add(p("M20 " + n(cy - 14) + "H" + n(tx - 8), 0, alpha(0xFFFFFFFF, 0.35), 2));
        body.add(p(GLASS[k], 0xFF141922));
        body.add(p("M" + n(GLASS_X[k] + 10) + " " + n(cy - 15) + "l8 -6", 0, alpha(0xFF8ED8F8, 0.35), 2));
        body.add(p(PILLAR[k], 0, color, 3));
        body.add(c(fw, cy, wr + 2, PAGE)); body.add(c(rw, cy, wr + 2, PAGE));
        body.addAll(lights);

        ops.add(e(70, 57, 62, 3, alpha(0xFF000000, 0.4)));
        if (state == REV || (state == WORK && look.fast)) { double q = (f % 4) / 4.0; ops.add(c(tx + 5 + q * 14, cy - 2 - q * 5, 2 + q * 4, alpha(MUTED, 0.8 - q * 0.7))); }
        // Working: dust kicked up behind both tyres, drifting back and up and breaking up over four frames (a speck when
        // light, a trail when heavy; the frame rate follows the burn), and the ground dashes scrolling back.
        if (state == WORK) {
            for (double x = -12 + (f % 3) * 4; x < 140; x += 12) ops.add(r(x, cy + wr + 1, 6, 1, alpha(0xFF606D81, 0.9)));
        }
        if (state == STUCK) for (int j = 0; j < 3; j++) { double q = ((f + j * 3) % 8) / 8.0; ops.add(c(rw + 6 + q * 22, cy + 6 - q * 12, 5 + q * 8, alpha(0xFFC9D0DA, 0.85 - q * 0.75))); }
        double dy = spins && f % 2 == 1 ? 1 : 0;
        boolean tilt = state == REV && f < 2, sputter = look.fuel && (f == 5 || f == 7);
        if (sputter) dy = 0;
        if (state == PIT) {
            ops.add(push(1, 0, 0, 1, 0, f < 4 ? -6 : -5)); ops.addAll(body); wheel(ops, rw, cy, wr, rot, look.hot); ops.add(pop());
            ops.add(e(fw - 18, cy + wr - 2, wr, 3.5, 0xFF1B1F27));
            ops.add(e(fw - 18, cy + wr - 3, wr * 0.6, 2, MUTED));
            ops.add(p("M" + n(fw - 4) + " " + n(cy + 10) + "h14l-3 -10h-8z", 0xFF606D81));
        } else {
            ops.add(push(1, 0, 0, 1, sputter ? 1.5 : 0, -dy));
            if (tilt) ops.add(rotate(-2, rw, cy));
            ops.addAll(body);
            if (tilt) ops.add(pop());
            ops.add(pop());
            wheel(ops, fw, cy, wr, rot, look.hot); wheel(ops, rw, cy, wr, rot, look.hot);
        }
        if (state == WORK) {
            // Dust over the car, big enough to read at ledger size (Ali, 1 Nov 2026): two puffs a tyre, four when heavy,
            // each born at the back of the tyre's contact point, then drifting back, rising, growing and thinning away
            // over eight frames.
            int puffs = look.fast ? 4 : 2;
            for (double wx : new double[] { fw, rw })
                for (int j = 0; j < puffs; j++) {
                    double q = ((f + j * 8 / puffs) % 8) / 8.0;
                    ops.add(c(wx + wr * 0.8 + q * 18, cy + wr - 1 - q * 7, 2 + q * 4.5, alpha(0xFFEDE3CF, 0.95 - q * 0.8)));
                }
        }
        if (state == END) {
            double drop = f < 7 ? -(7 - f) * 4 : 0;
            ops.add(push(1, 0, 0, 1, 0, drop));
            ops.add(p("M4 46 Q2 30 20 28 Q44 12 72 12 Q112 12 132 26 Q140 32 138 46 Z", 0xFF4A5361, alpha(PAGE, 0.6), 2));
            ops.add(p("M40 18 Q46 32 42 46M96 14 Q102 30 98 46", 0, alpha(PAGE, 0.3), 2));
            ops.add(pop());
        }
        if (look.fuel && f < 4) { ops.add(r(62, 0, 8, 8, AMBER)); ops.add(r(60, -2, 12, 12, alpha(AMBER, 0.3))); }
        return ops;
    }

    // A car seen from the front, driving toward the viewer (80 × 56); lit headlights for rolling out.
    public static List<Op> front(int color, boolean lit) {
        List<Op> ops = new ArrayList<Op>();
        ops.add(r(5, 40, 12, 15, 0xFF1B1F27, 2)); ops.add(r(63, 40, 12, 15, 0xFF1B1F27, 2));
        ops.add(p("M18 29 L25 12 Q27 9 31 9 L49 9 Q53 9 55 12 L62 29Z", color, alpha(PAGE, 0.7), 2)); ops.add(p("M23 28 L28 14 L52 14 L57 28Z", 0xFF141922)); ops.add(p("M30 16 L27 24", 0, alpha(0xFF8ED8F8, 0.4), 2));
        ops.add(r(11, 22, 7, 4, color)); ops.add(r(62, 22, 7, 4, color));
        ops.add(p("M4 46 Q4 31 12 29 L68 29 Q76 31 76 46 L76 49 Q76 51 72 51 L8 51 Q4 51 4 49Z", color, alpha(PAGE, 0.7), 2)); ops.add(r(5, 45, 70, 5, alpha(PAGE, 0.25)));
        ops.add(r(8, 33, 15, 6, lit ? 0xFFFFFFFF : 0xFFC9D0DA, 1)); ops.add(r(57, 33, 15, 6, lit ? 0xFFFFFFFF : 0xFFC9D0DA, 1)); ops.add(r(28, 37, 24, 8, 0xFF1B1F27, 1));
        if (lit) { ops.add(e(15, 36, 11, 7, alpha(0xFFFFF6D8, 0.5))); ops.add(e(65, 36, 11, 7, alpha(0xFFFFF6D8, 0.5))); }
        return ops;
    }

    // A car seen from the rear, driving away (80 × 56); brighter tail lights when braking.
    public static List<Op> rear(int color, boolean brake) {
        int tail = brake ? 0xFFFF8A8A : 0xFFE06A6A;
        double rx = brake ? 13 : 10, ry = brake ? 8 : 6;
        List<Op> ops = new ArrayList<Op>();
        ops.add(r(5, 40, 12, 15, 0xFF1B1F27, 2)); ops.add(r(63, 40, 12, 15, 0xFF1B1F27, 2));
        ops.add(p("M18 29 L25 12 Q27 9 31 9 L49 9 Q53 9 55 12 L62 29Z", color, alpha(PAGE, 0.7), 2)); ops.add(p("M24 27 L29 15 L51 15 L56 27Z", 0xFF141922));
        ops.add(p("M4 46 Q4 31 12 29 L68 29 Q76 31 76 46 L76 49 Q76 51 72 51 L8 51 Q4 51 4 49Z", color, alpha(PAGE, 0.7), 2)); ops.add(r(5, 45, 70, 5, alpha(PAGE, 0.25)));
        ops.add(r(7, 32, 18, 5, tail)); ops.add(r(55, 32, 18, 5, tail)); ops.add(r(25, 33, 30, 3, alpha(PAGE, 0.35)));
        ops.add(c(30, 49, 2.5, 0xFF353D49)); ops.add(c(50, 49, 2.5, 0xFF353D49));
        ops.add(e(16, 34.5, rx, ry, alpha(0xFFF09A9A, 0.45))); ops.add(e(64, 34.5, rx, ry, alpha(0xFFF09A9A, 0.45)));
        return ops;
    }

    public static double shutter(int state, boolean closed) { return closed || state == END ? 52 : state == WORK ? 20 : state == NEED ? 24 : 34; }

    // The pit garage from the front (100 × 100), frame 0 or 1: the beacon blinks when a car inside needs you, light
    // spills under the shutter while busy, and a folded garage shows its car count on the sign.
    public static List<Op> garage(int color, int state, boolean closed, int count, int f) {
        double shut = shutter(state, closed);
        List<Op> ops = new ArrayList<Op>();
        if (state == NEED) {
            if (f % 2 == 0) { ops.add(c(50, 8, 12, alpha(AMBER, 0.45))); ops.add(p("M38 8h-10M62 8h10", 0, AMBER, 3)); }
            ops.add(p("M44 14 Q44 4 50 4 Q56 4 56 14Z", AMBER)); ops.add(r(42, 13, 16, 4, 0xFF353D49, 1));
        }
        if (state == WORK && !closed) ops.add(r(20, 40 + shut, 60, 5, alpha(LIME, f % 2 == 1 ? 0.45 : 0.8)));
        boolean lit = !closed && (state == WORK || state == NEED || state == DONE);
        Op wall = r(8, 22, 84, 74, color, 3); wall.stroke = alpha(PAGE, 0.5); wall.strokeWidth = 1.5; ops.add(wall);
        ops.add(r(8, 22, 12, 74, alpha(PAGE, 0.12)));
        ops.add(r(5, 16, 90, 16, 0xFF353D49, 2));
        ops.add(r(30, 19, 40, 11, closed ? TEXT : MUTED, 2));
        if (closed && count > 0) ops.add(text(50, 29, count > 99 ? "99" : String.valueOf(count), 10, PAGE));
        ops.add(r(20, 40, 60, 56, PAGE));
        if (lit) { ops.add(r(20, 78, 60, 18, alpha(color, 0.22))); ops.add(p("M28 50h18M54 50h18M28 56h44", 0, 0xFF353D49, 2)); }
        ops.add(r(20, 40, 60, shut, 0xFF7C8798));
        for (double y = 46; y < 42 + shut - 1; y += 5) ops.add(p("M22 " + n(y) + "h56", 0, 0xFF4A5361, 1.2));
        ops.add(r(44, 34, 12, 4, lit ? TEXT : 0xFF606D81, 1));
        ops.add(r(4, 94, 92, 4, 0xFF353D49));
        return ops;
    }

    // For rolling in and out: the bay behind the cars, and the walls, sign and shutter (k of 8 down) in front of them.
    public static List<Op> garageBack(int color) {
        List<Op> ops = new ArrayList<Op>();
        ops.add(r(20, 40, 60, 56, PAGE)); ops.add(r(20, 78, 60, 18, alpha(color, 0.22))); ops.add(p("M28 50h18M54 50h18M28 56h44", 0, 0xFF353D49, 2));
        return ops;
    }
    public static List<Op> garageFront(int color, int k, int count, boolean lit) {
        double shut = Math.round(56.0 * k / 8);
        List<Op> ops = new ArrayList<Op>();
        Op walls = p("M8 22h84v74H8Z M20 40h60v56H20Z", color, alpha(PAGE, 0.5), 1.5); walls.evenOdd = true; ops.add(walls);
        ops.add(r(8, 22, 12, 74, alpha(PAGE, 0.12))); ops.add(r(5, 16, 90, 16, 0xFF353D49, 2)); ops.add(r(30, 19, 40, 11, count > 0 ? TEXT : MUTED, 2));
        if (count > 0) ops.add(text(50, 29, count > 99 ? "99" : String.valueOf(count), 10, PAGE));
        if (shut > 0) {
            ops.add(r(20, 40, 60, shut, 0xFF7C8798));
            for (double y = 46; y < 40 + shut - 1; y += 5) ops.add(p("M20 " + n(y) + "h60", 0, 0xFF4A5361, 1.2));
        }
        ops.add(r(44, 34, 12, 4, lit ? TEXT : 0xFF606D81, 1));
        if (lit) ops.add(p("M44 38 L36 50 H64 L56 38Z", alpha(TEXT, 0.12)));
        return ops;
    }

    // How fast a car's frames step in each state (working cars go with their burn), and the frame that stands for a
    // state when nothing moves: Ended's dropped cover, the first frame otherwise.
    public static double fps(int state, double share, boolean fuel) {
        switch (state) {
            case WORK: return ClaudePit.wheelFps(share);
            case STUCK: return 24;
            case NEED: return 6;
            case REV: return 8;
            case DONE: return 2;
            case END: return 12;
            case PIT: return 2;
            default: return fuel ? 6 : 0;
        }
    }
    public static int still(int state) { return state == END ? 7 : 0; }
    public static boolean once(int state) { return state == END; }
    // A sprite's pixel size for a box this wide: 2 units a pixel under 150 wide, 3 from there.
    public static int pixelSize(double width) { return width >= 150 ? 3 : 2; }
}
