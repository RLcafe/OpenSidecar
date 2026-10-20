package com.alialyami.widgetscreen.widgets;

import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.Deck;
import com.alialyami.widgetscreen.views.GlyphView;

import java.util.List;

// The paged Quick Launch deck: six buttons per page, or three in a short card.
public final class QuickLaunchWidget extends Widget {
    private int page;
    private LinearLayout grid, pager;
    private TextView pageLabel, empty;
    private GlyphView previousPage, nextPage;

    public QuickLaunchWidget(Dashboard dash) { super("Shortcuts", dash); }

    private boolean compact() { return height < 240; }

    @Override protected View create() {
        LinearLayout column = ui.column();
        LinearLayout header = ui.row();
        TextView title = ui.label("QUICK LAUNCH");
        header.addView(title, ui.margins(ui.ll(0, Ui.WRAP, 1), 4, 0, 4, 0));
        pager = ui.row();
        previousPage = ui.iconButton("chevron-left", "Previous button page", 16, false, new View.OnClickListener() { @Override public void onClick(View v) { page--; render(); } });
        pager.addView(previousPage, ui.ll(ui.px(48), ui.px(48)));
        pageLabel = ui.caption("");
        pageLabel.setGravity(Gravity.CENTER);
        pager.addView(pageLabel, ui.ll(ui.px(46), Ui.WRAP));
        nextPage = ui.iconButton("chevron-right", "Next button page", 16, false, new View.OnClickListener() { @Override public void onClick(View v) { page++; render(); } });
        pager.addView(nextPage, ui.ll(ui.px(48), ui.px(48)));
        header.addView(pager, ui.ll(Ui.WRAP, Ui.WRAP));
        TextView edit = ui.button("Edit", new View.OnClickListener() { @Override public void onClick(View v) { dash.openSettings("Quick Launch"); } });
        edit.setTextSize(TypedValue.COMPLEX_UNIT_PX, ui.fpx(14));
        edit.setPadding(ui.px(10), ui.px(4), ui.px(10), ui.px(4));
        edit.setContentDescription("Customize Quick Launch");
        header.addView(edit, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 6, 0, 0, 0));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(48)));

        FrameLayout body = new FrameLayout(ui.context);
        grid = ui.column();
        body.addView(grid, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        empty = ui.button("Add a button", new View.OnClickListener() { @Override public void onClick(View v) { dash.openSettings("Quick Launch"); } });
        empty.setTextSize(TypedValue.COMPLEX_UNIT_PX, ui.fpx(16));
        FrameLayout.LayoutParams emptyParams = new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH);
        emptyParams.setMargins(ui.px(4), ui.px(4), ui.px(4), ui.px(4));
        body.addView(empty, emptyParams);
        column.addView(body, ui.ll(Ui.MATCH, 0, 1));
        return ui.card(column, 20);
    }

    @Override public void render() {
        if (!built()) return;
        List<Deck.Shortcut> shortcuts = dash.settings.shortcuts;
        boolean compact = compact();
        int pageSize = compact ? 3 : 6, pages = Deck.pageCount(shortcuts.size(), pageSize);
        page = Math.max(0, Math.min(pages - 1, page));
        pager.setVisibility(pages > 1 ? View.VISIBLE : View.GONE);
        Ui.enable(previousPage, page > 0); Ui.enable(nextPage, page < pages - 1);
        pageLabel.setText((page + 1) + "/" + pages);
        empty.setVisibility(shortcuts.isEmpty() ? View.VISIBLE : View.GONE);
        grid.removeAllViews();
        int rows = compact ? 1 : 2;
        for (int r = 0; r < rows; r++) {
            LinearLayout row = ui.row();
            row.setGravity(Gravity.FILL);
            for (int c = 0; c < 3; c++) {
                int index = page * pageSize + r * 3 + c;
                View cell = index < shortcuts.size() ? button(shortcuts.get(index), compact) : new View(ui.context);
                LinearLayout.LayoutParams params = ui.ll(0, Ui.MATCH, 1);
                params.setMargins(ui.px(4), ui.px(4), ui.px(4), ui.px(4));
                row.addView(cell, params);
            }
            grid.addView(row, ui.ll(Ui.MATCH, 0, 1));
        }
    }

    private View button(final Deck.Shortcut shortcut, boolean compact) {
        LinearLayout button = compact ? ui.row() : ui.column();
        button.setGravity(Gravity.CENTER);
        button.setPadding(ui.px(8), ui.px(8), ui.px(8), ui.px(8));
        button.setBackground(ui.pressable(ui.theme.raised, ui.theme.controlBorder, 1, 12));
        button.setMinimumHeight(ui.px(48));
        GlyphView glyph = new GlyphView(ui.context, shortcut.glyph, ui.theme.accent, ui.fpx(compact ? 18 : 24));
        button.addView(glyph, ui.ll(ui.px(compact ? 22 : 28), ui.px(compact ? 22 : 28)));
        TextView name = ui.text(shortcut.label, (compact ? 14 : 15) * ui.textScale, ui.theme.text);
        name.setGravity(Gravity.CENTER);
        name.setMaxWidth(ui.px(compact ? 70 : 138));
        button.addView(name, compact ? ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 8, 0, 0, 0) : ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 8, 0, 0));
        button.setContentDescription(shortcut.label);
        button.setClickable(true);
        Ui.enable(button, !shortcut.target.trim().isEmpty());
        button.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { dash.runShortcut(shortcut); } });
        return button;
    }
}
