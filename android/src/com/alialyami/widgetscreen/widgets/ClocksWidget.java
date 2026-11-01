package com.alialyami.widgetscreen.widgets;

import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.core.WorldClockText;
import com.alialyami.widgetscreen.views.ReadingParts;
import com.alialyami.widgetscreen.views.Stickers;

import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.List;

// The PC's world clocks, in the same three designs as the Windows WorldClocksCard (CardSizing.clocks;
// docs/design/world-clocks.md), with analog faces (Sticker Sheet phase 3, docs/design/sticker-sheet.md): Lemon from
// 7 AM to 7 PM in the city and Lavender otherwise, labelled Day or Evening. Small rows with the time at the right (and a
// small face on roomy rows), Medium cities side by side with a face, Large tiles with a bigger face, the date and a
// 24-hour band with 9 to 5 shaded and a line at now. The PC sends each city's offset from UTC, so the card keeps time
// from the phone's clock between readings. Cities are chosen on the PC (Settings → Widgets).
public final class ClocksWidget extends Widget {
    private static final double GAP = 12;
    private TextView status;
    private final List<Part> parts = new ArrayList<Part>();
    private String builtFor = "", rendered = "";
    private int design;

    private static final class Part { TextView label, time, period, day, difference, word; ReadingParts.ClockFace face; ReadingParts.DayBand band; }

    public ClocksWidget(Dashboard dash) { super("Clocks", dash); }

    @Override public String part() { return "clocks"; }

