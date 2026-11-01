package com.alialyami.widgetscreen.widgets;

import android.graphics.drawable.GradientDrawable;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CalendarText;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

// The PC's calendars, in the same three designs as the Windows CalendarCard (CardSizing.calendar;
// docs/design/calendar.md): the next event with its countdown; the next event over the ones after it; and the agenda
// grouped by day. The PC reads the feeds (their addresses never reach the phone); the card counts down with the
// phone's clock between readings. Calendars are added on the PC (Settings → Calendar).
public final class CalendarWidget extends Widget {
    private TextView status;
    private FrameLayout body;
    private String rendered = "";
    private int design;
    private static final int[] FEED_COLORS = { 0, 0xFF8ED8F8, 0xFFC5AEFF, 0xFFF5C47D, 0xFFBAF28B };

    public CalendarWidget(Dashboard dash) { super("Calendar", dash); }

    @Override public String part() { return "calendar"; }

    private PcState.Calendar calendar() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.calendar; }

    @Override protected View create() {
        rendered = "";
        design = CardSizing.calendar(width, height);
        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(strip(), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        status = sticker(true);
        
        header.addView(status, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));
        body = new FrameLayout(ui.context);
        column.addView(body, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        FrameLayout card = ui.card(column, 24);
        // The whole card asks the PC to read the calendars again.
        card.setClickable(true);
        card.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "calendar.refresh"), "Updating the calendars on the PC."); } }));
        card.setContentDescription("Calendar. Tap to update.");
        return card;
    }

    @Override public void tick(int ticks, boolean quiet) { if (active()) render(); }

    @Override public void render() {
        if (!built()) return;
        PcState.Calendar c = calendar();
        long now = System.currentTimeMillis();
        boolean use24 = dash.use24();
        String key = c == null ? "none" : (now / 60000) + "|" + use24 + "|" + c.feeds + "|" + c.read + "|" + c.failed + "|" + c.events.size() + "|" + (c.events.isEmpty() ? 0 : c.events.get(0).start);
        if (key.equals(rendered)) return;
        rendered = key;
        body.removeAllViews();
        status.setText(!dash.online() ? "PC OFFLINE" : c == null ? "" : CalendarText.flag(c));
        double bodyWidth = Math.max(1, width - 48), bodyHeight = Math.max(1, height - 48 - 36);
        List<PcState.CalendarItem> current = new ArrayList<PcState.CalendarItem>();
        if (c != null) for (PcState.CalendarItem e : c.events) if ((e.end > e.start ? e.end : e.start + 60000) > now) current.add(e);
        View view;
        if (c == null) view = note("Waiting for the PC…");
        else if (c.feeds == 0) view = note("Add a calendar on the PC in Settings → Calendar: Google's secret iCal address or an Outlook published calendar.");
        else if (!c.read) view = note("The PC is reading its calendars…");
        else if (current.isEmpty()) view = note("Nothing in the next two weeks.");
        else if (design == CardSizing.LARGE) view = agenda(c, current, now, use24);
        else view = nextAndAfter(c, current, now, use24, bodyHeight);
        body.addView(view, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
    }

    private View note(String text) { TextView note = Ui.wrap(ui.caption(text), 4); note.setGravity(Gravity.CENTER_VERTICAL); return note; }

    private View dot(int feed) {
        View dot = new View(ui.context);
        GradientDrawable shape = new GradientDrawable(); shape.setShape(GradientDrawable.OVAL);
        int color = FEED_COLORS[feed % FEED_COLORS.length];
        shape.setColor(color == 0 ? ui.theme.accent : color);
        dot.setBackground(shape);
        return dot;
    }

    private View nextAndAfter(PcState.Calendar c, List<PcState.CalendarItem> current, long now, boolean use24, double height) {
        PcState.CalendarItem next = CalendarText.next(current, now);
        LinearLayout panel = ui.column();
        panel.setGravity(design == CardSizing.SMALL ? Gravity.CENTER_VERTICAL : Gravity.TOP);
        if (design == CardSizing.SMALL && height < CardSizing.calendarNextHeight(ui.textScale) - 21) {
            TextView line = ui.text(CalendarText.countdown(next, now) + "  ·  " + CalendarText.start(next, c.pcOffset, use24), ui.small(), ui.theme.accent);
            line.setTypeface(Ui.MEDIUM); panel.addView(line, ui.ll(Ui.MATCH, Ui.WRAP));
            panel.addView(ui.body(next.title), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
            return panel;
        }
        TextView when = ui.text(CalendarText.day(next, c.pcOffset, now).toUpperCase(Locale.US) + "  ·  " + CalendarText.when(next, c.pcOffset, use24), ui.small(), ui.theme.accent);
        when.setTypeface(Ui.MEDIUM); panel.addView(when, ui.ll(Ui.MATCH, Ui.WRAP));
        panel.addView(ui.heading(next.title), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 2));
        panel.addView(ui.smallText(CalendarText.countdown(next, now) + (next.location.isEmpty() ? "" : "  ·  " + next.location)), ui.ll(Ui.MATCH, Ui.WRAP));
        if (design == CardSizing.SMALL) return panel;
        List<PcState.CalendarItem> rest = new ArrayList<PcState.CalendarItem>(current); rest.remove(next);
        int rows = (int) Math.max(0, Math.min(rest.size(), Math.floor((height - CardSizing.calendarNextHeight(ui.textScale)) / CardSizing.CALENDAR_ROW)));
        if (rows > 0) {
            panel.addView(ui.divider(1, ui.theme.controlBorder), ui.margins(ui.ll(Ui.MATCH, ui.px(1)), 0, 12, 0, 8));
            for (int i = 0; i < rows; i++) {
                PcState.CalendarItem e = rest.get(i);
                LinearLayout row = ui.row();
                if (c.feeds > 1) row.addView(dot(e.feed), ui.margins(ui.ll(ui.px(8), ui.px(8)), 0, 0, 8, 0));
                String day = CalendarText.day(e, c.pcOffset, now);
                String label = day.equals("Today") ? CalendarText.start(e, c.pcOffset, use24) : day.equals("Tomorrow") ? "Tmrw " + (e.allDay ? "" : CalendarText.start(e, c.pcOffset, use24)) : day.substring(0, Math.min(3, day.length())) + (e.allDay ? "" : " " + CalendarText.start(e, c.pcOffset, use24));
                row.addView(ui.smallText(label), ui.ll(ui.px(CardSizing.calendarTimeColumn(use24)), Ui.WRAP));
                row.addView(ui.text(e.title, ui.small(), ui.theme.text), ui.ll(0, Ui.WRAP, 1));
                panel.addView(row, ui.margins(ui.ll(Ui.MATCH, ui.px(CardSizing.CALENDAR_ROW - 4)), 0, 0, 0, 4));
            }
        }
        return panel;
    }

    // Large: the agenda for the next two weeks, grouped by day, scrolling inside the card.
    private View agenda(PcState.Calendar c, List<PcState.CalendarItem> current, long now, boolean use24) {
        ScrollView scroll = new ScrollView(ui.context);
        scroll.setVerticalScrollBarEnabled(false);
        LinearLayout panel = ui.column();
        PcState.CalendarItem next = CalendarText.next(current, now);
        TextView head = ui.text(next.title + "  ·  " + CalendarText.countdown(next, now), ui.body(), ui.theme.accent);
        head.setTypeface(Ui.MEDIUM);
        panel.addView(head, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 8));
        String lastDay = null;
        for (PcState.CalendarItem e : current) {
            String day = CalendarText.day(e, c.pcOffset, now);
            if (!day.equals(lastDay)) { panel.addView(ui.label(day.toUpperCase(Locale.US)), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, lastDay == null ? 0 : 12, 0, 6)); lastDay = day; }
            LinearLayout row = ui.row(); row.setGravity(Gravity.TOP);
            if (c.feeds > 1) row.addView(dot(e.feed), ui.margins(ui.ll(ui.px(8), ui.px(8)), 0, 8, 8, 0));
            row.addView(ui.text(CalendarText.start(e, c.pcOffset, use24), ui.small(), e == next ? ui.theme.accent : ui.theme.muted), ui.ll(ui.px(CardSizing.calendarTimeColumn(use24) + 8), Ui.WRAP));
            LinearLayout text = ui.column();
            text.addView(ui.body(e.title), ui.ll(Ui.MATCH, Ui.WRAP));
            if (!e.location.isEmpty()) text.addView(ui.caption(e.location), ui.ll(Ui.MATCH, Ui.WRAP));
            row.addView(text, ui.ll(0, Ui.WRAP, 1));
            panel.addView(row, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 8));
        }
        scroll.addView(panel, new FrameLayout.LayoutParams(Ui.MATCH, Ui.WRAP));
        return scroll;
    }
}
