package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RectF;
import android.view.View;

// A ring that fills clockwise from the top with a percentage (RingGauge in the Windows CardGraphics.cs).
public final class RingGauge extends View {
    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF box = new RectF();
    private final float thickness;
    private int fill;
    private final int track;
    private float value;

    public RingGauge(Context context, int fill, int track, float thickness) {
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
        paint.setColor(track); canvas.drawOval(box, paint);
        if (value <= 0) return;
        paint.setColor(fill);
        if (value >= 99.9f) canvas.drawOval(box, paint); else canvas.drawArc(box, -90, value * 3.6f, false, paint);
    }
}
