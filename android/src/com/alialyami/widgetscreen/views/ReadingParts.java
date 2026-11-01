package com.alialyami.widgetscreen.views;

import android.graphics.Canvas;
import android.graphics.DashPathEffect;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.view.View;

import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.core.Weather;

// Drawn parts of the Sticker Sheet's readings (docs/design/sticker-sheet.md, phase 3), the same as the PC's
// ReadingParts.cs: Weather's 12-hour temperature curve over rain bars and its sunrise–sunset arc. Paints are made with
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
}
