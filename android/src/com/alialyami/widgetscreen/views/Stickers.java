package com.alialyami.widgetscreen.views;

import android.graphics.Canvas;
import android.graphics.DashPathEffect;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.text.TextPaint;
import android.text.TextUtils;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.Inks;
import com.alialyami.widgetscreen.core.Theme;

import java.util.Locale;

// The Sticker Sheet parts every card shares (decision 017, docs/design/sticker-sheet.md), the same as the PC's
// Stickers.cs: the label strip, the status sticker, fact-rail chips, the state language and die-cut sticker shapes.
// Nothing animates and nothing uses shadows (no setShadowLayer); a sticker's lift is a solid copy in the Page colour.
// Paints, paths and shaders are made once, never in onDraw.
public final class Stickers {
    private Stickers() { }

    public static final double STRIP_HEIGHT = 28, STICKER_HEIGHT = 24, STICKER_BORDER = 1.5, KEYLINE = 3, LIFT = 4, MAX_TILT = 3;
    public static final Typeface BLACK = Typeface.create("sans-serif-black", Typeface.NORMAL);
    public static final Typeface BOLD = Typeface.create("sans-serif", Typeface.BOLD);

    // Amber caution tape for warnings, 8 units a stripe pair; always with a glyph and words, never colour alone.
    public static Shader caution(Ui ui) {
        float step = ui.fpx(5.66);
        return new LinearGradient(0, 0, step, step, new int[] { Theme.INK_AMBER, Theme.INK_AMBER, Theme.INK_TEXT, Theme.INK_TEXT }, new float[] { 0, 0.5f, 0.5f, 1 }, Shader.TileMode.REPEAT);
    }

    // A fact-rail chip: a muted, letter-spaced caption (13, the caption floor) over a 17-unit medium value.
    public static LinearLayout factChip(Ui ui, String caption, String value) {
        LinearLayout chip = ui.column();
        chip.setBackground(ui.shape(ui.theme.raised, 0, 0, 10));
        chip.setPadding(ui.px(12), ui.px(7), ui.px(12), ui.px(8));
        TextView label = ui.text(caption.toUpperCase(Locale.ROOT), 13, ui.theme.muted);
        label.setTypeface(Ui.MEDIUM); label.setLetterSpacing(0.06f);
        TextView reading = Ui.tabular(ui.text(value, 17, ui.theme.text));
        reading.setTypeface(Ui.MEDIUM);
        chip.addView(label, ui.ll(Ui.WRAP, Ui.WRAP));
        chip.addView(reading, ui.margins(ui.ll(Ui.WRAP, Ui.WRAP), 0, 2, 0, 0));
        chip.setContentDescription(caption + ", " + value);
        return chip;
    }

    // Not set up or unavailable: a dashed outline, no fill, the reason in plain words.
    public static TextView unavailable(Ui ui, String reason) {
        TextView text = Ui.wrap(ui.text(reason, 15, ui.theme.muted), 4);
        text.setGravity(Gravity.CENTER);
        text.setMinHeight(ui.px(48));
        text.setPadding(ui.px(14), ui.px(10), ui.px(14), ui.px(10));
        GradientDrawable outline = new GradientDrawable();
        outline.setCornerRadius(ui.fpx(12));
        outline.setStroke(Math.max(1, ui.px(STICKER_BORDER)), ui.theme.muted, ui.fpx(3), ui.fpx(2));
        text.setBackground(outline);
        return text;
    }

