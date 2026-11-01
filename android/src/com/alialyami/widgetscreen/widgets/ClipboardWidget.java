package com.alialyami.widgetscreen.widgets;

import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.ClipText;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;

// The PC's clipboard history, in the same three designs as the Windows ClipboardCard (CardSizing.clip;
// docs/design/clipboard-history.md). A tap pastes that text on the PC, into the app in front there; the phone only
// gets short previews. Android doesn't let an app watch the clipboard in the background, so this phone's own copies
// aren't part of the history.
public final class ClipboardWidget extends Widget {
    private TextView status, empty;
    private LinearLayout rows;
    private View pause, clear;
    private String listed, builtFor = "";
    private int design;

    public ClipboardWidget(Dashboard dash) { super("Clipboard", dash); }

    @Override public String part() { return "clipboard"; }

    private PcState.Clipboard clipboard() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.clipboard; }
    private String shape(PcState.Clipboard c) { return c == null ? "none" : String.valueOf(c.paused); }

    @Override protected View create() {
        PcState.Clipboard c = clipboard();
        builtFor = shape(c); listed = null;
        design = CardSizing.clip(width, height);
        boolean small = design == CardSizing.SMALL;

        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(ui.label("CLIPBOARD"), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        status = ui.caption("");
        header.addView(status, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));

        FrameLayout body = new FrameLayout(ui.context);
        ScrollView scroll = new ScrollView(ui.context);
        scroll.setVerticalScrollBarEnabled(false);
        rows = ui.column();
        scroll.addView(rows, new FrameLayout.LayoutParams(Ui.MATCH, Ui.WRAP));
        body.addView(scroll, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        empty = Ui.wrap(ui.caption(c == null ? "Waiting for the PC…" : ""), 4);
        body.addView(empty, ui.frame(Ui.MATCH, Ui.WRAP, Gravity.CENTER_VERTICAL));
        column.addView(body, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));

        LinearLayout tools = ui.row(); tools.setGravity(Gravity.END);
        final boolean paused = c != null && c.paused;
        View.OnClickListener pauseClick = new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "clipboard.pause", "paused", !paused), paused ? "Clipboard history resumed on the PC." : "Clipboard history paused on the PC."); } };
        View.OnClickListener clearClick = new View.OnClickListener() { @Override public void onClick(View v) {
            dash.confirm("CLIPBOARD", "Clear the clipboard history?", "The texts kept on the PC are forgotten. What's on its clipboard now stays.", "Clear", new Runnable() {
                @Override public void run() { dash.command(Json.map("action", "clipboard.clear")); }
            });
        } };
        pause = small ? ui.iconButton(paused ? "play" : "pause", paused ? "Resume clipboard history" : "Pause clipboard history", 18, true, pauseClick) : ui.button(paused ? "Resume" : "Pause", pauseClick);
        clear = small ? ui.iconButton("close", "Clear clipboard history", 16, true, clearClick) : ui.button("Clear", clearClick);
        if (!small) for (View tool : new View[] { pause, clear }) { ((TextView) tool).setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(ui.small())); tool.setPadding(ui.px(16), ui.px(4), ui.px(16), ui.px(4)); }
        tools.addView(pause, ui.ll(small ? ui.px(48) : Ui.WRAP, ui.px(48)));
        tools.addView(clear, ui.margins(ui.ll(small ? ui.px(48) : Ui.WRAP, ui.px(48)), 8, 0, 0, 0));
        Ui.enable(pause, c != null); Ui.enable(clear, c != null);
        column.addView(tools, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 8, 0, 0));
        return ui.card(column, 24);
    }

    // A copied text as a button; a tap pastes it on the PC.
    private View row(final PcState.Clip item, long now) {
        LinearLayout content = ui.column();
        content.setPadding(ui.px(12), ui.px(6), ui.px(12), ui.px(6));
        content.setMinimumHeight(ui.px(48));
        content.setGravity(Gravity.CENTER_VERTICAL);
        content.setBackground(ui.pressable(ui.theme.raised, ui.theme.controlBorder, 1, 12));
        TextView text = ui.body(item.text);
        if (design == CardSizing.LARGE) Ui.wrap(text, 2);
        content.addView(text, ui.ll(Ui.MATCH, Ui.WRAP));
        if (design != CardSizing.SMALL)
            content.addView(ui.caption(ClipText.ago(item.at, now) + (design == CardSizing.LARGE ? "  ·  " + ClipText.length(item.length) : "")), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 2, 0, 0));
        content.setClickable(true);
        content.setContentDescription("Paste " + item.text + " on the PC");
        content.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "clipboard.paste", "id", item.id)); } }));
        return content;
    }

    @Override public void render() {
        if (!built()) return;
        PcState.Clipboard c = clipboard();
        if (!shape(c).equals(builtFor)) { dash.rebuildWidget(id); return; }
        if (c == null) { status.setText(!dash.online() ? "PC OFFLINE" : ""); return; }
        status.setText(ClipText.status(c));
        status.setTextColor(c.paused ? Theme.WARNING : ui.theme.muted);
        empty.setText(c.items.isEmpty() ? ClipText.empty(c) : "");
        empty.setVisibility(c.items.isEmpty() ? View.VISIBLE : View.GONE);
        Ui.enable(clear, !c.items.isEmpty());
        long now = System.currentTimeMillis();
        StringBuilder key = new StringBuilder(String.valueOf(now / 60000));
        for (PcState.Clip item : c.items) key.append('|').append(item.id).append(item.at);
        if (key.toString().equals(listed)) return;
        listed = key.toString();
        rows.removeAllViews();
        for (int i = 0; i < c.items.size(); i++) rows.addView(row(c.items.get(i), now), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, i == 0 ? 0 : 6, 0, 0));
    }
}
