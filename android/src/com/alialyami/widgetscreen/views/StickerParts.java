package com.alialyami.widgetscreen.views;

import android.graphics.Canvas;
import android.graphics.DashPathEffect;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.view.View;

import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.Theme;

// Drawn parts of the Sticker Sheet's default five (docs/design/sticker-sheet.md, phase 2), the same as the PC's
// StickerParts.cs: the Focus kitchen-timer dial and session pips, the record that peeks out of Now Playing's sleeve and
// the mixer's LED meters. Paints and paths are made when the view is made or resized; onDraw allocates nothing.
public final class StickerParts {
    private StickerParts() { }

    // The Focus kitchen timer: 60 minute ticks (every fifth longer), the time left as a wedge in the ink sweeping
    // clockwise from the top, a knob, a 3-unit keyline and a solid lift in the Page colour.
    public static final class TimerDial extends View {
        public static final double HOLE = 0.62;
        private final Ui ui;
        private final Paint face = new Paint(Paint.ANTI_ALIAS_FLAG), ink = new Paint(Paint.ANTI_ALIAS_FLAG), lift = new Paint(Paint.ANTI_ALIAS_FLAG),
            tick = new Paint(Paint.ANTI_ALIAS_FLAG), keyline = new Paint(Paint.ANTI_ALIAS_FLAG), knobLine = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Path ticks = new Path();
        private final RectF wedge = new RectF(), knob = new RectF();
        private float left = 1, cx, cy, r;

        public TimerDial(Ui ui, int inkColour) {
            super(ui.context);
            this.ui = ui;
            face.setColor(ui.theme.raised); ink.setColor(inkColour); lift.setColor(ui.theme.page);
            tick.setStyle(Paint.Style.STROKE); tick.setColor(ui.theme.muted); tick.setStrokeCap(Paint.Cap.ROUND);
            keyline.setStyle(Paint.Style.STROKE); keyline.setColor(ui.theme.text); keyline.setStrokeWidth(ui.fpx(3));
            knobLine.setStyle(Paint.Style.STROKE); knobLine.setColor(ui.theme.text); knobLine.setStrokeWidth(ui.fpx(2));
        }

        // The part of the session left, 0 to 1.
        public void setLeft(double value) { float next = (float) Math.max(0, Math.min(1, Double.isNaN(value) ? 0 : value)); if (Math.abs(next - left) > 0.0005f) { left = next; invalidate(); } }
        public float left() { return left; }

        @Override protected void onSizeChanged(int w, int h, int oldw, int oldh) {
            float size = Math.min(w, h - ui.fpx(Stickers.LIFT));
            r = size / 2 - ui.fpx(1.5); cx = w / 2f; cy = size / 2;
            tick.setStrokeWidth(Math.max(1, size / 160));
            ticks.reset();
            for (int i = 0; i < 60; i++) {
                double angle = i * Math.PI / 30; float from = r * (i % 5 == 0 ? 0.74f : 0.82f), to = r * 0.9f;
                ticks.moveTo(cx + from * (float) Math.sin(angle), cy - from * (float) Math.cos(angle));
                ticks.lineTo(cx + to * (float) Math.sin(angle), cy - to * (float) Math.cos(angle));
            }
            float outer = r * 0.94f; wedge.set(cx - outer, cy - outer, cx + outer, cy + outer);
            float k = Math.max(ui.fpx(10), size * 0.09f); knob.set(cx - k * 0.7f, cy - r - k * 0.55f, cx + k * 0.7f, cy - r + k * 0.25f);
        }

        @Override protected void onDraw(Canvas canvas) {
            if (r < 20) return;
            canvas.drawCircle(cx, cy + ui.fpx(Stickers.LIFT), r, lift);
            canvas.drawCircle(cx, cy, r, face);
            if (left > 0.001f) canvas.drawArc(wedge, -90, left * 360, true, ink);
            canvas.drawCircle(cx, cy, r * (float) HOLE, face);
            canvas.drawPath(ticks, tick);
            canvas.drawCircle(cx, cy, r, keyline);
            canvas.drawRoundRect(knob, knob.height() * 0.25f, knob.height() * 0.25f, ink);
            canvas.drawRoundRect(knob, knob.height() * 0.25f, knob.height() * 0.25f, knobLine);
        }
    }

    // Today's sessions as tomato pips: one filled pip per finished session, dashed empty ones up to the day's four.
    public static final class PipRow extends View {
        public static final int GOAL = 4, MOST_SHOWN = 8;
        private final Ui ui;
        private final Paint filled = new Paint(Paint.ANTI_ALIAS_FLAG), stem = new Paint(Paint.ANTI_ALIAS_FLAG), empty = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF leaf = new RectF();
        private int count;
        private final float pip;

