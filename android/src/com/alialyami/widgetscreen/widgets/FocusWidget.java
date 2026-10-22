package com.alialyami.widgetscreen.widgets;

import android.os.SystemClock;
import android.view.Gravity;
import android.view.View;
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

import java.time.LocalDateTime;

// The PC's focus timer: the phone shows it and its buttons start, pause and reset it on the PC.
public final class FocusWidget extends Widget {
    private static final int[] PRESETS = { 25, 50, 5 };
    private TextView time, status, start;
    private ProgressLine progress;
    private GlyphView reset;
    private final TextView[] presets = new TextView[3];
    private int selectedPreset = -1;

    public FocusWidget(Dashboard dash) { super("Focus", dash); }

    @Override public String part() { return "focus"; }

    @Override protected View create() {
        boolean compact = height < 320;
        selectedPreset = -1;
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
        middle.addView(status, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, compact ? 2 : 6, 0, compact ? 8 : 12));
        progress = new ProgressLine(ui.context, ui.theme.accent, ui.theme.track);
        progress.setContentDescription("Focus session progress");
        middle.addView(progress, ui.ll(Ui.MATCH, ui.px(5)));
        column.addView(middle, ui.ll(Ui.MATCH, 0, 1));

        LinearLayout presetRow = ui.row();
        for (int i = 0; i < PRESETS.length; i++) {
            final int minutes = PRESETS[i];
            TextView button = ui.button(minutes + "m", new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "focus.preset", "minutes", minutes)); } });
            button.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(16));
            button.setPadding(ui.px(5), ui.px(5), ui.px(5), ui.px(5));
            button.setContentDescription(minutes + " minutes");
            presets[i] = button;
            presetRow.addView(button, ui.margins(ui.ll(0, Ui.WRAP, 1), 0, 0, i < PRESETS.length - 1 ? 6 : 0, 0));
        }
        column.addView(presetRow, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, compact ? 8 : 12));

        LinearLayout bottom = ui.row();
        start = ui.accentButton("Start focus", new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "focus.toggle")); } });
        bottom.addView(start, ui.margins(ui.ll(0, Ui.WRAP, 1), 0, 0, 8, 0));
        reset = ui.iconButton("reset", "Reset timer", 22, true, new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "focus.reset")); } });
        bottom.addView(reset, ui.ll(ui.px(60), ui.px(48)));
        column.addView(bottom, ui.ll(Ui.MATCH, Ui.WRAP));
        return ui.card(column, 24);
    }

    @Override public void render() {
        if (!built()) return;
        PcState state = dash.state();
        PcState.Focus focus = state == null ? null : state.focus;
        boolean online = dash.online() && focus != null;
        for (TextView preset : presets) Ui.enable(preset, online);
        Ui.enable(start, online); Ui.enable(reset, online);
        if (!online) { time.setText("—:—"); status.setText(dash.offlineText()); progress.setValue(0); return; }
        // Between readings a running timer keeps counting down here.
        long remaining = (long) focus.remainingMs;
        if (focus.running) remaining = Math.max(0, remaining - (SystemClock.elapsedRealtime() - dash.pc.stateAt));
        double fraction = focus.durationMs <= 0 ? 0 : 1 - remaining / focus.durationMs;
        time.setText(Format.focus(remaining));
        String ends = Format.endsAt(LocalDateTime.now().plusNanos(remaining * 1000000L), dash.use24());
        boolean running = focus.running && remaining > 0;
        status.setText(Format.focusStatus(remaining, running, fraction, ends));
        progress.setValue(fraction);
        start.setText(Format.focusButton(remaining, running, fraction));
        int minutes = (int) Math.round(focus.durationMs / 60000);
        if (minutes == selectedPreset) return;
        selectedPreset = minutes;
        for (int i = 0; i < PRESETS.length; i++) {
            ui.select(presets[i], PRESETS[i] == minutes);
            presets[i].setContentDescription(PRESETS[i] + " minutes" + (PRESETS[i] == minutes ? ", selected" : ""));
        }
    }

    @Override public void tick(int ticks, boolean quiet) { render(); }
}
