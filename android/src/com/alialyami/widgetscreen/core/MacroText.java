package com.alialyami.widgetscreen.core;

import java.util.List;

// Macros wording, the same as MacroActions.cs on Windows (docs/design/macros.md). The PC describes each step; the phone
// only lists them.
public final class MacroText {
    public static String summary(PcState.MacroItem macro) { int n = macro.steps.size(); return n == 1 ? "1 step" : n + " steps"; }
    public static String kind(PcState.MacroItem macro) { return summary(macro) + (macro.confirm ? "  ·  asks first" : ""); }
    // "1.  Hotkey  ·  Ctrl+C\n2.  Wait 0.5 s": the confirm sheet's list.
    public static String detail(List<String> steps) {
        StringBuilder out = new StringBuilder();
        for (int i = 0; i < steps.size(); i++) { if (i > 0) out.append('\n'); out.append(i + 1).append(".  ").append(steps.get(i)); }
        return out.toString();
    }
}
