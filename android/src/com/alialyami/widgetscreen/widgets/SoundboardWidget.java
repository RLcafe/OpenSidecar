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
import com.alialyami.widgetscreen.core.SoundboardText;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Pc;
import com.alialyami.widgetscreen.views.GlyphView;
import com.alialyami.widgetscreen.views.Stickers;

import java.util.List;
import java.util.Map;

// The PC's Discord soundboard as sampler pads (Sticker Sheet phase 5, docs/design/sticker-sheet.md), the same as the
// Windows SoundboardWidget: sounds play through the Discord app on the PC, in the voice channel you're in, and a tapped
// pad lights in the card's ink for 150 ms (not in Quiet mode). The status sticker says where you are: the voice channel
// (filled, with a speaker), NOT IN VOICE, … while connecting, UNAVAILABLE when the PC can't reach Discord and NOT SET UP
// before Discord is connected on the PC. Discord not running, or no sounds yet, puts the reason in a dashed outline.
public final class SoundboardWidget extends Widget {
    private static final double GAP = 8;
    private LinearLayout grid, pager, emptyState;
    private TextView status, pageLabel, problem;
    private Stickers.Sticker voice;
    private View heading;
    private GlyphView previousPage, nextPage, reload;
    private int page;
    private boolean quiet;
    private String shown, message = "";
    private PcState.Discord reloaded;

    public SoundboardWidget(Dashboard dash) { super("Soundboard", dash); }

    @Override public String part() { return "discord"; }