    // A Quick Launch keycap (phase 2), as on the PC: the cap sits on a 4-unit darker skirt and drops 2 units while
    // pressed; an LED dot in the ink lights at the top right for a button that's on. The view keeps 4 more units of
    // bottom padding so its content stays on the cap.
    public static android.graphics.drawable.Drawable keycap(Ui ui, boolean on, int ledColour) {
        android.graphics.drawable.StateListDrawable states = new android.graphics.drawable.StateListDrawable();
        states.addState(new int[] { android.R.attr.state_pressed }, keycapLayers(ui, on, ledColour, true));
        states.addState(new int[0], keycapLayers(ui, on, ledColour, false));
        return states;
    }
    private static android.graphics.drawable.Drawable keycapLayers(Ui ui, boolean on, int ledColour, boolean pressed) {
        GradientDrawable skirt = ui.shape(ui.theme.input, ui.theme.controlBorder, 1, 12);
        GradientDrawable cap = ui.shape(on ? ui.theme.selected : ui.theme.raised, on ? ui.theme.accent : ui.theme.controlBorder, on ? 2 : 1, 12);
        java.util.List<android.graphics.drawable.Drawable> layers = new java.util.ArrayList<android.graphics.drawable.Drawable>();
        layers.add(skirt); layers.add(cap);
        if (pressed) layers.add(ui.shape(0x21FFFFFF, 0, 0, 12));
        GradientDrawable led = null;
        if (on) { led = new GradientDrawable(); led.setShape(GradientDrawable.OVAL); led.setColor(ledColour); led.setStroke(Math.max(1, ui.px(1)), ui.theme.page); layers.add(led); }
        android.graphics.drawable.LayerDrawable drawable = new android.graphics.drawable.LayerDrawable(layers.toArray(new android.graphics.drawable.Drawable[0]));
        int top = pressed ? ui.px(2) : 0, bottom = pressed ? ui.px(2) : ui.px(4);
        drawable.setLayerInset(0, 0, ui.px(4), 0, 0);
        for (int i = 1; i < layers.size(); i++) drawable.setLayerInset(i, 0, top, 0, bottom);
        if (led != null) {
            int index = layers.size() - 1;
            drawable.setLayerSize(index, ui.px(8), ui.px(8));
            drawable.setLayerGravity(index, Gravity.TOP | Gravity.END);
            drawable.setLayerInset(index, 0, top + ui.px(6), ui.px(6), 0);
        }
        return drawable;
    }

    // The same, centred in a card's body (it keeps its own height, as on the PC).
    public static View unavailableBody(Ui ui, String reason) {
        android.widget.FrameLayout body = new android.widget.FrameLayout(ui.context);
        body.addView(unavailable(ui, reason), new android.widget.FrameLayout.LayoutParams(android.widget.FrameLayout.LayoutParams.MATCH_PARENT, android.widget.FrameLayout.LayoutParams.WRAP_CONTENT, Gravity.CENTER));
        return body;
    }

    // A Soundboard sampler pad (phase 5), as on the PC: a rubber pad edged in the ink on a 3-unit solid lift, tinted 10%
    // with the ink; it fills with the ink while pressed and while activated (the 150 ms flash after a tap).
    public static android.graphics.drawable.Drawable pad(Ui ui, int ink) {
        android.graphics.drawable.StateListDrawable states = new android.graphics.drawable.StateListDrawable();
        states.addState(new int[] { android.R.attr.state_pressed }, padLayers(ui, ink, true));
        states.addState(new int[] { android.R.attr.state_activated }, padLayers(ui, ink, true));
        states.addState(new int[0], padLayers(ui, ink, false));
        return states;
    }
    private static android.graphics.drawable.Drawable padLayers(Ui ui, int ink, boolean lit) {
        GradientDrawable lift = ui.shape(ui.theme.page, 0, 0, 14);
        GradientDrawable face = ui.shape(lit ? ink : Theme.blend(ui.theme.raised, ink, 0.1), ink, 2, 14);
        android.graphics.drawable.LayerDrawable drawable = new android.graphics.drawable.LayerDrawable(new android.graphics.drawable.Drawable[] { lift, face });
        drawable.setLayerInset(0, 0, ui.px(3), 0, 0);
        drawable.setLayerInset(1, 0, lit ? ui.px(2) : 0, 0, lit ? ui.px(1) : ui.px(3));
        return drawable;
    }
    // Text on a pad: the card's text colour, dark text while the pad is lit.
    public static android.content.res.ColorStateList padText(Ui ui) {
        return new android.content.res.ColorStateList(new int[][] { { android.R.attr.state_pressed }, { android.R.attr.state_activated }, { } }, new int[] { Theme.INK_TEXT, Theme.INK_TEXT, ui.theme.text });
    }

    // Offline or stale: the last values stay, at 60%.
    public static void stale(View values, boolean stale) { values.setAlpha(stale ? 0.6f : 1f); }

    // A hero sticker may tilt by 3° at most, and only when it holds one short number.
    public static void tilt(View sticker, double degrees) { sticker.setRotation((float) Math.max(-MAX_TILT, Math.min(MAX_TILT, degrees))); }

