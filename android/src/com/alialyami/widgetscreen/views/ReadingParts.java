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
import com.alialyami.widgetscreen.core.Weather;

// Drawn parts of the Sticker Sheet's readings (docs/design/sticker-sheet.md, phase 3), the same as the PC's
// ReadingParts.cs: Weather's 12-hour temperature curve over rain bars and its sunrise–sunset arc, Storage's jars, and
// World clocks' faces and 24-hour bands. Paints are made with
// the view and paths when the data or size changes; onDraw allocates nothing.
public final class ReadingParts {
    private ReadingParts() { }

    // The next hours as a line over rain bars: now is a dot in the ink, the highest and lowest temperatures are written
    // by their points, and the hours are labelled every three.
    public static final class TemperatureCurve extends View {
        private final Ui ui;
        private final Paint line = new Paint(Paint.ANTI_ALIAS_FLAG), rain = new Paint(Paint.ANTI_ALIAS_FLAG), dot = new Paint(Paint.ANTI_ALIAS_FLAG),
            keyline = new Paint(Paint.ANTI_ALIAS_FLAG), value = new Paint(Paint.ANTI_ALIAS_FLAG), label = new Paint(Paint.ANTI_ALIAS_FLAG), now = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Path path = new Path();
        private final RectF bar = new RectF();
        private double[] temperatures = new double[0];
        private Integer[] chances = new Integer[0];
        private String[] hours = new String[0];
        private float[] xs = new float[0], ys = new float[0];
        private int high, low;

        public TemperatureCurve(Ui ui, int inkColour) {
            super(ui.context);
            this.ui = ui;
            line.setStyle(Paint.Style.STROKE); line.setStrokeWidth(ui.fpx(2.5)); line.setStrokeJoin(Paint.Join.ROUND); line.setStrokeCap(Paint.Cap.ROUND); line.setColor(ui.theme.text);
            rain.setColor(Theme.ink("Glacier")); rain.setAlpha(140);
            dot.setColor(inkColour);
            keyline.setStyle(Paint.Style.STROKE); keyline.setStrokeWidth(ui.fpx(2)); keyline.setColor(ui.theme.text);
            value.setColor(ui.theme.text); value.setTextSize(ui.fpx(13)); value.setTypeface(Ui.MEDIUM); value.setTextAlign(Paint.Align.CENTER);
            label.setColor(ui.theme.muted); label.setTextSize(ui.fpx(13)); label.setTextAlign(Paint.Align.CENTER);
            now.setColor(inkColour); now.setTextSize(ui.fpx(13)); now.setTextAlign(Paint.Align.CENTER);
        }

        // Each hour from now: its temperature, its chance of rain (or null) and its label (or null for none).
        public void show(double[] temperatures, Integer[] chances, String[] hours) {
            this.temperatures = temperatures; this.chances = chances; this.hours = hours;
            lay(); invalidate();
        }
        public int points() { return temperatures.length; }

        @Override protected void onSizeChanged(int w, int h, int oldw, int oldh) { lay(); }

        private float top() { return ui.fpx(18); }
        private float bottom() { return getHeight() - ui.fpx(18); }
        private void lay() {
            int n = temperatures.length;
            xs = new float[n]; ys = new float[n]; path.reset();
            if (n < 2 || getWidth() == 0) return;
            float pad = ui.fpx(14), step = (getWidth() - pad * 2) / (n - 1), chart = bottom() - top();
            double[] levels = Weather.curveLevels(temperatures);
            high = 0; low = 0;
            for (int i = 0; i < n; i++) {
                xs[i] = pad + i * step; ys[i] = top() + (float) (1 - levels[i]) * chart * 0.62f + ui.fpx(4);
                if (i == 0) path.moveTo(xs[i], ys[i]); else path.lineTo(xs[i], ys[i]);
                if (temperatures[i] > temperatures[high]) high = i;
                if (temperatures[i] < temperatures[low]) low = i;
            }
        }

