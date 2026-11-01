package com.alialyami.widgetscreen.widgets;

import android.graphics.Bitmap;
import android.os.SystemClock;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcLink;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Images;
import com.alialyami.widgetscreen.views.GlyphView;
import com.alialyami.widgetscreen.views.TouchSlider;

// Now Playing on the PC: whatever Windows says is playing, with its controls, run on the PC. The position keeps
// moving between readings, and the playing app's volume is one tap away.
public final class MediaWidget extends Widget {
    private TextView source, title, artist, position, duration, hint, volumeSubject, volumeNote, volumePercent;
    private ImageView art;
    private TouchSlider seek, volume;
    private GlyphView previous, next, play, volumeButton;
    private FrameLayout root, volumePanel;
    private String artKey = "", hintText = "", volumeSession;
    private Bitmap artwork;
    private long lastVolumeSent;

    public MediaWidget(Dashboard dash) { super("Media", dash); }

    @Override public String part() { return "media"; }

    @Override protected View create() {
        volumePanel = null;
        // A short card is one row of cover and song over the buttons; a narrow one draws the buttons closer (as on the PC).
        boolean folded = CardSizing.mediaShort(height), narrow = CardSizing.mediaNarrow(width);
        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(strip(), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        source = sticker(false);
        source.setMaxWidth(ui.px(220));
        header.addView(source, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));

        // Three designs (CardSizing.media, as on the PC): SMALL is one row of cover and song over the buttons; MEDIUM puts
        // the cover beside the song over the seek row; LARGE centres a big cover over the song.
        boolean large = CardSizing.media(width, height) == CardSizing.LARGE;
        LinearLayout content = large ? ui.column() : ui.row();
        if (large) content.setGravity(Gravity.CENTER);
        FrameLayout cover = new FrameLayout(ui.context);
        // The cover grows with the card (CardSizing, as on the PC); larger text takes more of the room under it.
        double size = large ? Math.max(120, CardSizing.mediaCover(width, height) - Math.round((ui.textScale - 1) * 80)) : CardSizing.mediaArt(width, height);
        cover.setBackground(ui.shape(ui.theme.clockSurface, 0, 0, size < 80 ? 10 : large ? 20 : 14));
        cover.setClipToOutline(true);
        cover.addView(new GlyphView(ui.context, "music", ui.theme.accent, ui.fpx((float) Math.round(Math.min(large ? 96 : 46, size * 0.44)))), new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        art = new ImageView(ui.context);
        art.setScaleType(ImageView.ScaleType.FIT_CENTER);
        cover.addView(art, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        content.addView(cover, large ? ui.ll(ui.px(size), ui.px(size)) : ui.margins(ui.ll(ui.px(size), ui.px(size)), 0, 0, 12, 0));
        LinearLayout text = ui.column();
        // One line for the title, as on the PC; the whole name is in the content description.
        title = Ui.wrap(large ? ui.text("Nothing playing", 28 * ui.textScale, ui.theme.text) : ui.heading("Nothing playing"), 1);
        title.setTypeface(Ui.MEDIUM);
        if (large) title.setGravity(Gravity.CENTER);
        text.addView(title, ui.ll(Ui.MATCH, Ui.WRAP));
        artist = Ui.wrap(ui.smallText(""), folded || large ? 1 : 2);
        if (large) artist.setGravity(Gravity.CENTER);
        text.addView(artist, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, folded ? 2 : large ? 6 : 8, 0, 0));
        if (large) content.addView(text, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 14, 0, 0));
        else content.addView(text, ui.margins(ui.ll(0, Ui.WRAP, 1), 8, 0, 0, 0));
        column.addView(content, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, folded ? 8 : 12, 0, 4));

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
        if (!folded) column.addView(timeline, ui.ll(Ui.MATCH, ui.px(48)));

