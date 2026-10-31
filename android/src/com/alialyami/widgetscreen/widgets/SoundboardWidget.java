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
import com.alialyami.widgetscreen.core.PcLink;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Pc;
import com.alialyami.widgetscreen.views.GlyphView;

import java.util.List;
import java.util.Map;

// The PC's Discord soundboard: sounds play through the Discord app on the PC, in the voice channel you're in.
public final class SoundboardWidget extends Widget {
    private static final double GAP = 8;
    private LinearLayout grid, pager, emptyState;
    private TextView channel, status, pageLabel, heading;
    private GlyphView previousPage, nextPage, reload;
    private int page;
    private String shown, message = "";
    private PcState.Discord reloaded;

    public SoundboardWidget(Dashboard dash) { super("Soundboard", dash); }

    @Override public String part() { return "discord"; }

    @Override protected View create() {
        shown = null;
        FrameLayout root = new FrameLayout(ui.context);
        LinearLayout column = ui.column();
        LinearLayout header = ui.row();
        heading = ui.label("SOUNDBOARD");
        header.addView(heading, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 4, 0, 12, 0));
        channel = ui.smallText("");
        channel.setSingleLine(true); channel.setEllipsize(android.text.TextUtils.TruncateAt.END);
        // The channel name needs a card of 360 units (CardSizing.soundboardChannelShown, as on the PC).
        channel.setVisibility(CardSizing.soundboardChannelShown(width) ? View.VISIBLE : View.INVISIBLE);
        header.addView(channel, ui.ll(0, Ui.WRAP, 1));
        pager = ui.row();
        previousPage = ui.iconButton("chevron-left", "Previous sound page", 16, false, new View.OnClickListener() { @Override public void onClick(View v) { page--; shown = null; render(); } });
        pager.addView(previousPage, ui.ll(ui.px(48), ui.px(48)));
        pageLabel = ui.caption("");
        pageLabel.setGravity(Gravity.CENTER);
        pager.addView(pageLabel, ui.ll(ui.px(46), Ui.WRAP));
        nextPage = ui.iconButton("chevron-right", "Next sound page", 16, false, new View.OnClickListener() { @Override public void onClick(View v) { page++; shown = null; render(); } });
        pager.addView(nextPage, ui.ll(ui.px(48), ui.px(48)));
        header.addView(pager, ui.ll(Ui.WRAP, Ui.WRAP));
        reload = ui.iconButton("refresh", "Reload Discord sounds", 16, false, new View.OnClickListener() { @Override public void onClick(View v) { reload(); } });
        header.addView(reload, ui.margins(ui.ll(ui.px(48), ui.px(48)), 6, 0, 0, 0));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(48)));
        grid = ui.column();
        column.addView(grid, ui.margins(ui.ll(Ui.MATCH, 0, 1), -4, 0, -4, 0));
        status = Ui.wrap(ui.smallText(""), 2);
        column.addView(status, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 4, 6, 4, 0));
        root.addView(column, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));

        // Shown instead of the sounds until Discord is connected on the PC.
        emptyState = ui.column();
        emptyState.setGravity(Gravity.CENTER_HORIZONTAL);
        TextView heading = ui.heading("Connect Discord");
        heading.setTypeface(Ui.MEDIUM);
        emptyState.addView(heading, ui.ll(Ui.WRAP, Ui.WRAP));
        TextView blurb = Ui.wrap(ui.smallText("Set up Discord on the PC (Settings → Discord) to play soundboard sounds in the voice channel you're in."), 3);
        blurb.setGravity(Gravity.CENTER);
        // The sentence goes when the card is too short for it (CardSizing.emptyDetail, as on the PC).
        if (CardSizing.emptyDetail(height - 90) >= 2) emptyState.addView(blurb, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
        FrameLayout.LayoutParams emptyParams = new FrameLayout.LayoutParams(ui.px(Math.min(380, Math.max(200, width - 40))), Ui.WRAP, Gravity.CENTER);
        root.addView(emptyState, emptyParams);
        return ui.card(root, 20);
    }

    private PcState.Discord discord() {
        PcState state = dash.state();
        PcState.Discord fresh = state == null || !dash.online() ? null : state.discord;
        return reloaded != null && fresh == null ? reloaded : fresh;
    }

    @Override public void render() {
        if (!built()) return;
        PcState.Discord d = discord();
        boolean connected = d != null && d.connected;
        emptyState.setVisibility(connected || !dash.online() ? View.GONE : View.VISIBLE);
        grid.setVisibility(connected ? View.VISIBLE : View.GONE);
        Ui.enable(reload, connected);
        channel.setText(connected ? d.channel : "");
        status.setText(!dash.online() ? dash.offlineText() : !connected ? "" : message.isEmpty() ? d.status : message);
        if (!connected) { pager.setVisibility(View.GONE); return; }
        // Six sounds a page that grow with the card, like the Windows widget (CardSizing.soundGrid): the card less its
        // padding, header and status line.
        int[] shape = CardSizing.soundGrid(width - 40 + GAP, height - 40 - 48 - 36, d.sounds.size());
        int columns = shape[0], rows = shape[1];
        int size = columns * rows, pages = Math.max(1, (d.sounds.size() + size - 1) / size);
        page = Math.max(0, Math.min(pages - 1, page));
        pager.setVisibility(pages > 1 ? View.VISIBLE : View.GONE);
        heading.setVisibility(CardSizing.headingShown(width, pages > 1) ? View.VISIBLE : View.GONE);
        Ui.enable(previousPage, page > 0); Ui.enable(nextPage, page < pages - 1);
        pageLabel.setText((page + 1) + "/" + pages);
        boolean ready = d.linked && !d.channel.isEmpty();
        // SMALL cells show the name alone; MEDIUM adds a play mark over it; LARGE is a tile with the mark, the name and
        // where the sound comes from (CardSizing.tile, as on the PC).
        int design = CardSizing.tile((width - 40 + GAP) / columns - GAP, (height - 40 - 48 - 36) / rows - GAP);
        StringBuilder signature = new StringBuilder().append(page).append('|').append(ready).append('|').append(columns).append('x').append(rows);
        for (PcState.Sound sound : d.sounds) signature.append('|').append(sound.id);
        if (signature.toString().equals(shown)) return;
        shown = signature.toString();
        grid.removeAllViews();
        List<PcState.Sound> sounds = d.sounds;
        for (int r = 0; r < rows; r++) {
            LinearLayout line = ui.row();
            for (int c = 0; c < columns; c++) {
                int index = page * size + r * columns + c;
                View cell = index < sounds.size() ? button(sounds.get(index), ready, d.channelGuild, design) : new View(ui.context);
                LinearLayout.LayoutParams params = ui.ll(0, Ui.MATCH, 1);
                params.setMargins(ui.px(GAP / 2), ui.px(GAP / 2), ui.px(GAP / 2), ui.px(GAP / 2));
                line.addView(cell, params);
            }
            grid.addView(line, ui.ll(Ui.MATCH, 0, 1));
        }
    }

    private View button(final PcState.Sound sound, boolean ready, String guild, int design) {
        boolean otherServer = !sound.isDefault && !guild.isEmpty() && !sound.guild.equals(guild);
        if (design != CardSizing.SMALL) {
            boolean large = design == CardSizing.LARGE;
            LinearLayout tile = ui.column();
            tile.setGravity(large ? Gravity.START | Gravity.CENTER_VERTICAL : Gravity.CENTER);
            tile.setBackground(ui.pressable(ui.theme.raised, ui.theme.controlBorder, 1, 12));
            tile.setPadding(ui.px(large ? 16 : 8), ui.px(large ? 10 : 4), ui.px(large ? 12 : 8), ui.px(large ? 10 : 4));
            tile.setMinimumHeight(ui.px(48));
            tile.addView(new GlyphView(ui.context, "play", ui.theme.accent, ui.fpx(large ? 26 : 16)), ui.margins(ui.ll(ui.px(large ? 30 : 20), ui.px(large ? 30 : 20)), 0, 0, 0, large ? 10 : 6));
            TextView name = ui.text(sound.name, large ? ui.body() : ui.small(), ui.theme.text);
            if (large) name.setTypeface(Ui.MEDIUM); else name.setGravity(Gravity.CENTER);
            tile.addView(name, ui.ll(Ui.WRAP, Ui.WRAP));
            if (large) tile.addView(ui.caption(sound.isDefault ? "Discord sound" : otherServer ? "From another server" : "This server"), ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 3, 0, 0));
            tile.setClickable(true); tile.setFocusable(true);
            tile.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { play(sound); } }));
            tile.setContentDescription("Play " + sound.name + (sound.isDefault ? ", Discord sound" : otherServer ? ", from another server" : ""));
            Ui.enable(tile, ready);
            return tile;
        }
        TextView button = ui.button(sound.name, new View.OnClickListener() { @Override public void onClick(View v) { play(sound); } });
        button.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(ui.small()));
        button.setPadding(ui.px(8), ui.px(4), ui.px(8), ui.px(4));
        button.setContentDescription("Play " + sound.name + (sound.isDefault ? ", Discord sound" : otherServer ? ", from another server" : ""));
        Ui.enable(button, ready);
        return button;
    }

    private void play(final PcState.Sound sound) {
        dash.pc.command(Json.map("action", "discord.play", "id", sound.id), new Async.Done<String>() {
            @Override public void ok(String text) { setMessage(text.isEmpty() ? "Played " + sound.name + "." : text); }
        }, new Async.Failed() { @Override public void error(Exception error) { setMessage(Pc.message(error)); } });
    }

    private void reload() {
        dash.pc.send(new Pc.Call<Map<String, Object>>() {
            @Override public Map<String, Object> run(PcLink link) throws Exception { return link.commandResult(Json.map("action", "discord.reload")); }
        }, new Async.Done<Map<String, Object>>() {
            @Override public void ok(Map<String, Object> result) { reloaded = PcState.parseDiscord(result); shown = null; message = ""; render(); dash.refreshSoon(); }
        }, new Async.Failed() { @Override public void error(Exception error) { setMessage(Pc.message(error)); } });
    }

    // Messages after a tap clear on their own after a few seconds.
    private void setMessage(final String text) {
        message = text;
        render();
        Async.MAIN.postDelayed(new Runnable() { @Override public void run() { if (text.equals(message)) { message = ""; render(); } } }, 6000);
    }

    @Override public void stateChanged() { reloaded = null; render(); }
}
