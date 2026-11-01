package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.view.View;

import com.alialyami.widgetscreen.core.Critter;
import com.alialyami.widgetscreen.core.Theme;

// One drawn critter (ClaudeCritter in the Windows CardGraphics), from the shared numbers in core/Critter.java. Nothing
// animates by itself: the pupils and the hop move only when the dashboard's heartbeat flips the phase.
public final class CritterView extends View {
    private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Path path = new Path();
    private final RectF box = new RectF();
    private final Theme theme;
    private String state = "ready", project = "";
    private boolean phase, quiet, collapsed;
    private int count;
    private float mouth;

    public CritterView(Context context, Theme theme) { super(context); this.theme = theme; paint.setStrokeCap(Paint.Cap.ROUND); paint.setStrokeJoin(Paint.Join.ROUND); }

    public void set(String nextState, String nextProject) {
        nextState = nextState == null ? "ready" : nextState; nextProject = nextProject == null ? "" : nextProject;
        if (!nextState.equals(state) || !nextProject.equals(project)) { state = nextState; project = nextProject; invalidate(); }
    }
    public void setPhase(boolean nextPhase, boolean nextQuiet) { if (nextPhase != phase || nextQuiet != quiet) { phase = nextPhase; quiet = nextQuiet; invalidate(); } }
    // A project critter that has eaten its threads (with how many), and its mouth while it eats or spits (0–1).
    public void setCollapsed(boolean next, int nextCount) { if (next != collapsed || nextCount != count) { collapsed = next; count = nextCount; invalidate(); } }
    public boolean collapsed() { return collapsed; }
    public void setMouth(float open) { if (open != mouth) { mouth = open; invalidate(); } }
    // Where the mouth is, in this view's pixels (babies are eaten into it and spat out of it).
    public float mouthX() { return getWidth() / 2f; }
    public float mouthY() { float size = Math.min(getWidth(), getHeight()); return (getHeight() - size) / 2 + (float) Critter.mouthY(Critter.body(Critter.shape(project))) * size / 100f; }

    private void fill(int color) { paint.setStyle(Paint.Style.FILL); paint.setColor(color); }
    private void stroke(int color, float width) { paint.setStyle(Paint.Style.STROKE); paint.setColor(color); paint.setStrokeWidth(width); }

