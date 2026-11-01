package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RectF;
import android.view.View;

import java.util.ArrayList;
import java.util.List;

// The recent history as bars, newest at the right (HistoryBars in the Windows CardGraphics.cs). Percentages use a 0–100
// scale; rates (maximum 0) scale to the largest shown. A second series (network upload) draws muted beside.
public final class HistoryBars extends View {
    private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG), muted = new Paint(Paint.ANTI_ALIAS_FLAG), guide = new Paint();
    private final RectF bar = new RectF();
    private final List<Double> values = new ArrayList<Double>(), second = new ArrayList<Double>();
    private double maximum;
    private final int capacity = 24;

    public HistoryBars(Context context, int color, int guideColor, int secondColor, double maximum) {
        super(context);
        this.maximum = maximum;
        fill.setColor(color); muted.setColor(secondColor); guide.setColor(guideColor);
    }
    public void setColor(int color) { fill.setColor(color); invalidate(); }
    public void setMaximum(double next) { if (next != maximum) { maximum = next; invalidate(); } }
    public void setValues(List<Double> next, List<Double> other) {
        values.clear(); values.addAll(next); second.clear(); if (other != null) second.addAll(other);
        invalidate();
    }

    @Override protected void onDraw(Canvas canvas) {
        float width = getWidth(), height = getHeight();
        if (width <= 0 || height <= 4) return;
        float slot = width / capacity, gap = Math.max(1, Math.round(slot * 0.25f)), thick = Math.max(1, slot - gap);
        double top = maximum;
        if (top <= 0) { top = 1; for (double v : values) top = Math.max(top, v); for (double v : second) top = Math.max(top, v); top *= 1.15; }
        if (guide.getColor() != 0) canvas.drawRect(0, height - 1, width, height, guide);
        boolean pair = !second.isEmpty() && muted.getColor() != 0;
        if (pair) draw(canvas, second, muted, width, height, slot, thick, top, thick / 2);
        draw(canvas, values, fill, width, height, slot, thick, top, pair ? thick / 2 : thick);
    }
    private void draw(Canvas canvas, List<Double> series, Paint paint, float width, float height, float slot, float thick, double top, float barWidth) {
        int skip = Math.max(0, series.size() - capacity);
        float radius = Math.min(2, barWidth / 2);
        for (int i = skip; i < series.size(); i++) {
            float x = width - (series.size() - i) * slot, h = Math.max(1, (float) (height * Math.max(0, Math.min(top, series.get(i))) / top));
            bar.set(x + (thick - barWidth), height - h, x + thick, height);
            canvas.drawRoundRect(bar, radius, radius, paint);
        }
    }
}