        @Override protected void onDraw(Canvas canvas) {
            int n = xs.length;
            if (n < 2 || getWidth() < ui.fpx(60) || getHeight() < ui.fpx(40)) return;
            float chart = bottom() - top(), step = n > 1 ? xs[1] - xs[0] : 0, w = Math.max(ui.fpx(3), step * 0.5f);
            for (int i = 0; i < n && i < chances.length; i++) {
                if (chances[i] == null || chances[i] < 5) continue;
                float h = chart * 0.5f * Math.min(100, chances[i]) / 100f;
                bar.set(xs[i] - w / 2, bottom() - h, xs[i] + w / 2, bottom());
                canvas.drawRoundRect(bar, ui.fpx(2), ui.fpx(2), rain);
            }
            canvas.drawPath(path, line);
            canvas.drawCircle(xs[0], ys[0], ui.fpx(5), dot);
            canvas.drawCircle(xs[0], ys[0], ui.fpx(5), keyline);
            drawValue(canvas, high, ys[high] - ui.fpx(6));
            if (low != high) drawValue(canvas, low, ys[low] + ui.fpx(5) + ui.fpx(13));
            for (int i = 0; i < n && i < hours.length; i++) {
                if (hours[i] == null || hours[i].isEmpty()) continue;
                Paint paint = i == 0 ? now : label;
                float width = paint.measureText(hours[i]);
                float x = Math.max(width / 2, Math.min(getWidth() - width / 2, xs[i]));
                canvas.drawText(hours[i], x, getHeight() - ui.fpx(3), paint);
            }
        }
        private void drawValue(Canvas canvas, int i, float y) {
            String text = Math.round(temperatures[i]) + "°";
            float width = value.measureText(text), x = Math.max(width / 2, Math.min(getWidth() - width / 2, xs[i]));
            canvas.drawText(text, x, Math.max(ui.fpx(13), Math.min(bottom(), y)), value);
        }
    }

    // The day as an arc from sunrise (left) to sunset (right): a dashed track, the part of the day gone in the Lemon
    // ink and the sun as a disc with a keyline; before sunrise or after sunset, a hollow dot at that end.
    public static final class SunArc extends View {
        private final Ui ui;
        private final Paint track = new Paint(Paint.ANTI_ALIAS_FLAG), horizon = new Paint(Paint.ANTI_ALIAS_FLAG), lit = new Paint(Paint.ANTI_ALIAS_FLAG),
            sun = new Paint(Paint.ANTI_ALIAS_FLAG), keyline = new Paint(Paint.ANTI_ALIAS_FLAG), hollow = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF oval = new RectF();
        private double part = Double.NaN;
        private float cx, cy, r;

        public SunArc(Ui ui) {
            super(ui.context);
            this.ui = ui;
            track.setStyle(Paint.Style.STROKE); track.setStrokeWidth(ui.fpx(2)); track.setColor(ui.theme.muted); track.setPathEffect(new DashPathEffect(new float[] { ui.fpx(2), ui.fpx(2) }, 0));
            horizon.setStyle(Paint.Style.STROKE); horizon.setStrokeWidth(ui.fpx(1.5)); horizon.setColor(ui.theme.muted);
            lit.setStyle(Paint.Style.STROKE); lit.setStrokeWidth(ui.fpx(3)); lit.setStrokeCap(Paint.Cap.ROUND); lit.setColor(Theme.ink("Lemon"));
            sun.setColor(Theme.ink("Lemon"));
            keyline.setStyle(Paint.Style.STROKE); keyline.setStrokeWidth(ui.fpx(2)); keyline.setColor(ui.theme.text);
            hollow.setStyle(Paint.Style.STROKE); hollow.setStrokeWidth(ui.fpx(2)); hollow.setColor(ui.theme.muted);
        }

        // Where the sun is (Weather.sunPart), or NaN without sunrise and sunset.
        public void setPart(double value) { if (Double.compare(value, part) != 0) { part = value; invalidate(); } }
        public double part() { return part; }

        @Override protected void onSizeChanged(int w, int h, int oldw, int oldh) {
            r = Math.min(w / 2f - ui.fpx(9), h - ui.fpx(12)); cx = w / 2f; cy = h - ui.fpx(4);
            oval.set(cx - r, cy - r, cx + r, cy + r);
        }

        @Override protected void onDraw(Canvas canvas) {
            if (Double.isNaN(part) || r < ui.fpx(12)) return;
            canvas.drawLine(cx - r - ui.fpx(8), cy, cx + r + ui.fpx(8), cy, horizon);
            canvas.drawArc(oval, 180, 180, false, track);
            boolean up = part >= 0 && part <= 1;
            float p = (float) Math.max(0, Math.min(1, part));
            if (up && p > 0.01f) canvas.drawArc(oval, 180, 180 * p, false, lit);
            float x = cx - r * (float) Math.cos(Math.PI * p), y = cy - r * (float) Math.sin(Math.PI * p);
            if (up) { canvas.drawCircle(x, y, ui.fpx(7), sun); canvas.drawCircle(x, y, ui.fpx(7), keyline); }
            else canvas.drawCircle(x, y, ui.fpx(6), hollow);
        }
    }

