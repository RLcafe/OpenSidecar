package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.view.View;

// The Windows ProgressBar style: a Track-colored line with an accent fill, no animation.
public final class ProgressLine extends View {
    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private int fill, track;
    private float value;

    public ProgressLine(Context context, int fill, int track) { super(context); this.fill = fill; this.track = track; }

    public void setValue(double next) {
        float clamped = (float) Math.max(0, Math.min(1, Double.isNaN(next) ? 0 : next));
        if (clamped != value) { value = clamped; invalidate(); }
    }

    public void setFill(int color) { if (color != fill) { fill = color; invalidate(); } }

    @Override protected void onDraw(Canvas canvas) {
        float h = getHeight(), r = h / 2;
        paint.setColor(track); canvas.drawRoundRect(0, 0, getWidth(), h, r, r, paint);
        if (value > 0) { paint.setColor(fill); canvas.drawRoundRect(0, 0, Math.max(h, getWidth() * value), h, r, r, paint); }
    }
}
