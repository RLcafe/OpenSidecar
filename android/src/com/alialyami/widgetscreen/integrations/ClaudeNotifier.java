package com.alialyami.widgetscreen.integrations;

import android.app.Activity;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Rect;
import android.graphics.drawable.Icon;
import android.os.Build;

import com.alialyami.widgetscreen.core.ClaudeText;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.PitArt;
import com.alialyami.widgetscreen.views.PitSprite;

import java.util.List;

// Claude Code alerts from the PC as phone notifications (package 3, decision 025). Every /state answer carries the
// PC's last ten minutes of alerts, numbered; this raises one notification for each number the phone hasn't seen, while
// the app is open and polling (there is no background service). The first answer after the app starts only sets the
// mark, so nothing from before pops up. The PC's Quiet mode and Settings → Widgets → Notify me keep it silent. Only the
// project, the thread's name and the waiting message the card already shows; never a prompt.
public final class ClaudeNotifier {
    public static final String CHANNEL = "claude", EXTRA = "com.alialyami.widgetscreen.CLAUDE";
    private static final int PERMISSION_REQUEST = 4310;
    private final Activity activity;
    private long seen = -1;
    private String pc = "";
    private boolean asked;
    private Icon icon;

    public ClaudeNotifier(Activity activity) { this.activity = activity; }

    // Called with every answer from the PC. Returns how many notifications it raised.
    public int update(PcState state, boolean enabled) {
        if (state == null) return 0;
        // Another PC (or the same one renamed) starts the count again.
        if (!state.name.equals(pc)) { pc = state.name; seen = -1; }
        List<PcState.ClaudeAlert> fresh = ClaudeText.freshAlerts(state.claudeAlerts, state.claudeAlertSeq, seen);
        seen = state.claudeAlertSeq;
        if (!enabled || state.claudeQuiet || fresh.isEmpty()) return 0;
        if (!allowed()) { ask(); return 0; }
        NotificationManager manager = (NotificationManager) activity.getSystemService(Context.NOTIFICATION_SERVICE);
        if (manager == null) return 0;
        channel(manager);
        int shown = 0;
        for (PcState.ClaudeAlert alert : fresh) {
            try {
                Notification.Builder builder = new Notification.Builder(activity, CHANNEL)
                    .setSmallIcon(icon())
                    .setContentTitle(ClaudeText.alertTitle(alert))
                    .setContentText(ClaudeText.alertText(alert))
                    .setStyle(new Notification.BigTextStyle().bigText(ClaudeText.alertText(alert)))
                    .setCategory(Notification.CATEGORY_STATUS)
                    .setAutoCancel(true)
                    .setColor(PitArt.AMBER)
                    .setWhen(alert.at > 0 ? (long) (alert.at * 1000) : System.currentTimeMillis())
                    .setShowWhen(true)
                    .setContentIntent(open(alert));
                // One notification per session: a newer alert for it replaces the older one.
                manager.notify(alert.session.isEmpty() ? "claude" : alert.session, 1, builder.build());
                shown++;
            } catch (Exception ignored) { /* A refused notification never breaks the dashboard. */ }
        }
        return shown;
    }

    // Asks for the notification permission (Android 13+) once per app start, when it's about to be needed.
    public void prepare(PcState state, boolean enabled) {
        if (enabled && state != null && state.claudePage >= 0 && !allowed()) ask();
    }

    private boolean allowed() {
        if (Build.VERSION.SDK_INT >= 33 && activity.checkSelfPermission("android.permission.POST_NOTIFICATIONS") != PackageManager.PERMISSION_GRANTED) return false;
        NotificationManager manager = (NotificationManager) activity.getSystemService(Context.NOTIFICATION_SERVICE);
        return manager == null || manager.areNotificationsEnabled();
    }

    private void ask() {
        if (asked || Build.VERSION.SDK_INT < 33) return;
        asked = true;
        try { activity.requestPermissions(new String[] { "android.permission.POST_NOTIFICATIONS" }, PERMISSION_REQUEST); } catch (Exception ignored) { }
    }

    private static void channel(NotificationManager manager) {
        if (manager.getNotificationChannel(CHANNEL) != null) return;
        NotificationChannel channel = new NotificationChannel(CHANNEL, "Claude Code on the PC", NotificationManager.IMPORTANCE_HIGH);
        channel.setDescription("A Claude Code session on your PC needs you, got stuck, or finished a long turn.");
        manager.createNotificationChannel(channel);
    }

    // A tap opens the app on the Claude card's page.
    private PendingIntent open(PcState.ClaudeAlert alert) {
        Intent intent = new Intent(activity, activity.getClass()).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP).putExtra(EXTRA, alert.session);
        return PendingIntent.getActivity(activity, (int) (alert.seq & 0x7fffffff), intent, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
    }

    // The status bar icon: the waiting car's silhouette, drawn in code like the card's (no image file).
    private Icon icon() {
        if (icon != null) return icon;
        Bitmap[] frames = PitSprite.carFrames(0xFFFFFFFF, PitArt.IDLE, new PitArt.Look(), 48);
        Bitmap car = frames[0], square = Bitmap.createBitmap(96, 96, Bitmap.Config.ARGB_8888);
        Paint pixels = new Paint(); pixels.setFilterBitmap(false);
        int h = Math.round(96f * car.getHeight() / car.getWidth());
        new Canvas(square).drawBitmap(car, new Rect(0, 0, car.getWidth(), car.getHeight()), new Rect(0, (96 - h) / 2, 96, (96 - h) / 2 + h), pixels);
        icon = Icon.createWithBitmap(square);
        return icon;
    }
}
