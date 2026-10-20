package com.alialyami.widgetscreen.integrations;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.net.Uri;

import com.alialyami.widgetscreen.core.Deck;

import java.text.Collator;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

// Opens apps, roles (the phone's default calculator, browser…) and links for Quick Launch and Spotify.
public final class Launcher {
    public static final class App {
        public final String label, packageName;
        App(String label, String packageName) { this.label = label; this.packageName = packageName; }
        @Override public String toString() { return label; }
    }

    private final Context context;

    public Launcher(Context context) { this.context = context.getApplicationContext(); }

    // On demand only (the Settings picker); never polled.
    public List<App> apps() {
        PackageManager packages = context.getPackageManager();
        Intent main = new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER);
        List<App> result = new ArrayList<App>();
        List<String> seen = new ArrayList<String>();
        for (ResolveInfo info : packages.queryIntentActivities(main, 0)) {
            String name = info.activityInfo.packageName;
            if (name.equals(context.getPackageName()) || seen.contains(name)) continue;
            seen.add(name);
            result.add(new App(info.loadLabel(packages).toString(), name));
        }
        final Collator collator = Collator.getInstance();
        Collections.sort(result, new Comparator<App>() { @Override public int compare(App a, App b) { return collator.compare(a.label, b.label); } });
        return result;
    }

    public String appName(String packageName) {
        try { PackageManager packages = context.getPackageManager(); return packages.getApplicationLabel(packages.getApplicationInfo(packageName, 0)).toString(); }
        catch (Exception e) { return null; }
    }

    public static String roleCategory(String role) {
        switch (role) {
            case "role:browser": return Intent.CATEGORY_APP_BROWSER;
            case "role:calculator": return Intent.CATEGORY_APP_CALCULATOR;
            case "role:calendar": return Intent.CATEGORY_APP_CALENDAR;
            case "role:contacts": return Intent.CATEGORY_APP_CONTACTS;
            case "role:email": return Intent.CATEGORY_APP_EMAIL;
            case "role:files": return "android.intent.category.APP_FILES";
            case "role:gallery": return Intent.CATEGORY_APP_GALLERY;
            case "role:maps": return Intent.CATEGORY_APP_MAPS;
            case "role:messaging": return Intent.CATEGORY_APP_MESSAGING;
            case "role:music": return Intent.CATEGORY_APP_MUSIC;
            default: return null;
        }
    }

    public Intent intentFor(Deck.Shortcut shortcut) {
        String target = shortcut.target.trim();
        Intent intent;
        if ("Website".equals(shortcut.action)) intent = new Intent(Intent.ACTION_VIEW, Uri.parse(target));
        else if (target.startsWith("role:")) {
            String category = roleCategory(target);
            if (category == null) throw new IllegalStateException("Choose an installed app from the list.");
            intent = Intent.makeMainSelectorActivity(Intent.ACTION_MAIN, category);
            if (intent.resolveActivity(context.getPackageManager()) == null) {
                Deck.Option role = Deck.find(Deck.ROLES, target);
                throw new IllegalStateException("No " + (role == null ? "matching" : role.name.toLowerCase(java.util.Locale.getDefault())) + " app on this device. Choose another app.");
            }
        } else {
            intent = context.getPackageManager().getLaunchIntentForPackage(target);
            if (intent == null) throw new IllegalStateException("This app isn't installed. Choose another app in Settings.");
        }
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        return intent;
    }

    public void open(Activity activity, Deck.Shortcut shortcut) {
        String error = Deck.validate(shortcut);
        if (error != null) throw new IllegalStateException(error);
        try { activity.startActivity(intentFor(shortcut)); }
        catch (ActivityNotFoundException e) { throw new IllegalStateException("No app on this device can open this."); }
    }

    public static void openUrl(Activity activity, String url) {
        try { activity.startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)); }
        catch (ActivityNotFoundException e) { throw new IllegalStateException("No browser on this device can open the link."); }
    }

    public static final String SPOTIFY_PACKAGE = "com.spotify.music";

    public boolean openSpotify(Activity activity) {
        Intent intent = context.getPackageManager().getLaunchIntentForPackage(SPOTIFY_PACKAGE);
        if (intent == null) return false;
        activity.startActivity(intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        return true;
    }
}
