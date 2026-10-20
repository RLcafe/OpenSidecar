package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.view.MotionEvent;
import android.view.View;

import com.alialyami.widgetscreen.Ui;

// The Windows slider after the touchscreen polish: a 6 unit track, a 30 unit accent thumb in a 48 × 48
// touch area, tap to jump, and it takes the finger directly so a scrolling list doesn't steal the drag.
public final class TouchSlider extends View {
    public interface Listener { void changed(TouchSlider slider, float value, boolean done); }

    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final float hit, thumb, border, trackHeight;
    private final int accent, track, surface;
    private float min, max = 100, step, value;
    private boolean dragging;
    private Listener listener;

    public TouchSlider(Context context, Ui ui) {
        super(context);
        hit = ui.fpx(48); thumb = ui.fpx(30); border = ui.fpx(3); trackHeight = ui.fpx(6);
        accent = ui.theme.accent; track = ui.theme.track; surface = ui.theme.surface;
        setFocusable(true);
    }

    public TouchSlider range(float min, float max, float step) { this.min = min; this.max = Math.max(min + 0.0001f, max); this.step = step; return this; }
    public void setListener(Listener listener) { this.listener = listener; }
    public float value() { return value; }
    public boolean dragging() { return dragging; }

    // Readings from polling never fight a finger that is still on the slider.
    public void setValue(float next) {
        if (dragging) return;
        next = clamp(next);
        if (next != value) { value = next; invalidate(); }
    }

    private float clamp(float v) {
        v = Math.max(min, Math.min(max, v));
        if (step > 0) v = min + Math.round((v - min) / step) * step;
        return Math.max(min, Math.min(max, v));
    }

    @Override protected void onMeasure(int widthSpec, int heightSpec) {
        setMeasuredDimension(resolveSize(Math.round(hit * 3), widthSpec), resolveSize(Math.round(hit), heightSpec));
    }

    private float left() { return hit / 2; }
    private float right() { return getWidth() - hit / 2; }

    @Override protected void onDraw(Canvas canvas) {
        float y = getHeight() / 2f, x = left() + (right() - left()) * (value - min) / (max - min);
        paint.setColor(track);
        canvas.drawRoundRect(left(), y - trackHeight / 2, right(), y + trackHeight / 2, trackHeight / 2, trackHeight / 2, paint);
        paint.setColor(accent);
        canvas.drawRoundRect(left(), y - trackHeight / 2, x, y + trackHeight / 2, trackHeight / 2, trackHeight / 2, paint);
        paint.setColor(surface); canvas.drawCircle(x, y, thumb / 2, paint);
        paint.setColor(accent); canvas.drawCircle(x, y, thumb / 2 - border, paint);
    }

    private void moveTo(float x, boolean done) {
        float span = Math.max(1, right() - left());
        float next = clamp(min + (x - left()) / span * (max - min));
        boolean changed = next != value;
        value = next;
        if (changed) invalidate();
        if (listener != null && (changed || done)) listener.changed(this, value, done);
    }

    @Override public boolean onTouchEvent(MotionEvent event) {
        if (!isEnabled()) return false;
        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                if (getParent() != null) getParent().requestDisallowInterceptTouchEvent(true);
                dragging = true; setPressed(true);
                moveTo(event.getX(), false);
                return true;
            case MotionEvent.ACTION_MOVE:
                if (dragging) moveTo(event.getX(), false);
                return true;
            case MotionEvent.ACTION_UP:
                if (dragging) { dragging = false; setPressed(false); moveTo(event.getX(), true); }
                return true;
            case MotionEvent.ACTION_CANCEL:
                if (dragging) { dragging = false; setPressed(false); if (listener != null) listener.changed(this, value, true); }
                return true;
        }
        return super.onTouchEvent(event);
    }
}
