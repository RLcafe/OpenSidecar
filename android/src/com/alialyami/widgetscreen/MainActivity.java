package com.alialyami.widgetscreen;

import android.app.Activity;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.ActivityInfo;
import android.graphics.Color;
import android.graphics.Insets;
import android.os.BatteryManager;
import android.os.Build;
import android.os.Bundle;
import android.view.DisplayCutout;
import android.view.View;
import android.view.Window;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import android.widget.FrameLayout;

import com.alialyami.widgetscreen.core.AppSettings;
import com.alialyami.widgetscreen.core.Theme;

// Startup and the window: edge-to-edge, optional hidden system bars, keep-awake (always, while charging, or never)
// and back handling (the system's back callback on Android 13+, onBackPressed before).
public final class MainActivity extends Activity {
    private Dashboard dashboard;
    private FrameLayout root;
    private boolean hideBars = true, charging;
    private AppSettings windowSettings;
    private final BroadcastReceiver battery = new BroadcastReceiver() {
        @Override public void onReceive(Context context, Intent intent) {
            int status = intent.getIntExtra(BatteryManager.EXTRA_STATUS, -1);
            boolean now = status == BatteryManager.BATTERY_STATUS_CHARGING || status == BatteryManager.BATTERY_STATUS_FULL;
            if (now != charging) { charging = now; applyKeepAwake(); }
        }
    };

    @Override protected void onCreate(Bundle saved) {
        super.onCreate(saved);
        Window window = getWindow();
        window.setStatusBarColor(Color.TRANSPARENT);
        window.setNavigationBarColor(Color.TRANSPARENT);
        if (Build.VERSION.SDK_INT >= 28) {
            WindowManager.LayoutParams attributes = window.getAttributes();
            attributes.layoutInDisplayCutoutMode = WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
            window.setAttributes(attributes);
        }
        if (Build.VERSION.SDK_INT >= 30) window.setDecorFitsSystemWindows(false);
        root = new FrameLayout(this);
        setContentView(root);
        root.setOnApplyWindowInsetsListener(new View.OnApplyWindowInsetsListener() {
            @Override public WindowInsets onApplyWindowInsets(View view, WindowInsets insets) { applyInsets(insets); return insets; }
        });
        dashboard = new Dashboard(this);
        hideBars = dashboard.settings.hideSystemBars;
        applySystemBars();
        dashboard.attach(root);
        if (getIntent() != null && getIntent().hasExtra(com.alialyami.widgetscreen.integrations.ClaudeNotifier.EXTRA)) dashboard.showClaude();
        // Android 13+ routes Back through this callback (predictive back); older versions call onBackPressed.
        if (Build.VERSION.SDK_INT >= 33) {
            getOnBackInvokedDispatcher().registerOnBackInvokedCallback(android.window.OnBackInvokedDispatcher.PRIORITY_DEFAULT, new android.window.OnBackInvokedCallback() {
                @Override public void onBackInvoked() { back(); }
            });
        }
    }

    // Hidden bars appear over the dashboard when swiped in, so only the display cutout pads it then;
    // visible bars pad it. The keyboard pads only the Settings panel, so typing never rebuilds the dashboard.
    private void applyInsets(WindowInsets insets) {
        int left = 0, top = 0, right = 0, bottom = 0, ime = 0;
        if (Build.VERSION.SDK_INT >= 30) {
            Insets bars = insets.getInsets(WindowInsets.Type.displayCutout() | (hideBars ? 0 : WindowInsets.Type.systemBars()));
            left = bars.left; top = bars.top; right = bars.right; bottom = bars.bottom;
            ime = Math.max(0, insets.getInsets(WindowInsets.Type.ime()).bottom - bottom);
        } else {
            if (!hideBars) { left = insets.getSystemWindowInsetLeft(); top = insets.getSystemWindowInsetTop(); right = insets.getSystemWindowInsetRight(); bottom = insets.getSystemWindowInsetBottom(); }
            if (Build.VERSION.SDK_INT >= 28) {
                DisplayCutout cutout = insets.getDisplayCutout();
                if (cutout != null) {
                    left = Math.max(left, cutout.getSafeInsetLeft()); top = Math.max(top, cutout.getSafeInsetTop());
                    right = Math.max(right, cutout.getSafeInsetRight()); bottom = Math.max(bottom, cutout.getSafeInsetBottom());
                }
            }
        }
        if (root.getPaddingLeft() != left || root.getPaddingTop() != top || root.getPaddingRight() != right || root.getPaddingBottom() != bottom) root.setPadding(left, top, right, bottom);
        if (dashboard != null) dashboard.imeInset(ime);
    }

    private void applySystemBars() {
        if (Build.VERSION.SDK_INT >= 30) {
            WindowInsetsController controller = getWindow().getInsetsController();
            if (controller != null) {
                if (hideBars) {
                    controller.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
                    controller.hide(WindowInsets.Type.systemBars());
                } else controller.show(WindowInsets.Type.systemBars());
            }
        } else {
            int flags = View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN;
            if (hideBars) flags |= View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY | View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION;
            getWindow().getDecorView().setSystemUiVisibility(flags);
        }
        root.requestApplyInsets();
    }

    // Called on every dashboard rebuild with the saved settings and the current palette.
    public void applyWindow(AppSettings settings, Theme theme) {
        windowSettings = settings;
        applyKeepAwake();
        getWindow().getDecorView().setBackgroundColor(theme.page);
        int orientation = settings.landscapeOnly ? ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE : ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED;
        if (getRequestedOrientation() != orientation) setRequestedOrientation(orientation);
        if (hideBars != settings.hideSystemBars) { hideBars = settings.hideSystemBars; applySystemBars(); }
    }

    private void applyKeepAwake() {
        if (windowSettings == null) return;
        if (windowSettings.keepsScreenOn(charging)) getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        else getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
    }

    @Override public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus && hideBars) applySystemBars();
    }

    @Override protected void onStart() {
        super.onStart();
        // The battery broadcast is sticky, so registering answers at once with the current state.
        try {
            Intent state = registerReceiver(battery, new IntentFilter(Intent.ACTION_BATTERY_CHANGED));
            if (state != null) battery.onReceive(this, state);
        } catch (Exception ignored) { }
        dashboard.onStart();
    }

    @Override protected void onStop() {
        try { unregisterReceiver(battery); } catch (Exception ignored) { }
        dashboard.onStop();
        super.onStop();
    }

    // A tapped Claude notification brings the app forward on the Claude card's page.
    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        if (intent != null && intent.hasExtra(com.alialyami.widgetscreen.integrations.ClaudeNotifier.EXTRA) && dashboard != null) dashboard.showClaude();
    }

    private void back() {
        dashboard.onBack(new Runnable() { @Override public void run() { moveTaskToBack(true); } });
    }

    @SuppressWarnings("deprecation")
    @Override public void onBackPressed() {
        if (Build.VERSION.SDK_INT >= 33) { super.onBackPressed(); return; }
        back();
    }
}
