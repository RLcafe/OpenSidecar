package com.alialyami.widgetscreen.integrations;

import android.app.NotificationManager;
import android.content.ComponentName;
import android.content.Context;
import android.content.pm.ApplicationInfo;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.media.MediaMetadata;
import android.media.session.MediaController;
import android.media.session.MediaSessionManager;
import android.media.session.PlaybackState;
import android.os.Build;
import android.os.SystemClock;
import android.provider.Settings;

import java.util.List;
import java.util.Locale;

// Android's version of Windows Now Playing (MediaService): the active media sessions of any app, read
// through notification access. Commands go to the session that is shown.
public final class MediaSessions {
    public static final class Snapshot {
        public boolean available, playing, canPlayPause, canPrevious, canNext, canSeek;
        public String title, artist, source, key;
        public long positionMs, durationMs;
        public Bitmap artwork;
    }

    private final Context context;
    private final ComponentName listener;
    private MediaController controller;
    private String artworkKey = "";
    private Bitmap artwork;

    public MediaSessions(Context context) {
        this.context = context.getApplicationContext();
        listener = new ComponentName(this.context, MediaListener.class);
    }

    public boolean hasAccess() {
        if (Build.VERSION.SDK_INT >= 27) {
            NotificationManager manager = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
            return manager != null && manager.isNotificationListenerAccessGranted(listener);
        }
        String enabled = Settings.Secure.getString(context.getContentResolver(), "enabled_notification_listeners");
        return enabled != null && enabled.contains(listener.flattenToString());
    }

    private MediaController pick() {
        MediaSessionManager manager = (MediaSessionManager) context.getSystemService(Context.MEDIA_SESSION_SERVICE);
        List<MediaController> sessions = manager.getActiveSessions(listener);
        if (sessions == null || sessions.isEmpty()) return null;
        // The list is in Android's priority order; a session that is playing wins over a paused one.
        for (MediaController session : sessions) {
            PlaybackState state = session.getPlaybackState();
            if (state != null && state.getState() == PlaybackState.STATE_PLAYING) return session;
        }
        return sessions.get(0);
    }

    private String appName(String packageName) {
        try {
            PackageManager packages = context.getPackageManager();
            ApplicationInfo info = packages.getApplicationInfo(packageName, 0);
            return packages.getApplicationLabel(info).toString();
        } catch (Exception e) { return ""; }
    }