        public PipRow(Ui ui, int inkColour, double pipUnits) {
            super(ui.context);
            this.ui = ui; pip = ui.fpx(pipUnits);
            filled.setColor(inkColour); stem.setColor(ui.theme.muted);
            empty.setStyle(Paint.Style.STROKE); empty.setColor(ui.theme.muted); empty.setStrokeWidth(ui.fpx(1.5));
            empty.setPathEffect(new DashPathEffect(new float[] { ui.fpx(2), ui.fpx(2) }, 0));
        }
        public static int shown(int count) { return CardSizing.pipsShown(count); }
        public void setCount(int value) { if (value != count) { count = Math.max(0, value); requestLayout(); invalidate(); } }
        public int count() { return count; }

        @Override protected void onMeasure(int widthSpec, int heightSpec) {
            int n = shown(count);
            setMeasuredDimension(resolveSize(Math.round(n * pip + (n - 1) * pip * 0.4f), widthSpec), resolveSize(Math.round(pip), heightSpec));
        }
        @Override protected void onDraw(Canvas canvas) {
            int n = shown(count); float r = pip / 2 - ui.fpx(1), step = pip * 1.4f;
            float start = (getWidth() - (n * pip + (n - 1) * pip * 0.4f)) / 2, cy = getHeight() / 2f;
            for (int i = 0; i < n; i++) {
                float cx = start + pip / 2 + i * step;
                if (i < count) {
                    canvas.drawCircle(cx, cy, r, filled);
                    leaf.set(cx - ui.fpx(1.5), cy - r - ui.fpx(1), cx + ui.fpx(1.5), cy - r + ui.fpx(3));
                    canvas.drawRoundRect(leaf, ui.fpx(1), ui.fpx(1), stem);
                } else canvas.drawCircle(cx, cy, r - ui.fpx(0.75), empty);
            }
        }
    }

    // A record peeking out of Now Playing's sleeve: a dark disc with grooves and a label in the ink, its right edge
    // at the view's right edge.
    public static final class DiscPeek extends View {
        private static final float[] GROOVES = { 0.45f, 0.6f, 0.75f, 0.88f };
        private final Paint vinyl = new Paint(Paint.ANTI_ALIAS_FLAG), groove = new Paint(Paint.ANTI_ALIAS_FLAG), label = new Paint(Paint.ANTI_ALIAS_FLAG);
        public DiscPeek(Ui ui, int inkColour) {
            super(ui.context);
            vinyl.setColor(0xFF0B0C0F); label.setColor(inkColour);
            groove.setStyle(Paint.Style.STROKE); groove.setColor(ui.theme.track); groove.setStrokeWidth(Math.max(1, ui.fpx(1)));
        }
        @Override protected void onDraw(Canvas canvas) {
            float size = Math.min(getWidth(), getHeight()); if (size < 20) return;
            float r = size / 2, cx = getWidth() - r, cy = getHeight() / 2f;
            canvas.drawCircle(cx, cy, r - 0.5f, vinyl);
            canvas.drawCircle(cx, cy, r - 0.5f, groove);
            for (float part : GROOVES) canvas.drawCircle(cx, cy, r * part, groove);
            canvas.drawCircle(cx, cy, r * 0.3f, label);
            canvas.drawCircle(cx, cy, Math.max(1.5f, r * 0.04f), vinyl);
        }
    }

    // An LED level meter: segments lit from the bottom (vertical) or the left, in the ink with the top two in Amber.
    public static final class LedMeter extends View {
        private final Paint lit = new Paint(Paint.ANTI_ALIAS_FLAG), unlit = new Paint(Paint.ANTI_ALIAS_FLAG), hot = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF cell = new RectF();
        private final boolean vertical;
        private final float segment, gap, corner;
        private float level;

        public LedMeter(Ui ui, int inkColour, boolean vertical, double segmentUnits) {
            super(ui.context);
            this.vertical = vertical; segment = ui.fpx(segmentUnits); gap = ui.fpx(3); corner = ui.fpx(2);
            lit.setColor(inkColour); unlit.setColor(ui.theme.track); hot.setColor(Theme.WARNING);
        }
        // 0 to 1, as the PC's output peak.
        public void setLevel(double value) { float next = (float) Math.max(0, Math.min(1, Double.isNaN(value) ? 0 : value)); if (next != level) { level = next; invalidate(); } }
        public static int lit(double level, int count) { return CardSizing.ledLit(level, count); }
        public int count() { float length = vertical ? getHeight() : getWidth(); return Math.max(4, (int) Math.floor((length + gap) / (segment + gap))); }
        @Override protected void onDraw(Canvas canvas) {
            if (getWidth() <= 0 || getHeight() <= 0) return;
            int count = count(), on = lit(level, count);
            float length = vertical ? getHeight() : getWidth(), size = (length - gap * (count - 1)) / count;
            if (size <= 0) return;
            for (int i = 0; i < count; i++) {
                float at = i * (size + gap);
                if (vertical) cell.set(0, getHeight() - at - size, getWidth(), getHeight() - at); else cell.set(at, 0, at + size, getHeight());
                canvas.drawRoundRect(cell, corner, corner, i >= on ? unlit : i >= count - 2 ? hot : lit);
            }
        }
    }
}