    // A storage jar: a lidded jar with a keyline and a solid lift, filled from the bottom by the used space in the ink;
    // a drive under the low-space threshold fills with Amber caution tape instead.
    public static final class StorageJar extends View {
        private final Ui ui;
        private final Paint glass = new Paint(Paint.ANTI_ALIAS_FLAG), fill = new Paint(Paint.ANTI_ALIAS_FLAG), tape = new Paint(Paint.ANTI_ALIAS_FLAG),
            lift = new Paint(Paint.ANTI_ALIAS_FLAG), keyline = new Paint(Paint.ANTI_ALIAS_FLAG), lid = new Paint(Paint.ANTI_ALIAS_FLAG), lidLine = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF body = new RectF(), shadow = new RectF(), cap = new RectF();
        private final Path clip = new Path();
        private float radius, key;
        private double used;
        private boolean low;

        public StorageJar(Ui ui, int inkColour) {
            super(ui.context);
            this.ui = ui;
            glass.setColor(ui.theme.track); fill.setColor(inkColour); tape.setShader(Stickers.caution(ui)); lift.setColor(ui.theme.page);
            keyline.setStyle(Paint.Style.STROKE); keyline.setColor(ui.theme.text);
            lid.setColor(inkColour); lidLine.setStyle(Paint.Style.STROKE); lidLine.setColor(ui.theme.text);
        }

        public static double widthFor(double height) { return CardSizing.jarWidth(height); }

        public void set(double usedPercent, boolean low) { if (usedPercent != used || low != this.low) { used = usedPercent; this.low = low; invalidate(); } }
        public double used() { return used; }
        public boolean low() { return low; }

        @Override protected void onSizeChanged(int w, int h, int oldw, int oldh) {
            boolean small = w < ui.fpx(40);
            key = ui.fpx(small ? 2 : Stickers.KEYLINE); float liftBy = ui.fpx(small ? 2 : Stickers.LIFT), lidHeight = Math.max(ui.fpx(3), Math.round(h * 0.09f));
            keyline.setStrokeWidth(key); lidLine.setStrokeWidth(Math.max(ui.fpx(1.5), key - ui.fpx(1)));
            body.set(key / 2, lidHeight + key, w - key / 2, h - liftBy - key);
            shadow.set(body.left, body.top + liftBy, body.right, body.bottom + liftBy);
            radius = Math.min(body.width() * 0.24f, ui.fpx(16));
            clip.reset(); clip.addRoundRect(body, radius, radius, Path.Direction.CW);
            cap.set(w * 0.14f, key / 2, w * 0.86f, key / 2 + lidHeight);
        }

        @Override protected void onDraw(Canvas canvas) {
            if (getWidth() < ui.fpx(12) || getHeight() < ui.fpx(16)) return;
            canvas.drawRoundRect(shadow, radius, radius, lift);
            canvas.drawRoundRect(body, radius, radius, glass);
            float level = (float) CardSizing.jarLevel(used);
            if (level > 0) {
                canvas.save(); canvas.clipPath(clip);
                canvas.drawRect(body.left, body.bottom - body.height() * level, body.right, body.bottom, low ? tape : fill);
                canvas.restore();
            }
            canvas.drawRoundRect(body, radius, radius, keyline);
            float r = Math.min(ui.fpx(4), cap.height() / 2);
            canvas.drawRoundRect(cap, r, r, lid); canvas.drawRoundRect(cap, r, r, lidLine);
        }
    }

    // An analog clock face (World clocks): a disc in the city's ink (Lemon by day, Lavender in the evening) with a
    // keyline and a solid lift, twelve hour marks and two hands in dark text.
    public static final class ClockFace extends View {
        private final Ui ui;
        private final Paint face = new Paint(Paint.ANTI_ALIAS_FLAG), lift = new Paint(Paint.ANTI_ALIAS_FLAG), keyline = new Paint(Paint.ANTI_ALIAS_FLAG),
            marks = new Paint(Paint.ANTI_ALIAS_FLAG), hour = new Paint(Paint.ANTI_ALIAS_FLAG), minute = new Paint(Paint.ANTI_ALIAS_FLAG), centre = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Path ticks = new Path();
        private float cx, cy, r, size;
        private java.time.LocalDateTime time;

        public ClockFace(Ui ui) {
            super(ui.context);
            this.ui = ui;
            lift.setColor(ui.theme.page); keyline.setStyle(Paint.Style.STROKE); keyline.setColor(ui.theme.text);
            for (Paint p : new Paint[] { marks, hour, minute }) { p.setStyle(Paint.Style.STROKE); p.setStrokeCap(Paint.Cap.ROUND); p.setColor(Theme.INK_TEXT); }
            centre.setColor(Theme.INK_TEXT);
        }
        public void show(java.time.LocalDateTime time) {
            if (time.equals(this.time)) return;
            this.time = time; face.setColor(Theme.ink(com.alialyami.widgetscreen.core.WorldClockText.ink(time)));
            invalidate();
        }
        public java.time.LocalDateTime time() { return time; }

