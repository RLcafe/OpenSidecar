package com.alialyami.widgetscreen;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Insets;
import android.net.Uri;
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

// Startup and the window: edge-to-edge, optional hidden system bars, keep-awake, back handling and the
// Spotify sign-in callback (widgetscreen://spotify-callback).
public final class MainActivity extends Activity {
    private Dashboard dashboard;
    private FrameLayout root;
    private boolean hideBars = true;

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
        handle(getIntent());
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
        if (settings.keepScreenOn) getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        else getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        getWindow().getDecorView().setBackgroundColor(theme.page);
        if (hideBars != settings.hideSystemBars) { hideBars = settings.hideSystemBars; applySystemBars(); }
    }

    @Override public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus && hideBars) applySystemBars();
    }

    @Override protected void onStart() { super.onStart(); dashboard.onStart(); }
    @Override protected void onStop() { dashboard.onStop(); super.onStop(); }

    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        handle(intent);
    }

    private void handle(Intent intent) {
        Uri data = intent == null ? null : intent.getData();
        if (data == null || !"widgetscreen".equals(data.getScheme())) return;
        // Handle the callback once, even if the activity is shown again with the same intent.
        setIntent(new Intent(this, MainActivity.class));
        dashboard.handleSpotifyCallback(data.toString());
    }

    @Override public void onBackPressed() {
        dashboard.onBack(new Runnable() { @Override public void run() { MainActivity.super.onBackPressed(); } });
    }
}