    @Override protected void onDraw(Canvas canvas) {
        float size = Math.min(getWidth(), getHeight());
        if (size < 8) return;
        float scale = size / 100f;
        canvas.save();
        canvas.translate((getWidth() - size) / 2, (getHeight() - size) / 2);
        canvas.scale(scale, scale);
        canvas.translate(0, (float) Critter.hop(state, phase, quiet));
        boolean needs = Critter.needs(state), working = "working".equals(state), done = "done".equals(state), ready = !needs && !working && !done;
        int accent = Critter.ACCENTS[Critter.accent(project)];
        double[] b = Critter.body(Critter.shape(project));

        // Arms up, behind the body, when it wants you.
        if (needs) {
            float armY = (float) (b[1] + (b[3] - b[1]) * 0.55);
            stroke(Critter.NEEDS_YOU, 7);
            canvas.drawLine((float) b[0] + 6, armY, (float) b[0] - 7, (float) b[1] + 2, paint);
            canvas.drawLine((float) b[2] - 6, armY, (float) b[2] + 7, (float) b[1] + 2, paint);
        }
        box.set((float) b[0], (float) b[1], (float) b[2], (float) b[3]);
        fill(needs ? Critter.NEEDS_YOU : ready ? theme.raised : accent);
        canvas.drawRoundRect(box, (float) b[4], (float) b[4], paint);
        if (ready) stroke(accent, 2 / scale); else stroke(theme.controlBorder, 1 / scale);
        canvas.drawRoundRect(box, (float) b[4], (float) b[4], paint);

        float eyeY = (float) Critter.eyeY(b), gap = (float) Critter.eyeGap(b), mouthY = (float) Critter.mouthY(b), r = (float) Critter.eyeRadius(state);
        for (int side = -1; side <= 1; side += 2) {
            float x = 50 + side * gap;
            if (Critter.eyesOpen(state)) {
                fill(theme.text); canvas.drawCircle(x, eyeY, r, paint);
                fill(theme.page); canvas.drawCircle(x + (float) Critter.pupilX(state, phase, quiet), eyeY + (float) Critter.pupilY(state), (float) Critter.pupilRadius(state), paint);
            } else if (done) {
                // Happy closed eyes: two upward arcs.
                path.reset(); path.moveTo(x - 6.5f, eyeY + 2); path.quadTo(x, eyeY - 7, x + 6.5f, eyeY + 2);
                stroke(theme.page, 3.2f); canvas.drawPath(path, paint);
            } else { stroke(theme.page, 3.2f); canvas.drawLine(x - 6, eyeY + 1, x + 6, eyeY + 1, paint); }
        }
        if (collapsed) {
            // Full of threads: a belly, and puffed cheeks either side of the mouth.
            double[] belly = Critter.belly(b);
            fill((theme.page & 0x00FFFFFF) | 0x38000000); box.set((float) belly[0], (float) belly[1], (float) (belly[0] + belly[2]), (float) (belly[1] + belly[3])); canvas.drawRoundRect(box, (float) belly[3] / 2, (float) belly[3] / 2, paint);
            float cheek = (float) Critter.cheekRadius(b), cx = (float) Critter.cheekX(b);
            fill((theme.text & 0x00FFFFFF) | 0x73000000);
            box.set(50 - cx - cheek, mouthY - 1 - cheek * 0.8f, 50 - cx + cheek, mouthY - 1 + cheek * 0.8f); canvas.drawOval(box, paint);
            box.set(50 + cx - cheek, mouthY - 1 - cheek * 0.8f, 50 + cx + cheek, mouthY - 1 + cheek * 0.8f); canvas.drawOval(box, paint);
        }
        if (mouth > 0.05f) { float w = (float) Critter.mouthWidth(mouth), h = (float) Critter.mouthHeight(mouth); fill(theme.page); box.set(50 - w, mouthY - h, 50 + w, mouthY + h); canvas.drawOval(box, paint); }
        else if (collapsed) { stroke(theme.page, 3); canvas.drawLine(45, mouthY, 55, mouthY, paint); }
        else if (needs) { stroke(theme.page, 3); canvas.drawCircle(50, mouthY, 4.5f, paint); }
        else if (done) { path.reset(); path.moveTo(41, mouthY - 2); path.quadTo(50, mouthY + 8, 59, mouthY - 2); stroke(theme.page, 3); canvas.drawPath(path, paint); }
        else { stroke(theme.page, 3); canvas.drawLine(working ? 44 : 45, mouthY, working ? 56 : 55, mouthY, paint); }

        if (collapsed) {
            // How many threads it holds, in a bubble on its top right.
            float bx = (float) Critter.bubbleX(b), by = (float) Critter.bubbleY(b), r2 = (float) Critter.BUBBLE_RADIUS;
            fill(theme.page); canvas.drawCircle(bx, by, r2, paint);
            stroke(needs ? Critter.NEEDS_YOU : accent, 2.4f); canvas.drawCircle(bx, by, r2, paint);
            String label = count > 99 ? "99+" : String.valueOf(count);
            fill(theme.text); paint.setTextAlign(Paint.Align.CENTER); paint.setTextSize(label.length() > 2 ? 10 : 14); paint.setFakeBoldText(true);
            canvas.drawText(label, bx, by - (paint.descent() + paint.ascent()) / 2, paint);
            paint.setFakeBoldText(false); paint.setTextAlign(Paint.Align.LEFT);
            if (needs) { stroke(theme.text, 5); canvas.drawLine(50, 3, 50, 14, paint); fill(theme.text); canvas.drawCircle(50, 21.5f, 2.8f, paint); }
        } else if (working) {
            // The thinking trail beside the head.
            fill(theme.muted); canvas.drawCircle(80, 24, 2.4f, paint); canvas.drawCircle(88, 15, 3, paint); canvas.drawCircle(95, 6, 3.6f, paint);
        } else if (needs) {
            stroke(theme.text, 5); canvas.drawLine(50, 3, 50, 14, paint);
            fill(theme.text); canvas.drawCircle(50, 21.5f, 2.8f, paint);
        } else if (ready) {
            path.reset(); path.moveTo(77, 7); path.lineTo(90, 7); path.lineTo(77, 20); path.lineTo(90, 20);
            stroke(theme.muted, 2.6f); canvas.drawPath(path, paint);
        }
        canvas.restore();
    }

    // The ten-segment plan meter (SegmentBar in the Windows CardGraphics): 4-unit gaps, radius 3.
    public static final class Segments extends View {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final float gap;
        private int fill, track, lit;
        public Segments(Context context, int fill, int track, float gap) { super(context); this.fill = fill; this.track = track; this.gap = gap; }
        public void set(int nextLit, int nextFill) { if (nextLit != lit || nextFill != fill) { lit = nextLit; fill = nextFill; invalidate(); } }
        @Override protected void onDraw(Canvas canvas) {
            float width = (getWidth() - gap * 9) / 10, height = getHeight(), radius = Math.min(gap * 0.75f, height / 2);
            if (width <= 1 || height <= 0) return;
            for (int i = 0; i < 10; i++) {
                paint.setColor(i < lit ? fill : track);
                canvas.drawRoundRect(i * (width + gap), 0, i * (width + gap) + width, height, radius, radius, paint);
            }
        }
    }
}
