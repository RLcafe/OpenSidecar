package com.alialyami.widgetscreen.widgets;

import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.StorageText;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.views.ProgressLine;
import com.alialyami.widgetscreen.views.ReadingParts;
import com.alialyami.widgetscreen.views.Stickers;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

// The PC's drives, in the same three designs as the Windows StorageCard (CardSizing.storage; docs/design/storage.md),
// each drive a jar filled by its used space (Sticker Sheet phase 3, docs/design/sticker-sheet.md): Small lists each
// drive's letter and free space over a thin bar; Medium puts a small jar beside the drive's name, free space and used of
// total; Large gives each drive a tile with a big jar labelled with its free percentage. A drive under the PC's
// low-space threshold fills with Amber caution tape, its free space turns to the warning colour and the heading names
// it. The PC reads the drives once a minute, so the card simply follows its answers.
public final class StorageWidget extends Widget {
    private static final double GAP = 12;
    private TextView status, more;
    private final List<Part> parts = new ArrayList<Part>();
    private String builtFor = "";
    private int design;

    private static final class Part {
        TextView title, free, detail;
        ProgressLine bar;
        ReadingParts.StorageJar jar;
    }

    public StorageWidget(Dashboard dash) { super("Storage", dash); }

    @Override public String part() { return "storage"; }