    @Override protected View create() {
        shown = null;
        FrameLayout root = new FrameLayout(ui.context);
        LinearLayout column = ui.column();
        LinearLayout header = ui.row(); header.setGravity(Gravity.CENTER_VERTICAL);
        heading = strip();
        header.addView(heading, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 4, 0, 12, 0));
        // The voice sticker needs a card of 360 units (CardSizing.soundboardChannelShown, as on the PC).
        voice = sticker(false);
        FrameLayout voiceBox = new FrameLayout(ui.context);
        voiceBox.addView(voice, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        voiceBox.setVisibility(CardSizing.soundboardChannelShown(width) ? View.VISIBLE : View.INVISIBLE);
        header.addView(voiceBox, ui.ll(0, Ui.WRAP, 1));
        pager = ui.row();
        previousPage = ui.iconButton("chevron-left", "Previous sound page", 16, false, new View.OnClickListener() { @Override public void onClick(View v) { page--; shown = null; render(); } });
        pager.addView(previousPage, ui.ll(ui.px(48), ui.px(48)));
        pageLabel = ui.caption("");
        pageLabel.setGravity(Gravity.CENTER);
        pager.addView(pageLabel, ui.ll(ui.px(46), Ui.WRAP));
        nextPage = ui.iconButton("chevron-right", "Next sound page", 16, false, new View.OnClickListener() { @Override public void onClick(View v) { page++; shown = null; render(); } });
        pager.addView(nextPage, ui.ll(ui.px(48), ui.px(48)));
        header.addView(pager, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 6, 0, 0, 0));
        reload = ui.iconButton("refresh", "Reload Discord sounds", 16, false, new View.OnClickListener() { @Override public void onClick(View v) { reload(); } });
        header.addView(reload, ui.margins(ui.ll(ui.px(48), ui.px(48)), 6, 0, 0, 0));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(48)));
        FrameLayout body = new FrameLayout(ui.context);
        grid = ui.column();
        body.addView(grid, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        // Discord not running, or no sounds yet: the reason in a dashed outline in place of the pads.
        problem = Stickers.unavailable(ui, "");
        problem.setVisibility(View.GONE);
        body.addView(problem, ui.frame(Ui.MATCH, Ui.WRAP, Gravity.CENTER));
        column.addView(body, ui.margins(ui.ll(Ui.MATCH, 0, 1), -4, 0, -4, 0));
        status = Ui.wrap(ui.smallText(""), 2);
        column.addView(status, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 4, 6, 4, 0));
        root.addView(column, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));

        // Shown instead of the sounds until Discord is connected on the PC: not set up, so in a dashed outline.
        emptyState = ui.column();
        emptyState.setGravity(Gravity.CENTER_HORIZONTAL);
        android.graphics.drawable.GradientDrawable outline = new android.graphics.drawable.GradientDrawable();
        outline.setCornerRadius(ui.fpx(12));
        outline.setStroke(Math.max(1, ui.px(Stickers.STICKER_BORDER)), ui.theme.muted, ui.fpx(3), ui.fpx(2));
        emptyState.setBackground(outline);
        emptyState.setPadding(ui.px(16), ui.px(12), ui.px(16), ui.px(12));
        TextView title = ui.heading("Connect Discord");
        title.setTypeface(Ui.MEDIUM);
        emptyState.addView(title, ui.ll(Ui.WRAP, Ui.WRAP));
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

    @Override public void tick(int ticks, boolean quiet) { this.quiet = quiet; }

    @Override public void render() {
        if (!built()) return;
        PcState.Discord d = discord();
        boolean connected = d != null && d.connected;
        emptyState.setVisibility(connected || !dash.online() ? View.GONE : View.VISIBLE);
        Ui.enable(reload, connected);
        boolean inVoice = connected && !d.failed && !d.channel.isEmpty();
        voice.setText(!dash.online() ? "PC OFFLINE" : SoundboardText.voice(connected, connected && d.linked, connected && d.failed, connected ? d.channel : ""));
        voice.setFilled(inVoice);
        voice.setGlyph(inVoice ? "volume" : null);
        voice.setContentDescription(inVoice ? "In voice: " + d.channel : null);
        // No sounds, or Discord not running with nothing loaded: the reason in place of the pads.
        String trouble = !connected ? null : d.sounds.isEmpty() ? (d.failed ? d.status : "No soundboard sounds yet. Add some to a server in Discord.") : null;
        problem.setText(trouble == null ? "" : trouble);
        problem.setVisibility(trouble == null ? View.GONE : View.VISIBLE);
        grid.setVisibility(connected && trouble == null ? View.VISIBLE : View.GONE);
        status.setText(!dash.online() ? dash.offlineText() : !connected || trouble != null ? "" : message.isEmpty() ? d.status : message);
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
        // Pads: SMALL shows the name alone; MEDIUM adds the pad number over it; LARGE is a pad with the number and a play
        // mark at the top and the name and where the sound comes from at the bottom (CardSizing.tile, as on the PC).
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
                View cell = index < sounds.size() ? pad(sounds.get(index), index, ready, d.channelGuild, design) : new View(ui.context);
                LinearLayout.LayoutParams params = ui.ll(0, Ui.MATCH, 1);
                params.setMargins(ui.px(GAP / 2), ui.px(GAP / 2), ui.px(GAP / 2), ui.px(GAP / 2));
                line.addView(cell, params);
            }
            grid.addView(line, ui.ll(Ui.MATCH, 0, 1));
        }
    }

    // A pad's text follows its state, so it turns dark when the pad lights.
    private TextView padText(String value, float size, boolean secondary) {
        TextView text = ui.text(value, size, ui.theme.text);
        text.setTextColor(Stickers.padText(ui));
        text.setDuplicateParentStateEnabled(true);
        text.setSingleLine(true); text.setEllipsize(android.text.TextUtils.TruncateAt.END);
        if (secondary) text.setAlpha(0.7f);
        return text;
    }

    private View pad(final PcState.Sound sound, int index, boolean ready, String guild, int design) {
        boolean otherServer = !sound.isDefault && !guild.isEmpty() && !sound.guild.equals(guild);
        boolean large = design == CardSizing.LARGE;
        final LinearLayout pad = ui.column();
        pad.setBackground(Stickers.pad(ui, dash.ink(id)));
        pad.setGravity(large ? Gravity.START : Gravity.CENTER);
        pad.setPadding(ui.px(large ? 14 : 8), ui.px(large ? 10 : 4), ui.px(large ? 12 : 8), ui.px(large ? 13 : 7));
        pad.setMinimumHeight(ui.px(48));
        TextView name = padText(sound.name, large ? ui.body() : ui.small(), false);
        GlyphView mark = null;
        if (design == CardSizing.SMALL) pad.addView(name, ui.ll(Ui.WRAP, Ui.WRAP));
        else if (!large) {
            TextView number = padText(SoundboardText.padNumber(index), 13, true); number.setTypeface(Stickers.BOLD);
            pad.addView(number, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 0, 0, 4));
            pad.addView(name, ui.ll(Ui.WRAP, Ui.WRAP));
        } else {
            LinearLayout top = ui.row(); top.setGravity(Gravity.CENTER_VERTICAL);
            TextView number = padText(SoundboardText.padNumber(index), 13, true); number.setTypeface(Stickers.BOLD);
            top.addView(number, ui.ll(0, Ui.WRAP, 1));
            mark = new GlyphView(ui.context, "play", ui.theme.text, ui.fpx(16)); mark.setAlpha(0.7f);
            top.addView(mark, ui.ll(ui.px(18), ui.px(18)));
            pad.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
            pad.addView(new View(ui.context), ui.ll(Ui.MATCH, 0, 1));
            name.setTypeface(Ui.MEDIUM);
            pad.addView(name, ui.ll(Ui.MATCH, Ui.WRAP));
            pad.addView(padText(SoundboardText.source(sound.isDefault, otherServer), 13, true), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 2, 0, 0));
        }
        final GlyphView play = mark;
        pad.setClickable(true); pad.setFocusable(true);
        pad.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { light(pad, play); play(sound); } }));
        pad.setContentDescription("Play " + sound.name + ", pad " + SoundboardText.padNumber(index) + (sound.isDefault ? ", Discord sound" : otherServer ? ", from another server" : ""));
        Ui.enable(pad, ready);
        return pad;
    }

    // A tapped pad lights for 150 ms, once; in Quiet mode it only shows while pressed.
    private void light(final View pad, final GlyphView mark) {
        if (quiet) return;
        pad.setActivated(true);
        if (mark != null) mark.setColor(Theme.INK_TEXT);
        Async.MAIN.postDelayed(new Runnable() { @Override public void run() { pad.setActivated(false); if (mark != null) mark.setColor(ui.theme.text); } }, SoundboardText.FLASH_MS);
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
