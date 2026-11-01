package com.alialyami.widgetscreen.widgets;

import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.HomeText;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.views.GlyphView;
import com.alialyami.widgetscreen.views.TouchSlider;

import java.util.ArrayList;
import java.util.List;

// Smart home, in the same three designs as the Windows HomeCard (CardSizing.home; docs/design/smart-home.md): rows
// that scroll (Small), tiles (Medium), tiles with dimmers (Large). Every change goes to the PC, which asks Home
// Assistant; the phone never talks to Home Assistant itself.
public final class HomeWidget extends Widget {
    private TextView status, empty;
    private FrameLayout body;
    private String shown;
    private int page;
    private final List<TouchSlider> sliders = new ArrayList<TouchSlider>();

    public HomeWidget(Dashboard dash) { super("Home", dash); }

    @Override public String part() { return "home"; }

    @Override protected View create() {
        shown = null;
        LinearLayout column = ui.column();
        FrameLayout header = new FrameLayout(ui.context);
        header.addView(strip(), ui.frame(Ui.WRAP, Ui.WRAP, Gravity.START | Gravity.CENTER_VERTICAL));
        status = sticker(true);
        header.addView(status, ui.frame(Ui.WRAP, Ui.WRAP, Gravity.END | Gravity.CENTER_VERTICAL));
        column.addView(header, ui.ll(Ui.MATCH, ui.px(28)));
        body = new FrameLayout(ui.context);
        empty = Ui.wrap(ui.caption(""), 4); empty.setGravity(Gravity.CENTER_VERTICAL);
        column.addView(body, ui.margins(ui.ll(Ui.MATCH, 0, 1), -4, 4, -4, -4));
        return ui.card(column, 24);
    }

