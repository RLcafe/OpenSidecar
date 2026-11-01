package com.alialyami.widgetscreen.widgets;

import android.os.SystemClock;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcLink;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Pc;
import com.alialyami.widgetscreen.views.GlyphView;
import com.alialyami.widgetscreen.views.Stickers;
import com.alialyami.widgetscreen.views.TouchSlider;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

// The PC's sound mixer: master volume and output, every app's volume, mute and output, and the microphone.
// Output choices open in a panel inside the card, as on Windows.
public final class MixerWidget extends Widget {
    private final class Row {
        TextView label, percent;
        TouchSlider slider;
        GlyphView mute, output;
        PcState.Session session;
    }

    private TextView status, device, percent, sessionCount, noSessions, microphoneLabel, appsButton;
    private TouchSlider master;
    private GlyphView mute, microphoneGlyph, microphoneCompact;
    private LinearLayout sessionList, microphone, outputButton, masterRow, sliderRow;
    private int appColumns = 1;
    private FrameLayout picker, root, listHost;
    // FULL, COMPACT or MASTER (CardSizing.mixer, as on the PC); a MASTER card shows the app list in place of the
    // master volume while appsOpen is set.
    private int mode;
    private boolean appsOpen;
    private final Map<String, Row> rows = new HashMap<String, Row>();
    private String rowSignature;
    // Slider moves are sent at most every 150 ms, then once more when the finger lifts.
    private final Map<String, Float> pendingVolume = new HashMap<String, Float>();
    private long lastSent;
    private boolean sendQueued;
    // Sticker Sheet phase 2 (docs/design/sticker-sheet.md): channel strips on LARGE cards (master first, one 88-unit
    // strip per app that fits; Apps opens the list) and the ON AIR badge. The phone link doesn't carry output levels,
    // so the phone has no LED meter.
    private final class Strip { TextView badge, name, percent, device; TouchSlider slider; GlyphView mute, output; PcState.Session session; View root; }
    private final List<Strip> strips = new ArrayList<Strip>();
    private LinearLayout stripsHost;
    private TouchSlider masterStrip;
    private TextView masterStripPercent, masterStripDevice;
    private GlyphView masterStripMute;
    private View appsHeaderView;
    private Stickers.Sticker micState;

    public MixerWidget(Dashboard dash) { super("Mixer", dash); }

    @Override public String part() { return "audio"; }