    // Die-cut outlines in design units: build one per size and keep it.
    public static Path rounded(Ui ui, double width, double height, double radius) {
        Path path = new Path(); path.addRoundRect(new RectF(0, 0, ui.fpx(width), ui.fpx(height)), ui.fpx(radius), ui.fpx(radius), Path.Direction.CW); return path;
    }
    public static Path disc(Ui ui, double size) { Path path = new Path(); float r = ui.fpx(size) / 2; path.addCircle(r, r, r, Path.Direction.CW); return path; }
    public static Path burst(Ui ui, double size, int points) {
        Path path = new Path(); float r = ui.fpx(size) / 2, inner = r * 0.86f; int steps = Math.max(6, points) * 2;
        for (int i = 0; i < steps; i++) {
            double angle = i * Math.PI * 2 / steps - Math.PI / 2; float radius = i % 2 == 0 ? r : inner;
            float x = (float) (r + radius * Math.cos(angle)), y = (float) (r + radius * Math.sin(angle));
            if (i == 0) path.moveTo(x, y); else path.lineTo(x, y);
        }
        path.close();
        return path;
    }

    // The label strip that names a card: a 28-unit pill in the widget's ink with the name in sans-serif-black 13,
    // letter-spaced 0.09 em, in dark text; under 200 units wide, a 12-unit ink dot and the short name.
    public static final class LabelStrip extends View {
        private final Ui ui;
        public final String id;
        private final int ink;
        private final boolean compact;
        private final String words;
        private final TextPaint text = new TextPaint(Paint.ANTI_ALIAS_FLAG);
        private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF pill = new RectF();
        private String shown;
        private float natural;

        public LabelStrip(Ui ui, String id, int ink, double cardWidth) {
            super(ui.context);
            this.ui = ui; this.id = id; this.ink = ink;
            compact = Inks.compact(cardWidth);
            words = compact ? Inks.shortName(id) : Inks.stripName(id);
            text.setTypeface(BLACK); text.setLetterSpacing(0.09f);
            text.setTextSize(ui.fpx(Inks.stripTextSize(ui.textScale)));
            text.setColor(compact ? ui.theme.text : Theme.INK_TEXT);
            fill.setColor(ink);
            natural = text.measureText(words) + (compact ? ui.fpx(20) : ui.fpx(24));
            shown = words;
            setContentDescription(com.alialyami.widgetscreen.core.WidgetLayout.title(id));
        }

        public boolean compact() { return compact; }
        public String words() { return words; }
        public double naturalUnits() { return natural / ui.unit; }
        public int ink() { return ink; }

        @Override protected void onMeasure(int widthSpec, int heightSpec) {
            int width = resolveSize((int) Math.ceil(natural), widthSpec);
            setMeasuredDimension(width, resolveSize(ui.px(STRIP_HEIGHT), heightSpec));
        }

        @Override protected void onSizeChanged(int w, int h, int oldw, int oldh) {
            float room = w - (compact ? ui.fpx(20) : ui.fpx(24));
            shown = TextUtils.ellipsize(words, text, Math.max(0, room), TextUtils.TruncateAt.END).toString();
            // A strip laid out wider than its words keeps its pill to the words.
            float height = ui.fpx(STRIP_HEIGHT), top = (h - height) / 2;
            pill.set(0, top, compact ? 0 : Math.min(w, natural), top + height);
        }

        @Override protected void onDraw(Canvas canvas) {
            float middle = getHeight() / 2f, baseline = middle - (text.descent() + text.ascent()) / 2;
            if (compact) {
                canvas.drawCircle(ui.fpx(6), middle, ui.fpx(6), fill);
                canvas.drawText(shown, ui.fpx(20), baseline, text);
            } else {
                canvas.drawRoundRect(pill, pill.height() / 2, pill.height() / 2, fill);
                canvas.drawText(shown, ui.fpx(12), baseline, text);
            }
        }
    }

