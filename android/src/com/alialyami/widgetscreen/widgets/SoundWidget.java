package com.alialyami.widgetscreen.widgets;

import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.integrations.AudioControl;
import com.alialyami.widgetscreen.views.GlyphView;
import com.alialyami.widgetscreen.views.TouchSlider;

// The Windows sound mixer on Android: media volume is the master slider, its row shows the output and
// opens Android's output switcher, and the other volume streams take the place of app sessions.
public final class SoundWidget extends Widget {
    private TextView status, device, percent;
    private TouchSlider master;
    private GlyphView mute;
    private final TouchSlider[] sliders = new TouchSlider[AudioControl.OTHER_STREAMS.length];
    private final TextView[] percents = new TextView[AudioControl.OTHER_STREAMS.length];
    private String warning = "";

    public SoundWidget(Dashboard dash) { super("Mixer", dash); }

    @Override protected View create() {
        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(ui.label("SOUND"), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        status = ui.caption("");
        status.setTextColor(Theme.WARNING);
        header.addView(status, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(34)));

        LinearLayout masterRow = ui.row();
        LinearLayout output = ui.column();
        output.setBackground(ui.pressable(0x00000000, 0, 0, 12));
        output.setPadding(ui.px(8), 0, ui.px(8), 0);
        output.setGravity(Gravity.CENTER_VERTICAL);
        output.addView(ui.heading("Media volume"), ui.ll(Ui.WRAP, Ui.WRAP));
        LinearLayout deviceRow = ui.row();
        device = ui.smallText("Connecting to audio…");
        device.setMaxWidth(ui.px(300));
        deviceRow.addView(device, ui.ll(Ui.WRAP, Ui.WRAP));
        deviceRow.addView(new GlyphView(ui.context, "chevron-down", ui.theme.muted, ui.fpx(14)), ui.margins(ui.ll(ui.px(16), ui.px(16)), 8, 2, 0, 0));
        output.addView(deviceRow, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 5, 0, 0));
        output.setClickable(true);
        output.setContentDescription("Choose output device");
        output.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) {
            try { dash.audio.openOutputSwitcher(dash.activity); } catch (Exception e) { dash.notify("Android's output picker isn't available on this device."); }
        } });
        masterRow.addView(output, ui.margins(ui.ll(0, Ui.MATCH, 1), -8, 0, 8, 0));
        percent = ui.text("—", 36, ui.theme.text);
        percent.setTypeface(Ui.LIGHT); percent.setGravity(Gravity.END);
        masterRow.addView(percent, ui.ll(ui.px(80), Ui.WRAP));
        column.addView(masterRow, ui.ll(Ui.MATCH, ui.px(66)));

        LinearLayout sliderRow = ui.row();
        mute = ui.iconButton("volume", "Mute media", 22, false, new View.OnClickListener() { @Override public void onClick(View v) { act(new Runnable() { @Override public void run() { dash.audio.toggleMute(); } }); } });
        sliderRow.addView(mute, ui.ll(ui.px(58), ui.px(52)));
        master = slider(AudioControl.MEDIA, null);
        master.setContentDescription("Media volume");
        sliderRow.addView(master, ui.margins(ui.ll(0, ui.px(52), 1), 8, 0, 8, 0));
        GlyphView settings = ui.iconButton("settings", "Open Android sound settings", 22, false, new View.OnClickListener() { @Override public void onClick(View v) {
            try { AudioControl.openSoundSettings(dash.activity); } catch (Exception e) { dash.notify("Couldn't open Android sound settings."); }
        } });
        sliderRow.addView(settings, ui.ll(ui.px(60), ui.px(52)));
        column.addView(sliderRow, ui.ll(Ui.MATCH, ui.px(52)));

        column.addView(ui.label("OTHER VOLUMES"), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 18, 0, 8));
        LinearLayout list = ui.column();
        for (int i = 0; i < AudioControl.OTHER_STREAMS.length; i++) {
            int stream = AudioControl.OTHER_STREAMS[i];
            FrameLayout labels = new FrameLayout(ui.context);
            labels.addView(ui.text(AudioControl.name(stream), 16 * ui.textScale, ui.theme.text), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
            percents[i] = ui.text("—", 14 * ui.textScale, ui.theme.muted);
            labels.addView(percents[i], ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
            list.addView(labels, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 4, i == 0 ? 0 : 8, 4, 0));
            sliders[i] = slider(stream, percents[i]);
            sliders[i].setContentDescription(AudioControl.name(stream) + " volume");
            list.addView(sliders[i], ui.ll(Ui.MATCH, ui.px(48)));
        }
        ScrollView scroll = new ScrollView(ui.context);
        scroll.setVerticalScrollBarEnabled(false);
        // Inside the phone list, the inner list scrolls first and hands the rest to the page.
        scroll.setNestedScrollingEnabled(true);
        scroll.addView(list);
        column.addView(scroll, ui.ll(Ui.MATCH, 0, 1));
        return ui.card(column, 24);
    }

    private TouchSlider slider(final int stream, final TextView label) {
        final TouchSlider slider = new TouchSlider(ui.context, ui).range(0, 100, 0);
        slider.setListener(new TouchSlider.Listener() {
            @Override public void changed(TouchSlider s, final float value, boolean done) {
                try { dash.audio.setPercent(stream, value); warning = ""; }
                catch (SecurityException e) { warning = "DO NOT DISTURB IS ON"; }
                catch (Exception e) { warning = "UNAVAILABLE"; }
                if (label != null) label.setText(Math.round(value) + "%");
                else percent.setText(Math.round(value) + "%");
                status.setText(warning);
                if (done) refresh();
            }
        });
        return slider;
    }

    private void act(Runnable action) {
        try { action.run(); } catch (Exception e) { dash.notify("Android didn't allow that volume change."); }
        refresh();
    }

    public void refresh() {
        if (!active()) return;
        AudioControl audio = dash.audio;
        if (!audio.available()) { status.setText("UNAVAILABLE"); device.setText("Audio unavailable"); return; }
        try {
            boolean fixed = audio.fixed();
            int value = audio.percent(AudioControl.MEDIA);
            boolean muted = audio.muted();
            percent.setText(muted ? "Muted" : value + "%");
            percent.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(muted ? 24 : 36));
            master.setValue(value);
            mute.setGlyph(muted ? "mute" : "volume");
            mute.setContentDescription(muted ? "Unmute media" : "Mute media");
            device.setText(audio.outputName());
            Ui.enable(master, !fixed); Ui.enable(mute, !fixed);
            for (int i = 0; i < sliders.length; i++) {
                int level = audio.percent(AudioControl.OTHER_STREAMS[i]);
                sliders[i].setValue(level);
                if (!sliders[i].dragging()) percents[i].setText(level + "%");
                Ui.enable(sliders[i], !fixed);
            }
            status.setText(fixed ? "FIXED VOLUME" : warning);
        } catch (Exception e) {
            status.setText("UNAVAILABLE");
        }
    }

    @Override public void render() { refresh(); }

    @Override public void tick(int ticks, boolean quiet) { if (ticks % (quiet ? 5 : 2) == 0) refresh(); }
}
