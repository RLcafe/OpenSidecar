package com.alialyami.widgetscreen.widgets;

import android.graphics.Bitmap;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcLink;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Images;
import com.alialyami.widgetscreen.views.GlyphView;
import com.alialyami.widgetscreen.views.TouchSlider;

// Now Playing on the PC: whatever Windows says is playing, with its controls, run on the PC.
public final class MediaWidget extends Widget {
    private TextView source, title, artist, position, duration, hint;
    private ImageView art;
    private TouchSlider seek;
    private GlyphView previous, next, play;
    private String artKey = "", hintText = "";
    private Bitmap artwork;

    public MediaWidget(Dashboard dash) { super("Media", dash); }

    @Override public String part() { return "media"; }

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
        art.setScaleType(ImageView.ScaleType.FIT_CENTER);
        cover.addView(art, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        content.addView(cover, ui.margins(ui.ll(ui.px(104), ui.px(104)), 0, 0, 12, 0));
        LinearLayout text = ui.column();
        title = Ui.wrap(ui.heading("Nothing playing"), 2);
        title.setTypeface(Ui.MEDIUM);
        text.addView(title, ui.ll(Ui.MATCH, Ui.WRAP));
        artist = Ui.wrap(ui.smallText(""), 2);
        text.addView(artist, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 8, 0, 0));
        content.addView(text, ui.margins(ui.ll(0, Ui.WRAP, 1), 8, 0, 0, 0));
        column.addView(content, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 12, 0, 4));

        LinearLayout timeline = ui.row();
        position = Ui.tabular(ui.smallText("—:—"));
        timeline.addView(position, ui.ll(ui.px(52), Ui.WRAP));
        seek = new TouchSlider(ui.context, ui).range(0, 1000, 0);
        seek.setContentDescription("Seek the PC's media");
        seek.setListener(new TouchSlider.Listener() {
            @Override public void changed(TouchSlider slider, float value, boolean done) {
                PcState.Media media = media();
                if (media == null || media.durationMs <= 0) return;
                long target = (long) (value / 1000f * media.durationMs);
                position.setText(Format.mediaTime(target));
                if (done) seekTo(media.key, target);
            }
        });
        timeline.addView(seek, ui.ll(0, ui.px(48), 1));
        duration = Ui.tabular(ui.smallText("—:—"));
        duration.setGravity(Gravity.END);
        timeline.addView(duration, ui.ll(ui.px(60), Ui.WRAP));
        column.addView(timeline, ui.ll(Ui.MATCH, ui.px(48)));

        LinearLayout controls = ui.row();
        controls.setGravity(Gravity.CENTER);
        previous = ui.iconButton("previous", "Previous track", 22, false, new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "media.previous")); } });
        controls.addView(previous, ui.margins(ui.ll(ui.px(56), ui.px(48)), 0, 0, 14, 0));
        play = new GlyphView(ui.context, "play", Theme.ON_ACCENT, ui.fpx(23));
        play.setBackground(ui.pressable(ui.theme.accent, 0, 0, 12));
        play.setClickable(true);
        play.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "media.toggle")); } });
        controls.addView(play, ui.ll(ui.px(76), ui.px(48)));
        next = ui.iconButton("next", "Next track", 22, false, new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "media.next")); } });
        controls.addView(next, ui.margins(ui.ll(ui.px(56), ui.px(48)), 14, 0, 0, 0));
        column.addView(controls, ui.ll(Ui.MATCH, ui.px(54)));

        hint = ui.smallText("");
        column.addView(hint, ui.ll(Ui.MATCH, ui.px(26)));
        return ui.card(column, 24);
    }

    private PcState.Media media() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.media; }

    @Override public void render() {
        if (!built()) return;
        PcState.Media m = media();
        boolean available = m != null && !m.error && m.available;
        if (!dash.online()) { title.setText("PC offline"); artist.setText(dash.offlineText()); }
        else if (m != null && m.error) { title.setText("Media controls unavailable"); artist.setText("Windows isn't sharing what's playing right now."); }
        else { title.setText(available ? m.title : "Nothing playing"); artist.setText(available ? m.artist : "Start music or a video in any app on the PC."); }
        source.setText(available ? m.source : "");
        position.setText(available ? Format.mediaTime((long) m.positionMs) : "—:—");
        duration.setText(available && m.durationMs > 0 ? Format.mediaTime((long) m.durationMs) : "—:—");
        if (!seek.dragging()) seek.setValue(available && m.durationMs > 0 ? (float) (1000 * m.positionMs / m.durationMs) : 0);
        Ui.enable(seek, available && m.canSeek);
        play.setGlyph(available && m.playing ? "pause" : "play");
        play.setContentDescription(available && m.playing ? "Pause playback" : "Play media");
        Ui.enable(play, available && m.canPlayPause);
        Ui.enable(previous, available && m.canPrevious);
        Ui.enable(next, available && m.canNext);
        hint.setText(hintText);
        String key = available && m.art ? m.key : "";
        if (!key.equals(artKey)) {
            artKey = key; artwork = null;
            if (!key.isEmpty()) loadArt(key);
        }
        art.setImageBitmap(artwork);
    }

    private void loadArt(final String key) {
        dash.pc.send(new com.alialyami.widgetscreen.integrations.Pc.Call<Bitmap>() {
            @Override public Bitmap run(PcLink link) throws Exception { return Images.decode(link.art(key), 232); }
        }, new Async.Done<Bitmap>() {
            @Override public void ok(Bitmap bitmap) { if (key.equals(artKey)) { artwork = bitmap; if (built()) art.setImageBitmap(bitmap); } }
        }, null);
    }

    private void seekTo(String key, long target) {
        dash.pc.command(Json.map("action", "media.seek", "key", key, "positionMs", target), new Async.Done<String>() {
            @Override public void ok(String message) { hintText = ""; dash.refreshSoon(); }
        }, new Async.Failed() {
            @Override public void error(Exception error) { hintText = com.alialyami.widgetscreen.integrations.Pc.message(error); if (built()) hint.setText(hintText); }
        });
    }
}
