package com.alialyami.widgetscreen.core;

import java.util.Locale;

// The critters' shapes and faces (docs/design/claude-watcher.md) in a 100 × 100 box, the same numbers as CritterPose in
// the Windows ClaudeCritter.cs. Each project always gets the same body and colour from a stable hash of its name; Amber
// is kept for Needs you. States are the PC's words: "ready", "working", "needs", "done".
public final class Critter {
    public static final int[] ACCENTS = { 0xFFBAF28B, 0xFF8ED8F8, 0xFFC5AEFF };
    public static final int NEEDS_YOU = 0xFFF5C47D;

    // FNV-1a over the lower-case name, as an unsigned 32-bit value.
    public static long hash(String project) {
        long hash = 2166136261L;
        for (char c : (project == null ? "" : project.trim().toLowerCase(Locale.ROOT)).toCharArray()) { hash ^= c; hash = (hash * 16777619L) & 0xFFFFFFFFL; }
        return hash;
    }
    public static int shape(String project) { return (int) (hash(project) % 3); }
    public static int accent(String project) { return (int) (hash(project) / 3 % 3); }

    // Bean, tall pill and squat round: left, top, right, bottom and corner radius.
    public static double[] body(int shape) {
        switch (shape) {
            case 1: return new double[] { 28, 26, 72, 94, 22 };
            case 2: return new double[] { 10, 44, 90, 94, 25 };
            default: return new double[] { 14, 32, 86, 94, 30 };
        }
    }
    public static boolean needs(String state) { return "needs".equals(state); }
    // Needs you hops 4 units on alternate heartbeats; Quiet mode keeps it still.
    public static double hop(String state, boolean phase, boolean quiet) { return needs(state) && phase && !quiet ? -4 : 0; }
    public static double eyeY(double[] b) { return b[1] + (b[3] - b[1]) * 0.36; }
    public static double eyeGap(double[] b) { return (b[2] - b[0]) * 0.2; }
    public static double mouthY(double[] b) { return eyeY(b) + (b[3] - b[1]) * 0.28; }
    public static double eyeRadius(String state) { return needs(state) ? 10.5 : 8.5; }
    public static double pupilRadius(String state) { return needs(state) ? 2.6 : 4; }
    // Working pupils look down and to one side, and hop to the other side on alternate heartbeats.
    public static double pupilX(String state, boolean phase, boolean quiet) { return "working".equals(state) ? (phase && !quiet ? 3 : -3) : 0; }
    public static double pupilY(String state) { return "working".equals(state) ? 3 : needs(state) ? -1 : 0; }
    public static boolean eyesOpen(String state) { return "working".equals(state) || needs(state); }
    // A project critter that has eaten its threads: cheeks, a belly and a count bubble on the body's top right.
    public static double cheekRadius(double[] b) { return Math.min(9, (b[2] - b[0]) * 0.12); }
    public static double cheekX(double[] b) { return (b[2] - b[0]) * 0.3; }
    public static double[] belly(double[] b) { double w = (b[2] - b[0]) * 0.56, h = (b[3] - b[1]) * 0.2; return new double[] { 50 - w / 2, b[3] - h - 4, w, h }; }
    public static final double BUBBLE_RADIUS = 13;
    public static double bubbleX(double[] b) { return Math.min(86, b[2] - 2); }
    public static double bubbleY(double[] b) { return Math.max(14, b[1] - 2); }
    // The mouth opening as it eats or spits (0 closed, 1 wide).
    public static double mouthWidth(double open) { return 6 + 8 * Math.max(0, Math.min(1, open)); }
    public static double mouthHeight(double open) { return 1.5 + 10 * Math.max(0, Math.min(1, open)); }
    // Babies are 40% of their project's critter, never under 24 units.
    public static double baby(double parent) { return Math.max(24, Math.round(parent * 0.4)); }
}