    private PcState.Storage storage() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.storage; }
    private static int count(PcState.Storage s) { return s == null || s.error ? -1 : s.drives.size(); }

    @Override protected View create() {
        PcState.Storage s = storage();
        builtFor = String.valueOf(count(s));
        parts.clear(); more = null;
        design = CardSizing.storage(width, height);

        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(strip(), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        status = sticker(true);
        
        header.addView(status, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));

        // The body: the card less its 24-unit padding, the 28-unit heading and the 8-unit gap under it.
        double bodyWidth = Math.max(1, width - 48), bodyHeight = Math.max(1, height - 48 - 36);
        View body;
        if (s == null || s.error) body = ui.caption(s == null ? "Waiting for the PC…" : "The PC couldn't read its drives.");
        else if (s.drives.isEmpty()) body = ui.caption("No fixed drives found on the PC.");
        else {
            int capacity = CardSizing.storageCapacity(design, bodyWidth, bodyHeight, s.drives.size());
            int shown = capacity >= s.drives.size() ? s.drives.size() : Math.max(1, capacity - 1);
            boolean overflow = shown < s.drives.size();
            body = design == CardSizing.SMALL ? list(shown, bodyHeight, overflow) : design == CardSizing.MEDIUM ? rows(shown, overflow, CardSizing.storageDetailRows(bodyHeight, s.drives.size())) : tiles(shown, bodyWidth, bodyHeight, overflow);
        }
        column.addView(body, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 0));
        return ui.card(column, 24);
    }

    private TextView reading(String text, double size) {
        TextView view = Ui.tabular(ui.text(text, (float) size, ui.theme.text));
        view.setTypeface(Ui.LIGHT);
        return view;
    }
    private ProgressLine bar() { return new ProgressLine(ui.context, dash.ink(id), ui.theme.track); }
    private TextView moreLine() { more = ui.caption(""); return more; }

    // Small: one row per drive, the letter at the left and the free space at the right, a thin bar under them.
    private View list(int shown, double height, boolean overflow) {
        LinearLayout list = ui.column();
        double rows = height - (overflow ? 26 : 0), rowHeight = (rows - 8 * (shown - 1)) / shown, size = CardSizing.storageListValue(rowHeight);
        for (int i = 0; i < shown; i++) {
            Part part = new Part(); parts.add(part);
            LinearLayout row = ui.column(); row.setGravity(Gravity.CENTER_VERTICAL);
            LinearLayout top = ui.row();
            part.title = ui.label(""); top.addView(part.title, ui.ll(0, Ui.WRAP, 1));
            part.free = reading("—", size); top.addView(part.free, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, 0, 0));
            row.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
            part.bar = bar(); row.addView(part.bar, ui.margins(ui.ll(Ui.MATCH, ui.px(4)), 0, 4, 0, 0));
            list.addView(row, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, i == 0 ? 0 : 8, 0, 0));
        }
        if (overflow) list.addView(moreLine(), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
        return list;
    }

    // Medium: a row per drive with a small jar, its name and free space, and used of total under them.
    private View rows(int shown, boolean overflow, boolean detail) {
        LinearLayout view = ui.column(); view.setGravity(Gravity.CENTER_VERTICAL);
        double jar = detail ? 46 : 24;
        for (int i = 0; i < shown; i++) {
            Part part = new Part(); parts.add(part);
            LinearLayout line = ui.row(); line.setGravity(Gravity.CENTER_VERTICAL);
            part.jar = new ReadingParts.StorageJar(ui, dash.ink(id));
            line.addView(part.jar, ui.margins(ui.ll(ui.px(ReadingParts.StorageJar.widthFor(jar)), ui.px(jar)), 0, 0, 12, 0));
            LinearLayout row = ui.column();
            LinearLayout top = ui.row();
            part.title = Ui.wrap(detail ? ui.body("") : ui.text("", ui.small(), ui.theme.text), 1); top.addView(part.title, ui.ll(0, Ui.WRAP, 1));
            part.free = detail ? ui.body("—") : ui.text("—", ui.small(), ui.theme.text); top.addView(part.free, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, 0, 0));
            row.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
            if (detail) { part.detail = Ui.wrap(ui.caption(""), 1); row.addView(part.detail, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0)); }
            line.addView(row, ui.ll(0, Ui.WRAP, 1));
            view.addView(line, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, i == 0 ? 0 : detail ? 8 : 4, 0, 0));
        }
        if (overflow) view.addView(moreLine(), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
        return view;
    }

    // Large: a tile per drive with a jar of used space labelled with the free percentage, and the drive and sizes under it.
    private View tiles(int shown, double width, double height, boolean overflow) {
        int columns = CardSizing.storageTileColumns(width, shown), rows = (shown + columns - 1) / columns;
        double room = height - (overflow ? 26 : 0), tileHeight = (room - GAP * (rows - 1)) / rows, tileWidth = (width - GAP * (columns - 1)) / columns;
        double diameter = CardSizing.storageRing(tileWidth, tileHeight);
        LinearLayout view = ui.column();
        for (int r = 0; r < rows; r++) {
            LinearLayout line = ui.row(); line.setGravity(Gravity.FILL_VERTICAL);
            for (int c = 0; c < columns; c++) {
                int index = r * columns + c;
                if (index >= shown) { line.addView(new View(ui.context), ui.margins(ui.ll(0, Ui.MATCH, 1), GAP / 2, 0, 0, 0)); continue; }
                Part part = new Part(); parts.add(part);
                LinearLayout content = ui.column(); content.setGravity(Gravity.CENTER);
                double jarWidth = Math.min(ReadingParts.StorageJar.widthFor(diameter), tileWidth - 24);
                FrameLayout dial = new FrameLayout(ui.context);
                part.jar = new ReadingParts.StorageJar(ui, dash.ink(id));
                dial.addView(part.jar, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
                // The jar's label: the free percentage on a pill in the card's colour, readable over any fill.
                LinearLayout inside = ui.column(); inside.setGravity(Gravity.CENTER_HORIZONTAL);
                inside.setBackground(ui.shape(ui.theme.surface, 0, 0, 10));
                inside.setPadding(ui.px(10), ui.px(2), ui.px(10), ui.px(4));
                part.free = reading("—", Math.max(18, Math.round(diameter * 0.17))); inside.addView(part.free, ui.ll(Ui.WRAP, Ui.WRAP));
                inside.addView(ui.caption("free"), ui.ll(Ui.WRAP, Ui.WRAP));
                FrameLayout.LayoutParams at = ui.frame(Ui.WRAP, Ui.WRAP, Gravity.CENTER); at.topMargin = ui.px(diameter * 0.12);
                dial.addView(inside, at);
                content.addView(dial, ui.ll(ui.px(jarWidth), ui.px(diameter)));
                part.title = ui.body(""); part.title.setGravity(Gravity.CENTER); part.title.setMaxWidth(ui.px(Math.max(60, tileWidth - 24)));
                content.addView(part.title, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 10, 0, 0));
                part.detail = ui.caption(""); part.detail.setGravity(Gravity.CENTER); part.detail.setMaxWidth(ui.px(Math.max(60, tileWidth - 24)));
                content.addView(part.detail, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 2, 0, 0));
                FrameLayout tile = new FrameLayout(ui.context);
                tile.setBackground(ui.shape(ui.theme.raised, 0, 0, 16));
                tile.setPadding(ui.px(12), ui.px(12), ui.px(12), ui.px(12));
                tile.addView(content, ui.frame(Ui.MATCH, Ui.WRAP, Gravity.CENTER));
                line.addView(tile, ui.margins(ui.ll(0, Ui.MATCH, 1), c == 0 ? 0 : GAP / 2, 0, c == columns - 1 ? 0 : GAP / 2, 0));
            }
            view.addView(line, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, r == 0 ? 0 : GAP, 0, 0));
        }
        if (overflow) view.addView(moreLine(), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
        return view;
    }

    // Fills the current design from the PC's last answer; a new number of drives rebuilds the card.
    @Override public void render() {
        if (!built()) return;
        PcState.Storage s = storage();
        if (!String.valueOf(count(s)).equals(builtFor)) { dash.rebuildWidget(id); return; }
        status.setText(!dash.online() ? "PC OFFLINE" : s == null ? "" : s.error ? "UNAVAILABLE" : StorageText.lowFlag(s.drives, s.lowPercent));
        if (s == null || s.error) return;
        for (int i = 0; i < parts.size() && i < s.drives.size(); i++) {
            PcState.Drive drive = s.drives.get(i); Part part = parts.get(i);
            boolean low = StorageText.low(drive, s.lowPercent);
            String free = StorageText.bytes(drive.free);
            if (design == CardSizing.SMALL) { part.title.setText(drive.name); part.free.setText(free); }
            else if (design == CardSizing.MEDIUM) {
                part.title.setText(StorageText.title(drive)); part.free.setText(free + " free");
                if (part.detail != null) part.detail.setText(StorageText.bytes(drive.total - drive.free) + " used of " + StorageText.bytes(drive.total) + "  ·  " + Math.round(StorageText.freePercent(drive)) + "% free");
            } else {
                part.free.setText(String.format(Locale.US, "%d%%", Math.round(StorageText.freePercent(drive))));
                part.title.setText(StorageText.title(drive));
                part.detail.setText(free + " free of " + StorageText.bytes(drive.total));
            }
            part.free.setTextColor(low ? Theme.WARNING : ui.theme.text);
            if (part.bar != null) { part.bar.setValue(StorageText.usedPercent(drive) / 100); if (low) part.bar.setStripes(Stickers.caution(ui)); else part.bar.setFill(dash.ink(id)); }
            if (part.jar != null) part.jar.set(StorageText.usedPercent(drive), low);
        }
        if (more != null) {
            more.setText(StorageText.more(s.drives, parts.size(), s.lowPercent));
            boolean hiddenLow = false;
            for (int i = parts.size(); i < s.drives.size(); i++) if (StorageText.low(s.drives.get(i), s.lowPercent)) hiddenLow = true;
            more.setTextColor(hiddenLow ? Theme.WARNING : ui.theme.muted);
        }
    }
}
