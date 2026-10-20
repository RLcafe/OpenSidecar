package com.alialyami.widgetscreen.widgets;

import android.content.Intent;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.integrations.MediaSessions;
import com.alialyami.widgetscreen.views.GlyphView;
import com.alialyami.widgetscreen.views.TouchSlider;

// Now Playing: whatever app is playing on this device, through Android's media sessions.
public final class MediaWidget extends Widget {
    private TextView source, title, artist, position, duration, hint;
    private ImageView art;
    private TouchSlider seek;
    private GlyphView previous, next, play;
    private LinearLayout controls;
    private TextView allow;
    private MediaSessions.Snapshot snapshot;
    private boolean noAccess, failed, commandBusy;

    public MediaWidget(Dashboard dash) { super("Media", dash); }

    @Override protected View create() {
        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(ui.label("NOW PLAYING"), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        source = ui.smallText("");
        source.setMaxWidth(ui.px(220));
        header.addView(source, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));

        LinearLayout content = ui.row();
        FrameLayout cover = new FrameLayout(ui.context);
        cover.setBackground(ui.shape(ui.theme.clockSurface, 0, 0, 14));
        cover.setClipToOutline(true);
        cover.addView(new GlyphView(ui.context, "music", ui.theme.accent, ui.fpx(46)), new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        art = new ImageView(ui.context);
        art.setScaleType(ImageView.ScaleType.CENTER_CROP);
        cover.addView(art, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        content.addView(cover, ui.margins(ui.ll(ui.px(104), ui.px(104)), 0, 0, 12, 0));
        LinearLayout text = ui.column();
        title = Ui.wrap(ui.heading("Nothing playing"), 2);
        title.setTypeface(Ui.MEDIUM);
        text.addView(title, ui.ll(Ui.MATCH, Ui.WRAP));
        artist = Ui.wrap(ui.smallText("Start music or a video in any app."), 2);
        text.addView(artist, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 8, 0, 0));
        content.addView(text, ui.margins(ui.ll(0, Ui.WRAP, 1), 8, 0, 0, 0));
        column.addView(content, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 12, 0, 4));

        LinearLayout timeline = ui.row();
        position = Ui.tabular(ui.smallText("—:—"));
        timeline.addView(position, ui.ll(ui.px(52), Ui.WRAP));
        seek = new TouchSlider(ui.context, ui).range(0, 1000, 0);
        seek.setContentDescription("Seek current media");
        seek.setListener(new TouchSlider.Listener() {
            @Override public void changed(TouchSlider slider, float value, boolean done) {
                if (snapshot == null || snapshot.durationMs <= 0) return;
                long target = (long) (value / 1000f * snapshot.durationMs);
                position.setText(Format.mediaTime(target));
                if (done) seekTo(snapshot.key, target);
            }
        });
        timeline.addView(seek, ui.ll(0, ui.px(48), 1));
        duration = Ui.tabular(ui.smallText("—:—"));
        duration.setGravity(Gravity.END);
        timeline.addView(duration, ui.ll(ui.px(60), Ui.WRAP));
        column.addView(timeline, ui.ll(Ui.MATCH, ui.px(48)));

        FrameLayout bottom = new FrameLayout(ui.context);
        controls = ui.row();
        controls.setGravity(Gravity.CENTER);
        previous = ui.iconButton("previous", "Previous track", 22, false, new View.OnClickListener() { @Override public void onClick(View v) { command("previous"); } });
        controls.addView(previous, ui.margins(ui.ll(ui.px(56), ui.px(48)), 0, 0, 14, 0));
        play = new GlyphView(ui.context, "play", com.alialyami.widgetscreen.core.Theme.ON_ACCENT, ui.fpx(23));
        play.setBackground(ui.pressable(ui.theme.accent, 0, 0, 12));
        play.setClickable(true);
        play.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { command("toggle"); } });
        controls.addView(play, ui.ll(ui.px(76), ui.px(48)));
        next = ui.iconButton("next", "Next track", 22, false, new View.OnClickListener() { @Override public void onClick(View v) { command("next"); } });
        controls.addView(next, ui.margins(ui.ll(ui.px(56), ui.px(48)), 14, 0, 0, 0));
        bottom.addView(controls, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        allow = ui.accentButton("Allow access", new View.OnClickListener() {
            @Override public void onClick(View v) {
                try { dash.activity.startActivity(new Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)); }
                catch (Exception e) { dash.notify("Open Android Settings → Notifications → Device & app notifications, then allow Widget Screen."); }
            }
        });
        bottom.addView(allow, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.CENTER));
        column.addView(bottom, ui.ll(Ui.MATCH, ui.px(54)));

        hint = ui.smallText("");
        column.addView(hint, ui.ll(Ui.MATCH, ui.px(26)));
        return ui.card(column, 24);
    }

    @Override public void render() {
        if (!built()) return;
        allow.setVisibility(noAccess ? View.VISIBLE : View.GONE);
        controls.setVisibility(noAccess ? View.GONE : View.VISIBLE);
        if (noAccess) {
            title.setText("Allow notification access");
            artist.setText("Android only shares what's playing with apps you allow. Turn on Widget Screen there.");
            source.setText(""); position.setText("—:—"); duration.setText("—:—"); art.setImageBitmap(null); hint.setText("");
            seek.setValue(0); Ui.enable(seek, false);
            return;
        }
        if (failed) {
            title.setText("Media controls unavailable"); artist.setText("Android isn't sharing what's playing right now.");
            source.setText(""); position.setText("—:—"); duration.setText("—:—"); art.setImageBitmap(null);
            hint.setText("Retrying automatically.");
            seek.setValue(0); Ui.enable(seek, false);
            Ui.enable(play, false); Ui.enable(previous, false); Ui.enable(next, false);
            return;
        }
        MediaSessions.Snapshot s = snapshot;
        boolean available = s != null && s.available;
        title.setText(available ? s.title : "Nothing playing");
        artist.setText(available ? s.artist : "Start music or a video in any app.");
        source.setText(available ? s.source : "");
        position.setText(available ? Format.mediaTime(s.positionMs) : "—:—");
        duration.setText(available && s.durationMs > 0 ? Format.mediaTime(s.durationMs) : "—:—");
        if (!seek.dragging()) seek.setValue(available && s.durationMs > 0 ? 1000f * s.positionMs / s.durationMs : 0);
        Ui.enable(seek, available && s.canSeek);
        art.setImageBitmap(available ? s.artwork : null);
        play.setGlyph(available && s.playing ? "pause" : "play");
        play.setContentDescription(available && s.playing ? "Pause playback" : "Play media");
        Ui.enable(play, available && s.canPlayPause && !commandBusy);
        Ui.enable(previous, available && s.canPrevious && !commandBusy);
        Ui.enable(next, available && s.canNext && !commandBusy);
    }

    public void refresh() {
        if (!active() || seek.dragging() || commandBusy) return;
        noAccess = !dash.media.hasAccess();
        if (noAccess) { snapshot = null; failed = false; render(); return; }
        try { snapshot = dash.media.read(); failed = false; hint.setText(""); }
        catch (Exception e) { snapshot = null; failed = !(e instanceof SecurityException); noAccess = e instanceof SecurityException; }
        render();
    }

    public void command(String command) {
        if (commandBusy) return;
        commandBusy = true;
        try { if (!dash.media.command(command)) dash.notify("Nothing is playing to control."); }
        catch (Exception e) { dash.notify("Media controls aren't available. Check that your media app is open."); }
        finally {
            commandBusy = false;
            // Give the app a moment to update its session before reading it again.
            com.alialyami.widgetscreen.integrations.Async.MAIN.postDelayed(new Runnable() { @Override public void run() { refresh(); } }, 400);
        }
    }

    private void seekTo(String key, long target) {
        try { if (!dash.media.seek(key, target)) hint.setText("Couldn't jump there. The app may not allow it, or the song changed."); }
        catch (Exception e) { hint.setText("This media app doesn't allow jumping to a position."); }
    }

    @Override public void tick(int ticks, boolean quiet) {
        if (ticks % (quiet ? 6 : 3) == 0 || ticks == 1) refresh();
    }
}
