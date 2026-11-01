package com.alialyami.widgetscreen.widgets;

import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.GameText;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.views.GlyphView;

import java.util.List;

// The PC's installed games (docs/design/games.md), in the same six-a-page grid as Quick Launch, recently played first.
// A tap starts the game on the PC through its store. Which stores are listed is set on the PC (Settings → Widgets).
public final class GamesWidget extends Widget {
    private int page;
    private LinearLayout grid, pager;
    private TextView pageLabel, empty, heading;
    private View previousPage, nextPage;
    private String shown;

    public GamesWidget(Dashboard dash) { super("Games", dash); }

    @Override public String part() { return "games"; }

    @Override protected View create() {
        shown = null;
        LinearLayout column = ui.column();
        LinearLayout header = ui.row();
        heading = ui.label("GAMES");
        header.addView(heading, ui.margins(ui.ll(0, Ui.WRAP, 1), 4, 0, 4, 0));
        pager = ui.row();
        previousPage = ui.iconButton("chevron-left", "Previous games page", 16, false, new View.OnClickListener() { @Override public void onClick(View v) { page--; shown = null; render(); } });
        pager.addView(previousPage, ui.ll(ui.px(48), ui.px(48)));
        pageLabel = ui.caption(""); pageLabel.setGravity(Gravity.CENTER);
        pager.addView(pageLabel, ui.ll(ui.px(46), Ui.WRAP));
        nextPage = ui.iconButton("chevron-right", "Next games page", 16, false, new View.OnClickListener() { @Override public void onClick(View v) { page++; shown = null; render(); } });
        pager.addView(nextPage, ui.ll(ui.px(48), ui.px(48)));
        header.addView(pager, ui.ll(Ui.WRAP, Ui.WRAP));
        header.addView(ui.iconButton("refresh", "Look for games on the PC again", 16, false, new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "games.refresh"), "Looking for games on the PC."); } }), ui.margins(ui.ll(ui.px(48), ui.px(48)), 6, 0, 0, 0));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(48)));
        FrameLayout body = new FrameLayout(ui.context);
        grid = ui.column();
        body.addView(grid, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        empty = Ui.wrap(ui.caption(""), 4); empty.setGravity(Gravity.CENTER_VERTICAL);
        body.addView(empty, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        column.addView(body, ui.ll(Ui.MATCH, 0, 1));
        return ui.card(column, 20);
    }

    @Override public void render() {
        if (!built()) return;
        PcState state = dash.state();
        PcState.Games games = state == null || !dash.online() ? null : state.games;
        long now = System.currentTimeMillis();
        String signature = games == null ? "offline" : games.read + "|" + games.failed + "|" + games.items.size() + "|" + (games.items.isEmpty() ? "" : games.items.get(0).id) + "|" + page + "|" + (now / 3600000);
        if (signature.equals(shown)) return;
        shown = signature;
        grid.removeAllViews();
        List<PcState.Game> items = games == null ? null : games.items;
        if (items == null || items.isEmpty()) {
            pager.setVisibility(View.GONE);
            empty.setVisibility(View.VISIBLE);
            empty.setText(games == null ? dash.offlineText() : games.failed ? "The PC couldn't read its game library." : !games.read ? "The PC is looking for games…" : "No games found on the PC from Steam, Epic or the Xbox app.");
            return;
        }
        empty.setVisibility(View.GONE);
        double areaWidth = Math.max(0, width - 42), areaHeight = Math.max(0, height - 90);
        int[] shape = CardSizing.deckGrid(areaWidth, areaHeight, items.size());
        int columns = shape[0], rows = shape[1];
        double cellWidth = areaWidth / columns, cellHeight = areaHeight / rows;
        int labelMode = CardSizing.deckLabel(cellWidth, cellHeight);
        int pageSize = columns * rows, pages = Math.max(1, (items.size() + pageSize - 1) / pageSize);
        page = Math.max(0, Math.min(pages - 1, page));
        pager.setVisibility(pages > 1 ? View.VISIBLE : View.GONE);
        heading.setVisibility(CardSizing.headingShown(width, pages > 1) ? View.VISIBLE : View.INVISIBLE);
        Ui.enable(previousPage, page > 0); Ui.enable(nextPage, page < pages - 1);
        pageLabel.setText((page + 1) + "/" + pages);
        for (int r = 0; r < rows; r++) {
            LinearLayout row = ui.row();
            for (int c = 0; c < columns; c++) {
                int index = page * pageSize + r * columns + c;
                View cell = index < items.size() ? button(items.get(index), labelMode, cellWidth, cellHeight, now) : new View(ui.context);
                LinearLayout.LayoutParams params = ui.ll(0, Ui.MATCH, 1);
                params.setMargins(ui.px(4), ui.px(4), ui.px(4), ui.px(4));
                row.addView(cell, params);
            }
            grid.addView(row, ui.ll(Ui.MATCH, 0, 1));
        }
    }

    private View button(final PcState.Game game, int labelMode, double cellWidth, double cellHeight, long now) {
        boolean beside = labelMode == CardSizing.DECK_LABEL_BESIDE;
        double glyph = CardSizing.deckGlyph(cellHeight, beside);
        int color = GameText.storeColor(game.store);
        LinearLayout button = beside ? ui.row() : ui.column();
        button.setGravity(Gravity.CENTER);
        button.setPadding(ui.px(8), ui.px(8), ui.px(8), ui.px(8));
        button.setBackground(ui.pressable(ui.theme.raised, ui.theme.controlBorder, 1, 12));
        button.setMinimumHeight(ui.px(48));
        if (CardSizing.tile(cellWidth, cellHeight) == CardSizing.LARGE) {
            button.setOrientation(LinearLayout.VERTICAL); button.setGravity(Gravity.START | Gravity.CENTER_VERTICAL);
            button.setPadding(ui.px(16), ui.px(12), ui.px(12), ui.px(12));
            double size = Math.min(40, glyph);
            button.addView(new GlyphView(ui.context, "game", color, ui.fpx(size)), ui.ll(ui.px(size + 4), ui.px(size + 4)));
            TextView title = ui.text(game.name, 17 * ui.textScale, ui.theme.text); title.setTypeface(Ui.MEDIUM); title.setMaxWidth(ui.px(cellWidth - 44));
            button.addView(title, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 12, 0, 0));
            TextView detail = ui.caption(GameText.detail(game, now)); detail.setMaxWidth(ui.px(cellWidth - 44));
            button.addView(detail, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 3, 0, 0));
        } else {
            button.addView(new GlyphView(ui.context, "game", color, ui.fpx(glyph)), ui.ll(ui.px(glyph + 4), ui.px(glyph + 4)));
            if (labelMode != CardSizing.DECK_LABEL_NONE) {
                TextView name = ui.text(game.name, (beside ? 14 : 15) * ui.textScale, ui.theme.text);
                name.setGravity(Gravity.CENTER); name.setMaxWidth(ui.px(Math.max(40, beside ? cellWidth - glyph - 40 : cellWidth - 24)));
                button.addView(name, beside ? ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 8, 0, 0, 0) : ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 8, 0, 0));
            }
        }
        button.setContentDescription("Play " + game.name + " on the PC");
        button.setClickable(true);
        button.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "games.launch", "id", game.id)); } }));
        return button;
    }
}