    private PcState.Clocks clocks() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.clocks; }
    private String shape(PcState.Clocks c) {
        if (c == null) return "none";
        StringBuilder key = new StringBuilder(String.valueOf(dash.use24()));
        for (PcState.Clock clock : c.clocks) key.append('|').append(clock.label);
        return key.toString();
    }

    @Override protected View create() {
        PcState.Clocks c = clocks();
        builtFor = shape(c); rendered = "";
        parts.clear();
        design = CardSizing.clocks(width, height);

        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(strip(), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        status = sticker(false);
        header.addView(status, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));

        // The body: the card less its 24-unit padding, the 28-unit heading and the 8-unit gap under it.
        double bodyWidth = Math.max(1, width - 48), bodyHeight = Math.max(1, height - 48 - 36);
        View body;
        if (c == null) body = ui.caption("Waiting for the PC…");
        else if (c.clocks.isEmpty()) body = Stickers.unavailableBody(ui, "Choose up to three time zones in Settings → Widgets on the PC.");
        else {
            int n = c.clocks.size();
            int shown = design == CardSizing.SMALL ? Math.min(n, CardSizing.clockRows(bodyHeight)) : n;
            body = design == CardSizing.SMALL ? list(shown, bodyHeight) : design == CardSizing.MEDIUM ? columns(n, bodyWidth, bodyHeight) : tiles(n, bodyWidth, bodyHeight);
        }
        column.addView(body, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        return ui.card(column, 24);
    }

    private Part part(double timeSize) {
        Part part = new Part(); parts.add(part);
        part.time = Ui.tabular(ui.text("", (float) timeSize, ui.theme.text));
        part.time.setTypeface(Ui.LIGHT);
        part.period = ui.text("", ui.small(), ui.theme.muted);
        return part;
    }
    private View timeRow(Part part, double timeSize) {
        LinearLayout row = ui.row();
        row.setGravity(Gravity.BOTTOM);
        row.addView(part.time, ui.ll(Ui.WRAP, Ui.WRAP));
        row.addView(part.period, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 5, 0, 0, Math.round(timeSize * 0.12)));
        return row;
    }

    // Small: one row per city, the name and the day over the time at the right.
    private View list(int shown, double height) {
        LinearLayout list = ui.column();
        double rowHeight = (height - 8 * (shown - 1)) / shown, size = CardSizing.clockListTime(rowHeight);
        boolean detail = rowHeight >= CardSizing.CLOCK_DETAIL_ROW;
        double f = CardSizing.clockListFace(this.width, rowHeight);
        for (int i = 0; i < shown; i++) {
            Part part = part(size);
            LinearLayout row = ui.row(); row.setGravity(Gravity.CENTER_VERTICAL);
            if (f > 0) { part.face = new ReadingParts.ClockFace(ui); row.addView(part.face, ui.margins(ui.ll(ui.px(f), ui.px(f + Stickers.LIFT)), 0, 0, 10, 0)); }
            LinearLayout left = ui.column();
            part.label = ui.body(""); left.addView(part.label, ui.ll(Ui.MATCH, Ui.WRAP));
            if (detail) { part.day = ui.caption(""); left.addView(part.day, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 2, 0, 0)); }
            row.addView(left, ui.ll(0, Ui.WRAP, 1));
            row.addView(timeRow(part, size), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, 0, 0));
            list.addView(row, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, i == 0 ? 0 : 8, 0, 0));
        }
        return list;
    }

    // The city's name over Day or Evening in its ink, beside its face (at the left, or the right on Large tiles).
    private View heading(Part part, double face, boolean right, boolean bold) {
        LinearLayout heading = ui.row(); heading.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout words = ui.column();
        part.label = Ui.wrap(ui.body(""), 1); if (bold) part.label.setTypeface(Ui.MEDIUM); words.addView(part.label, ui.ll(Ui.MATCH, Ui.WRAP));
        part.word = ui.text("", 13, ui.theme.muted); part.word.setTypeface(Ui.MEDIUM); words.addView(part.word, ui.ll(Ui.MATCH, Ui.WRAP));
        part.face = new ReadingParts.ClockFace(ui);
        LinearLayout.LayoutParams at = ui.ll(ui.px(face), ui.px(face + Stickers.LIFT));
        if (right) { heading.addView(words, ui.ll(0, Ui.WRAP, 1)); heading.addView(part.face, ui.margins(at, 10, 0, 0, 0)); }
        else { heading.addView(part.face, ui.margins(at, 0, 0, 10, 0)); heading.addView(words, ui.ll(0, Ui.WRAP, 1)); }
        return heading;
    }

    // Medium: the cities side by side, each with its face, name, a large time, the day and the hours against the PC.
    private View columns(int n, double width, double height) {
        LinearLayout view = ui.row();
        boolean faces = CardSizing.clockColumnFaces(height);
        double column = (width - GAP * (n - 1)) / n, face = CardSizing.clockFace(column, height);
        double size = CardSizing.clockColumnTime(column, height - (faces ? Math.max(0, face + Stickers.LIFT - 22) : 0), dash.use24());
        for (int i = 0; i < n; i++) {
            Part part = part(size);
            LinearLayout content = ui.column();
            if (faces) content.addView(heading(part, face, false, false), ui.ll(Ui.MATCH, Ui.WRAP));
            else { part.label = Ui.wrap(ui.body(""), 1); content.addView(part.label, ui.ll(Ui.MATCH, Ui.WRAP)); }
            content.addView(timeRow(part, size), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 6, 0, 4));
            part.day = ui.caption(""); content.addView(part.day, ui.ll(Ui.MATCH, Ui.WRAP));
            part.difference = ui.caption(""); content.addView(part.difference, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 2, 0, 0));
            view.addView(content, ui.margins(ui.ll(0, Ui.WRAP, 1), i == 0 ? 0 : GAP / 2, 0, i == n - 1 ? 0 : GAP / 2, 0));
        }
        return view;
    }

    // Large: a tile per city with a larger time and the date; side by side, or stacked on a narrow card.
    private View tiles(int n, double width, double height) {
        boolean across = CardSizing.clockTilesAcross(width, n);
        double tileWidth = across ? (width - GAP * (n - 1)) / n : width, tileHeight = across ? height : (height - GAP * (n - 1)) / n;
        double face = across ? CardSizing.clockTileFace(tileWidth, tileHeight) : CardSizing.clockStackedFace(tileHeight);
        boolean bands = across || CardSizing.clockStackedBand(tileHeight);
        double size = across ? CardSizing.clockTileTime(tileWidth, tileHeight - Math.max(0, face + Stickers.LIFT - 22) - 30, dash.use24()) : CardSizing.clockStackedTime(tileWidth - face - 10, tileHeight - (bands ? 26 : 0));
        LinearLayout view = across ? ui.row() : ui.column();
        for (int i = 0; i < n; i++) {
            Part part = part(size);
            FrameLayout tile = new FrameLayout(ui.context);
            tile.setBackground(ui.shape(ui.theme.raised, 0, 0, 16));
            if (across) {
                tile.setPadding(ui.px(16), ui.px(16), ui.px(16), ui.px(16));
                LinearLayout content = ui.column();
                content.addView(heading(part, face, true, true), ui.ll(Ui.MATCH, Ui.WRAP));
                content.addView(timeRow(part, size), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 6, 0, 6));
                part.day = ui.text("", ui.small(), ui.theme.text); content.addView(part.day, ui.ll(Ui.MATCH, Ui.WRAP));
                part.difference = ui.caption(""); content.addView(part.difference, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 2, 0, 0));
                part.band = new ReadingParts.DayBand(ui); content.addView(part.band, ui.margins(ui.ll(Ui.MATCH, ui.px(20)), 0, 10, 0, 0));
                tile.addView(content, ui.frame(Ui.MATCH, Ui.WRAP, Gravity.CENTER_VERTICAL));
                view.addView(tile, ui.margins(ui.ll(0, Ui.MATCH, 1), i == 0 ? 0 : GAP / 2, 0, i == n - 1 ? 0 : GAP / 2, 0));
            } else {
                // Stacked tiles put the time at the right of the name, with the date and hours on one line.
                tile.setPadding(ui.px(16), ui.px(10), ui.px(16), ui.px(10));
                LinearLayout row = ui.row(); row.setGravity(Gravity.CENTER_VERTICAL);
                part.face = new ReadingParts.ClockFace(ui);
                row.addView(part.face, ui.margins(ui.ll(ui.px(face), ui.px(face + Stickers.LIFT)), 0, 0, 10, 0));
                LinearLayout left = ui.column();
                part.label = Ui.wrap(ui.body(""), 1); part.label.setTypeface(Ui.MEDIUM);
                left.addView(part.label, ui.ll(Ui.MATCH, Ui.WRAP));
                part.day = Ui.wrap(ui.caption(""), 1); left.addView(part.day, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 2, 0, 0));
                row.addView(left, ui.ll(0, Ui.WRAP, 1));
                row.addView(timeRow(part, size), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, 0, 0));
                LinearLayout stack = ui.column(); stack.setGravity(Gravity.CENTER_VERTICAL);
                stack.addView(row, ui.ll(Ui.MATCH, Ui.WRAP));
                if (bands) { part.band = new ReadingParts.DayBand(ui); stack.addView(part.band, ui.margins(ui.ll(Ui.MATCH, ui.px(18)), 0, 6, 0, 0)); }
                tile.addView(stack, ui.frame(Ui.MATCH, Ui.WRAP, Gravity.CENTER_VERTICAL));
                view.addView(tile, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, i == 0 ? 0 : GAP / 2, 0, i == n - 1 ? 0 : GAP / 2));
            }
        }
        return view;
    }

    // Times move on with the phone's clock; the text is only set again when a minute turns.
    @Override public void tick(int ticks, boolean quiet) { if (active()) render(); }

    @Override public void render() {
        if (!built()) return;
        PcState.Clocks c = clocks();
        if (!shape(c).equals(builtFor)) { dash.rebuildWidget(id); return; }
        status.setText(!dash.online() ? "PC OFFLINE" : c != null && c.clocks.size() > parts.size() ? "+" + (c.clocks.size() - parts.size()) + " MORE" : "");
        if (c == null) return;
        long now = System.currentTimeMillis();
        boolean use24 = dash.use24();
        String key = (now / 60000) + "|" + c.pcOffset;
        for (PcState.Clock clock : c.clocks) key += "|" + clock.offset;
        if (key.equals(rendered)) return;
        rendered = key;
        for (int i = 0; i < parts.size() && i < c.clocks.size(); i++) {
            PcState.Clock clock = c.clocks.get(i); Part part = parts.get(i);
            LocalDateTime time = WorldClockText.at(now, clock.offset);
            int dayOffset = WorldClockText.dayOffset(now, clock.offset, c.pcOffset), difference = clock.offset - c.pcOffset;
            part.label.setText(clock.label);
            part.time.setText(Format.clockTime(time, use24));
            part.period.setText(use24 ? "" : Format.clockPeriod(time));
            part.period.setVisibility(use24 ? View.GONE : View.VISIBLE);
            String day = WorldClockText.day(dayOffset), hours = WorldClockText.difference(difference);
            if (part.face != null) { part.face.show(time); part.face.setContentDescription(clock.label + ", " + WorldClockText.partOfDay(time)); }
            if (part.band != null) part.band.show(time);
            if (part.word != null) { part.word.setText(WorldClockText.partOfDay(time)); part.word.setTextColor(Theme.ink(WorldClockText.ink(time))); }
            if (design == CardSizing.SMALL) { if (part.day != null) part.day.setText(day + "  ·  " + hours); }
            else if (design == CardSizing.MEDIUM) { part.day.setText(day); part.difference.setText(hours); }
            else if (part.difference == null) part.day.setText(WorldClockText.date(time, dayOffset) + "  ·  " + hours);
            else { part.day.setText(WorldClockText.date(time, dayOffset)); part.difference.setText(difference == 0 ? "Same time as the PC" : hours + " from the PC"); }
        }
    }
}
