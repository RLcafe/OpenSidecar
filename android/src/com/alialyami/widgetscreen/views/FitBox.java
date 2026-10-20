package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.view.View;
import android.view.ViewGroup;

// A Viewbox for one child: the child keeps its natural size and is scaled uniformly to fit, centered.
// System stats use it so readings grow with their widget.
public final class FitBox extends ViewGroup {
    private final View child;

    public FitBox(Context context, View child) { super(context); this.child = child; addView(child); }

    @Override protected void onMeasure(int widthSpec, int heightSpec) {
        child.measure(MeasureSpec.makeMeasureSpec(0, MeasureSpec.UNSPECIFIED), MeasureSpec.makeMeasureSpec(0, MeasureSpec.UNSPECIFIED));
        setMeasuredDimension(MeasureSpec.getSize(widthSpec), MeasureSpec.getSize(heightSpec));
    }

    @Override protected void onLayout(boolean changed, int l, int t, int r, int b) {
        int width = r - l, height = b - t, cw = Math.max(1, child.getMeasuredWidth()), ch = Math.max(1, child.getMeasuredHeight());
        float scale = Math.max(0, Math.min(width / (float) cw, height / (float) ch));
        child.layout(0, 0, cw, ch);
        child.setPivotX(0); child.setPivotY(0);
        child.setScaleX(scale); child.setScaleY(scale);
        child.setTranslationX((width - cw * scale) / 2); child.setTranslationY((height - ch * scale) / 2);
    }
}
