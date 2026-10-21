package com.alialyami.widgetscreen;

import android.content.Context;
import android.graphics.Typeface;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.LayerDrawable;
import android.graphics.drawable.StateListDrawable;
import android.text.TextUtils;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.core.AppSettings;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.views.CheckBox;
import com.alialyami.widgetscreen.views.GlyphView;

// The Windows styles (MainWindow.xaml resources) as view factories. Sizes are design units: inside the
// scaled dashboard one unit is one logical pixel; Settings uses a unit sized for the device.
public final class Ui {
    public static final Typeface REGULAR = Typeface.create("sans-serif", Typeface.NORMAL);
    public static final Typeface MEDIUM = Typeface.create("sans-serif-medium", Typeface.NORMAL);
    public static final Typeface LIGHT = Typeface.create("sans-serif-light", Typeface.NORMAL);
    public static final int MATCH = ViewGroup.LayoutParams.MATCH_PARENT, WRAP = ViewGroup.LayoutParams.WRAP_CONTENT;

    public final Context context;
    public final Theme theme;
    public final AppSettings settings;
    public final float unit, textScale, radius;

    public Ui(Context context, Theme theme, AppSettings settings, float unit) {
        this.context = context; this.theme = theme; this.settings = settings; this.unit = unit;
        textScale = (float) settings.textScale; radius = (float) settings.cornerRadius;
    }

    public int px(double units) { return Math.round((float) units * unit); }
    public float fpx(double units) { return (float) units * unit; }

    // SmallSize, BodySize, TitleSize and HeaderClockSize follow the text size setting.
    public float small() { return 15 * textScale; }
    public float body() { return 18 * textScale; }
    public float title() { return 24 * textScale; }
    public float headerClock() { return 52 * Math.min(1.06f, textScale); }

    public TextView text(String value, float size, int color) {
        TextView view = new TextView(context);
        view.setText(value);
        view.setTextSize(TypedValue.COMPLEX_UNIT_PX, fpx(size));
        view.setTextColor(color);
        view.setTypeface(REGULAR);
        view.setIncludeFontPadding(false);
        view.setSingleLine(true);
        view.setEllipsize(TextUtils.TruncateAt.END);
        return view;
    }
    public TextView label(String value) { TextView view = text(value, small(), theme.muted); view.setTypeface(MEDIUM); view.setLetterSpacing(0.02f); return view; }
    public TextView smallText(String value) { return text(value, small(), theme.muted); }
    // The smallest dashboard text (status flags, hints, credits): 13, not scaled.
    public TextView caption(String value) { return text(value, 13, theme.muted); }
    public TextView body(String value) { return text(value, body(), theme.text); }
    public TextView heading(String value) { return text(value, title(), theme.text); }
    public static TextView wrap(TextView view, int lines) { view.setSingleLine(false); view.setMaxLines(lines); view.setEllipsize(TextUtils.TruncateAt.END); return view; }
    public static TextView tabular(TextView view) { view.setFontFeatureSettings("tnum"); return view; }

    public GradientDrawable shape(int fill, int stroke, double strokeUnits, double radiusUnits) {
        GradientDrawable shape = new GradientDrawable();
        shape.setColor(fill);
        if (strokeUnits > 0) shape.setStroke(Math.max(1, px(strokeUnits)), stroke);
        shape.setCornerRadius(fpx(radiusUnits));
        return shape;
    }

    // A pressed state that brightens the control, like the Windows template's white overlay.
    public Drawable pressable(int fill, int stroke, double strokeUnits, double radiusUnits) {
        StateListDrawable states = new StateListDrawable();
        GradientDrawable overlay = shape(0x21FFFFFF, 0, 0, radiusUnits);
        states.addState(new int[] { android.R.attr.state_pressed }, new LayerDrawable(new Drawable[] { shape(fill, stroke, strokeUnits, radiusUnits), overlay }));
        states.addState(new int[0], shape(fill, stroke, strokeUnits, radiusUnits));
        return states;
    }

