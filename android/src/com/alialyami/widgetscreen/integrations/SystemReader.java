package com.alialyami.widgetscreen.integrations;

import android.app.ActivityManager;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.net.TrafficStats;
import android.os.BatteryManager;
import android.os.Environment;
import android.os.StatFs;
import android.os.SystemClock;

// System stats on Android. Apps can't read CPU or GPU usage on modern Android, so the Windows CPU and GPU
// rows become Battery and Storage; RAM, network speed and uptime carry over. Each reading is optional.
public final class SystemReader {
    public static final class Snapshot {
        public boolean batteryAvailable, memoryAvailable, storageAvailable, networkAvailable;
        public int batteryPercent;
        public boolean charging;
        public double batteryTemperature = Double.NaN;
        public long memoryUsed, memoryTotal, storageUsed, storageTotal, uptimeMs;
        public double download, upload;
    }

    private final Context context;
    private long lastReceived = -1, lastSent, lastNetwork;

    public SystemReader(Context context) { this.context = context.getApplicationContext(); }

    public Snapshot read(boolean battery, boolean memory, boolean storage, boolean network, boolean uptime) {
        Snapshot snapshot = new Snapshot();
        if (battery) {
            // The battery broadcast is sticky: reading it registers nothing.
            Intent status = context.registerReceiver(null, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
            if (status != null) {
                int level = status.getIntExtra(BatteryManager.EXTRA_LEVEL, -1), scale = status.getIntExtra(BatteryManager.EXTRA_SCALE, -1);
                if (level >= 0 && scale > 0) {
                    snapshot.batteryAvailable = true;
                    snapshot.batteryPercent = Math.round(100f * level / scale);
                    int plugged = status.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0), state = status.getIntExtra(BatteryManager.EXTRA_STATUS, -1);
                    snapshot.charging = plugged != 0 || state == BatteryManager.BATTERY_STATUS_CHARGING || state == BatteryManager.BATTERY_STATUS_FULL;
                    int temperature = status.getIntExtra(BatteryManager.EXTRA_TEMPERATURE, Integer.MIN_VALUE);
                    if (temperature != Integer.MIN_VALUE && temperature > -400 && temperature < 1200) snapshot.batteryTemperature = temperature / 10.0;
                }
            }
        }
        if (memory) {
            ActivityManager manager = (ActivityManager) context.getSystemService(Context.ACTIVITY_SERVICE);
            if (manager != null) {
                ActivityManager.MemoryInfo info = new ActivityManager.MemoryInfo();
                manager.getMemoryInfo(info);
                if (info.totalMem > 0) { snapshot.memoryAvailable = true; snapshot.memoryTotal = info.totalMem; snapshot.memoryUsed = info.totalMem - info.availMem; }
            }
        }
        if (storage) {
            try {
                StatFs stat = new StatFs(Environment.getDataDirectory().getPath());
                if (stat.getTotalBytes() > 0) { snapshot.storageAvailable = true; snapshot.storageTotal = stat.getTotalBytes(); snapshot.storageUsed = stat.getTotalBytes() - stat.getAvailableBytes(); }
            } catch (IllegalArgumentException ignored) { }
        }
        if (uptime) snapshot.uptimeMs = SystemClock.elapsedRealtime();
        if (network) {
            long received = TrafficStats.getTotalRxBytes(), sent = TrafficStats.getTotalTxBytes(), now = SystemClock.elapsedRealtime();
            if (received == TrafficStats.UNSUPPORTED || sent == TrafficStats.UNSUPPORTED) lastReceived = -1;
            else {
                double seconds = (now - lastNetwork) / 1000.0;
                if (lastReceived >= 0 && seconds > 0) {
                    snapshot.networkAvailable = true;
                    snapshot.download = Math.max(0, received - lastReceived) / seconds;
                    snapshot.upload = Math.max(0, sent - lastSent) / seconds;
                }
                lastReceived = received; lastSent = sent; lastNetwork = now;
            }
        } else lastReceived = -1;
        return snapshot;
    }
}
