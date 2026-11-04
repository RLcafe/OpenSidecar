package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.DashPathEffect;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.view.View;

import com.alialyami.widgetscreen.core.ClaudeText;
import com.alialyami.widgetscreen.core.Theme;

// The Claude card's instruments on the phone (decision 028, docs/design/claude-card.md), the same as the Windows
// ClaudeGauges.cs: the needle gauge (the 5-hour use and tokens per minute), the week as seven laps, and the burn line.
// Sizes are in the dashboard's units (unit pixels each); the gauge draws its 140 × 84 box scaled to fit and centred.
public final class ClaudeGauges {
    private ClaudeGauges() { }

    private abstract static class Instrument extends View {
        protected final Theme theme; protected final float unit;
        protected int ink;
        protected final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        protected final Path clip = new Path();
        Instrument(Context context, Theme theme, int ink, float unit) {
            super(context, null); this.theme = theme; this.ink = ink; this.unit = unit;
            setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO);
        }
        public void setInk(int color) { ink = color; }
        protected static int faded(int color, double opacity) { return (Math.round((float) (((color >>> 24) & 0xFF) * opacity)) << 24) | (color & 0xFFFFFF); }
        // Diagonal hatching inside a rectangle: where this pace ends up.
        protected void hatch(Canvas canvas, RectF area, int color, float radius) {
            if (area.width() <= 0 || area.height() <= 0) return;
            canvas.save();
            clip.reset(); clip.addRoundRect(area, radius, radius, Path.Direction.CW); canvas.clipPath(clip);
            paint.setStyle(Paint.Style.FILL); paint.setColor(faded(color, 0.18)); canvas.drawRect(area, paint);
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(2 * unit); paint.setColor(faded(color, 0.55)); paint.setStrokeCap(Paint.Cap.BUTT);
            for (float x = area.left - area.height(); x < area.right; x += 6 * unit) canvas.drawLine(x, area.bottom, x + area.height(), area.top, paint);
            canvas.restore();
            paint.setStyle(Paint.Style.FILL);
        }
    }

    // A half-circle needle gauge in a 140 × 84 box (values 0–1; NaN leaves a part out): the value as an arc in the ink
    // (Amber when warn), a needle, the projection as a dashed Amber arc past it, a pace tick and a redline.
    public static final class Gauge extends Instrument {
        public static final float BOX_WIDTH = 140, BOX_HEIGHT = 84;
        public double value, projection = Double.NaN, pace = Double.NaN, red;
        public String low = "0", high = "100";
        public boolean warn;
        private final RectF oval = new RectF();
        private final DashPathEffect dashes = new DashPathEffect(new float[] { 3, 3 }, 0);
        public Gauge(Context context, Theme theme, int ink, float unit) { super(context, theme, ink, unit); }
        // The scale the box is drawn at, in pixels a box unit.
        public float scale() { return Math.max(0, Math.min(getWidth() / BOX_WIDTH, getHeight() / BOX_HEIGHT)); }
        private static double angle(double f) { return Math.PI + Math.max(0, Math.min(1, f)) * Math.PI; }
        private void arc(Canvas canvas, double from, double to, double r, int color, boolean round, boolean dashed) {
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(10); paint.setColor(color);
            paint.setStrokeCap(round ? Paint.Cap.ROUND : Paint.Cap.BUTT);
            paint.setPathEffect(dashed ? dashes : null);
            oval.set((float) (70 - r), (float) (74 - r), (float) (70 + r), (float) (74 + r));
            canvas.drawArc(oval, (float) Math.toDegrees(angle(from)), (float) Math.toDegrees(angle(to) - angle(from)), false, paint);
            paint.setPathEffect(null);
        }
        private static float px(double a, double r) { return (float) (70 + r * Math.cos(a)); }
        private static float py(double a, double r) { return (float) (74 + r * Math.sin(a)); }
        @Override protected void onDraw(Canvas canvas) {
            float k = scale();
            if (k <= 0) return;
            canvas.save();
            canvas.translate(Math.round((getWidth() - BOX_WIDTH * k) / 2), Math.round((getHeight() - BOX_HEIGHT * k) / 2));
            canvas.scale(k, k);
            double r = 58;
            arc(canvas, 0, 1, r, theme.track, true, false);
            if (red > 0) arc(canvas, red, 1, r, 0x8CF09A9A, false, false);
            if (!Double.isNaN(projection) && projection > value) arc(canvas, value, Math.min(1, projection), r, faded(Theme.WARNING, 0.55), false, true);
            if (value > 0.002) arc(canvas, 0, value, r, warn ? Theme.WARNING : ink, true, false);
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeCap(Paint.Cap.BUTT); paint.setStrokeWidth(1.5f); paint.setColor(theme.muted);
            for (int i = 0; i <= 10; i++) { double a = angle(i / 10.0), r2 = r - (i % 5 == 0 ? 20 : 16); canvas.drawLine(px(a, r - 12), py(a, r - 12), px(a, r2), py(a, r2), paint); }
            paint.setStrokeCap(Paint.Cap.ROUND); paint.setColor(theme.text);
            if (!Double.isNaN(pace)) { double a = angle(pace); paint.setStrokeWidth(2.5f); canvas.drawLine(px(a, r - 7), py(a, r - 7), px(a, r + 8), py(a, r + 8), paint); }
            double n = angle(value); paint.setStrokeWidth(3); canvas.drawLine(70, 74, px(n, r - 18), py(n, r - 18), paint);
            paint.setStyle(Paint.Style.FILL); canvas.drawCircle(70, 74, 6, paint);
            if (k >= 0.7 * unit) {
                paint.setTextSize(10); paint.setColor(theme.muted);
                paint.setTextAlign(Paint.Align.LEFT); canvas.drawText(low, 10, 84, paint);
                paint.setTextAlign(Paint.Align.RIGHT); canvas.drawText(high, 130, 84, paint);
            }
            canvas.restore();
        }
    }

    // The week as seven laps with the day names (or their first letters when narrow) from 30 units tall, the lap it's
    // on outlined, the projection hatched, and a chequered flag at the reset.
    public static final class Laps extends Instrument {
        public static final float BAR = 14, WITH_DAYS = 30;
        public double percent, projection = Double.NaN;
        public int now = -1;
        public boolean warn;
        public String[] days = new String[7];
        private final RectF box = new RectF();
        public Laps(Context context, Theme theme, int ink, float unit) { super(context, theme, ink, unit); }
        @Override protected void onDraw(Canvas canvas) {
            float u = unit, w = getWidth(), flag = 22 * u, gap = 3 * u, lap = (w - flag - gap * 7) / 7, bar = BAR * u, r = 4 * u;
            if (lap <= 4 * u) return;
            boolean named = getHeight() >= (WITH_DAYS - 0.5f) * u;
            int fill = warn ? Theme.WARNING : ink;
            paint.setTextSize(11 * u); paint.setTextAlign(Paint.Align.CENTER);
            for (int i = 0; i < 7; i++) {
                box.set(i * (lap + gap), 0, i * (lap + gap) + lap, bar);
                paint.setStyle(Paint.Style.FILL); paint.setColor(theme.track); canvas.drawRoundRect(box, r, r, paint);
                double used = ClaudeText.lap(percent, i), proj = Double.isNaN(projection) ? 0 : ClaudeText.lap(projection, i);
                if (proj > used) hatch(canvas, new RectF(box.left, 0, (float) (box.left + lap * proj), bar), fill, r);
                if (used > 0) {
                    canvas.save(); clip.reset(); clip.addRoundRect(box, r, r, Path.Direction.CW); canvas.clipPath(clip);
                    paint.setStyle(Paint.Style.FILL); paint.setColor(fill); canvas.drawRect(box.left, 0, (float) (box.left + lap * used), bar, paint); canvas.restore();
                }
                if (i == now) { paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(u); paint.setColor(theme.text); canvas.drawRoundRect(new RectF(box.left + u / 2, u / 2, box.right - u / 2, bar - u / 2), r, r, paint); }
                if (!named) continue;
                String name = days[i] == null ? "" : days[i];
                if (paint.measureText(name) > lap + gap && name.length() > 1) name = name.substring(0, 1);
                if (paint.measureText(name) > lap + gap) continue;
                paint.setStyle(Paint.Style.FILL); paint.setColor(i == now ? theme.text : theme.muted);
                canvas.drawText(name, box.centerX(), bar + 13 * u, paint);
            }
            float fx = 7 * (lap + gap);
            paint.setStyle(Paint.Style.FILL);
            paint.setColor(theme.text); canvas.drawRoundRect(new RectF(fx, 0, fx + flag, bar), 3 * u, 3 * u, paint);
            paint.setColor(theme.page);
            for (int c = 0; c < 4; c++) canvas.drawRect(fx + c * flag / 4, c % 2 == 0 ? 0 : bar / 2, fx + (c + 1) * flag / 4, c % 2 == 0 ? bar / 2 : bar, paint);
        }
    }

    // The half hour's tokens a minute: a line over a soft fill, the redline dashed, the latest minute marked.
    public static final class Burn extends Instrument {
        public double[] values = new double[0];
        public double maximum = 1;
        private final Path line = new Path(), area = new Path();
        private final DashPathEffect dashes;
        public Burn(Context context, Theme theme, int ink, float unit) { super(context, theme, ink, unit); dashes = new DashPathEffect(new float[] { 3 * unit, 4 * unit }, 0); }
        private float y(double v, float h) { return (float) (h - 2 * unit - (h - 4 * unit) * Math.max(0, Math.min(1, v / Math.max(1, maximum)))); }
        @Override protected void onDraw(Canvas canvas) {
            float w = getWidth() - 3.5f * unit, h = getHeight();
            if (w <= 0 || h <= 0 || values == null || values.length < 2) return;
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(unit); paint.setColor(theme.track); paint.setPathEffect(dashes);
            float red = (float) (h - h * ClaudeText.REDLINE); canvas.drawLine(0, red, getWidth(), red, paint); paint.setPathEffect(null);
            line.reset(); area.reset();
            area.moveTo(0, h);
            for (int i = 0; i < values.length; i++) {
                float x = i * w / (values.length - 1), y = y(values[i], h);
                if (i == 0) line.moveTo(x, y); else line.lineTo(x, y);
                area.lineTo(x, y);
            }
            area.lineTo(w, h); area.close();
            paint.setStyle(Paint.Style.FILL); paint.setColor(faded(ink, 0.16)); canvas.drawPath(area, paint);
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(2 * unit); paint.setStrokeCap(Paint.Cap.ROUND); paint.setColor(ink); canvas.drawPath(line, paint);
            paint.setStyle(Paint.Style.FILL); canvas.drawCircle(w, y(values[values.length - 1], h), 3.5f * unit, paint);
        }
    }
}