    @Override public void render() {
        if (!built()) return;
        for (TouchSlider slider : sliders) if (slider.dragging()) return;
        PcState state = dash.state();
        PcState.Home home = state == null || !dash.online() ? null : state.home;
        StringBuilder signature = new StringBuilder(home == null ? "offline" : home.connected + "|" + home.chosen + "|" + home.read + "|" + home.problem + "|" + page);
        if (home != null) for (PcState.HomeDevice d : home.devices) signature.append(';').append(d.id).append(d.state).append(d.on).append(d.available).append(d.brightness).append(d.target);
        if (signature.toString().equals(shown)) return;
        shown = signature.toString();
        body.removeAllViews(); sliders.clear();
        status.setText(home == null ? "" : HomeText.status(home));
        if (home == null || home.devices.isEmpty()) {
            empty.setText(home == null ? dash.offlineText() : HomeText.empty(home));
            body.addView(empty, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
            return;
        }
        int design = CardSizing.home(width, height);
        double areaWidth = Math.max(1, width - 48), areaHeight = Math.max(1, height - 48 - 36);
        List<PcState.HomeDevice> devices = home.devices;
        if (design == CardSizing.SMALL) {
            ScrollView scroll = new ScrollView(ui.context);
            scroll.setVerticalScrollBarEnabled(false);
            LinearLayout rows = ui.column();
            for (PcState.HomeDevice device : devices) rows.addView(tile(device, design, true, false), ui.ll(Ui.MATCH, Ui.WRAP));
            scroll.addView(rows, new FrameLayout.LayoutParams(Ui.MATCH, Ui.WRAP));
            body.addView(scroll, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
            return;
        }
        int[] shape = CardSizing.homeGrid(areaWidth, areaHeight, ui.textScale, devices.size());
        int columns = shape[0], rows = shape[1], pageSize = columns * rows, pages = Math.max(1, (devices.size() + pageSize - 1) / pageSize);
        page = Math.max(0, Math.min(pages - 1, page));
        boolean under = areaHeight / rows >= CardSizing.homeTile(ui.textScale);
        LinearLayout outer = ui.row(); outer.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout grid = ui.column();
        for (int r = 0; r < rows; r++) {
            LinearLayout line = ui.row(); line.setGravity(Gravity.TOP);
            for (int c = 0; c < columns; c++) {
                int index = page * pageSize + r * columns + c;
                View cell = index < devices.size() ? tile(devices.get(index), design, false, under) : new View(ui.context);
                line.addView(cell, ui.ll(0, Ui.MATCH, 1));
            }
            grid.addView(line, ui.ll(Ui.MATCH, 0, 1));
        }
        outer.addView(grid, ui.ll(0, Ui.MATCH, 1));
        if (shape[2] == 1) {
            LinearLayout pager = ui.column(); pager.setGravity(Gravity.CENTER_HORIZONTAL);
            View previous = ui.iconButton("chevron-up", "Previous devices", 16, true, new View.OnClickListener() { @Override public void onClick(View v) { page--; shown = null; render(); } });
            Ui.enable(previous, page > 0);
            pager.addView(previous, ui.ll(ui.px(48), ui.px(48)));
            TextView label = ui.caption((page + 1) + "/" + pages); label.setGravity(Gravity.CENTER);
            pager.addView(label, ui.margins(ui.ll(ui.px(48), Ui.WRAP), 0, 4, 0, 4));
            View next = ui.iconButton("chevron-down", "Next devices", 16, true, new View.OnClickListener() { @Override public void onClick(View v) { page++; shown = null; render(); } });
            Ui.enable(next, page < pages - 1);
            pager.addView(next, ui.ll(ui.px(48), ui.px(48)));
            outer.addView(pager, ui.margins(ui.ll(ui.px(CardSizing.HOME_PAGER - 8), Ui.WRAP), 8, 0, 0, 0));
        }
        body.addView(outer, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
    }

    // A device: the tap target is its glyph, name and state together; thermostats add − and +, and dimmable lights on
    // a Large card a slider. The same parts as the PC's HomeCard.Tile.
    private View tile(final PcState.HomeDevice device, int design, boolean row, boolean under) {
        LinearLayout toggle = ui.row();
        toggle.setPadding(ui.px(row ? 12 : 14), ui.px(8), ui.px(10), ui.px(8));
        toggle.setMinimumHeight(ui.px(48));
        toggle.setBackground(ui.pressable(device.on ? ui.theme.selected : ui.theme.raised, device.on ? ui.theme.accent : ui.theme.controlBorder, device.on ? 2 : 1, 12));
        double glyphSize = row ? 18 : 22;
        toggle.addView(new GlyphView(ui.context, HomeText.glyph(device), device.on ? ui.theme.accent : ui.theme.muted, ui.fpx(glyphSize)), ui.margins(ui.ll(ui.px(glyphSize + 4), ui.px(glyphSize + 4)), 0, 0, row ? 8 : 10, 0));
        TextView name = ui.text(device.name, row ? ui.small() : ui.body(), ui.theme.text); name.setSingleLine(true); name.setEllipsize(android.text.TextUtils.TruncateAt.END);
        TextView state = ui.text(device.state, ui.small(), device.on ? ui.theme.text : ui.theme.muted); state.setSingleLine(true); state.setEllipsize(android.text.TextUtils.TruncateAt.END);
        if (row) {
            toggle.addView(name, ui.ll(0, Ui.WRAP, 1));
            toggle.addView(state, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 10, 0, 0, 0));
        } else {
            LinearLayout words = ui.column();
            words.addView(name, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 0, 0, 2));
            words.addView(state, ui.ll(Ui.MATCH, Ui.WRAP));
            toggle.addView(words, ui.ll(0, Ui.WRAP, 1));
        }
        toggle.setContentDescription(device.name + ", " + device.state + ". Tap to turn " + (device.on ? "off" : "on") + ".");
        toggle.setClickable(true);
        toggle.setOnClickListener(Ui.haptic(new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "home.toggle", "id", device.id)); } }));
        Ui.enable(toggle, device.available);

        LinearLayout tile = ui.column();
        tile.setPadding(ui.px(4), ui.px(4), ui.px(4), ui.px(4));
        boolean thermostat = device.domain.equals("climate") && device.available && device.target != null && !row;
        if (thermostat && !under) {
            LinearLayout beside = ui.row();
            beside.addView(toggle, ui.ll(0, Ui.MATCH, 1));
            LinearLayout stack = ui.column(); stack.setGravity(Gravity.CENTER);
            stack.addView(step(device, 1), ui.ll(ui.px(52), ui.px(48)));
            stack.addView(step(device, -1), ui.margins(ui.ll(ui.px(52), ui.px(48)), 0, 3, 0, 0));
            beside.addView(stack, ui.margins(ui.ll(Ui.WRAP, Ui.MATCH), 6, 0, 0, 0));
            tile.addView(beside, ui.ll(Ui.MATCH, 0, 1));
            return tile;
        }
        tile.addView(toggle, row ? ui.ll(Ui.MATCH, Ui.WRAP) : ui.ll(Ui.MATCH, 0, 1));
        if (thermostat) {
            LinearLayout controls = ui.row();
            controls.addView(step(device, -1), ui.margins(ui.ll(0, ui.px(48), 1), 0, 0, 3, 0));
            controls.addView(step(device, 1), ui.margins(ui.ll(0, ui.px(48), 1), 3, 0, 0, 0));
            tile.addView(controls, ui.margins(ui.ll(Ui.MATCH, Ui.WRAP), 0, 6, 0, 0));
        } else if (device.domain.equals("light") && device.dimmable && device.available && design == CardSizing.LARGE && under) {
            final TouchSlider slider = new TouchSlider(ui.context, ui).range(1, 100, 1);
            slider.setValue(device.brightness == null ? 1 : device.brightness);
            slider.setContentDescription(device.name + " brightness");
            slider.setListener(new TouchSlider.Listener() { @Override public void changed(TouchSlider s, float value, boolean done) {
                if (done) { dash.command(Json.map("action", "home.brightness", "id", device.id, "value", Math.round(value))); shown = null; }
            } });
            sliders.add(slider);
            tile.addView(slider, ui.margins(ui.ll(Ui.MATCH, ui.px(48)), 0, 4, 0, 0));
        }
        return tile;
    }

    private View step(final PcState.HomeDevice device, final int direction) {
        final double next = HomeText.stepped(device, direction);
        TextView button = ui.button(direction < 0 ? "−" : "+", new View.OnClickListener() { @Override public void onClick(View v) { dash.command(Json.map("action", "home.temperature", "id", device.id, "value", next)); } });
        button.setGravity(Gravity.CENTER); button.setPadding(0, 0, 0, 0);
        button.setContentDescription((direction < 0 ? "Lower " : "Raise ") + device.name + " to " + HomeText.degrees(next));
        Ui.enable(button, direction < 0 ? device.target > device.min : device.target < device.max);
        return button;
    }

    // The PC reads Home Assistant every ten seconds while its card is shown; the phone asks for the section with the
    // other cards' (every 2 s, 5 in Quiet mode), so nothing else polls here.
}
