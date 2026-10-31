package com.alialyami.widgetscreen.widgets;

import android.media.Ringtone;
import android.media.RingtoneManager;
import android.os.SystemClock;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.views.GlyphView;
import com.alialyami.widgetscreen.views.ProgressLine;
import com.alialyami.widgetscreen.views.TouchSlider;

import java.time.LocalDateTime;

// The PC's focus timer: the phone shows it and its buttons start, pause and reset it on the PC. A finished session
// tints the card and chimes (when the PC says so) until it's tapped; the fourth preset's length is picked in the card.
public final class FocusWidget extends Widget {
    private static final int[] PRESETS = { 25, 50, 5 };
    private TextView time, status, sessions, start, custom, lengthLabel, lengthStep;
    private ProgressLine progress;
    private GlyphView reset;
    private final TextView[] presets = new TextView[3];
    private FrameLayout root, picker;
    private View finishedTint;
    private TouchSlider length;
    private int selectedPreset = -1, selectedCustom = -1;
    private boolean wasFinished, folded;

    public FocusWidget(Dashboard dash) { super("Focus", dash); }

    @Override public String part() { return "focus"; }

    @Override protected View create() {
        boolean compact = height < 320;
        // A short card keeps the countdown and the start row; its length button steps through the presets (as on the PC).
        folded = CardSizing.focusShort(height);
        selectedPreset = -1; selectedCustom = -1; picker = null;
        LinearLayout column = ui.column();
        column.addView(ui.label("FOCUS TIMER"), ui.ll(Ui.MATCH, Ui.WRAP));

        LinearLayout middle = ui.column();
        middle.setGravity(Gravity.CENTER_VERTICAL);
        // The countdown grows with the card (CardSizing, as on the PC).
        time = Ui.tabular(ui.text("25:00", (float) CardSizing.focusTime(width, height), ui.theme.text));
        time.setTypeface(Ui.LIGHT); time.setGravity(Gravity.CENTER);
        middle.addView(time, ui.ll(Ui.MATCH, Ui.WRAP));
        status = Ui.wrap(ui.smallText(""), 2);
        status.setGravity(Gravity.CENTER);
        if (!folded) middle.addView(status, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, compact ? 2 : 6, 0, compact ? 8 : 12));
        progress = new ProgressLine(ui.context, ui.theme.accent, ui.theme.track);
        progress.setContentDescription("Focus session progress");
        middle.addView(progress, folded ? ui.margins(ui.ll(Ui.MATCH, ui.px(5)), 0, 4, 0, 12) : ui.ll(Ui.MATCH, ui.px(5)));
        sessions = ui.caption("");
        sessions.setGravity(Gravity.CENTER); sessions.setSingleLine(true);
        if (!folded) middle.addView(sessions, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 10, 0, 0));
        column.addView(middle, ui.ll(Ui.MATCH, 0, 1));

        LinearLayout presetRow = ui.row();
        for (int i = 0; i < PRESETS.length; i++) {
            final int minutes = PRESETS[i];
            TextView button = ui.button(minutes + "m", new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "focus.preset", "minutes", minutes)); } });
            button.setTextSize(TypedValue.COMPLEX_UNIT_PX, ui.fpx(16));
            button.setPadding(ui.px(5), ui.px(5), ui.px(5), ui.px(5));
            button.setContentDescription(minutes + " minutes");
            presets[i] = button;
            presetRow.addView(button, ui.margins(ui.ll(0, Ui.WRAP, 1), 0, 0, 6, 0));
        }
        // The fourth preset: tap to pick a length once, then it stays as a button like the others.
        custom = ui.button("…", new View.OnClickListener() { @Override public void onClick(View v) {
            PcState.Focus focus = focus();
            if (focus != null && focus.customMinutes > 0 && picker == null && Math.round(focus.durationMs / 60000) != focus.customMinutes) dash.command(Json.map("action", "focus.preset", "minutes", focus.customMinutes));
            else openPicker();
        } });
        custom.setTextSize(TypedValue.COMPLEX_UNIT_PX, ui.fpx(16));
        custom.setPadding(ui.px(5), ui.px(5), ui.px(5), ui.px(5));
        custom.setContentDescription("Choose another length");
        presetRow.addView(custom, ui.ll(0, Ui.WRAP, 1));
        if (!folded) column.addView(presetRow, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, compact ? 8 : 12));

        LinearLayout bottom = ui.row();
        lengthStep = ui.button("25m", new View.OnClickListener() { @Override public void onClick(View v) { nextLength(); } });
        lengthStep.setTextSize(TypedValue.COMPLEX_UNIT_PX, ui.fpx(16));
        lengthStep.setPadding(ui.px(4), ui.px(4), ui.px(4), ui.px(4));
        if (folded) bottom.addView(lengthStep, ui.margins(ui.ll(ui.px(64), ui.px(48)), 0, 0, 8, 0));
        start = ui.accentButton("Start focus", new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "focus.toggle")); } });
        bottom.addView(start, ui.margins(ui.ll(0, Ui.WRAP, 1), 0, 0, 8, 0));
        reset = ui.iconButton("reset", "Reset timer", 22, true, new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "focus.reset")); } });
        bottom.addView(reset, ui.ll(ui.px(60), ui.px(48)));
        column.addView(bottom, ui.ll(Ui.MATCH, Ui.WRAP));

        root = ui.card(column, 24);
        // A finished session tints the whole card (behind the content) until it's tapped or a minute passes on the PC.
        finishedTint = new View(ui.context);
        finishedTint.setBackground(ui.shape(Ui.alpha(ui.theme.accent, 0.15f), 0, 0, ui.radius));
        finishedTint.setVisibility(View.GONE);
        FrameLayout.LayoutParams tint = new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH);
        tint.setMargins(-ui.px(24), -ui.px(24), -ui.px(24), -ui.px(24));
        root.addView(finishedTint, 0, tint);
        root.setClickable(true);
        root.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) {
            PcState.Focus focus = focus();
            if (focus != null && focus.finished) dash.command(Json.map("action", "focus.dismiss"));
        } });
        return root;
    }

    // The next length for the folded presets: 25, 50, 5, then the chosen fourth length when there is one.
    private void nextLength() {
        PcState.Focus focus = focus();
        if (focus == null || focus.running) return;
        int current = (int) Math.round(focus.durationMs / 60000);
        boolean extra = focus.customMinutes > 0 && focus.customMinutes != 25 && focus.customMinutes != 50 && focus.customMinutes != 5;
        int[] lengths = extra ? new int[] { 25, 50, 5, focus.customMinutes } : PRESETS;
        int index = -1;
        for (int i = 0; i < lengths.length; i++) if (lengths[i] == current) index = i;
        dash.command(Json.map("action", "focus.preset", "minutes", lengths[(index + 1) % lengths.length]));
    }

    private PcState.Focus focus() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.focus; }

    @Override public void render() {
        if (!built()) return;
        PcState state = dash.state();
        PcState.Focus focus = focus();
        boolean online = focus != null;
        for (TextView preset : presets) Ui.enable(preset, online);
        Ui.enable(custom, online); Ui.enable(start, online); Ui.enable(reset, online); Ui.enable(lengthStep, online && !focus.running);
        if (!online) { time.setText("—:—"); time.setTextColor(ui.theme.text); status.setText(dash.offlineText()); sessions.setText(""); progress.setValue(0); finishedTint.setVisibility(View.GONE); return; }
        // Between readings a running timer keeps counting down here.
        long remaining = (long) focus.remainingMs;
        if (focus.running) remaining = Math.max(0, remaining - (SystemClock.elapsedRealtime() - dash.pc.stateAt));
        double fraction = focus.durationMs <= 0 ? 0 : 1 - remaining / focus.durationMs;
        time.setText(Format.focus(remaining));
        // Paused reads dimmer from across the room; a finished session lights the whole card.
        time.setTextColor(focus.paused ? ui.theme.muted : ui.theme.text);
        finishedTint.setVisibility(focus.finished ? View.VISIBLE : View.GONE);
        String ends = Format.endsAt(LocalDateTime.now().plusNanos(remaining * 1000000L), dash.use24());
        boolean running = focus.running && remaining > 0;
        status.setText(remaining == 0 && focus.finished ? "Done. Take a break. Tap to clear." : Format.focusStatus(remaining, running, fraction, ends));
        progress.setValue(fraction);
        sessions.setText(Format.sessions(focus.sessionsToday, focus.minutesToday));
        String label = Format.focusButton(remaining, running, fraction);
        start.setText(folded && label.equals("Start focus") ? "Start" : label);
        int length = (int) Math.round(focus.durationMs / 60000);
        lengthStep.setText(length + "m");
        lengthStep.setContentDescription("Session length " + length + " minutes. Tap for the next length.");
        if (focus.finished && !wasFinished && focus.chime) chime();
        wasFinished = focus.finished;
        int minutes = (int) Math.round(focus.durationMs / 60000);
        if (minutes == selectedPreset && focus.customMinutes == selectedCustom) return;
        selectedPreset = minutes; selectedCustom = focus.customMinutes;
        for (int i = 0; i < PRESETS.length; i++) {
            ui.select(presets[i], PRESETS[i] == minutes);
            presets[i].setContentDescription(PRESETS[i] + " minutes" + (PRESETS[i] == minutes ? ", selected" : ""));
        }
        boolean customSelected = focus.customMinutes > 0 && minutes == focus.customMinutes && minutes != 25 && minutes != 50 && minutes != 5;
        custom.setText(focus.customMinutes > 0 ? focus.customMinutes + "m" : "…");
        ui.select(custom, customSelected);
        custom.setContentDescription((focus.customMinutes > 0 ? focus.customMinutes + " minutes" : "Choose another length") + (customSelected ? ", selected" : ""));
    }

    // The phone plays its notification sound when the PC says the session just ended; Quiet mode on the PC keeps it off.
    private void chime() {
        try {
            Ringtone tone = RingtoneManager.getRingtone(ui.context, RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION));
            if (tone != null) tone.play();
        } catch (Exception ignored) { }
    }

    // The fourth preset's length, picked inside the card (5 to 180 minutes), starts a session with it on the PC.
    private void openPicker() {
        closePicker();
        PcState.Focus focus = focus();
        int initial = focus != null && focus.customMinutes > 0 ? focus.customMinutes : 45;
        picker = new FrameLayout(ui.context);
        picker.setBackgroundColor(ui.theme.surface);
        picker.setClickable(true);
        LinearLayout column = ui.column();
        LinearLayout top = ui.row();
        top.addView(ui.label("SESSION LENGTH"), ui.ll(0, Ui.WRAP, 1));
        top.addView(ui.iconButton("close", "Close session length", 22, false, new View.OnClickListener() { @Override public void onClick(View v) { closePicker(); } }), ui.ll(ui.px(52), ui.px(52)));
        column.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
        LinearLayout middle = ui.column();
        middle.setGravity(Gravity.CENTER_VERTICAL);
        lengthLabel = Ui.tabular(ui.text(initial + " min", 40, ui.theme.text));
        lengthLabel.setTypeface(Ui.LIGHT); lengthLabel.setGravity(Gravity.CENTER);
        middle.addView(lengthLabel, ui.ll(Ui.MATCH, Ui.WRAP));
        length = new TouchSlider(ui.context, ui).range(5, 180, 5);
        length.setValue(initial);
        length.setContentDescription("Session length in minutes");
        length.setListener(new TouchSlider.Listener() { @Override public void changed(TouchSlider s, float value, boolean done) { lengthLabel.setText(Math.round(value) + " min"); } });
        middle.addView(length, ui.margins(ui.ll(Ui.MATCH, ui.px(48)), 0, 12, 0, 0));
        TextView note = Ui.wrap(ui.smallText("5 to 180 minutes. The length stays as your fourth preset."), 2);
        note.setGravity(Gravity.CENTER);
        middle.addView(note, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 10, 0, 0));
        column.addView(middle, ui.ll(Ui.MATCH, 0, 1));
        column.addView(ui.accentButton("Start with this length", new View.OnClickListener() { @Override public void onClick(View v) {
            int minutes = Math.round(length.value()); closePicker();
            dash.command(Json.map("action", "focus.custom", "minutes", minutes));
        } }), ui.ll(Ui.MATCH, ui.px(52)));
        picker.addView(column, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        root.addView(picker, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
    }

    private void closePicker() {
        if (picker != null && picker.getParent() != null) ((FrameLayout) picker.getParent()).removeView(picker);
        picker = null;
    }

    @Override public void release() { super.release(); picker = null; }

    @Override public void tick(int ticks, boolean quiet) { render(); }
}
