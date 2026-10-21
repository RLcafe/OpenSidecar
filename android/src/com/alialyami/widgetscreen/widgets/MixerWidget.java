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
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcLink;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.integrations.Async;
import com.alialyami.widgetscreen.integrations.Pc;
import com.alialyami.widgetscreen.views.GlyphView;
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

    private TextView status, device, percent, sessionCount, noSessions, microphoneLabel;
    private TouchSlider master;
    private GlyphView mute, microphoneGlyph;
    private LinearLayout sessionList, microphone, outputButton;
    private FrameLayout picker, root;
    private final Map<String, Row> rows = new HashMap<String, Row>();
    private String rowSignature;
    // Slider moves are sent at most every 150 ms, then once more when the finger lifts.
    private final Map<String, Float> pendingVolume = new HashMap<String, Float>();
    private long lastSent;
    private boolean sendQueued;

    public MixerWidget(Dashboard dash) { super("Mixer", dash); }

    @Override public String part() { return "audio"; }

    @Override protected View create() {
        rows.clear(); rowSignature = null; picker = null;
        root = new FrameLayout(ui.context);
        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(ui.label("SOUND MIXER"), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        status = ui.caption("");
        status.setTextColor(Theme.WARNING);
        header.addView(status, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(34)));

        LinearLayout masterRow = ui.row();
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

        LinearLayout sliderRow = ui.row();
        mute = ui.iconButton("volume", "Mute output", 22, false, new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "audio.mute")); } });
        sliderRow.addView(mute, ui.ll(ui.px(58), ui.px(52)));
        master = slider(null);
        master.setContentDescription("Master volume");
        sliderRow.addView(master, ui.margins(ui.ll(0, ui.px(52), 1), 8, 0, 0, 0));
        column.addView(sliderRow, ui.ll(Ui.MATCH, ui.px(52)));

        FrameLayout appsHeader = new FrameLayout(ui.context);
        appsHeader.addView(ui.label("APPS"), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        sessionCount = ui.smallText("");
        appsHeader.addView(sessionCount, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(appsHeader, ui.margins(ui.ll(Ui.MATCH, ui.px(24)), 0, 18, 0, 0));
        FrameLayout listHost = new FrameLayout(ui.context);
        ScrollView scroll = new ScrollView(ui.context);
        scroll.setVerticalScrollBarEnabled(false);
        scroll.setNestedScrollingEnabled(true);
        sessionList = ui.column();
        scroll.addView(sessionList);
        listHost.addView(scroll, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        noSessions = Ui.wrap(ui.smallText(""), 3);
        noSessions.setGravity(Gravity.CENTER);
        listHost.addView(noSessions, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        column.addView(listHost, ui.ll(Ui.MATCH, 0, 1));

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
        column.addView(microphone, ui.margins(ui.ll(Ui.MATCH, ui.px(48)), 0, 8, 0, 0));
        root.addView(column, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        return ui.card(root, 24);
    }

    private PcState.Audio audio() { PcState state = dash.state(); return state == null || !dash.online() ? null : state.audio; }

    private TouchSlider slider(final String sessionId) {
        TouchSlider slider = new TouchSlider(ui.context, ui).range(0, 100, 0);
        slider.setListener(new TouchSlider.Listener() {
            @Override public void changed(TouchSlider s, float value, boolean done) {
                pendingVolume.put(sessionId == null ? "" : sessionId, value);
                if (sessionId == null) percent.setText(Math.round(value) + "%");
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
            if (entry.getKey().isEmpty()) dash.command(Json.map("action", "audio.volume", "value", value));
            else dash.command(Json.map("action", "audio.session.volume", "id", entry.getKey(), "value", value));
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
        renderSessions(available ? a : null);
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
        noSessions.setVisibility(sessions.isEmpty() ? View.VISIBLE : View.GONE);
        noSessions.setText(a == null ? "" : !a.sessionsAvailable ? "App volumes aren't available right now. Master volume still works." : "Apps show up here when they play sound on the PC.");
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
        sessionList.addView(box, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 8, 0, 0));
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