    public TextView button(String label, View.OnClickListener click) {
        TextView button = text(label, body(), theme.text);
        button.setGravity(Gravity.CENTER);
        button.setMinHeight(px(48)); button.setMinWidth(px(48));
        button.setPadding(px(16), px(8), px(16), px(8));
        button.setBackground(pressable(theme.raised, theme.controlBorder, 1, 12));
        button.setClickable(true); button.setFocusable(true);
        button.setOnClickListener(click);
        return button;
    }

    public TextView accentButton(String label, View.OnClickListener click) {
        TextView button = button(label, click);
        button.setTextColor(Theme.ON_ACCENT); button.setTypeface(MEDIUM);
        button.setBackground(pressable(theme.accent, 0, 0, 12));
        return button;
    }

    // Transparent buttons (media controls, card corners) keep the pressed overlay but no chrome.
    public TextView flatButton(String label, View.OnClickListener click) {
        TextView button = button(label, click);
        button.setBackground(pressable(0x00000000, 0, 0, 12));
        return button;
    }

    public GlyphView iconButton(String glyph, String description, double glyphSize, boolean chrome, View.OnClickListener click) {
        GlyphView view = new GlyphView(context, glyph, theme.text, fpx(glyphSize));
        view.setMinimumWidth(px(48)); view.setMinimumHeight(px(48));
        view.setBackground(chrome ? pressable(theme.raised, theme.controlBorder, 1, 12) : pressable(0x00000000, 0, 0, 12));
        view.setContentDescription(description);
        view.setClickable(true); view.setFocusable(true);
        view.setOnClickListener(click);
        return view;
    }

    // The selected look shared by Focus presets, Quiet mode and tabs: Selected background and a 2 unit accent border.
    public void select(View button, boolean on) {
        button.setBackground(pressable(on ? theme.selected : theme.raised, on ? theme.accent : theme.controlBorder, on ? 2 : 1, 12));
        button.setSelected(on);
    }

    public static void enable(View view, boolean on) { view.setEnabled(on); view.setAlpha(on ? 1f : 0.35f); }

    public FrameLayout card(View content, double padding) {
        FrameLayout card = new FrameLayout(context);
        card.setBackground(shape(theme.surface, theme.controlBorder, 1, radius));
        card.setPadding(px(padding), px(padding), px(padding), px(padding));
        card.setClipToOutline(true);
        card.addView(content, new FrameLayout.LayoutParams(MATCH, MATCH));
        return card;
    }

    public LinearLayout row() { LinearLayout row = new LinearLayout(context); row.setOrientation(LinearLayout.HORIZONTAL); row.setGravity(Gravity.CENTER_VERTICAL); return row; }
    public LinearLayout column() { LinearLayout column = new LinearLayout(context); column.setOrientation(LinearLayout.VERTICAL); return column; }

    public LinearLayout.LayoutParams ll(int width, int height, float weight) { return new LinearLayout.LayoutParams(width, height, weight); }
    public LinearLayout.LayoutParams ll(int width, int height) { return new LinearLayout.LayoutParams(width, height); }
    public <T extends ViewGroup.MarginLayoutParams> T margins(T params, double left, double top, double right, double bottom) {
        params.setMargins(px(left), px(top), px(right), px(bottom));
        return params;
    }
    public FrameLayout.LayoutParams frame(int width, int height, int gravity) { return new FrameLayout.LayoutParams(width, height, gravity); }

    public CheckBox check(String label, boolean checked, CheckBox.Listener listener) { return new CheckBox(this, label, checked, listener); }

    public EditText input(String value, String hint) {
        EditText input = new EditText(context);
        input.setText(value);
        input.setHint(hint);
        input.setTextSize(TypedValue.COMPLEX_UNIT_PX, fpx(body()));
        input.setTextColor(theme.text); input.setHintTextColor(theme.muted);
        input.setTypeface(REGULAR);
        input.setSingleLine(true);
        input.setMinHeight(px(52));
        input.setPadding(px(12), px(8), px(12), px(8));
        input.setBackground(shape(theme.input, theme.controlBorder, 1, 8));
        input.setHighlightColor(0x66000000 | (theme.accent & 0x00FFFFFF));
        return input;
    }

    public View divider(double thickness, int color) { View line = new View(context); line.setBackgroundColor(color); return line; }

    public static int alpha(int color, float alpha) { return (Math.round(alpha * 255) << 24) | (color & 0x00FFFFFF); }
}
