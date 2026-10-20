package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.graphics.Canvas;
import android.view.View;

public final class GlyphView extends View {
    private String glyph;
    private int color;
    private float size;

    public GlyphView(Context context, String glyph, int color, float size) {
        super(context);
        this.glyph = glyph; this.color = color; this.size = size;
    }

    public void setGlyph(String glyph) { if (glyph != null && !glyph.equals(this.glyph)) { this.glyph = glyph; invalidate(); } }
    public void setColor(int color) { if (color != this.color) { this.color = color; invalidate(); } }
    public String glyph() { return glyph; }

    @Override protected void onMeasure(int widthSpec, int heightSpec) {
        int natural = Math.round(size) + getPaddingLeft() + getPaddingRight();
        setMeasuredDimension(resolveSize(Math.max(natural, getSuggestedMinimumWidth()), widthSpec), resolveSize(Math.max(natural, getSuggestedMinimumHeight()), heightSpec));
    }

    @Override protected void onDraw(Canvas canvas) {
        float available = Math.min(getWidth(), getHeight());
        Glyphs.draw(canvas, glyph, getWidth() / 2f, getHeight() / 2f, Math.min(size, available), color);
    }
}
