package com.alialyami.widgetscreen;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RectF;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.widget.FrameLayout;

import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.core.WidgetLayout;
import com.alialyami.widgetscreen.core.WidgetLayout.Box;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

// The free widget canvas. Placements are in design units on a canvas shaped like the screen (DashboardFit), which
// ScaledHost scales to pixels. In Edit layout each card gets a chrome on top: an outline and four 48 × 48 corner
// grips, and the whole card moves when dragged elsewhere.
public final class WidgetCanvas extends ViewGroup {
    private final LayoutEditor editor;
    private final Ui ui;
    private final Map<String, FrameLayout> hosts = new HashMap<String, FrameLayout>();
    private final Map<String, Chrome> chrome = new HashMap<String, Chrome>();
    private final List<View> order = new ArrayList<View>();
    private float screenScale = 1;
    // A quick sideways swipe on the dashboard switches pages (not while editing; sliders keep their own drags).
    public interface Swipe { void swiped(int direction); }
    private Swipe swipe;
    private float swipeX, swipeY;
    private long swipeAt;
    private boolean swiping;

    public WidgetCanvas(Context context, Ui ui, LayoutEditor editor) {
        super(context);
        this.ui = ui; this.editor = editor;
        setChildrenDrawingOrderEnabled(true);
        setClipChildren(true);
    }

    public void setScreenScale(float screenScale) { this.screenScale = screenScale; }
    public void setSwipe(Swipe swipe) { this.swipe = swipe; }

