package com.alialyami.widgetscreen.integrations;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.media.AudioAttributes;
import android.media.AudioDeviceInfo;
import android.media.AudioManager;
import android.media.MediaRouter2;
import android.os.Build;
import android.provider.Settings;

import java.util.List;

// Android has no per-app volume, so the Windows mixer becomes the device's volume streams: media as the
// master slider, then ringtone, notifications, alarms and calls. The output can't be moved per app;
// tapping the media volume opens Android's own output switcher instead.
public final class AudioControl {
    public static final int MEDIA = AudioManager.STREAM_MUSIC;
    public static final int[] OTHER_STREAMS = { AudioManager.STREAM_RING, AudioManager.STREAM_NOTIFICATION, AudioManager.STREAM_ALARM, AudioManager.STREAM_VOICE_CALL };
    private final Context context;
    private final AudioManager audio;

    public AudioControl(Context context) {
        this.context = context.getApplicationContext();
        audio = (AudioManager) this.context.getSystemService(Context.AUDIO_SERVICE);
    }

    public boolean available() { return audio != null; }
    public boolean fixed() { return audio == null || audio.isVolumeFixed(); }

    public static String name(int stream) {
        switch (stream) {
            case AudioManager.STREAM_RING: return "Ringtone";
            case AudioManager.STREAM_NOTIFICATION: return "Notifications";
            case AudioManager.STREAM_ALARM: return "Alarms";
            case AudioManager.STREAM_VOICE_CALL: return "Calls";
            default: return "Media";
        }
    }

    private int min(int stream) { return Build.VERSION.SDK_INT >= 28 ? audio.getStreamMinVolume(stream) : 0; }

    public int percent(int stream) {
        int min = min(stream), max = audio.getStreamMaxVolume(stream);
        if (max <= min) return 0;
        return Math.round(100f * (audio.getStreamVolume(stream) - min) / (max - min));
    }

    public int steps(int stream) { return Math.max(1, audio.getStreamMaxVolume(stream) - min(stream)); }

    // Throws SecurityException when Do Not Disturb blocks the change.
    public void setPercent(int stream, float percent) {
        int min = min(stream), max = audio.getStreamMaxVolume(stream);
        int index = min + Math.round(Math.max(0, Math.min(100, percent)) / 100f * (max - min));
        if (index != audio.getStreamVolume(stream)) audio.setStreamVolume(stream, index, 0);
    }

    public boolean muted() { return audio.isStreamMute(MEDIA); }
    public void toggleMute() { audio.adjustStreamVolume(MEDIA, AudioManager.ADJUST_TOGGLE_MUTE, 0); }
    public void step(boolean up) { audio.adjustStreamVolume(MEDIA, up ? AudioManager.ADJUST_RAISE : AudioManager.ADJUST_LOWER, 0); }

    public String outputName() {
        AudioDeviceInfo device = null;
        if (Build.VERSION.SDK_INT >= 33) {
            List<AudioDeviceInfo> devices = audio.getAudioDevicesForAttributes(new AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_MEDIA).build());
            if (devices != null && !devices.isEmpty()) device = devices.get(0);
        }
        if (device == null) {
            // Older Android doesn't say which output media uses; the highest-priority connected one is it.
            int best = -1;
            for (AudioDeviceInfo candidate : audio.getDevices(AudioManager.GET_DEVICES_OUTPUTS)) {
                int rank = rank(candidate.getType());
                if (rank > best) { best = rank; device = candidate; }
            }
        }
        if (device == null) return "Output unavailable";
        switch (device.getType()) {
            case AudioDeviceInfo.TYPE_BUILTIN_SPEAKER: return "This device's speaker";
            case AudioDeviceInfo.TYPE_BUILTIN_EARPIECE: return "Earpiece";
            case AudioDeviceInfo.TYPE_WIRED_HEADPHONES:
            case AudioDeviceInfo.TYPE_WIRED_HEADSET: return "Wired headphones";
            case AudioDeviceInfo.TYPE_HDMI: return "HDMI";
            default:
                CharSequence product = device.getProductName();
                return product == null || product.length() == 0 ? "Connected device" : product.toString();
        }
    }

    private static int rank(int type) {
        switch (type) {
            case AudioDeviceInfo.TYPE_BLUETOOTH_A2DP: return 6;
            case 26: /* TYPE_BLE_HEADSET */ case 27: /* TYPE_BLE_SPEAKER */ return 5;
            case AudioDeviceInfo.TYPE_WIRED_HEADPHONES: case AudioDeviceInfo.TYPE_WIRED_HEADSET: return 4;
            case AudioDeviceInfo.TYPE_USB_HEADSET: case AudioDeviceInfo.TYPE_USB_DEVICE: return 3;
            case AudioDeviceInfo.TYPE_HDMI: return 2;
            case AudioDeviceInfo.TYPE_BUILTIN_SPEAKER: return 1;
            default: return 0;
        }
    }

    // Android's output switcher on 14 and later, the volume panel on 10–13, otherwise sound settings.
    public void openOutputSwitcher(Activity activity) {
        if (Build.VERSION.SDK_INT >= 34) {
            try { if (MediaRouter2.getInstance(context).showSystemOutputSwitcher()) return; } catch (Exception ignored) { }
        }
        if (Build.VERSION.SDK_INT >= 29) {
            try { activity.startActivity(new Intent(Settings.Panel.ACTION_VOLUME)); return; } catch (Exception ignored) { }
        }
        openSoundSettings(activity);
    }

    public static void openSoundSettings(Activity activity) {
        activity.startActivity(new Intent(Settings.ACTION_SOUND_SETTINGS));
    }
}
