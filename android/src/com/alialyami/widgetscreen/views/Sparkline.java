package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Path;
import android.view.View;

import java.util.ArrayList;
import java.util.List;

// The CPU history line from the Windows System stats card: the last readings, newest at the right.
public final class Sparkline extends View {
    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Path path = new Path();
    private final List<Double> values = new ArrayList<Double>();
    private int capacity = 48;

    public Sparkline(Context context, int color, float stroke) {
        super(context);
        paint.setColor(color); paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(stroke);
        paint.setStrokeJoin(Paint.Join.ROUND); paint.setStrokeCap(Paint.Cap.ROUND);
    }

    public void setValues(List<Double> next, int capacity) {
        values.clear(); values.addAll(next); this.capacity = Math.max(2, capacity);
        invalidate();
    }

    @Override protected void onDraw(Canvas canvas) {
        if (values.size() < 2) return;
        path.rewind();
        float width = getWidth(), height = getHeight(), pad = paint.getStrokeWidth();
        for (int i = 0; i < values.size(); i++) {
            float x = width * (capacity - values.size() + i) / (capacity - 1f);
            float y = pad + (height - 2 * pad) * (1 - (float) Math.max(0, Math.min(100, values.get(i))) / 100f);
            if (i == 0) path.moveTo(x, y); else path.lineTo(x, y);
        }
        canvas.drawPath(path, paint);
    }
}