    // Throws SecurityException while notification access isn't granted.
    public Snapshot read() {
        controller = pick();
        Snapshot snapshot = new Snapshot();
        if (controller == null) { artworkKey = ""; artwork = null; return snapshot; }
        MediaMetadata metadata = controller.getMetadata();
        PlaybackState state = controller.getPlaybackState();
        String title = metadata == null ? null : first(metadata, MediaMetadata.METADATA_KEY_DISPLAY_TITLE, MediaMetadata.METADATA_KEY_TITLE);
        String artist = metadata == null ? null : first(metadata, MediaMetadata.METADATA_KEY_DISPLAY_SUBTITLE, MediaMetadata.METADATA_KEY_ARTIST, MediaMetadata.METADATA_KEY_ALBUM_ARTIST, MediaMetadata.METADATA_KEY_ALBUM);
        String key = controller.getPackageName() + "|" + title + "|" + artist;
        if (!key.equals(artworkKey)) {
            artworkKey = key; artwork = null;
            if (metadata != null) {
                Bitmap art = metadata.getBitmap(MediaMetadata.METADATA_KEY_ALBUM_ART);
                if (art == null) art = metadata.getBitmap(MediaMetadata.METADATA_KEY_ART);
                if (art == null) art = metadata.getBitmap(MediaMetadata.METADATA_KEY_DISPLAY_ICON);
                // Some apps publish artwork far larger than the card; keep a card-sized copy.
                try { artwork = Images.shrink(art, 320); } catch (Exception e) { artwork = null; }
            }
        }
        long actions = state == null ? 0 : state.getActions();
        // A few apps report no actions at all; then every basic control is offered and the app decides.
        boolean unknown = actions == 0;
        long duration = metadata == null ? 0 : Math.max(0, metadata.getLong(MediaMetadata.METADATA_KEY_DURATION));
        long position = 0;
        if (state != null) {
            position = state.getPosition();
            if (state.getState() == PlaybackState.STATE_PLAYING && state.getLastPositionUpdateTime() > 0) {
                long elapsed = SystemClock.elapsedRealtime() - state.getLastPositionUpdateTime();
                if (elapsed > 0 && elapsed < 86400000L) position += (long) (elapsed * (state.getPlaybackSpeed() == 0 ? 1 : state.getPlaybackSpeed()));
            }
        }
        snapshot.available = true;
        snapshot.playing = state != null && state.getState() == PlaybackState.STATE_PLAYING;
        snapshot.canPlayPause = unknown || (actions & (PlaybackState.ACTION_PLAY_PAUSE | PlaybackState.ACTION_PLAY | PlaybackState.ACTION_PAUSE)) != 0;
        snapshot.canPrevious = unknown || (actions & PlaybackState.ACTION_SKIP_TO_PREVIOUS) != 0;
        snapshot.canNext = unknown || (actions & PlaybackState.ACTION_SKIP_TO_NEXT) != 0;
        snapshot.canSeek = (actions & PlaybackState.ACTION_SEEK_TO) != 0 && duration > 0;
        snapshot.title = title == null || title.trim().isEmpty() ? "Untitled media" : title;
        snapshot.artist = artist == null ? "" : artist;
        snapshot.source = appName(controller.getPackageName()).toUpperCase(Locale.getDefault());
        snapshot.key = key;
        snapshot.durationMs = duration;
        snapshot.positionMs = Math.max(0, duration > 0 ? Math.min(duration, position) : position);
        snapshot.artwork = artwork;
        return snapshot;
    }

    private static String first(MediaMetadata metadata, String... keys) {
        for (String key : keys) {
            CharSequence value = metadata.getText(key);
            if (value != null && value.toString().trim().length() > 0) return value.toString();
        }
        return null;
    }

    // Quick Launch can send commands while Now Playing is hidden, so the session is found again if needed.
    public boolean command(String command) {
        if (controller == null) controller = pick();
        if (controller == null) return false;
        MediaController.TransportControls controls = controller.getTransportControls();
        if (command.equals("previous")) controls.skipToPrevious();
        else if (command.equals("next")) controls.skipToNext();
        else {
            PlaybackState state = controller.getPlaybackState();
            if (state != null && state.getState() == PlaybackState.STATE_PLAYING) controls.pause(); else controls.play();
        }
        return true;
    }

    public boolean seek(String expectedKey, long positionMs) {
        MediaController current = pick();
        if (current == null || current.getPlaybackState() == null || (current.getPlaybackState().getActions() & PlaybackState.ACTION_SEEK_TO) == 0) return false;
        MediaMetadata metadata = current.getMetadata();
        String title = metadata == null ? null : first(metadata, MediaMetadata.METADATA_KEY_DISPLAY_TITLE, MediaMetadata.METADATA_KEY_TITLE);
        String artist = metadata == null ? null : first(metadata, MediaMetadata.METADATA_KEY_DISPLAY_SUBTITLE, MediaMetadata.METADATA_KEY_ARTIST, MediaMetadata.METADATA_KEY_ALBUM_ARTIST, MediaMetadata.METADATA_KEY_ALBUM);
        if (!(current.getPackageName() + "|" + title + "|" + artist).equals(expectedKey)) return false;
        long duration = metadata == null ? 0 : metadata.getLong(MediaMetadata.METADATA_KEY_DURATION);
        if (duration <= 0) return false;
        current.getTransportControls().seekTo(Math.max(0, Math.min(duration - 1, positionMs)));
        return true;
    }
}
