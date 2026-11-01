package com.alialyami.widgetscreen.core;

import java.util.Locale;

// Smart home wording and thermostat steps (docs/design/smart-home.md), the same as HomeAssistantService on the PC.
public final class HomeText {
    private HomeText() { }

    // The next target a thermostat's − or + asks for: rounded to the device's step and kept in its range.
    public static double stepped(PcState.HomeDevice device, int direction) {
        double step = device.step > 0 ? device.step : 0.5, target = device.target == null ? (device.min + device.max) / 2 : device.target;
        double next = Math.round((target + direction * step) / step) * step;
        return Math.max(device.min, Math.min(device.max, next));
    }

    public static String degrees(double value) {
        return Math.abs(value - Math.rint(value)) < 0.05 ? String.format(Locale.US, "%d°", Math.round(value)) : String.format(Locale.US, "%.1f°", value);
    }

    public static String glyph(PcState.HomeDevice device) { return device.domain.equals("light") ? "bulb" : device.domain.equals("climate") ? "thermometer" : "power"; }

    // What the card says when it has nothing to list, from what the PC sent.
    public static String empty(PcState.Home home) {
        if (!home.connected) return "Connect Home Assistant on the PC, in Settings → Home.";
        if (home.chosen == 0) return "Choose the lights, switches and thermostats to show on the PC, in Settings → Home.";
        if (!home.read) return home.problem.isEmpty() ? "The PC is reading your devices…" : home.problem;
        return "The chosen devices aren't in Home Assistant anymore.";
    }

    // The heading's note: nothing while working.
    public static String status(PcState.Home home) {
        if (home.problem.isEmpty() || !home.connected) return "";
        return home.read ? "NOT UPDATING" : "OFFLINE";
    }
}
