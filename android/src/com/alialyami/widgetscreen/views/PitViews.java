package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.DashPathEffect;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.view.View;

import com.alialyami.widgetscreen.core.ClaudePit;
import com.alialyami.widgetscreen.core.PitArt;
import com.alialyami.widgetscreen.core.Theme;

// The Claude card's pit wall instruments on the phone (docs/design/claude-watcher-v2.html; decision 017), the same as
// the Windows PitGraphics.cs: the needle gauges, the pace meter, the week's laps, the burn line, the model mix and the
// 5-hour lanes. Sizes are in the dashboard's units (unit pixels each).
public final class PitViews {
    private PitViews() { }

    private abstract static class Instrument extends View {
        protected final Theme theme; protected final float unit;
        protected final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        Instrument(Context context, Theme theme, float unit) { super(context, null); this.theme = theme; this.unit = unit; setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO); }
        protected static int faded(int color, double opacity) { return PitArt.alpha(color, opacity); }
        // Diagonal hatching inside a rectangle: the projection's "where this pace ends up".
        protected void hatch(Canvas canvas, RectF area, int color, float radius) {
            if (area.width() <= 0 || area.height() <= 0) return;
            canvas.save();
            Path clip = new Path(); clip.addRoundRect(area, radius, radius, Path.Direction.CW); canvas.clipPath(clip);
            paint.setStyle(Paint.Style.FILL); paint.setColor(faded(color, 0.18)); canvas.drawRect(area, paint);
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(2 * unit); paint.setColor(faded(color, 0.55)); paint.setStrokeCap(Paint.Cap.BUTT);
            for (float x = area.left - area.height(); x < area.right; x += 6 * unit) canvas.drawLine(x, area.bottom, x + area.height(), area.top, paint);
            canvas.restore();
            paint.setStyle(Paint.Style.FILL);
        }
    }

    // A half-circle needle gauge in a 140 × 84 box (values 0–1; NaN leaves a part out).
    public static final class Gauge extends Instrument {
        public double value, projection = Double.NaN, pace = Double.NaN, red;
        public String low = "0", high = "100";
        public boolean warn;
        public Gauge(Context context, Theme theme, float unit) { super(context, theme, unit); }
        private static double angle(double f) { return Math.PI + Math.max(0, Math.min(1, f)) * Math.PI; }
        private void arc(Canvas canvas, double from, double to, double r, int color, boolean round, boolean dashed) {
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(10); paint.setColor(color);
            paint.setStrokeCap(round ? Paint.Cap.ROUND : Paint.Cap.BUTT);
            paint.setPathEffect(dashed ? new DashPathEffect(new float[] { 3, 3 }, 0) : null);
            RectF oval = new RectF((float) (70 - r), (float) (74 - r), (float) (70 + r), (float) (74 + r));
            canvas.drawArc(oval, (float) Math.toDegrees(angle(from)), (float) Math.toDegrees(angle(to) - angle(from)), false, paint);
            paint.setPathEffect(null);
        }
        private float px(double a, double r) { return (float) (70 + r * Math.cos(a)); }
        private float py(double a, double r) { return (float) (74 + r * Math.sin(a)); }
        @Override protected void onDraw(Canvas canvas) {
            float k = Math.min(getWidth() / 140f, getHeight() / 84f);
            if (k <= 0) return;
            canvas.save(); canvas.scale(k, k);
            double r = 58;
            arc(canvas, 0, 1, r, theme.track, true, false);
            if (red > 0) arc(canvas, red, 1, r, 0x8CF09A9A, false, false);
            if (!Double.isNaN(projection) && projection > value) arc(canvas, value, Math.min(1, projection), r, faded(Theme.WARNING, 0.55), false, true);
            if (value > 0.002) arc(canvas, 0, value, r, warn ? Theme.WARNING : theme.accent, true, false);
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeCap(Paint.Cap.BUTT); paint.setStrokeWidth(1.5f); paint.setColor(theme.muted);
            for (int i = 0; i <= 10; i++) { double a = angle(i / 10.0), r2 = r - (i % 5 == 0 ? 20 : 16); canvas.drawLine(px(a, r - 12), py(a, r - 12), px(a, r2), py(a, r2), paint); }
            paint.setStrokeCap(Paint.Cap.ROUND); paint.setColor(theme.text);
            if (!Double.isNaN(pace)) { double a = angle(pace); paint.setStrokeWidth(2.5f); canvas.drawLine(px(a, r - 7), py(a, r - 7), px(a, r + 8), py(a, r + 8), paint); }
            double n = angle(value); paint.setStrokeWidth(3); canvas.drawLine(70, 74, px(n, r - 18), py(n, r - 18), paint);
            paint.setStyle(Paint.Style.FILL); canvas.drawCircle(70, 74, 6, paint);
            paint.setTextSize(10); paint.setColor(theme.muted);
            paint.setTextAlign(Paint.Align.LEFT); canvas.drawText(low, 10, 84, paint);
            paint.setTextAlign(Paint.Align.RIGHT); canvas.drawText(high, 130, 84, paint);
            canvas.restore();
        }
    }

    // A flat meter: used, the projection hatched past it, and a pace tick (values 0–1).
    public static final class PaceBar extends Instrument {
        public double value, projection = Double.NaN, pace = Double.NaN;
        public boolean warn;
        public PaceBar(Context context, Theme theme, float unit) { super(context, theme, unit); }
        @Override protected void onDraw(Canvas canvas) {
            float w = getWidth(), h = getHeight() - 6 * unit, top = 3 * unit, radius = h / 2;
            if (w <= 0 || h <= 0) return;
            paint.setStyle(Paint.Style.FILL);
            paint.setColor(theme.track); canvas.drawRoundRect(new RectF(0, top, w, top + h), radius, radius, paint);
            if (!Double.isNaN(projection) && projection > value) hatch(canvas, new RectF(0, top, (float) (w * Math.min(1, projection)), top + h), warn ? Theme.WARNING : theme.accent, radius);
            if (value > 0) { paint.setColor(warn && value >= 0.8 ? Theme.WARNING : theme.accent); canvas.drawRoundRect(new RectF(0, top, Math.max(h, (float) (w * Math.min(1, value))), top + h), radius, radius, paint); }
            if (!Double.isNaN(pace)) { paint.setColor(theme.text); float x = (float) Math.max(0, Math.min(w - 2 * unit, w * pace - unit)); canvas.drawRoundRect(new RectF(x, 0, x + 2 * unit, top * 2 + h), unit, unit, paint); }
        }
    }

    // The week as seven laps with day names, the lap it's on outlined, the projection hatched, and a chequered flag.
    public static final class Laps extends Instrument {
        public double percent, projection = Double.NaN;
        public int now = -1;
        public String[] days = new String[7];
        public Laps(Context context, Theme theme, float unit) { super(context, theme, unit); }
        @Override protected void onDraw(Canvas canvas) {
            float u = unit, w = getWidth(), flag = 22 * u, gap = 3 * u, lap = (w - flag - gap * 7) / 7, bar = 14 * u, r = 4 * u;
            if (lap <= 4 * u) return;
            paint.setTextSize(11 * u); paint.setTextAlign(Paint.Align.CENTER);
            for (int i = 0; i < 7; i++) {
                RectF box = new RectF(i * (lap + gap), 0, i * (lap + gap) + lap, bar);
                paint.setStyle(Paint.Style.FILL); paint.setColor(theme.track); canvas.drawRoundRect(box, r, r, paint);
                double used = ClaudePit.lap(percent, i), proj = Double.isNaN(projection) ? 0 : ClaudePit.lap(projection, i);
                if (proj > used) hatch(canvas, new RectF(box.left, 0, (float) (box.left + lap * proj), bar), theme.accent, r);
                if (used > 0) {
                    canvas.save(); Path clip = new Path(); clip.addRoundRect(box, r, r, Path.Direction.CW); canvas.clipPath(clip);
                    paint.setStyle(Paint.Style.FILL); paint.setColor(theme.accent); canvas.drawRect(box.left, 0, (float) (box.left + lap * used), bar, paint); canvas.restore();
                }
                if (i == now) { paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(u); paint.setColor(theme.text); canvas.drawRoundRect(new RectF(box.left + u / 2, u / 2, box.right - u / 2, bar - u / 2), r, r, paint); }
                paint.setStyle(Paint.Style.FILL); paint.setColor(theme.muted);
                canvas.drawText(days[i] == null ? "" : days[i], box.centerX(), bar + 13 * u, paint);
            }
            float fx = 7 * (lap + gap);
            paint.setColor(theme.text); canvas.drawRoundRect(new RectF(fx, 0, fx + flag, bar), 3 * u, 3 * u, paint);
            paint.setColor(theme.page);
            for (int c = 0; c < 4; c++) canvas.drawRect(fx + c * flag / 4, c % 2 == 0 ? 0 : bar / 2, fx + (c + 1) * flag / 4, c % 2 == 0 ? bar / 2 : bar, paint);
        }
    }

    // The last hour's tokens a minute: a line over a soft fill, the redline dashed, the latest point marked.
    public static final class Burn extends Instrument {
        public double[] values = new double[0];
        public double maximum = 1;
        public Burn(Context context, Theme theme, float unit) { super(context, theme, unit); }
        @Override protected void onDraw(Canvas canvas) {
            float w = getWidth(), h = getHeight();
            if (w <= 0 || h <= 0 || values.length < 2) return;
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(unit); paint.setColor(theme.track); paint.setPathEffect(new DashPathEffect(new float[] { 3 * unit, 4 * unit }, 0));
            float red = (float) (h - h * ClaudePit.REDLINE); canvas.drawLine(0, red, w, red, paint); paint.setPathEffect(null);
            Path line = new Path(), area = new Path();
            area.moveTo(0, h);
            for (int i = 0; i < values.length; i++) {
                float x = i * w / (values.length - 1), y = (float) (h - 2 * unit - (h - 4 * unit) * Math.max(0, Math.min(1, values[i] / Math.max(1, maximum))));
                if (i == 0) line.moveTo(x, y); else line.lineTo(x, y);
                area.lineTo(x, y);
            }
            area.lineTo(w, h); area.close();
            paint.setStyle(Paint.Style.FILL); paint.setColor(faded(theme.accent, 0.16)); canvas.drawPath(area, paint);
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(2 * unit); paint.setStrokeCap(Paint.Cap.ROUND); paint.setColor(theme.accent); canvas.drawPath(line, paint);
            float lastY = (float) (h - 2 * unit - (h - 4 * unit) * Math.max(0, Math.min(1, values[values.length - 1] / Math.max(1, maximum))));
            paint.setStyle(Paint.Style.FILL); canvas.drawCircle(w, lastY, 3.5f * unit, paint);
        }
    }

    // A model mix: Opus, Sonnet and Haiku shares as one bar.
    public static final class Mix extends Instrument {
        public double[] shares = new double[3];
        public Mix(Context context, Theme theme, float unit) { super(context, theme, unit); }
        @Override protected void onDraw(Canvas canvas) {
            float w = getWidth(), h = 6 * unit, top = (getHeight() - h) / 2;
            if (w <= 0) return;
            canvas.save(); Path clip = new Path(); clip.addRoundRect(new RectF(0, top, w, top + h), h / 2, h / 2, Path.Direction.CW); canvas.clipPath(clip);
            paint.setStyle(Paint.Style.FILL); paint.setColor(theme.track); canvas.drawRect(0, top, w, top + h, paint);
            float x = 0;
            for (int i = 0; i < 3; i++) { float part = (float) (w * Math.max(0, shares[i])); if (part > 0) { paint.setColor(PitArt.ACCENTS[i]); canvas.drawRect(x, top, x + part, top + h, paint); } x += part; }
            canvas.restore();
        }
    }

    // One project's lane on the 5-hour timeline: a bar per 10-minute bin, as tall and strong as its share.
    public static final class Lane extends Instrument {
        public double[] bins = new double[ClaudePit.LANE_BINS];
        public double maximum = 1;
        public int color = PitArt.LIME;
        public Lane(Context context, Theme theme, float unit) { super(context, theme, unit); }
        @Override protected void onDraw(Canvas canvas) {
            float w = getWidth(), h = getHeight(), gap = 2 * unit, bar = (w - gap * (bins.length - 1)) / bins.length;
            if (bar <= 0) return;
            paint.setStyle(Paint.Style.FILL);
            for (int i = 0; i < bins.length; i++) {
                double v = maximum <= 0 ? 0 : bins[i] / maximum;
                float height = v > 0 ? (float) Math.max(3 * unit, h * v) : 2 * unit;
                paint.setColor(v > 0 ? faded(color, 0.45 + 0.55 * v) : faded(theme.track, 0.6));
                canvas.drawRoundRect(new RectF(i * (bar + gap), h - height, i * (bar + gap) + bar, h), 2 * unit, 2 * unit, paint);
            }
        }
    }
}