    // The status sticker at a card's top right: one short live fact in an outlined pill (ink border and text, 1.5-unit
    // border, 13 bold), or filled for an active state; Amber with caution stripes and a glyph for a warning; a dashed
    // outline for unavailable; a stamp for offline or stale; a static outline while loading. It hides while empty.
    // The card sets its words as before; the sticker picks its own colours, so setTextColor from a card is ignored.
    public static final class Sticker extends TextView {
        private Ui ui;
        private int ink;
        public static final double MIN_ROOM = 40, MIN_WARNING_ROOM = 64, MIN_GLYPH_ROOM = 62;
        private double room = Double.MAX_VALUE;
        private boolean warns, filled, cardShown = true, roomy = true;
        private int state = Inks.FACT;
        private final Paint stroke = new Paint(Paint.ANTI_ALIAS_FLAG), solid = new Paint(Paint.ANTI_ALIAS_FLAG), tape = new Paint(Paint.ANTI_ALIAS_FLAG), mark = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final RectF pill = new RectF();
        private final Path tab = new Path(), warning = new Path();
        private DashPathEffect dashes;

        public Sticker(Ui ui, int ink, boolean warns) {
            super(ui.context);
            this.ui = ui; this.ink = ink; this.warns = warns;
            setTextSize(TypedValue.COMPLEX_UNIT_PX, ui.fpx(13));
            setTypeface(BOLD); setIncludeFontPadding(false);
            setSingleLine(true); setEllipsize(TextUtils.TruncateAt.END);
            setGravity(Gravity.CENTER_VERTICAL);
            setFontFeatureSettings("tnum");
            setMinHeight(ui.px(STICKER_HEIGHT));
            stroke.setStyle(Paint.Style.STROKE); stroke.setStrokeWidth(ui.fpx(STICKER_BORDER));
            solid.setStyle(Paint.Style.FILL);
            tape.setShader(caution(ui));
            mark.setStyle(Paint.Style.STROKE); mark.setStrokeWidth(ui.fpx(1.6)); mark.setStrokeCap(Paint.Cap.ROUND); mark.setStrokeJoin(Paint.Join.ROUND); mark.setColor(Theme.INK_TEXT);
            dashes = new DashPathEffect(new float[] { ui.fpx(3), ui.fpx(2) }, 0);
            refresh();
        }

        public int state() { return state; }
        public boolean filled() { return filled; }
        public void setFilled(boolean on) { if (on != filled) { filled = on; refresh(); } }
        public void setWarns(boolean on) { if (on != warns) { warns = on; refresh(); } }
        // A glyph before a fact's words (the mixer's microphone in ON AIR), drawn in the words' colour; null for none.
        private String glyphName;
        public void setGlyph(String name) { if (name == null ? glyphName != null : !name.equals(glyphName)) { glyphName = name; refresh(); } }
        // The width the header leaves it, in design units: the words trim to it, and under 40 the sticker steps aside.
        public void setRoom(double units) { room = units; setMaxWidth(Math.max(0, ui.px(units))); refresh(); }
        public boolean roomy() { return roomy; }

        @Override public void setText(CharSequence words, BufferType type) { super.setText(words, type); refresh(); }
        // Cards set their caption's colour; the sticker owns its colours.
        @Override public void setTextColor(int color) { }
        @Override public void setVisibility(int visibility) { cardShown = visibility == VISIBLE; refresh(); }

        private void refresh() {
            if (ui == null) return;
            CharSequence words = getText();
            state = Inks.stateOf(words == null ? "" : words.toString(), warns);
            boolean warning = state == Inks.WARNING;
            // A warning also carries its stripes and glyph.
            roomy = room >= (warning ? MIN_WARNING_ROOM : state == Inks.FACT && glyphName != null ? MIN_GLYPH_ROOM : MIN_ROOM);
            boolean glyphShown = state == Inks.FACT && glyphName != null;
            int left = warning ? ui.px(14 + 6 + 12 + 6) : glyphShown ? ui.px(10 + 12 + 6) : ui.px(10);
            setPadding(left, 0, ui.px(10), 0);
            int color = state == Inks.WARNING ? Theme.INK_TEXT : state == Inks.UNAVAILABLE || state == Inks.LOADING ? ui.theme.muted : state == Inks.OFFLINE ? ui.theme.text : filled ? Theme.INK_TEXT : ink;
            super.setTextColor(color);
            stroke.setColor(warning ? Theme.INK_AMBER : state == Inks.FACT ? ink : ui.theme.muted);
            stroke.setStrokeWidth(ui.fpx(state == Inks.OFFLINE ? 2 : STICKER_BORDER));
            stroke.setPathEffect(state == Inks.UNAVAILABLE ? dashes : null);
            solid.setColor(warning ? Theme.INK_AMBER : ink);
            super.setVisibility(cardShown && roomy && words != null && words.length() > 0 ? VISIBLE : GONE);
            invalidate();
        }

