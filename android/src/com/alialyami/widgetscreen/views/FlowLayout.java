package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.view.View;
import android.view.ViewGroup;

// Lays children left to right and wraps to a new line when a row is full (choice chips in Settings).
public final class FlowLayout extends ViewGroup {
    private final int gap;

    public FlowLayout(Context context, int gap) { super(context); this.gap = gap; }

    @Override protected void onMeasure(int widthSpec, int heightSpec) {
        int width = MeasureSpec.getSize(widthSpec);
        int x = 0, y = 0, line = 0;
        for (int i = 0; i < getChildCount(); i++) {
            View child = getChildAt(i);
            if (child.getVisibility() == GONE) continue;
            child.measure(MeasureSpec.makeMeasureSpec(width, MeasureSpec.AT_MOST), MeasureSpec.makeMeasureSpec(0, MeasureSpec.UNSPECIFIED));
            if (x > 0 && x + child.getMeasuredWidth() > width) { x = 0; y += line + gap; line = 0; }
            x += child.getMeasuredWidth() + gap;
            line = Math.max(line, child.getMeasuredHeight());
        }
        setMeasuredDimension(width, resolveSize(y + line, heightSpec));
    }

    @Override protected void onLayout(boolean changed, int l, int t, int r, int b) {
        int width = r - l, x = 0, y = 0, line = 0;
        for (int i = 0; i < getChildCount(); i++) {
            View child = getChildAt(i);
            if (child.getVisibility() == GONE) continue;
            if (x > 0 && x + child.getMeasuredWidth() > width) { x = 0; y += line + gap; line = 0; }
            child.layout(x, y, x + child.getMeasuredWidth(), y + child.getMeasuredHeight());
            x += child.getMeasuredWidth() + gap;
            line = Math.max(line, child.getMeasuredHeight());
        }
    }
}
