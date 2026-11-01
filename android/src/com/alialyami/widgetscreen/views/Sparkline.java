package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Path;
import android.view.View;

import java.util.ArrayList;
import java.util.List;

// The recent history of a reading, newest at the right: a line over a faint area, with hairlines at the floor and (for
// percentages) at 50% (HistoryGraph in the Windows CardGraphics.cs). Percentages use a 0–100 scale; rates (maximum 0)
// scale to the largest value shown, and an optional second series is drawn as a muted line.
public final class Sparkline extends View {
    private final Paint line = new Paint(Paint.ANTI_ALIAS_FLAG), area = new Paint(Paint.ANTI_ALIAS_FLAG), muted = new Paint(Paint.ANTI_ALIAS_FLAG), guide = new Paint();
    private final Path path = new Path();
    private final List<Double> values = new ArrayList<Double>(), second = new ArrayList<Double>();
    private int capacity = 48;
    private double maximum;
    private final float stroke;

    public Sparkline(Context context, int color, float stroke) { this(context, color, 0, 0, stroke, 100); }
    public Sparkline(Context context, int color, int guideColor, int secondColor, float stroke, double maximum) {
        super(context);
        this.stroke = stroke; this.maximum = maximum;
        line.setStyle(Paint.Style.STROKE); line.setStrokeWidth(stroke); line.setStrokeJoin(Paint.Join.ROUND); line.setStrokeCap(Paint.Cap.ROUND);
        muted.setStyle(Paint.Style.STROKE); muted.setStrokeWidth(stroke * 0.75f); muted.setStrokeJoin(Paint.Join.ROUND); muted.setColor(secondColor);
        area.setStyle(Paint.Style.FILL);
        guide.setColor(guideColor);
        setColor(color);
    }

    // The frame rate's scale follows the highest reading (60, 120, 144…).
    public void setMaximum(double next) { if (next != maximum) { maximum = next; invalidate(); } }
    public void setColor(int color) { line.setColor(color); area.setColor((Math.round(0.16f * 255) << 24) | (color & 0x00FFFFFF)); invalidate(); }

    public void setValues(List<Double> next, int capacity) { setValues(next, null, capacity); }
    public void setValues(List<Double> next, List<Double> other, int capacity) {
        values.clear(); values.addAll(next); second.clear(); if (other != null) second.addAll(other);
        this.capacity = Math.max(2, capacity);
        invalidate();
    }

    @Override protected void onDraw(Canvas canvas) {
        float width = getWidth(), height = getHeight();
        if (width <= 0 || height <= 4) return;
        if (guide.getColor() != 0) {
            float hair = Math.max(1, stroke / 2);
            canvas.drawRect(0, height - hair, width, height, guide);
            if (maximum > 0) canvas.drawRect(0, Math.round(height / 2), width, Math.round(height / 2) + hair, guide);
        }
        double top = maximum;
        if (top <= 0) { top = 1; for (double v : values) top = Math.max(top, v); for (double v : second) top = Math.max(top, v); top *= 1.15; }
        if (second.size() >= 2 && muted.getColor() != 0) { trace(second, width, height, top, false); canvas.drawPath(path, muted); }
        if (values.size() < 2) return;
        if (guide.getColor() != 0) { trace(values, width, height, top, true); canvas.drawPath(path, area); }
        trace(values, width, height, top, false); canvas.drawPath(path, line);
    }

    private void trace(List<Double> series, float width, float height, double top, boolean closed) {
        path.rewind();
        float pad = stroke;
        for (int i = 0; i < series.size(); i++) {
            float x = width * (capacity - series.size() + i) / (capacity - 1f);
            float y = pad + (height - 2 * pad) * (1 - (float) (Math.max(0, Math.min(top, series.get(i))) / top));
            if (i == 0) { if (closed) { path.moveTo(x, height); path.lineTo(x, y); } else path.moveTo(x, y); }
            else path.lineTo(x, y);
        }
        if (closed) { path.lineTo(width, height); path.close(); }
    }
}
