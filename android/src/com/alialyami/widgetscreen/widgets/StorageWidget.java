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
import com.alialyami.widgetscreen.views.RingGauge;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

// The PC's drives, in the same three designs as the Windows StorageCard (CardSizing.storage; docs/design/storage.md):
// Small lists each drive's letter and free space over a thin bar; Medium adds the drive's name, a thicker bar and used
// of total; Large gives each drive a tile with a ring of used space and the free percentage inside. A drive under the
// PC's low-space threshold turns to the warning colour and the heading names it. The PC reads the drives once a minute,
// so the card simply follows its answers.
public final class StorageWidget extends Widget {
    private static final double GAP = 12;
    private TextView status, more;
    private final List<Part> parts = new ArrayList<Part>();
    private String builtFor = "";
    private int design;

    private static final class Part {
        TextView title, free, detail;
        ProgressLine bar;
        RingGauge ring;
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
    private ProgressLine bar() { return new ProgressLine(ui.context, ui.theme.accent, ui.theme.track); }
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

    // Medium: a row per drive with its name and free space, a bar, and used of total under it.
    private View rows(int shown, boolean overflow, boolean detail) {
        LinearLayout view = ui.column(); view.setGravity(Gravity.CENTER_VERTICAL);
        for (int i = 0; i < shown; i++) {
            Part part = new Part(); parts.add(part);
            LinearLayout row = ui.column();
            LinearLayout top = ui.row();
            part.title = detail ? ui.body("") : ui.text("", ui.small(), ui.theme.text); top.addView(part.title, ui.ll(0, Ui.WRAP, 1));
            part.free = detail ? ui.body("—") : ui.text("—", ui.small(), ui.theme.text); top.addView(part.free, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 12, 0, 0, 0));
            row.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
            part.bar = bar(); row.addView(part.bar, ui.margins(ui.ll(Ui.MATCH, ui.px(detail ? 10 : 4)), 0, detail ? 6 : 3, 0, 0));
            if (detail) { part.detail = ui.caption(""); row.addView(part.detail, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 5, 0, 0)); }
            view.addView(row, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, i == 0 ? 0 : detail ? 8 : 4, 0, 0));
        }
        if (overflow) view.addView(moreLine(), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
        return view;
    }

    // Large: a tile per drive with a ring of used space, the free percentage inside, and the drive and sizes under it.
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
                FrameLayout dial = new FrameLayout(ui.context);
                part.ring = new RingGauge(ui.context, ui.theme.accent, ui.theme.track, ui.fpx(Math.max(6, Math.min(14, Math.round(diameter * 0.08)))));
                dial.addView(part.ring, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
                LinearLayout inside = ui.column(); inside.setGravity(Gravity.CENTER_HORIZONTAL);
                part.free = reading("—", Math.round(diameter * 0.24)); inside.addView(part.free, ui.ll(Ui.WRAP, Ui.WRAP));
                inside.addView(ui.caption("free"), ui.ll(Ui.WRAP, Ui.WRAP));
                dial.addView(inside, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.CENTER));
                content.addView(dial, ui.ll(ui.px(diameter), ui.px(diameter)));
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
            int fill = low ? Theme.WARNING : ui.theme.accent;
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
            if (part.bar != null) { part.bar.setValue(StorageText.usedPercent(drive) / 100); part.bar.setFill(fill); }
            if (part.ring != null) { part.ring.setValue(StorageText.usedPercent(drive)); part.ring.setFill(fill); }
        }
        if (more != null) {
            more.setText(StorageText.more(s.drives, parts.size(), s.lowPercent));
            boolean hiddenLow = false;
            for (int i = parts.size(); i < s.drives.size(); i++) if (StorageText.low(s.drives.get(i), s.lowPercent)) hiddenLow = true;
            more.setTextColor(hiddenLow ? Theme.WARNING : ui.theme.muted);
        }
    }
}
