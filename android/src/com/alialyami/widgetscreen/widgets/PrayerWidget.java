package com.alialyami.widgetscreen.widgets;

import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.views.Stickers;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.PrayerText;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.views.ProgressLine;
import com.alialyami.widgetscreen.views.ReadingParts;

import java.util.ArrayList;
import java.util.List;

// The PC's prayer times, in the same three designs as the Windows PrayerCard (CardSizing.prayer;
// docs/design/prayer-times.md), with the day's sun path (Sticker Sheet phase 3, docs/design/sticker-sheet.md): an arc
// from Fajr to Isha with each prayer placed by its time and the sun at now. The next prayer with its countdown; beside
// today's five times (over the sun path when there's room, else a progress line); and on Large beside the sun path,
// with Hijri, Method and Alert chips over rows or tiles. The PC works the times out and plays the alert; while it
// plays, the card offers Stop. The city, method and alert are set on the PC (Settings → Prayer).
public final class PrayerWidget extends Widget {
    private Stickers.Sticker status;
    private TextView nextName, nextTime, countdown, hijri, method, alert;
    private ProgressLine progress;
    private ReadingParts.PrayerPath path;
    private View stop;
    private final List<TextView[]> rows = new ArrayList<TextView[]>();
    private String builtFor = "", rendered = "";
    private int design;

    public PrayerWidget(Dashboard dash) { super("Prayer", dash); }

    @Override public String part() { return "prayer"; }

