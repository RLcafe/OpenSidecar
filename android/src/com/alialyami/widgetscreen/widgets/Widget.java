package com.alialyami.widgetscreen.widgets;

import android.view.View;

import com.alialyami.widgetscreen.Dashboard;
import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.views.Stickers;

// One dashboard card, mirroring the same card on the PC. Its state lives here and survives rebuilds (theme, text
// size, rotation); build() creates fresh views for the card's current size, then render() fills them from the
// last state the PC sent.
public abstract class Widget {
    public final String id;
    protected final Dashboard dash;
    protected Ui ui;
    protected double width, height;
    private View card;

    protected Widget(String id, Dashboard dash) { this.id = id; this.dash = dash; }

    public final View build(Ui ui, double width, double height) {
        this.ui = ui; this.width = width; this.height = height;
        card = create();
        render();
        return card;
    }

    // Called when the card is no longer on screen (hidden, or before a rebuild).
    public void release() { card = null; }

    public boolean built() { return card != null; }

    public boolean builtAt(double width, double height) { return built() && Math.abs(width - this.width) < 0.5 && Math.abs(height - this.height) < 0.5; }

    // The section of the PC's state this card shows. Only shown cards are asked for, so hidden ones don't poll.
    public abstract String part();

    protected abstract View create();

    public void render() { }

    // A new reading from the PC arrived. Most cards simply redraw.
    public void stateChanged() { render(); }

    // The 1 s heartbeat. Widgets keep their own cadence from the tick count, like the Windows controller.
    public void tick(int ticks, boolean quiet) { }

    protected boolean active() { return built() && dash.isActive(id); }

    // The card's label strip in its ink, and a status sticker for its right-hand fact (docs/design/sticker-sheet.md).
    private double stripWidth;
    // The label strip's natural width, for headers that share their row with more than one sticker or button.
    protected double stripWidth() { return stripWidth; }
    protected View strip() { Stickers.LabelStrip strip = new Stickers.LabelStrip(ui, id, dash.ink(id), width); stripWidth = strip.naturalUnits(); return strip; }
    // The sticker gets the header's room beside the strip (card less its 24-unit padding and a 12-unit gap), and steps
    // aside when that's under 40 units, as on the PC.
    protected Stickers.Sticker sticker(boolean warns) {
        Stickers.Sticker sticker = new Stickers.Sticker(ui, dash.ink(id), warns);
        sticker.setRoom(width - 48 - stripWidth - 12);
        return sticker;
    }
}
