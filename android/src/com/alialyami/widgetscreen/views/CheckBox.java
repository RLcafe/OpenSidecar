package com.alialyami.widgetscreen.views;

import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RectF;
import android.view.Gravity;
import android.view.View;
import android.widget.LinearLayout;
import android.widget.TextView;

import com.alialyami.widgetscreen.Ui;
import com.alialyami.widgetscreen.core.Theme;

// The Windows CheckBox template: a 24 unit rounded box that fills with the accent and shows a check mark,
// beside a wrapping label. The whole 48 unit row is the touch target.
public final class CheckBox extends LinearLayout {
    public interface Listener { void changed(CheckBox box, boolean checked); }

    private final Box box;
    private final TextView label;
    private boolean checked;
    private Listener listener;

    private final class Box extends View {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Ui ui;
        Box(Ui ui) { super(ui.context); this.ui = ui; }
        @Override protected void onDraw(Canvas canvas) {
            float size = ui.fpx(24), top = (getHeight() - size) / 2f, r = ui.fpx(6);
            RectF rect = new RectF(ui.fpx(1), top + ui.fpx(1), size - ui.fpx(1), top + size - ui.fpx(1));
            paint.setStyle(Paint.Style.FILL); paint.setColor(checked ? ui.theme.accent : ui.theme.input);
            canvas.drawRoundRect(rect, r, r, paint);
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(Math.max(1, ui.fpx(1))); paint.setColor(checked ? ui.theme.accent : Theme.CHECK_BORDER);
            canvas.drawRoundRect(rect, r, r, paint);
            if (checked) Glyphs.draw(canvas, "check", size / 2, top + size / 2, size * 0.8f, Theme.ON_ACCENT);
        }
    }

    public CheckBox(Ui ui, String text, boolean checked, Listener listener) {
        super(ui.context);
        this.checked = checked; this.listener = listener;
        setOrientation(HORIZONTAL); setGravity(Gravity.CENTER_VERTICAL);
        setMinimumHeight(ui.px(48));
        setPadding(ui.px(4), ui.px(4), ui.px(4), ui.px(4));
        setBackground(ui.pressable(0x00000000, 0, 0, 8));
        box = new Box(ui);
        addView(box, new LayoutParams(ui.px(34), ui.px(40)));
        label = Ui.wrap(ui.body(text), 3);
        addView(label, new LayoutParams(0, LayoutParams.WRAP_CONTENT, 1));
        setClickable(true); setFocusable(true);
        setOnClickListener(new OnClickListener() { @Override public void onClick(View v) { setChecked(!CheckBox.this.checked, true); } });
        setContentDescription(text);
    }

    public boolean checked() { return checked; }
    public TextView label() { return label; }

    public void setChecked(boolean value, boolean fromUser) {
        if (value == checked) return;
        checked = value; box.invalidate();
        if (fromUser && listener != null) listener.changed(this, value);
    }
}
