package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.view.View;

import com.alialyami.widgetscreen.core.ClaudeText;

// The Claude card's mascot (decision 027): the original 16 × 16 pixel robot from ClaudeText.SPRITE, the same as the
// Windows ClaudeMascot, in the card's ink with its antenna, eyes and chest panel in the colour of the most urgent count.
// Asleep (no sessions) it closes its eyes and dims. Drawn as whole-unit squares, no image, and it never animates.
public final class ClaudeMascot extends View {
    private final Paint body = new Paint(), visor = new Paint(), light = new Paint();
    private int mood = ClaudeText.SLEEPING;

    public ClaudeMascot(Context context, int bodyColor, int visorColor) {
        super(context);
        body.setColor(bodyColor); visor.setColor(visorColor);
    }
    public int mood() { return mood; }
    public void set(int nextMood, int lightColor) {
        if (nextMood == mood && light.getColor() == lightColor) return;
        mood = nextMood; light.setColor(lightColor); invalidate();
    }

    @Override protected void onDraw(Canvas canvas) {
        float side = Math.min(getWidth(), getHeight());
        float cell = (float) Math.floor(side / ClaudeText.SIZE);
        if (cell < 1) cell = side / ClaudeText.SIZE;
        if (cell <= 0) return;
        float left = Math.round((getWidth() - cell * ClaudeText.SIZE) / 2), top = Math.round((getHeight() - cell * ClaudeText.SIZE) / 2);
        boolean asleep = mood == ClaudeText.SLEEPING;
        int alpha = asleep ? 140 : 255;
        body.setAlpha(alpha); visor.setAlpha(alpha); light.setAlpha(alpha);
        for (int row = 0; row < ClaudeText.SIZE; row++)
            for (int col = 0; col < ClaudeText.SIZE; col++) {
                char c = ClaudeText.pixel(mood, row, col);
                Paint paint = c == '#' ? body : c == 'v' ? visor : c == 'L' ? light : null;
                if (paint != null) canvas.drawRect(left + col * cell, top + row * cell, left + (col + 1) * cell, top + (row + 1) * cell, paint);
            }
    }
}
