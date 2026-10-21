package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.view.View;
import android.view.ViewGroup;

// The Android counterpart of the Windows Viewbox: lays its child out at a logical size in design units
// and scales it uniformly to the screen. With scrolls set, the child keeps its natural height and the
// host reports the scaled height so a ScrollView can scroll it.
public final class ScaledHost extends ViewGroup {
    private final View child;
    private final float scale;
    private final int logicalWidth, logicalHeight;
    private final boolean scrolls;

    public ScaledHost(Context context, View child, float scale, int logicalWidth, int logicalHeight, boolean scrolls) {
        super(context);
        this.child = child; this.scale = scale; this.logicalWidth = logicalWidth; this.logicalHeight = logicalHeight; this.scrolls = scrolls;
        setClipChildren(true);
        addView(child);
    }

    public float scale() { return scale; }

    @Override protected void onMeasure(int widthSpec, int heightSpec) {
        int width = MeasureSpec.getSize(widthSpec);
        if (scrolls) {
            child.measure(MeasureSpec.makeMeasureSpec(logicalWidth, MeasureSpec.EXACTLY), MeasureSpec.makeMeasureSpec(0, MeasureSpec.UNSPECIFIED));
            setMeasuredDimension(width, (int) Math.ceil(child.getMeasuredHeight() * scale));
        } else {
            child.measure(MeasureSpec.makeMeasureSpec(logicalWidth, MeasureSpec.EXACTLY), MeasureSpec.makeMeasureSpec(logicalHeight, MeasureSpec.EXACTLY));
            setMeasuredDimension(width, MeasureSpec.getSize(heightSpec));
        }
    }

    @Override protected void onLayout(boolean changed, int l, int t, int r, int b) {
        child.layout(0, 0, child.getMeasuredWidth(), child.getMeasuredHeight());
        child.setPivotX(0); child.setPivotY(0);
        child.setScaleX(scale); child.setScaleY(scale);
    }
}