        FrameLayout controlsRow = new FrameLayout(ui.context);
        LinearLayout controls = ui.row();
        controls.setGravity(Gravity.CENTER);
        int side = narrow ? 48 : 56, gap = narrow ? 4 : 14;
        previous = ui.iconButton("previous", "Previous track", 22, false, new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "media.previous")); } });
        controls.addView(previous, ui.margins(ui.ll(ui.px(side), ui.px(48)), 0, 0, gap, 0));
        play = new GlyphView(ui.context, "play", Theme.ON_ACCENT, ui.fpx(23));
        play.setBackground(ui.pressable(ui.theme.accent, 0, 0, 12));
        play.setClickable(true);
        play.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "media.toggle")); } });
        controls.addView(play, ui.ll(ui.px(narrow ? 56 : 76), ui.px(48)));
        next = ui.iconButton("next", "Next track", 22, false, new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "media.next")); } });
        controls.addView(next, ui.margins(ui.ll(ui.px(side), ui.px(48)), gap, 0, 0, 0));
        controlsRow.addView(controls, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.CENTER));
        // The playing app's volume, one tap away: opens a small panel with that app's mixer slider.
        volumeButton = ui.iconButton("volume", "Volume of the playing app", 22, false, new View.OnClickListener() { @Override public void onClick(View v) { openVolume(); } });
        controlsRow.addView(volumeButton, ui.frame(ui.px(48), ui.px(48), Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(controlsRow, ui.ll(Ui.MATCH, ui.px(54)));

        hint = ui.smallText("");
        if (!folded) column.addView(hint, ui.ll(Ui.MATCH, ui.px(26)));
        root = ui.card(column, 24);
        return root;
    }

    private PcState.Media media() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.media; }

    @Override public void render() {
        if (!built()) return;
        PcState.Media m = media();
        boolean available = m != null && !m.error && m.available;
        if (!dash.online()) { title.setText("PC offline"); artist.setText(dash.offlineText()); }
        else if (m != null && m.error) { title.setText("Media controls unavailable"); artist.setText("Windows isn't sharing what's playing right now."); }
        else { title.setText(available ? m.title : "Nothing playing"); artist.setText(available ? m.artist : "Start music or a video in any app on the PC."); }
        title.setContentDescription(available ? m.title : null);
        source.setText(available ? m.source : "");
        // Between readings a playing song keeps moving here; the next reading corrects it.
        double shownPosition = available ? m.positionMs : 0;
        if (available && m.playing && m.durationMs > 0) shownPosition = Math.min(m.durationMs, m.positionMs + (SystemClock.elapsedRealtime() - dash.pc.stateAt));
        position.setText(available ? Format.mediaTime((long) shownPosition) : "—:—");
        duration.setText(available && m.durationMs > 0 ? Format.mediaTime((long) m.durationMs) : "—:—");
        if (!seek.dragging()) seek.setValue(available && m.durationMs > 0 ? (float) (1000 * shownPosition / m.durationMs) : 0);
        Ui.enable(seek, available && m.canSeek);
        play.setGlyph(available && m.playing ? "pause" : "play");
        play.setContentDescription(available && m.playing ? "Pause playback" : "Play media");
        Ui.enable(play, available && m.canPlayPause);
        Ui.enable(previous, available && m.canPrevious);
        Ui.enable(next, available && m.canNext);
        Ui.enable(volumeButton, available);
        if (!available) closeVolume();
        hint.setText(hintText);
        String key = available && m.art ? m.key : "";
        if (!key.equals(artKey)) {
            artKey = key; artwork = null;
            if (!key.isEmpty()) loadArt(key);
        }
        art.setImageBitmap(artwork);
        if (volumePanel != null) renderVolume();
    }

    @Override public void tick(int ticks, boolean quiet) {
        PcState.Media m = media();
        if (m != null && m.available && m.playing && !seek.dragging()) render();
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

    // ----- The playing app's volume -----

    private void openVolume() {
        closeVolume();
        volumePanel = new FrameLayout(ui.context);
        volumePanel.setBackgroundColor(ui.theme.surface);
        volumePanel.setClickable(true);
        LinearLayout column = ui.column();
        LinearLayout top = ui.row();
        LinearLayout titles = ui.column();
        titles.addView(ui.label("VOLUME"), ui.ll(Ui.WRAP, Ui.WRAP));
        volumeSubject = ui.heading("Reading the mixer…");
        volumeSubject.setSingleLine(true); volumeSubject.setEllipsize(android.text.TextUtils.TruncateAt.END);
        titles.addView(volumeSubject, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        top.addView(titles, ui.ll(0, Ui.WRAP, 1));
        top.addView(ui.iconButton("close", "Close volume", 22, false, new View.OnClickListener() { @Override public void onClick(View v) { closeVolume(); } }), ui.ll(ui.px(52), ui.px(52)));
        column.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
        LinearLayout middle = ui.row();
        volume = new TouchSlider(ui.context, ui).range(0, 100, 0);
        volume.setContentDescription("Volume of the playing app");
        volume.setListener(new TouchSlider.Listener() {
            @Override public void changed(TouchSlider s, float value, boolean done) {
                volumePercent.setText(Math.round(value) + "%");
                // At most every 150 ms while dragging, then once more when the finger lifts (like the mixer).
                long now = SystemClock.elapsedRealtime();
                if (!done && now - lastVolumeSent < 150) return;
                lastVolumeSent = now;
                double level = value / 100.0;
                dash.commandQuietly(volumeSession == null ? Json.map("action", "audio.volume", "value", level) : Json.map("action", "audio.session.volume", "id", volumeSession, "value", level));
            }
        });
        middle.addView(volume, ui.ll(0, ui.px(52), 1));
        volumePercent = ui.text("—", 24 * ui.textScale, ui.theme.text);
        volumePercent.setGravity(Gravity.END);
        middle.addView(volumePercent, ui.ll(ui.px(72), Ui.WRAP));
        column.addView(middle, ui.ll(Ui.MATCH, 0, 1));
        volumeNote = Ui.wrap(ui.smallText(""), 2);
        column.addView(volumeNote, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 6));
        volumePanel.addView(column, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        FrameLayout.LayoutParams params = new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH);
        params.topMargin = ui.px(28);
        root.addView(volumePanel, params);
        dash.wantPart("audio", true);
        renderVolume();
    }

    // Fills the panel from the mixer reading: the playing app's row, or the master volume when it has no row.
    private void renderVolume() {
        PcState state = dash.state();
        PcState.Media m = media();
        if (state == null || state.audio == null || !state.audio.available) { volumeSubject.setText(state != null && state.audio != null ? "Mixer unavailable" : "Reading the mixer…"); return; }
        PcState.Session session = state.audio.sessionFor(m == null ? "" : m.app);
        volumeSession = session == null ? null : session.id;
        volumeSubject.setText(session == null ? "Master volume" : session.name);
        volumeNote.setText(session == null ? "This app isn't in the mixer yet, so this is the master volume." : "Only " + session.name + ". Master volume stays where it is.");
        float level = (float) ((session == null ? state.audio.volume : session.volume) * 100);
        if (!volume.dragging()) { volume.setValue(level); volumePercent.setText(Math.round(level) + "%"); }
    }

    private void closeVolume() {
        if (volumePanel != null && volumePanel.getParent() != null) ((FrameLayout) volumePanel.getParent()).removeView(volumePanel);
        if (volumePanel != null) dash.wantPart("audio", false);
        volumePanel = null; volumeSession = null;
    }

    @Override public void release() { closeVolume(); super.release(); }
}
