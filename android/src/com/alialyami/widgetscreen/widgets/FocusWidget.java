package com.alialyami.widgetscreen.widgets;

import android.view.Gravity;
import android.view.View;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.FocusTimer;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.views.GlyphView;
import com.alialyami.widgetscreen.views.ProgressLine;

import java.time.LocalDateTime;

public final class FocusWidget extends Widget {
    private static final int[] PRESETS = { 25, 50, 5 };
    private TextView time, status, start;
    private ProgressLine progress;
    private final TextView[] presets = new TextView[3];
    private int selectedPreset = -1;

    public FocusWidget(Dashboard dash) { super("Focus", dash); }

    @Override protected View create() {
        boolean compact = height < 320, tall = height >= 440 && width >= 296;
        selectedPreset = -1;
        LinearLayout column = ui.column();
        column.addView(ui.label("FOCUS TIMER"), ui.ll(Ui.MATCH, Ui.WRAP));

        LinearLayout middle = ui.column();
        middle.setGravity(Gravity.CENTER_VERTICAL);
        time = Ui.tabular(ui.text("25:00", compact ? 42 : tall ? 84 : 54, ui.theme.text));
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
            TextView button = ui.button(minutes + "m", new View.OnClickListener() { @Override public void onClick(View v) { dash.focus.reset(minutes); render(); } });
            button.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(16));
            button.setPadding(ui.px(5), ui.px(5), ui.px(5), ui.px(5));
            button.setContentDescription(minutes + " minutes");
            presets[i] = button;
            presetRow.addView(button, ui.margins(ui.ll(0, Ui.WRAP, 1), 0, 0, i < PRESETS.length - 1 ? 6 : 0, 0));
        }
        column.addView(presetRow, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, compact ? 8 : 12));

        LinearLayout bottom = ui.row();
        start = ui.accentButton("Start focus", new View.OnClickListener() { @Override public void onClick(View v) { dash.focus.toggle(); render(); } });
        bottom.addView(start, ui.margins(ui.ll(0, Ui.WRAP, 1), 0, 0, 8, 0));
        GlyphView reset = ui.iconButton("reset", "Reset timer", 22, true, new View.OnClickListener() {
            @Override public void onClick(View v) { dash.focus.reset(dash.focus.durationMinutes()); render(); }
        });
        bottom.addView(reset, ui.ll(ui.px(60), ui.px(48)));
        column.addView(bottom, ui.ll(Ui.MATCH, Ui.WRAP));
        return ui.card(column, 24);
    }

    @Override public void render() {
        if (!built()) return;
        FocusTimer focus = dash.focus;
        focus.finishIfElapsed();
        long remaining = focus.remainingMs();
        time.setText(Format.focus(remaining));
        String ends = Format.endsAt(LocalDateTime.now().plusNanos(remaining * 1000000L), dash.settings.use24HourClock);
        status.setText(Format.focusStatus(remaining, focus.running(), focus.progress(), ends));
        progress.setValue(focus.progress());
        start.setText(Format.focusButton(remaining, focus.running(), focus.progress()));
        int minutes = focus.durationMinutes();
        if (minutes == selectedPreset) return;
        selectedPreset = minutes;
        for (int i = 0; i < PRESETS.length; i++) {
            ui.select(presets[i], PRESETS[i] == minutes);
            presets[i].setContentDescription(PRESETS[i] + " minutes" + (PRESETS[i] == minutes ? ", selected" : ""));
        }
    }

    @Override public void tick(int ticks, boolean quiet) { render(); }
}