        @Override protected void onSizeChanged(int w, int h, int oldw, int oldh) {
            super.onSizeChanged(w, h, oldw, oldh);
            float inset = ui.fpx(STICKER_BORDER) / 2, height = Math.min(h, ui.fpx(STICKER_HEIGHT)), top = (h - height) / 2;
            pill.set(inset, top + inset, w - inset, top + height - inset);
            float radius = height / 2;
            tab.reset(); tab.addRoundRect(new RectF(0, top, w, top + height), radius, radius, Path.Direction.CW);
            // The warning glyph: a triangle with a mark, 12 units, after the stripes.
            float x = ui.fpx(14 + 6), s = ui.fpx(12), cy = top + height / 2;
            warning.reset();
            warning.moveTo(x + s / 2, cy - s * 0.45f); warning.lineTo(x + s, cy + s * 0.45f); warning.lineTo(x, cy + s * 0.45f); warning.close();
            warning.moveTo(x + s / 2, cy - s * 0.1f); warning.lineTo(x + s / 2, cy + s * 0.15f);
            warning.moveTo(x + s / 2, cy + s * 0.32f); warning.lineTo(x + s / 2, cy + s * 0.33f);
        }

        @Override protected void onDraw(Canvas canvas) {
            float radius = pill.height() / 2;
            if (state == Inks.WARNING) {
                canvas.drawRoundRect(pill, radius, radius, solid);
                canvas.save(); canvas.clipPath(tab);
                canvas.drawRect(0, 0, ui.fpx(14), getHeight(), tape);
                canvas.restore();
                canvas.drawPath(warning, mark);
            } else if (state == Inks.FACT && filled) canvas.drawRoundRect(pill, radius, radius, solid);
            canvas.drawRoundRect(pill, radius, radius, stroke);
            if (state == Inks.FACT && glyphName != null) Glyphs.draw(canvas, glyphName, ui.fpx(10 + 6), getHeight() / 2f, ui.fpx(13), getCurrentTextColor());
            super.onDraw(canvas);
        }
    }

    // A die-cut hero sticker: the shape in its fill with a 3-unit light keyline (Text colour), over a solid copy 4 units
    // lower in the Page colour. The path comes from rounded, disc or burst and is kept.
    public static final class StickerShape extends View {
        private final Ui ui;
        private final Path shape;
        private final RectF bounds = new RectF();
        private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG), keyline = new Paint(Paint.ANTI_ALIAS_FLAG), lift = new Paint(Paint.ANTI_ALIAS_FLAG);

        public StickerShape(Ui ui, Path shape, int ink) {
            super(ui.context);
            this.ui = ui; this.shape = shape;
            shape.computeBounds(bounds, true);
            fill.setColor(ink);
            keyline.setStyle(Paint.Style.STROKE); keyline.setStrokeWidth(ui.fpx(KEYLINE)); keyline.setStrokeJoin(Paint.Join.ROUND); keyline.setColor(ui.theme.text);
            lift.setColor(ui.theme.page);
        }

        @Override protected void onMeasure(int widthSpec, int heightSpec) {
            float pad = ui.fpx(KEYLINE) / 2;
            setMeasuredDimension(resolveSize((int) Math.ceil(bounds.right + pad * 2), widthSpec), resolveSize((int) Math.ceil(bounds.bottom + ui.fpx(LIFT) + pad * 2), heightSpec));
        }

        @Override protected void onDraw(Canvas canvas) {
            float pad = ui.fpx(KEYLINE) / 2;
            canvas.save(); canvas.translate(pad, pad + ui.fpx(LIFT)); canvas.drawPath(shape, lift); canvas.restore();
            canvas.save(); canvas.translate(pad, pad); canvas.drawPath(shape, fill); canvas.drawPath(shape, keyline); canvas.restore();
        }
    }
}
