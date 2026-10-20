package com.alialyami.widgetscreen.core;

import java.net.URI;
import java.util.ArrayList;
import java.util.List;

// Quick Launch on Android. App opens an installed app (by package, or the phone's own calculator,
// files, browser and so on), Website opens a link, Control runs a dashboard or media control.
// Windows' File, Folder and Hotkey actions have no Android equivalent.
public final class Deck {
    public static final class Option {
        public final String id, name;
        Option(String id, String name) { this.id = id; this.name = name; }
        @Override public String toString() { return name; }
    }

    public static final class Shortcut {
        public String label = "Button", action = "App", target = "", glyph = "app";
        public boolean confirm;
        public Shortcut() { }
        public Shortcut(String label, String action, String target, String glyph) { this.label = label; this.action = action; this.target = target; this.glyph = glyph; }
        public Shortcut copy() { Shortcut copy = new Shortcut(label, action, target, glyph); copy.confirm = confirm; return copy; }
    }

    public static final Option[] TYPES = { new Option("App", "Open an app"), new Option("Website", "Open a website"), new Option("Control", "Dashboard / media control") };
    public static final Option[] CONTROLS = {
        new Option("play", "Play / pause"), new Option("previous", "Previous track"), new Option("next", "Next track"),
        new Option("mute", "Mute / unmute sound"), new Option("volume-up", "Volume up"), new Option("volume-down", "Volume down"),
        new Option("focus", "Start / pause focus timer"), new Option("quiet", "Toggle Quiet mode"), new Option("sound", "Android sound settings")
    };
    // Apps Android finds by role, so the defaults work whichever calculator or file manager the phone has.
    public static final Option[] ROLES = {
        new Option("role:browser", "Web browser"), new Option("role:calculator", "Calculator"), new Option("role:calendar", "Calendar"),
        new Option("role:contacts", "Contacts"), new Option("role:email", "Email"), new Option("role:files", "Files"),
        new Option("role:gallery", "Gallery"), new Option("role:maps", "Maps"), new Option("role:messaging", "Messages"), new Option("role:music", "Music")
    };
    // Names of the line icons drawn by the app (Glyphs); Android has no Segoe MDL2.
    public static final Option[] ICONS = {
        new Option("app", "App"), new Option("globe", "Globe"), new Option("folder", "Folder"), new Option("calculator", "Calculator"),
        new Option("notes", "Notes"), new Option("music", "Music"), new Option("volume", "Sound"), new Option("play", "Play"),
        new Option("next", "Next"), new Option("timer", "Clock"), new Option("settings", "Settings"), new Option("star", "Star"),
        new Option("camera", "Camera"), new Option("game", "Game"), new Option("calendar", "Calendar"), new Option("heart", "Heart")
    };

    public static List<Shortcut> defaults() {
        List<Shortcut> list = new ArrayList<Shortcut>();
        list.add(new Shortcut("Spotify", "App", "com.spotify.music", "music"));
        list.add(new Shortcut("Browser", "Website", "https://www.google.com", "globe"));
        list.add(new Shortcut("Files", "App", "role:files", "folder"));
        list.add(new Shortcut("Calculator", "App", "role:calculator", "calculator"));
        list.add(new Shortcut("Calendar", "App", "role:calendar", "calendar"));
        list.add(new Shortcut("Sound", "Control", "sound", "volume"));
        return list;
    }

    public static List<Shortcut> presets() {
        List<Shortcut> list = defaults();
        for (Option role : ROLES) if (!role.id.equals("role:files") && !role.id.equals("role:calculator") && !role.id.equals("role:calendar")) list.add(new Shortcut(role.name, "App", role.id, roleGlyph(role.id)));
        for (Option control : CONTROLS) list.add(new Shortcut(control.name, "Control", control.id, controlGlyph(control.id)));
        return list;
    }

    public static String roleGlyph(String role) {
        if (role.equals("role:browser")) return "globe";
        if (role.equals("role:music")) return "music";
        if (role.equals("role:gallery")) return "camera";
        if (role.equals("role:calendar")) return "calendar";
        if (role.equals("role:calculator")) return "calculator";
        if (role.equals("role:files")) return "folder";
        if (role.equals("role:email") || role.equals("role:messaging")) return "notes";
        return "app";
    }

    public static String controlGlyph(String control) {
        if (control.equals("play")) return "play";
        if (control.equals("next")) return "next";
        if (control.equals("previous")) return "previous";
        if (control.equals("focus")) return "timer";
        if (control.equals("quiet")) return "star";
        if (control.equals("sound")) return "settings";
        return "volume";
    }

    public static String inferAction(String target) { return isWebsite(target) ? "Website" : "App"; }

    public static boolean isWebsite(String target) {
        try {
            URI uri = new URI(target == null ? "" : target.trim());
            return ("https".equals(uri.getScheme()) || "http".equals(uri.getScheme())) && uri.getHost() != null && !uri.getHost().isEmpty();
        } catch (Exception e) { return false; }
    }

    public static boolean isPackageName(String target) { return target != null && target.matches("[A-Za-z][A-Za-z0-9_]*(\\.[A-Za-z][A-Za-z0-9_]*)+"); }

    public static Option find(Option[] options, String id) {
        for (Option option : options) if (option.id.equals(id)) return option;
        return null;
    }

    // Checks what can be checked without the device. Whether the app is installed is checked when the button runs.
    public static String validate(Shortcut shortcut) {
        if (shortcut.label == null || shortcut.label.trim().isEmpty()) return "Give this button a name.";
        String target = shortcut.target == null ? "" : shortcut.target.trim();
        if (target.isEmpty()) return "Choose an action or enter a target for this button.";
        if ("Control".equals(shortcut.action)) return find(CONTROLS, target) != null ? null : "Choose a control from the list.";
        if ("Website".equals(shortcut.action)) return isWebsite(target) ? null : "Enter a complete website address starting with https:// or http://.";
        if ("App".equals(shortcut.action)) return find(ROLES, target) != null || isPackageName(target) ? null : "Choose an installed app from the list.";
        return "This action type is unavailable. Choose another action.";
    }

    public static boolean needsConfirmation(Shortcut shortcut) { return shortcut.confirm; }

    public static String describe(Shortcut shortcut, String appName) {
        if ("Control".equals(shortcut.action)) { Option control = find(CONTROLS, shortcut.target); if (control != null) return control.name; }
        if ("App".equals(shortcut.action)) { Option role = find(ROLES, shortcut.target); if (role != null) return role.name + " (your default)"; if (appName != null) return appName; }
        return shortcut.target;
    }

    public static int pageCount(int count, int perPage) { return Math.max(1, (count + perPage - 1) / perPage); }
}