    @Override public boolean onInterceptTouchEvent(MotionEvent event) {
        if (swipe == null || editor.editing()) return false;
        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                swipeX = event.getX(); swipeY = event.getY(); swipeAt = event.getEventTime(); swiping = false;
                return false;
            case MotionEvent.ACTION_MOVE:
                float dx = event.getX() - swipeX, dy = event.getY() - swipeY;
                if (Math.abs(dx) > 40 * screenScale && Math.abs(dx) > 2 * Math.abs(dy)) { swiping = true; return true; }
                return false;
        }
        return false;
    }

    @Override public boolean onTouchEvent(MotionEvent event) {
        if (!swiping) return false;
        if (event.getActionMasked() == MotionEvent.ACTION_UP) {
            float dx = event.getX() - swipeX;
            swiping = false;
            // At least 160 design units, like the PC, within 0.8 s.
            if (Math.abs(dx) >= 160 * screenScale && event.getEventTime() - swipeAt < 800) swipe.swiped(dx < 0 ? 1 : -1);
        } else if (event.getActionMasked() == MotionEvent.ACTION_CANCEL) swiping = false;
        return true;
    }

    public void setCard(String id, View card) {
        FrameLayout host = hosts.get(id);
        if (host == null) {
            host = new FrameLayout(getContext());
            host.setTag(id);
            hosts.put(id, host);
            addView(host);
        }
        host.removeAllViews();
        if (card != null) host.addView(card, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        Chrome overlay = new Chrome(id);
        chrome.put(id, overlay);
        host.addView(overlay, new FrameLayout.LayoutParams(Ui.MATCH, Ui.MATCH));
        sync();
    }

    public void removeCard(String id) {
        FrameLayout host = hosts.remove(id);
        chrome.remove(id);
        if (host != null) removeView(host);
        sync();
    }

    public boolean hasCard(String id) { return hosts.containsKey(id); }

    // Applies the editor state: positions, visibility, stacking and chrome.
    public void sync() {
        for (Map.Entry<String, FrameLayout> entry : hosts.entrySet()) {
            boolean visible = editor.isVisible(entry.getKey());
            entry.getValue().setVisibility(visible ? VISIBLE : GONE);
            Chrome overlay = chrome.get(entry.getKey());
            if (overlay != null) { overlay.setVisibility(editor.editing() ? VISIBLE : GONE); overlay.invalidate(); }
        }
        requestLayout();
        invalidate();
    }

    @Override protected void onMeasure(int widthSpec, int heightSpec) {
        for (Map.Entry<String, FrameLayout> entry : hosts.entrySet()) {
            if (entry.getValue().getVisibility() == GONE) continue;
            Box b = editor.bounds(entry.getKey());
            entry.getValue().measure(MeasureSpec.makeMeasureSpec((int) Math.round(b.width), MeasureSpec.EXACTLY), MeasureSpec.makeMeasureSpec((int) Math.round(b.height), MeasureSpec.EXACTLY));
        }
        setMeasuredDimension(MeasureSpec.getSize(widthSpec), MeasureSpec.getSize(heightSpec));
    }

    @Override protected void onLayout(boolean changed, int l, int t, int r, int b) {
        for (Map.Entry<String, FrameLayout> entry : hosts.entrySet()) {
            View host = entry.getValue();
            if (host.getVisibility() == GONE) continue;
            Box box = editor.bounds(entry.getKey());
            int left = (int) Math.round(box.x), top = (int) Math.round(box.y);
            host.layout(left, top, left + host.getMeasuredWidth(), top + host.getMeasuredHeight());
        }
        order.clear();
        for (int i = 0; i < getChildCount(); i++) order.add(getChildAt(i));
        // Saved layers decide stacking; while editing the selected widget comes to the front so all four
        // grips stay reachable through overlaps.
        Collections.sort(order, new Comparator<View>() {
            @Override public int compare(View a, View b) { return Integer.compare(rank(a), rank(b)); }
        });
    }

    private int rank(View host) {
        String id = (String) host.getTag();
        if (editor.editing() && id.equals(editor.selected())) return Integer.MAX_VALUE;
        return editor.layer(id);
    }

    @Override protected int getChildDrawingOrder(int count, int i) {
        if (order.size() != count) return i;
        return indexOfChild(order.get(i));
    }

    private final class Chrome extends View {
        private final String id;
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private float downX, downY;
        private boolean tracking;

        Chrome(String id) {
            super(WidgetCanvas.this.getContext());
            this.id = id;
            setContentDescription("Move or resize " + WidgetLayout.title(id));
            setClickable(true);
        }

        private String corner(float x, float y) {
            float hit = ui.fpx(48);
            boolean west = x < hit, east = x > getWidth() - hit, north = y < hit, south = y > getHeight() - hit;
            if (north && west) return "NW";
            if (north && east) return "NE";
            if (south && west) return "SW";
            if (south && east) return "SE";
            return "";
        }

        @Override protected void onDraw(Canvas canvas) {
            boolean selected = id.equals(editor.selected());
            float stroke = ui.fpx(selected ? 2 : 1), radius = ui.fpx(ui.radius);
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(stroke);
            paint.setColor(selected ? ui.theme.accent : ui.theme.muted);
            canvas.drawRoundRect(new RectF(stroke / 2, stroke / 2, getWidth() - stroke / 2, getHeight() - stroke / 2), radius, radius, paint);
            // While a finger drags this card, its size and place read at the top of the card, away from the finger
            // (the status line is under it).
            if (selected && tracking) {
                Box b = editor.bounds(id);
                String readout = Math.round(b.width) + " × " + Math.round(b.height) + "  ·  X " + Math.round(b.x) + ", Y " + Math.round(b.y);
                paint.setStyle(Paint.Style.FILL); paint.setTextSize(ui.fpx(15)); paint.setTypeface(Ui.MEDIUM);
                float textWidth = paint.measureText(readout), padX = ui.fpx(12), boxHeight = ui.fpx(30);
                float left = Math.max(ui.fpx(56), (getWidth() - textWidth) / 2 - padX), top = ui.fpx(8);
                paint.setColor(ui.theme.accent);
                canvas.drawRoundRect(new RectF(left, top, left + textWidth + padX * 2, top + boxHeight), ui.fpx(8), ui.fpx(8), paint);
                paint.setColor(Theme.ON_ACCENT);
                canvas.drawText(readout, left + padX, top + boxHeight / 2 + ui.fpx(5), paint);
                paint.setTypeface(null);
            }
            float hit = ui.fpx(48), grip = ui.fpx(16), r = ui.fpx(4);
            for (int i = 0; i < 4; i++) {
                float cx = i % 2 == 0 ? hit / 2 : getWidth() - hit / 2, cy = i < 2 ? hit / 2 : getHeight() - hit / 2;
                RectF box = new RectF(cx - grip / 2, cy - grip / 2, cx + grip / 2, cy + grip / 2);
                paint.setStyle(Paint.Style.FILL); paint.setColor(ui.theme.accent);
                canvas.drawRoundRect(box, r, r, paint);
                paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(ui.fpx(2)); paint.setColor(ui.theme.page);
                canvas.drawRoundRect(box, r, r, paint);
            }
        }

        @Override public boolean onTouchEvent(MotionEvent event) {
            if (!editor.editing()) return false;
            switch (event.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    if (editor.gestureActive()) return true;
                    if (getParent() != null) getParent().requestDisallowInterceptTouchEvent(true);
                    downX = event.getRawX(); downY = event.getRawY(); tracking = true;
                    editor.startGesture(id, corner(event.getX(), event.getY()));
                    return true;
                case MotionEvent.ACTION_MOVE:
                    if (tracking) { editor.continueGesture((event.getRawX() - downX) / screenScale, (event.getRawY() - downY) / screenScale); invalidate(); }
                    return true;
                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_CANCEL:
                    if (tracking) {
                        if (event.getActionMasked() == MotionEvent.ACTION_UP) editor.continueGesture((event.getRawX() - downX) / screenScale, (event.getRawY() - downY) / screenScale);
                        tracking = false;
                        editor.endGesture();
                        invalidate();
                    }
                    return true;
            }
            return true;
        }
    }
}