    private PcState.Prayer prayer() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.prayer; }
    private String shape(PcState.Prayer p) { return p == null ? "none" : p.located + "|" + dash.use24(); }

    @Override protected View create() {
        PcState.Prayer p = prayer();
        builtFor = shape(p); rendered = "";
        rows.clear(); nextName = nextTime = countdown = hijri = method = alert = null; progress = null; path = null;
        design = CardSizing.prayer(width, height);

        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(strip(), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        status = sticker(false);
        header.addView(status, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));

        // The body: the card less its 24-unit padding, the 28-unit heading and the 8-unit gap under it.
        double bodyWidth = Math.max(1, width - 48), bodyHeight = Math.max(1, height - 48 - 36);
        View body;
        if (p == null) body = ui.caption("Waiting for the PC…");
        else if (!p.located) body = Stickers.unavailableBody(ui, "Choose a city in Settings → Prayer on the PC, or set the weather city.");
        else if (design == CardSizing.SMALL) body = next(bodyHeight, CardSizing.prayerTime(bodyWidth, bodyHeight), false);
        else if (design == CardSizing.MEDIUM) {
            LinearLayout row = ui.row();
            double left = CardSizing.prayerNextColumn(bodyWidth);
            row.addView(next(bodyHeight, CardSizing.prayerTime(left, bodyHeight), true), ui.ll(ui.px(left), Ui.MATCH));
            row.addView(list(bodyHeight, false), ui.margins(ui.ll(0, Ui.WRAP, 1), 20, 0, 0, 0));
            body = row;
        } else {
            boolean across = CardSizing.prayerTilesAcross(bodyWidth);
            boolean rail = CardSizing.prayerChips(bodyHeight);
            double chips = rail ? 57 + 12 : 0, top = CardSizing.prayerPathTop(bodyHeight - chips, across), rest = bodyHeight - top - chips - 12;
            LinearLayout stack = ui.column();
            LinearLayout upper = ui.row(); upper.setGravity(Gravity.CENTER_VERTICAL);
            upper.addView(next(top, CardSizing.prayerTime(bodyWidth * 0.46, top), false), ui.ll(0, Ui.WRAP, 1));
            path = new ReadingParts.PrayerPath(ui, dash.ink(id));
            upper.addView(path, ui.margins(ui.ll(0, ui.px(Math.min(top, Math.round(bodyWidth / 4 + 12))), 1), 12, 0, 0, 0));
            stack.addView(upper, ui.ll(Ui.MATCH, ui.px(top)));
            if (rail) stack.addView(chips(), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 12, 0, 0));
            stack.addView(across ? tiles(bodyWidth) : list(rest, CardSizing.prayerSunriseRow(rest)), ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 12, 0, 0));
            body = stack;
        }
        FrameLayout content = new FrameLayout(ui.context);
        content.addView(body, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        // Shown while the PC plays the alert.
        stop = ui.accentButton("Stop", new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "prayer.stop"), "Stopped the alert on the PC."); } });
        stop.setMinimumWidth(ui.px(120));
        stop.setContentDescription("Stop the prayer alert on the PC");
        stop.setVisibility(View.GONE);
        content.addView(stop, ui.frame(Ui.WRAP, ui.px(48), Gravity.END | Gravity.BOTTOM));
        column.addView(content, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        return ui.card(column, 24);
    }

    // The next prayer: its name, time and countdown, with a progress line and the Hijri date on the larger designs.
    private View next(double height, double size, boolean line) {
        LinearLayout panel = ui.column(); panel.setGravity(Gravity.CENTER_VERTICAL);
        nextName = ui.body(""); nextName.setTextColor(ui.theme.accent); nextName.setTypeface(Ui.MEDIUM);
        nextTime = Ui.tabular(ui.text("", (float) size, ui.theme.text)); nextTime.setTypeface(Ui.LIGHT);
        countdown = ui.smallText("");
        if (design == CardSizing.SMALL && height < 100) {
            LinearLayout top = ui.row();
            top.addView(nextName, ui.ll(Ui.WRAP, Ui.WRAP));
            countdown.setGravity(Gravity.END);
            top.addView(countdown, ui.margins(ui.ll(0, Ui.WRAP, 1), 10, 0, 0, 0));
            panel.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
            panel.addView(nextTime, ui.ll(Ui.WRAP, Ui.WRAP));
            return panel;
        }
        panel.addView(nextName, ui.ll(Ui.MATCH, Ui.WRAP));
        panel.addView(nextTime, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 2, 0, 2));
        panel.addView(countdown, ui.ll(Ui.MATCH, Ui.WRAP));
        // Medium: the sun path under the countdown when it has room, or else a progress line.
        double room = CardSizing.prayerPathRoom(height, size);
        // The path takes what the text leaves, so larger text shrinks it.
        if (line && room >= CardSizing.PRAYER_PATH_MINIMUM) {
            panel.setGravity(Gravity.TOP);
            path = new ReadingParts.PrayerPath(ui, dash.ink(id));
            FrameLayout holder = new FrameLayout(ui.context);
            holder.addView(path, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
            panel.addView(holder, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 10, 0, 0));
        }
        else if (line && height >= 150) { progress = new ProgressLine(ui.context, ui.theme.accent, ui.theme.track); panel.addView(progress, ui.margins(ui.ll(Ui.MATCH, ui.px(6)), 0, 12, 0, 0)); }
        return panel;
    }

    // Large: Hijri, Method and Alert as fact-rail chips.
    private View chips() {
        LinearLayout rail = ui.row();
        String[] captions = { "Hijri", "Method", "Alert" };
        for (int i = 0; i < 3; i++) {
            LinearLayout chip = Stickers.factChip(ui, captions[i], "");
            TextView value = (TextView) chip.getChildAt(1);
            value.setSingleLine(true); value.setEllipsize(android.text.TextUtils.TruncateAt.END);
            if (i == 0) hijri = value; else if (i == 1) method = value; else alert = value;
            rail.addView(chip, ui.margins(ui.ll(0, Ui.WRAP, 1), i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0));
        }
        return rail;
    }

    // Today's times as rows; Large adds sunrise and spreads them over its room.
    private View list(double height, boolean sunrise) {
        LinearLayout list = ui.column(); list.setGravity(Gravity.CENTER_VERTICAL);
        int count = sunrise ? 6 : 5;
        double room = height / count;
        float size = room >= 34 ? ui.body() : room >= 30 ? ui.small() : 13;
        for (int i = 0; i < count; i++) {
            LinearLayout row = ui.row();
            TextView name = ui.text("", size, ui.theme.text), time = Ui.tabular(ui.text("", size, ui.theme.text));
            row.addView(name, ui.ll(0, Ui.WRAP, 1));
            row.addView(time, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, 0, 0));
            list.addView(row, sunrise ? ui.ll(Ui.MATCH, 0, 1) : ui.ll(Ui.MATCH, Ui.WRAP));
            if (!sunrise) ((LinearLayout.LayoutParams) row.getLayoutParams()).bottomMargin = ui.px(Math.max(0, room - size * 1.33));
            rows.add(new TextView[] { name, time });
        }
        return list;
    }

    // A wide Large card: the five prayers and sunrise as tiles across.
    private View tiles(double width) {
        LinearLayout row = ui.row(); row.setGravity(Gravity.FILL_VERTICAL);
        double size = CardSizing.prayerTileTime((width - 50) / 6, dash.use24());
        for (int i = 0; i < 6; i++) {
            LinearLayout content = ui.column(); content.setGravity(Gravity.CENTER_VERTICAL);
            TextView name = ui.smallText(""), time = Ui.tabular(ui.text("", (float) size, ui.theme.text));
            time.setTypeface(Ui.LIGHT);
            content.addView(name, ui.ll(Ui.MATCH, Ui.WRAP));
            content.addView(time, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
            FrameLayout tile = new FrameLayout(ui.context);
            tile.setBackground(ui.shape(ui.theme.raised, 0, 0, 14));
            tile.setPadding(ui.px(14), ui.px(10), ui.px(10), ui.px(10));
            tile.addView(content, ui.frame(Ui.MATCH, Ui.WRAP, Gravity.CENTER_VERTICAL));
            row.addView(tile, ui.margins(ui.ll(0, Ui.MATCH, 1), i == 0 ? 0 : 5, 0, i == 5 ? 0 : 5, 0));
            rows.add(new TextView[] { name, time });
        }
        return row;
    }

    @Override public void tick(int ticks, boolean quiet) { if (active()) render(); }

    @Override public void render() {
        if (!built()) return;
        PcState.Prayer p = prayer();
        if (!shape(p).equals(builtFor)) { dash.rebuildWidget(id); return; }
        stop.setVisibility(p != null && p.alerting ? View.VISIBLE : View.GONE);
        if (p == null || !p.located) { status.setText(!dash.online() ? "PC OFFLINE" : ""); return; }
        long now = System.currentTimeMillis();
        boolean use24 = dash.use24(), started = PrayerText.justStarted(now, p.previousAt);
        // Between readings the next prayer may have begun; the PC's next reading moves it on.
        String left = PrayerText.countdown((long) Math.max(0, p.nextAt - now));
        String key = left + "|" + p.next + "|" + started + "|" + p.alerting + "|" + p.times[0] + "|" + use24;
        if (key.equals(rendered)) return;
        rendered = key;
        boolean now_ = started || p.alerting;
        String city = p.city.contains(",") ? p.city.substring(0, p.city.indexOf(',')).trim() : p.city;
        status.setText(now_ ? (PrayerText.name(p.current) + " now").toUpperCase(java.util.Locale.US) : city);
        status.setFilled(now_);
        nextName.setText(PrayerText.name(p.next));
        nextTime.setText(PrayerText.time(p.nextAt, p.pcOffset, use24));
        countdown.setText(left);
        if (progress != null) progress.setValue(PrayerText.progress(now, p.previousAt, p.nextAt));
        if (hijri != null) { hijri.setText(p.hijri); method.setText(PrayerText.methodShort(p.method)); alert.setText(p.alert); }
        if (path != null) {
            double[] parts = new double[5]; int nextToday = -1;
            for (int i = 0; i < 5; i++) { parts[i] = PrayerText.pathPart(p.times[i], p.times[0], p.times[4]); if (p.times[i] == p.nextAt) nextToday = i; }
            path.show(parts, PrayerText.pathPart(p.sunrise, p.times[0], p.times[4]), PrayerText.pathPart(now, p.times[0], p.times[4]), nextToday);
        }
        int r = 0;
        for (int i = 0; i < 5 && r < rows.size(); i++) {
            fill(rows.get(r++), PrayerText.name(i), p.times[i], p, i, now, started, use24);
            if (i == 0 && rows.size() == 6) fill(rows.get(r++), "Sunrise", p.sunrise, p, -1, now, started, use24);
        }
    }

    // The next prayer in the accent, past ones (and sunrise) muted.
    private void fill(TextView[] row, String name, double at, PcState.Prayer p, int index, long now, boolean started, boolean use24) {
        boolean isNext = index == p.next && at == p.nextAt, isNow = started && index == p.current && at == p.previousAt;
        row[0].setText(name);
        row[1].setText(PrayerText.time(at, p.pcOffset, use24));
        int color = isNext || isNow ? ui.theme.accent : index < 0 || (at > 0 && at < now) ? ui.theme.muted : ui.theme.text;
        for (TextView view : row) { view.setTextColor(color); view.setTypeface(isNext ? Ui.MEDIUM : Ui.REGULAR); }
    }
}
