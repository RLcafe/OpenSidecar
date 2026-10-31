package com.alialyami.widgetscreen.widgets;

import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.views.GlyphView;

import java.util.List;

// The PC's Quick Launch deck. Each button runs on the PC; buttons the PC would confirm are confirmed here.
// The buttons themselves are edited on the PC (Settings → Quick Launch).
public final class QuickLaunchWidget extends Widget {
    private int page;
    private LinearLayout grid, pager;
    private TextView pageLabel, empty, heading;
    private GlyphView previousPage, nextPage;
    private String shown;

    public QuickLaunchWidget(Dashboard dash) { super("Shortcuts", dash); }

    @Override public String part() { return "deck"; }


    @Override protected View create() {
        shown = null;
        LinearLayout column = ui.column();
        LinearLayout header = ui.row();
        heading = ui.label("QUICK LAUNCH");
        heading.setSingleLine(true); heading.setEllipsize(android.text.TextUtils.TruncateAt.END);
        header.addView(heading, ui.margins(ui.ll(0, Ui.WRAP, 1), 4, 0, 4, 0));
        pager = ui.row();
        previousPage = ui.iconButton("chevron-left", "Previous button page", 16, false, new View.OnClickListener() { @Override public void onClick(View v) { page--; shown = null; render(); } });
        pager.addView(previousPage, ui.ll(ui.px(48), ui.px(48)));
        pageLabel = ui.caption("");
        pageLabel.setGravity(Gravity.CENTER);
        pager.addView(pageLabel, ui.ll(ui.px(46), Ui.WRAP));
        nextPage = ui.iconButton("chevron-right", "Next button page", 16, false, new View.OnClickListener() { @Override public void onClick(View v) { page++; shown = null; render(); } });
        pager.addView(nextPage, ui.ll(ui.px(48), ui.px(48)));
        header.addView(pager, ui.ll(Ui.WRAP, Ui.WRAP));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(48)));

        FrameLayout body = new FrameLayout(ui.context);
        grid = ui.column();
        body.addView(grid, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        empty = Ui.wrap(ui.smallText(""), 3);
        empty.setGravity(Gravity.CENTER);
        body.addView(empty, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        column.addView(body, ui.ll(Ui.MATCH, 0, 1));
        return ui.card(column, 20);
    }

    @Override public void render() {
        if (!built()) return;
        PcState state = dash.state();
        List<PcState.Button> buttons = state == null || !dash.online() ? null : state.deck;
        // Rebuild the buttons only when they changed; the PC answers every couple of seconds.
        String signature = buttons == null ? "offline" : signature(buttons) + "|" + page + "|" + width + "x" + height;
        if (signature.equals(shown)) return;
        shown = signature;
        grid.removeAllViews();
        if (buttons == null || buttons.isEmpty()) {
            pager.setVisibility(View.GONE);
            empty.setVisibility(View.VISIBLE);
            empty.setText(buttons == null ? dash.offlineText() : "No buttons yet. Add them on the PC in Settings → Quick Launch.");
            return;
        }
        empty.setVisibility(View.GONE);
        // Six buttons a page that grow with the card, like the Soundboard (CardSizing, as on the PC): the card less its
        // padding and header.
        double areaWidth = Math.max(0, width - 42), areaHeight = Math.max(0, height - 90);
        int[] shape = CardSizing.deckGrid(areaWidth, areaHeight, buttons.size());
        int columns = shape[0], rows = shape[1];
        double cellWidth = areaWidth / columns, cellHeight = areaHeight / rows;
        int labelMode = CardSizing.deckLabel(cellWidth, cellHeight);
        int pageSize = columns * rows, pages = Math.max(1, (buttons.size() + pageSize - 1) / pageSize);
        page = Math.max(0, Math.min(pages - 1, page));
        pager.setVisibility(pages > 1 ? View.VISIBLE : View.GONE);
        // A narrow card keeps its pager over its heading (CardSizing.headingShown, as on the PC).
        heading.setVisibility(CardSizing.headingShown(width, pages > 1) ? View.VISIBLE : View.INVISIBLE);
        Ui.enable(previousPage, page > 0); Ui.enable(nextPage, page < pages - 1);
        pageLabel.setText((page + 1) + "/" + pages);
        for (int r = 0; r < rows; r++) {
            LinearLayout row = ui.row();
            for (int c = 0; c < columns; c++) {
                int index = page * pageSize + r * columns + c;
                View cell = index < buttons.size() ? button(buttons.get(index), index, labelMode, cellWidth, cellHeight) : new View(ui.context);
                LinearLayout.LayoutParams params = ui.ll(0, Ui.MATCH, 1);
                params.setMargins(ui.px(4), ui.px(4), ui.px(4), ui.px(4));
                row.addView(cell, params);
            }
            grid.addView(row, ui.ll(Ui.MATCH, 0, 1));
        }
    }

    private static String signature(List<PcState.Button> buttons) {
        StringBuilder out = new StringBuilder();
        for (PcState.Button b : buttons) out.append(b.label).append('\u0001').append(b.glyph).append('\u0001').append(b.enabled).append('\u0001').append(b.color).append('\u0001').append(b.on).append('\u0001').append(b.describe).append('\u0001').append(b.kind).append('\u0002');
        return out.toString();
    }

    private View button(final PcState.Button shortcut, final int index, int labelMode, double cellWidth, double cellHeight) {
        boolean compact = labelMode == CardSizing.DECK_LABEL_BESIDE;
        double glyph = CardSizing.deckGlyph(cellHeight, compact);
        LinearLayout button = compact ? ui.row() : ui.column();
        button.setGravity(Gravity.CENTER);
        button.setPadding(ui.px(8), ui.px(8), ui.px(8), ui.px(8));
        // Control buttons that toggle something show whether it's on; a button's own colour tells it apart.
        ui.select(button, shortcut.on);
        button.setMinimumHeight(ui.px(48));
        int color = shortcut.color == null || shortcut.color.isEmpty() ? ui.theme.accent : Theme.accentColor(shortcut.color);
        button.addView(new GlyphView(ui.context, shortcut.glyph, color, ui.fpx(glyph)), ui.ll(ui.px(glyph + 4), ui.px(glyph + 4)));
        TextView name = ui.text(shortcut.label, (compact ? 14 : 15) * ui.textScale, ui.theme.text);
        name.setGravity(Gravity.CENTER);
        name.setMaxWidth(ui.px(Math.max(40, compact ? cellWidth - glyph - 40 : cellWidth - 24)));
        // LARGE cells are tiles: the glyph, the name and what the button does, from the top left (as on the PC).
        if (CardSizing.tile(cellWidth, cellHeight) == CardSizing.LARGE) {
            button.removeAllViews();
            button.setOrientation(LinearLayout.VERTICAL); button.setGravity(Gravity.START | Gravity.CENTER_VERTICAL);
            button.setPadding(ui.px(16), ui.px(12), ui.px(12), ui.px(12));
            double size = Math.min(40, glyph);
            button.addView(new GlyphView(ui.context, shortcut.glyph, color, ui.fpx(size)), ui.ll(ui.px(size + 4), ui.px(size + 4)));
            TextView title = ui.text(shortcut.label, 17 * ui.textScale, ui.theme.text);
            title.setTypeface(Ui.MEDIUM); title.setMaxWidth(ui.px(cellWidth - 44));
            button.addView(title, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 12, 0, 0));
            TextView kind = ui.caption(shortcut.kind); kind.setMaxWidth(ui.px(cellWidth - 44));
            button.addView(kind, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 3, 0, 0));
        } else if (labelMode != CardSizing.DECK_LABEL_NONE) button.addView(name, compact ? ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 8, 0, 0, 0) : ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 8, 0, 0));
        button.setContentDescription(shortcut.label + (shortcut.on ? ", on" : ""));
        button.setClickable(true);
        Ui.enable(button, shortcut.enabled);
        button.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { run(shortcut, index); } });
        return button;
    }

    private void run(final PcState.Button shortcut, final int index) {
        if (!shortcut.confirm) { dash.command(Json.map("action", "deck.run", "index", index, "label", shortcut.label, "confirmed", false), "Ran " + shortcut.label + " on the PC."); return; }
        // The same themed sheet as the PC, not the system dialog.
        dash.confirm("QUICK LAUNCH", shortcut.label, shortcut.describe + "\n\nRuns on the PC.", "Run " + shortcut.label, new Runnable() {
            @Override public void run() { dash.command(Json.map("action", "deck.run", "index", index, "label", shortcut.label, "confirmed", true), "Ran " + shortcut.label + " on the PC."); }
        });
    }
}
