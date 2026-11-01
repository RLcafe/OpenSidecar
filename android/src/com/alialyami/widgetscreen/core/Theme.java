package com.alialyami.widgetscreen.core;

// The Windows palettes and accents (DashboardAppearance and spec §3.3–3.5) as ARGB colors.
public final class Theme {
    public static final String[] THEMES = { "Midnight", "Slate", "Warm" };
    public static final String[] ACCENTS = { "Lime", "Glacier", "Lavender", "Amber" };
    public static final int WARNING = 0xFFF5C47D;
    // Text on an accent-colored button.
    public static final int ON_ACCENT = 0xFF11170E;
    public static final int CHECK_BORDER = 0xFF798598;
    public static final int SCROLL_THUMB = 0xFF606D81;

    public final int page, surface, raised, input, track, controlBorder, selected, muted, text, clockSurface, clockBorder, clockMuted, accent;

    private Theme(int[] colors, int accent) {
        page = colors[0]; surface = colors[1]; raised = colors[2]; input = colors[3]; track = colors[4]; controlBorder = colors[5];
        selected = colors[6]; muted = colors[7]; text = colors[8]; clockSurface = colors[9]; clockBorder = colors[10]; clockMuted = colors[11];
        this.accent = accent;
    }

    public static Theme of(String theme, String accent) {
        int[] colors = "Slate".equals(theme)
            ? new int[] { 0xFF101923, 0xFF1B2938, 0xFF293C50, 0xFF142130, 0xFF40546A, 0xFF4C637C, 0xFF3A536E, 0xFFB6C7DA, 0xFFF0F6FF, 0xFF1E3341, 0xFF476175, 0xFFB6CDD9 }
            : "Warm".equals(theme)
            ? new int[] { 0xFF181411, 0xFF27211D, 0xFF3B3028, 0xFF1F1915, 0xFF544638, 0xFF665344, 0xFF564534, 0xFFC8B8A6, 0xFFFAF3E9, 0xFF30291F, 0xFF61533C, 0xFFD2C4A8 }
            : new int[] { 0xFF0C0E13, 0xFF161A22, 0xFF252B36, 0xFF10141C, 0xFF353D49, 0xFF343C49, 0xFF394252, 0xFFA0AABB, 0xFFF2F4F8, 0xFF1C2420, 0xFF344235, 0xFFA8BAAA };
        return new Theme(colors, accentColor(accent));
    }

    // The Sticker Sheet inks (decision 021), the same names and colours as the Windows "InkLime"…"InkLemon" resources.
    // Lime, Glacier, Lavender and Amber are also the accents; text on an ink is ON_ACCENT (INK_TEXT).
    public static final String[] INKS = { "Lime", "Glacier", "Lavender", "Amber", "Coral", "Mint", "Rose", "Lemon" };
    public static final int INK_LIME = 0xFFBAF28B, INK_GLACIER = 0xFF8ED8F8, INK_LAVENDER = 0xFFC5AEFF, INK_AMBER = 0xFFF5C47D,
        INK_CORAL = 0xFFFF9E85, INK_MINT = 0xFF7FE6C0, INK_ROSE = 0xFFFFA6CF, INK_LEMON = 0xFFF0E36E, INK_TEXT = ON_ACCENT;

    public static int ink(String name) {
        if (name == null) return INK_LIME;
        switch (name) {
            case "Glacier": return INK_GLACIER;
            case "Lavender": return INK_LAVENDER;
            case "Amber": return INK_AMBER;
            case "Coral": return INK_CORAL;
            case "Mint": return INK_MINT;
            case "Rose": return INK_ROSE;
            case "Lemon": return INK_LEMON;
            default: return INK_LIME;
        }
    }

    public static int accentColor(String accent) {
        return "Glacier".equals(accent) ? 0xFF8ED8F8 : "Lavender".equals(accent) ? 0xFFC5AEFF : "Amber".equals(accent) ? 0xFFF5C47D : 0xFFBAF28B;
    }

    public static boolean contains(String[] values, String value) {
        for (String candidate : values) if (candidate.equals(value)) return true;
        return false;
    }
}
