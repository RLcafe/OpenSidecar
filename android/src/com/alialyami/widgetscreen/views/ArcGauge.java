package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RectF;
import android.view.View;

// A gauge: a 240° arc open at the bottom, filled clockwise from the left with a percentage (ArcGauge in the Windows
// CardGraphics.cs; temperatures by default).
public final class ArcGauge extends View {
    private static final float SWEEP = 240, START = 150; // Android angles: 0° is at 3 o'clock, clockwise.
    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF box = new RectF();
    private final float thickness;
    private int fill;
    private final int track;
    private float value;

    public ArcGauge(Context context, int fill, int track, float thickness) {
        super(context);
        this.fill = fill; this.track = track; this.thickness = thickness;
        paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(thickness); paint.setStrokeCap(Paint.Cap.ROUND);
    }

    // 0–100.
    public void setValue(double next) {
        float clamped = (float) Math.max(0, Math.min(100, Double.isNaN(next) ? 0 : next));
        if (clamped != value) { value = clamped; invalidate(); }
    }
    public void setFill(int color) { if (color != fill) { fill = color; invalidate(); } }

    @Override protected void onDraw(Canvas canvas) {
        float size = Math.min(getWidth(), getHeight());
        if (size <= thickness) return;
        float left = (getWidth() - size) / 2 + thickness / 2, top = (getHeight() - size) / 2 + thickness / 2;
        box.set(left, top, left + size - thickness, top + size - thickness);
        paint.setColor(track); canvas.drawArc(box, START, SWEEP, false, paint);
        if (value <= 0.2f) return;
        paint.setColor(fill); canvas.drawArc(box, START, SWEEP * value / 100, false, paint);
    }
}