        @Override protected void onSizeChanged(int w, int h, int oldw, int oldh) {
            size = (float) Math.floor(Math.min(w, h - ui.fpx(Stickers.LIFT)));
            float key = size < ui.fpx(48) ? ui.fpx(2) : ui.fpx(Stickers.KEYLINE);
            keyline.setStrokeWidth(key);
            r = size / 2 - key / 2; cx = w / 2f; cy = size / 2;
            marks.setStrokeWidth(Math.max(ui.fpx(1.5), size / 36)); hour.setStrokeWidth(Math.max(ui.fpx(2.5), size / 18)); minute.setStrokeWidth(Math.max(ui.fpx(2), size / 26));
            ticks.reset();
            for (int i = 0; i < 12; i++) {
                double a = i * Math.PI / 6; float from = r * (i % 3 == 0 ? 0.72f : 0.8f), to = r * 0.88f;
                ticks.moveTo(cx + from * (float) Math.sin(a), cy - from * (float) Math.cos(a));
                ticks.lineTo(cx + to * (float) Math.sin(a), cy - to * (float) Math.cos(a));
            }
        }

        @Override protected void onDraw(Canvas canvas) {
            if (time == null || size < ui.fpx(20)) return;
            canvas.drawCircle(cx, cy + ui.fpx(Stickers.LIFT), r, lift);
            canvas.drawCircle(cx, cy, r, face); canvas.drawCircle(cx, cy, r, keyline);
            canvas.drawPath(ticks, marks);
            hand(canvas, com.alialyami.widgetscreen.core.WorldClockText.hourAngle(time), r * 0.5f, hour);
            hand(canvas, com.alialyami.widgetscreen.core.WorldClockText.minuteAngle(time), r * 0.74f, minute);
            canvas.drawCircle(cx, cy, Math.max(ui.fpx(2), size / 22), centre);
        }
        private void hand(Canvas canvas, double degrees, float length, Paint paint) {
            double a = Math.toRadians(degrees);
            canvas.drawLine(cx, cy, cx + length * (float) Math.sin(a), cy - length * (float) Math.cos(a), paint);
        }
    }

    // A city's 24 hours as a band (Large World clocks): midnight to midnight, 9 to 5 shaded in the city's ink, and a
    // line at the city's time now.
    public static final class DayBand extends View {
        private final Ui ui;
        private final Paint track = new Paint(Paint.ANTI_ALIAS_FLAG), work = new Paint(Paint.ANTI_ALIAS_FLAG), line = new Paint(Paint.ANTI_ALIAS_FLAG), tick = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF bar = new RectF(), shade = new RectF();
        private double now = -1;

        public DayBand(Ui ui) {
            super(ui.context);
            this.ui = ui;
            track.setColor(ui.theme.track); line.setColor(ui.theme.text); line.setStrokeWidth(ui.fpx(2.5));
            tick.setColor(ui.theme.track); tick.setStrokeWidth(ui.fpx(1));
        }
        public void show(java.time.LocalDateTime time) {
            double next = com.alialyami.widgetscreen.core.WorldClockText.dayFraction(time);
            if (Math.abs(next - now) < 0.0001) return;
            now = next; work.setColor(Theme.ink(com.alialyami.widgetscreen.core.WorldClockText.ink(time))); work.setAlpha(191);
            invalidate();
        }
        public double now() { return now; }

        @Override protected void onSizeChanged(int w, int h, int oldw, int oldh) {
            float height = Math.min(ui.fpx(14), h - ui.fpx(4)), top = (h - height) / 2;
            bar.set(0, top, w, top + height);
            shade.set(w * com.alialyami.widgetscreen.core.WorldClockText.WORK_STARTS / 24f, top, w * com.alialyami.widgetscreen.core.WorldClockText.WORK_ENDS / 24f, top + height);
        }

        @Override protected void onDraw(Canvas canvas) {
            if (now < 0 || getWidth() < ui.fpx(40)) return;
            canvas.drawRoundRect(bar, bar.height() / 2, bar.height() / 2, track);
            canvas.drawRoundRect(shade, ui.fpx(3), ui.fpx(3), work);
            for (int hour : new int[] { 6, 12, 18 }) canvas.drawLine(getWidth() * hour / 24f, bar.top - ui.fpx(2), getWidth() * hour / 24f, bar.top, tick);
            float x = Math.max(ui.fpx(1.5), Math.min(getWidth() - ui.fpx(1.5), (float) (getWidth() * now)));
            canvas.drawLine(x, 0, x, getHeight(), line);
        }
    }
}