    @Override protected View create() {
        rows.clear(); rowSignature = null; picker = null;
        mode = CardSizing.mixer(height);
        appColumns = CardSizing.mixerColumns(width, height);
        // A MASTER card opens the list over the master volume; a LARGE one switches between strips and the list.
        if (mode == CardSizing.MIXER_COMPACT) appsOpen = false;
        strips.clear(); masterStrip = null;
        boolean full = mode == CardSizing.MIXER_FULL, masterOnly = mode == CardSizing.MIXER_MASTER;
        root = new FrameLayout(ui.context);
        LinearLayout column = ui.column();
        LinearLayout header = ui.row();
        header.setGravity(Gravity.CENTER_VERTICAL);
        header.addView(strip(), ui.ll(Ui.WRAP, Ui.WRAP));
        status = sticker(true);
        
        status.setGravity(Gravity.END); status.setSingleLine(true); status.setEllipsize(android.text.TextUtils.TruncateAt.END);
        header.addView(status, ui.margins(ui.ll(0, Ui.WRAP, 1), 8, 0, 0, 0));
        micState = sticker(false);
        header.addView(micState, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 8, 0, 0, 0));
        // On a MASTER card the app list is one tap away: this swaps the master rows for the list and back.
        appsButton = ui.button("Apps", new View.OnClickListener() { @Override public void onClick(View v) { appsOpen = !appsOpen; closePicker(); showApps(); } });
        appsButton.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, ui.fpx(14));
        appsButton.setPadding(ui.px(12), ui.px(4), ui.px(12), ui.px(4));
        if (masterOnly || full) header.addView(appsButton, ui.margins(ui.ll(Ui.WRAP, ui.px(48)), 8, 0, 0, 0));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(masterOnly || full ? 48 : 34)));
        stripsHost = ui.row();
        stripsHost.setGravity(Gravity.TOP | Gravity.START);
        if (full) { buildStrips(); column.addView(stripsHost, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 8, 0, 10)); }

        masterRow = ui.row();
        outputButton = ui.column();
        outputButton.setBackground(ui.pressable(0x00000000, 0, 0, 12));
        outputButton.setPadding(ui.px(8), 0, ui.px(8), 0);
        outputButton.setGravity(Gravity.CENTER_VERTICAL);
        outputButton.addView(ui.heading("Master volume"), ui.ll(Ui.WRAP, Ui.WRAP));
        LinearLayout deviceRow = ui.row();
        device = ui.smallText("Connecting to the PC…");
        device.setMaxWidth(ui.px(300));
        deviceRow.addView(device, ui.ll(Ui.WRAP, Ui.WRAP));
        deviceRow.addView(new GlyphView(ui.context, "chevron-down", ui.theme.muted, ui.fpx(14)), ui.margins(ui.ll(ui.px(16), ui.px(16)), 8, 2, 0, 0));
        outputButton.addView(deviceRow, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 5, 0, 0));
        outputButton.setClickable(true);
        outputButton.setContentDescription("Choose output device");
        outputButton.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { openMasterOutputs(); } });
        masterRow.addView(outputButton, ui.margins(ui.ll(0, Ui.MATCH, 1), -8, 0, 8, 0));
        percent = ui.text("—", 36, ui.theme.text);
        percent.setTypeface(Ui.LIGHT); percent.setGravity(Gravity.END);
        masterRow.addView(percent, ui.ll(ui.px(80), Ui.WRAP));
        column.addView(masterRow, ui.ll(Ui.MATCH, ui.px(66)));

        sliderRow = ui.row();
        mute = ui.iconButton("volume", "Mute output", 22, false, new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "audio.mute")); } });
        sliderRow.addView(mute, ui.ll(ui.px(58), ui.px(52)));
        master = slider(null);
        master.setContentDescription("Master volume");
        sliderRow.addView(master, ui.margins(ui.ll(0, ui.px(52), 1), 8, 0, 0, 0));
        // Smaller cards move the microphone beside the master slider.
        microphoneCompact = ui.iconButton("microphone", "Microphone", 20, false, new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "audio.microphone")); } });
        if (!full) sliderRow.addView(microphoneCompact, ui.margins(ui.ll(ui.px(52), ui.px(52)), 4, 0, 0, 0));
        column.addView(sliderRow, ui.ll(Ui.MATCH, ui.px(52)));

        FrameLayout appsHeader = new FrameLayout(ui.context);
        appsHeader.addView(ui.label("APPS"), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        sessionCount = ui.smallText("");
        appsHeader.addView(sessionCount, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        appsHeaderView = appsHeader;
        if (full) column.addView(appsHeader, ui.margins(ui.ll(Ui.MATCH, ui.px(24)), 0, 18, 0, 0));
        listHost = new FrameLayout(ui.context);
        ScrollView scroll = new ScrollView(ui.context);
        scroll.setVerticalScrollBarEnabled(false);
        scroll.setNestedScrollingEnabled(true);
        sessionList = ui.column();
        scroll.addView(sessionList);
        listHost.addView(scroll, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        noSessions = Ui.wrap(ui.smallText(""), 3);
        noSessions.setGravity(Gravity.CENTER);
        listHost.addView(noSessions, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        column.addView(listHost, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, full ? 0 : 8, 0, 0));

        microphone = ui.row();
        microphone.setBackground(ui.pressable(ui.theme.raised, ui.theme.controlBorder, 1, 12));
        microphone.setPadding(ui.px(16), ui.px(8), ui.px(16), ui.px(8));
        microphone.setMinimumHeight(ui.px(48));
        microphoneGlyph = new GlyphView(ui.context, "microphone", ui.theme.text, ui.fpx(18));
        microphone.addView(microphoneGlyph, ui.margins(ui.ll(ui.px(22), ui.px(22)), 0, 0, 12, 0));
        microphoneLabel = ui.text("Checking microphone…", 16, ui.theme.text);
        microphone.addView(microphoneLabel, ui.ll(Ui.WRAP, Ui.WRAP));
        microphone.setClickable(true);
        microphone.setOnClickListener(new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "audio.microphone")); } });
        if (full) column.addView(microphone, ui.margins(ui.ll(Ui.MATCH, ui.px(48)), 0, 8, 0, 0));
        root.addView(column, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        showApps();
        return ui.card(root, 24);
    }

    private void showApps() {
        boolean masterOnly = mode == CardSizing.MIXER_MASTER, list = !masterOnly || appsOpen;
        boolean stripsShown = mode == CardSizing.MIXER_FULL && !appsOpen;
        masterRow.setVisibility((masterOnly && appsOpen) || stripsShown ? View.GONE : View.VISIBLE);
        sliderRow.setVisibility((masterOnly && appsOpen) || stripsShown ? View.GONE : View.VISIBLE);
        listHost.setVisibility(list && !stripsShown ? View.VISIBLE : View.GONE);
        appsHeaderView.setVisibility(stripsShown ? View.GONE : View.VISIBLE);
        stripsHost.setVisibility(stripsShown ? View.VISIBLE : View.GONE);
        String count = sessionCount.getText().toString();
        appsButton.setText(appsOpen ? "Done" : count.isEmpty() ? "Apps" : "Apps · " + count);
        appsButton.setContentDescription(appsOpen ? "Back to master volume" : "Show app volumes");
    }

    private PcState.Audio audio() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.audio; }

    private TouchSlider slider(final String sessionId) {
        TouchSlider slider = new TouchSlider(ui.context, ui).range(0, 100, 0);
        slider.setListener(new TouchSlider.Listener() {
            @Override public void changed(TouchSlider s, float value, boolean done) {
                pendingVolume.put(sessionId == null ? "" : sessionId, value);
                if (sessionId == null) { percent.setText(Math.round(value) + "%"); if (masterStripPercent != null) masterStripPercent.setText(Math.round(value) + "%"); }
                else { Row row = rows.get(sessionId); if (row != null) row.percent.setText(Math.round(value) + "%"); }
                flush(done);
            }
        });
        return slider;
    }

    private void flush(boolean now) {
        long wait = 150 - (SystemClock.elapsedRealtime() - lastSent);
        if (!now && wait > 0) {
            if (!sendQueued) { sendQueued = true; Async.MAIN.postDelayed(new Runnable() { @Override public void run() { sendQueued = false; flush(true); } }, wait); }
            return;
        }
        lastSent = SystemClock.elapsedRealtime();
        for (Map.Entry<String, Float> entry : new ArrayList<Map.Entry<String, Float>>(pendingVolume.entrySet())) {
            double value = entry.getValue() / 100.0;
            if (entry.getKey().isEmpty()) dash.commandQuietly(Json.map("action", "audio.volume", "value", value));
            else dash.commandQuietly(Json.map("action", "audio.session.volume", "id", entry.getKey(), "value", value));
        }
        pendingVolume.clear();
    }

    @Override public void render() {
        if (!built()) return;
        PcState.Audio a = audio();
        boolean available = a != null && a.available;
        Ui.enable(master, available); Ui.enable(mute, available); Ui.enable(outputButton, available && !a.devices.isEmpty());
        if (!dash.online()) { status.setText("PC OFFLINE"); device.setText("The PC isn't answering"); percent.setText("—"); }
        else if (!available) { status.setText("UNAVAILABLE"); device.setText("No audio output found on the PC"); percent.setText("—"); }
        else {
            status.setText("");
            device.setText(a.device);
            if (!master.dragging()) { master.setValue((float) (a.volume * 100)); percent.setText(Math.round(a.volume * 100) + "%"); }
            mute.setGlyph(a.muted ? "mute" : "volume");
            mute.setContentDescription(a.muted ? "Unmute output" : "Mute output");
        }
        // The microphone states what is true now; muted takes the accent so it stands out.
        Boolean mic = available ? a.microphoneMuted : null;
        Ui.enable(microphone, mic != null);
        microphoneLabel.setText(!dash.online() ? "Microphone unavailable" : mic == null ? "No microphone" : mic ? "Microphone muted" : "Microphone on");
        int micColor = Boolean.TRUE.equals(mic) ? ui.theme.accent : ui.theme.text;
        microphoneLabel.setTextColor(micColor); microphoneGlyph.setColor(micColor);
        microphone.setContentDescription(microphoneLabel.getText());
        Ui.enable(microphoneCompact, mic != null);
        microphoneCompact.setColor(micColor); microphoneCompact.setContentDescription(microphoneLabel.getText());
        // The heading's microphone badge: ON AIR (filled) while it's live, MUTED while muted.
        micState.setText(mic == null ? "" : mic ? "MUTED" : "ON AIR");
        micState.setFilled(Boolean.FALSE.equals(mic)); micState.setGlyph(mic == null ? null : "microphone");
        renderSessions(available ? a : null);
        renderStrips(available ? a : null);
        if (picker != null && !available) closePicker();
    }

    private void renderSessions(PcState.Audio a) {
        List<PcState.Session> sessions = a == null ? new ArrayList<PcState.Session>() : a.sessions;
        StringBuilder signature = new StringBuilder();
        for (PcState.Session s : sessions) signature.append(s.id).append('\u0001');
        if (!signature.toString().equals(rowSignature)) {
            rowSignature = signature.toString();
            sessionList.removeAllViews(); rows.clear();
            for (PcState.Session s : sessions) { Row row = createRow(s); rows.put(s.id, row); }
        }
        for (PcState.Session s : sessions) {
            Row row = rows.get(s.id);
            row.session = s;
            PcState.Device output = a.find(s.deviceId);
            boolean elsewhere = output != null && !output.isDefault;
            row.label.setText(elsewhere ? s.name + "  ·  " + output.name : s.name);
            if (!row.slider.dragging()) { row.slider.setValue((float) (s.volume * 100)); row.percent.setText(Math.round(s.volume * 100) + "%"); }
            row.mute.setGlyph(s.muted ? "mute" : "volume");
            row.mute.setContentDescription((s.muted ? "Unmute " : "Mute ") + s.name);
            row.output.setContentDescription("Output for " + s.name + ": " + (output == null ? "Unknown output" : output.name));
        }
        sessionCount.setText(sessions.isEmpty() ? "" : Integer.toString(sessions.size()));
        showApps();
        noSessions.setVisibility(sessions.isEmpty() ? View.VISIBLE : View.GONE);
        noSessions.setText(a == null ? "" : !a.sessionsAvailable ? "App volumes aren't available right now. Master volume still works." : "Apps show up here when they play sound on the PC.");
    }

    // ----- Channel strips (LARGE) -----

    private LinearLayout stripBox() {
        LinearLayout box = ui.column();
        box.setGravity(Gravity.CENTER_HORIZONTAL);
        box.setBackground(ui.shape(ui.theme.raised, 0, 0, 14));
        box.setPadding(ui.px(6), ui.px(10), ui.px(6), ui.px(6));
        return box;
    }
    private TextView stripText(String value, double size, int colour) {
        TextView text = ui.text(value, (float) size, colour);
        text.setGravity(Gravity.CENTER);
        return text;
    }

    private void buildStrips() {
        LinearLayout master = stripBox();
        TextView label = stripText("MASTER", 13, ui.theme.muted); label.setTypeface(Ui.MEDIUM); label.setLetterSpacing(0.08f);
        master.addView(label, ui.ll(Ui.MATCH, Ui.WRAP));
        masterStripPercent = Ui.tabular(stripText("—", 30 * Math.min(1.2, ui.textScale), ui.theme.text)); masterStripPercent.setTypeface(Ui.LIGHT);
        master.addView(masterStripPercent, ui.ll(Ui.MATCH, Ui.WRAP));
        masterStrip = slider(null).vertical();
        masterStrip.setContentDescription("Master volume");
        master.addView(masterStrip, ui.margins(ui.ll(ui.px(48), 0, 1), 0, 6, 0, 6));
        masterStripMute = ui.iconButton("volume", "Mute output", 18, false, new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "audio.mute")); } });
        master.addView(masterStripMute, ui.ll(Ui.MATCH, ui.px(48)));
        masterStripDevice = stripText("", 13, ui.theme.muted);
        masterStripDevice.setMinHeight(ui.px(48));
        masterStripDevice.setBackground(ui.pressable(0x00000000, 0, 0, 10));
        masterStripDevice.setClickable(true); masterStripDevice.setContentDescription("Choose output device");
        masterStripDevice.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { openMasterOutputs(); } }));
        master.addView(masterStripDevice, ui.ll(Ui.MATCH, ui.px(48)));
        stripsHost.addView(master, ui.ll(ui.px(CardSizing.MASTER_STRIP_WIDTH), Ui.MATCH));
        int slots = CardSizing.mixerStripSlots(width);
        for (int i = 0; i < slots; i++) {
            final Strip strip = new Strip();
            LinearLayout box = stripBox();
            strip.badge = stripText("", 14, Theme.INK_TEXT); strip.badge.setTypeface(Stickers.BLACK);
            strip.badge.setBackground(ui.shape(dash.ink(id), 0, 0, 15));
            box.addView(strip.badge, ui.ll(ui.px(30), ui.px(30)));
            strip.name = stripText("", 13 * ui.textScale, ui.theme.text); strip.name.setTypeface(Ui.MEDIUM);
            box.addView(strip.name, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
            strip.percent = Ui.tabular(stripText("", 22 * Math.min(1.2, ui.textScale), ui.theme.text)); strip.percent.setTypeface(Ui.LIGHT);
            box.addView(strip.percent, ui.ll(Ui.MATCH, Ui.WRAP));
            strip.slider = new TouchSlider(ui.context, ui).range(0, 100, 0).vertical();
            strip.slider.setListener(new TouchSlider.Listener() {
                @Override public void changed(TouchSlider s, float value, boolean done) {
                    if (strip.session == null) return;
                    pendingVolume.put(strip.session.id, value); strip.percent.setText(Math.round(value) + "%"); flush(done);
                }
            });
            box.addView(strip.slider, ui.margins(ui.ll(ui.px(48), 0, 1), 0, 6, 0, 6));
            strip.mute = ui.iconButton("volume", "Mute", 18, false, new View.OnClickListener() { @Override public void onClick(View v) { if (strip.session != null) dash.command(Json.map("action", "audio.session.mute", "id", strip.session.id)); } });
            box.addView(strip.mute, ui.ll(Ui.MATCH, ui.px(48)));
            strip.device = stripText("", 13, ui.theme.muted);
            strip.device.setMinHeight(ui.px(48));
            strip.device.setBackground(ui.pressable(0x00000000, 0, 0, 10));
            strip.device.setClickable(true);
            strip.device.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { if (strip.session != null) openAppOutputs(strip.session); } }));
            box.addView(strip.device, ui.ll(Ui.MATCH, ui.px(48)));
            strip.root = box;
            stripsHost.addView(box, ui.margins(ui.ll(ui.px(CardSizing.APP_STRIP_WIDTH), Ui.MATCH), CardSizing.STRIP_GAP, 0, 0, 0));
            strips.add(strip);
        }
    }

    private void renderStrips(PcState.Audio a) {
        if (masterStrip == null) return;
        boolean available = a != null;
        Ui.enable(masterStrip, available); Ui.enable(masterStripMute, available); Ui.enable(masterStripDevice, available && !a.devices.isEmpty());
        if (available && !masterStrip.dragging()) { masterStrip.setValue((float) (a.volume * 100)); masterStripPercent.setText(Math.round(a.volume * 100) + "%"); }
        if (!available) masterStripPercent.setText("—");
        masterStripMute.setGlyph(available && a.muted ? "mute" : "volume");
        masterStripMute.setContentDescription(available && a.muted ? "Unmute output" : "Mute output");
        masterStripDevice.setText(available ? Format.shortDevice(a.device) + " ▾" : "No output");
        List<PcState.Session> sessions = available ? a.sessions : new ArrayList<PcState.Session>();
        for (int i = 0; i < strips.size(); i++) {
            Strip strip = strips.get(i); PcState.Session s = i < sessions.size() ? sessions.get(i) : null;
            strip.session = s; strip.root.setVisibility(s == null ? View.INVISIBLE : View.VISIBLE);
            if (s == null) continue;
            strip.badge.setText(s.name.isEmpty() ? "?" : s.name.substring(0, 1).toUpperCase(java.util.Locale.ROOT));
            strip.name.setText(s.name);
            if (!strip.slider.dragging()) { strip.slider.setValue((float) (s.volume * 100)); strip.percent.setText(Math.round(s.volume * 100) + "%"); }
            strip.slider.setContentDescription(s.name + " volume");
            strip.mute.setGlyph(s.muted ? "mute" : "volume"); strip.mute.setColor(s.muted ? Theme.WARNING : ui.theme.text);
            strip.mute.setContentDescription((s.muted ? "Unmute " : "Mute ") + s.name);
            PcState.Device output = a.find(s.deviceId);
            strip.device.setText(Format.shortDevice(output == null ? "Output" : output.name));
            strip.device.setContentDescription("Output for " + s.name + ": " + (output == null ? "unknown" : output.name));
        }
    }

    private Row createRow(final PcState.Session session) {
        final Row row = new Row();
        LinearLayout box = ui.column();
        FrameLayout labels = new FrameLayout(ui.context);
        row.label = ui.text(session.name, 16 * ui.textScale, ui.theme.text);
        labels.addView(row.label, ui.frame(Ui.MATCH, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        row.label.setPadding(0, 0, ui.px(56), 0);
        row.percent = ui.text("", 14 * ui.textScale, ui.theme.muted);
        labels.addView(row.percent, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        box.addView(labels, ui.ll(Ui.MATCH, ui.px(24)));
        LinearLayout controls = ui.row();
        row.mute = ui.iconButton("volume", "Mute " + session.name, 18, false, new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "audio.session.mute", "id", row.session.id)); } });
        controls.addView(row.mute, ui.ll(ui.px(48), ui.px(48)));
        row.slider = slider(session.id);
        row.slider.setContentDescription(session.name + " volume");
        controls.addView(row.slider, ui.margins(ui.ll(0, ui.px(48), 1), 10, 0, 0, 0));
        row.output = ui.iconButton("output", "Output", 18, false, new View.OnClickListener() { @Override public void onClick(View v) { openAppOutputs(row.session); } });
        controls.addView(row.output, ui.margins(ui.ll(ui.px(48), ui.px(48)), 4, 0, 0, 0));
        box.addView(controls, ui.ll(Ui.MATCH, ui.px(48)));
        // A wide LARGE card lists the apps in two columns, 24 apart (CardSizing.mixerColumns, as on the PC).
        if (appColumns > 1) {
            LinearLayout line = sessionList.getChildCount() == 0 ? null : (LinearLayout) sessionList.getChildAt(sessionList.getChildCount() - 1);
            if (line == null || line.getChildCount() >= appColumns) { line = ui.row(); sessionList.addView(line, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 8, 0, 0)); }
            line.addView(box, ui.margins(ui.ll(0, Ui.WRAP, 1), line.getChildCount() == 0 ? 0 : 24, 0, 0, 0));
        } else sessionList.addView(box, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 8, 0, 0));
        return row;
    }

    // ----- Output picker -----

    private static final class Choice { String id, label, detail; boolean selected; }

    private void openMasterOutputs() {
        PcState.Audio a = audio();
        if (a == null || a.devices.isEmpty()) return;
        List<Choice> choices = new ArrayList<Choice>();
        for (PcState.Device d : a.devices) { Choice c = new Choice(); c.id = d.id; c.label = d.name; c.selected = d.isDefault; choices.add(c); }
        showPicker("MASTER OUTPUT", "Master volume", choices, true, "Apps set to Windows default follow this output.", null);
    }

    private void openAppOutputs(final PcState.Session session) {
        final PcState.Audio a = audio();
        if (a == null || session == null || a.devices.isEmpty()) return;
        dash.pc.send(new Pc.Call<Map<String, Object>>() {
            @Override public Map<String, Object> run(PcLink link) throws Exception { return link.commandResult(Json.map("action", "audio.session.routed", "processId", session.processId)); }
        }, new Async.Done<Map<String, Object>>() {
            @Override public void ok(Map<String, Object> result) {
                if (!built()) return;
                String routed = Json.field(result, "routed");
                boolean routable = Boolean.TRUE.equals(Json.value(result, "canRoute"));
                boolean system = session.processId == 0;
                boolean missing = routed != null && a.find(routed) == null;
                PcState.Device masterDevice = null;
                for (PcState.Device d : a.devices) if (d.isDefault) masterDevice = d;
                List<Choice> choices = new ArrayList<Choice>();
                Choice follow = new Choice(); follow.label = "Windows default"; follow.detail = masterDevice == null ? null : "Same as master  ·  " + masterDevice.name; follow.selected = routed == null; choices.add(follow);
                for (PcState.Device d : a.devices) {
                    Choice c = new Choice(); c.id = d.id; c.label = d.name; c.detail = d.id.equalsIgnoreCase(session.deviceId) ? "Playing here now" : null;
                    c.selected = routed != null && d.id.equalsIgnoreCase(routed); choices.add(c);
                }
                String note = system ? "System sounds always play on the master output. Change it at the top of the mixer."
                    : !routable ? "Windows didn't allow changing this app's output. Use Windows sound settings on the PC."
                    : missing ? "This app is set to an output that isn't connected. Choose another output or Windows default."
                    : "Windows remembers this for the app. Some apps switch only after they restart.";
                showPicker("APP OUTPUT", session.name, choices, routable && !system, note, session);
            }
        }, new Async.Failed() { @Override public void error(Exception error) { dash.say(Pc.message(error)); } });
    }

    private void showPicker(String title, String subject, List<Choice> choices, boolean enabled, String note, final PcState.Session session) {
        closePicker();
        picker = new FrameLayout(ui.context);
        picker.setBackgroundColor(ui.theme.surface);
        picker.setClickable(true);
        LinearLayout column = ui.column();
        LinearLayout top = ui.row();
        LinearLayout titles = ui.column();
        titles.addView(ui.label(title), ui.ll(Ui.WRAP, Ui.WRAP));
        titles.addView(ui.heading(subject), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 4, 0, 0));
        top.addView(titles, ui.ll(0, Ui.WRAP, 1));
        top.addView(ui.iconButton("close", "Close output devices", 22, false, new View.OnClickListener() { @Override public void onClick(View v) { closePicker(); } }), ui.ll(ui.px(52), ui.px(52)));
        column.addView(top, ui.ll(Ui.MATCH, Ui.WRAP));
        ScrollView scroll = new ScrollView(ui.context);
        scroll.setNestedScrollingEnabled(true);
        LinearLayout list = ui.column();
        for (final Choice choice : choices) {
            LinearLayout option = ui.column();
            option.setPadding(ui.px(14), ui.px(8), ui.px(14), ui.px(8));
            option.setMinimumHeight(ui.px(56));
            option.setGravity(Gravity.CENTER_VERTICAL);
            ui.select(option, choice.selected);
            option.addView(ui.body(choice.label), ui.ll(Ui.MATCH, Ui.WRAP));
            if (choice.detail != null) option.addView(ui.smallText(choice.detail), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 3, 0, 0));
            option.setClickable(true);
            Ui.enable(option, enabled);
            option.setOnClickListener(new View.OnClickListener() {
                @Override public void onClick(View v) {
                    closePicker();
                    if (session == null) dash.command(Json.map("action", "audio.output", "id", choice.id), "Sound now plays on " + choice.label + ".");
                    else dash.command(Json.map("action", "audio.session.output", "processId", session.processId, "id", choice.id),
                        session.name + (choice.id == null ? " now follows the master output." : " now plays on " + choice.label + "."));
                }
            });
            list.addView(option, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 6));
        }
        scroll.addView(list);
        column.addView(scroll, ui.margins(ui.ll(Ui.MATCH, 0, 1), 0, 14, 0, 0));
        column.addView(Ui.wrap(ui.smallText(note), 4), ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 10, 0, 0));
        picker.addView(column, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        root.addView(picker, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
    }

    private void closePicker() {
        if (picker != null && picker.getParent() != null) ((FrameLayout) picker.getParent()).removeView(picker);
        picker = null;
    }

    @Override public void release() { super.release(); picker = null; }
}
