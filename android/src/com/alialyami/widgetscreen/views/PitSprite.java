package com.alialyami.widgetscreen.views;

import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Matrix;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.Rect;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.view.View;

import com.alialyami.widgetscreen.core.ClaudeText;
import com.alialyami.widgetscreen.core.PitArt;
import com.alialyami.widgetscreen.core.PitPath;

import java.util.HashMap;
import java.util.List;
import java.util.Map;

// A pit sprite on the phone's Claude card (docs/design/claude-watcher-v2.html; decision 017): a car or a garage drawn
// from PitArt's steps into a small bitmap at its native pixel size, every pixel then made opaque (alpha above 80, its
// colour on a 16-step ramp) or clear, and shown scaled up with filtering off so the pixels stay square. The card's
// frame clock steps it at its own rate; Quiet mode and a hidden card hold it still. The same as the Windows PitSprite.
public final class PitSprite extends View {
    private static final Map<String, Bitmap> CACHE = new HashMap<String, Bitmap>();
    private static final Paint PIXELS = new Paint();
    static { PIXELS.setFilterBitmap(false); PIXELS.setAntiAlias(false); PIXELS.setDither(false); }
    private Bitmap[] frames = new Bitmap[0];
    private int frame, still;
    private String key;
    public double fps, clock;
    public boolean once;
    private final Rect source = new Rect();
    private final RectF target = new RectF();
    // A car's context ring (decision 023), on its top right corner: how full the thread's context window is, from 60%
    // (muted), 80% (Amber, compaction soon) and 95% (Amber with a filled centre). NaN or under 60% draws none.
    private double ring = Double.NaN;
    private int ringPage, ringTrack, ringMuted;
    private final Paint ringPaint = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final RectF ringBox = new RectF();

    public PitSprite(Context context) { super(context); setImportantForAccessibility(IMPORTANT_FOR_ACCESSIBILITY_NO); }

    public String key() { return key; }
    public int frame() { return frame; }
    public int frameCount() { return frames.length; }
    public boolean moving() { return frames.length > 1 && fps > 0 && !(once && frame >= frames.length - 1); }

    public void show(String next, Bitmap[] bitmaps, int stillFrame, boolean restart) {
        if (next.equals(key) && !restart) return;
        boolean same = next.equals(key);
        key = next; frames = bitmaps; still = Math.max(0, Math.min(bitmaps.length - 1, stillFrame));
        frame = restart ? 0 : same ? Math.min(frame, bitmaps.length - 1) : still;
        clock = 0;
        invalidate();
    }
    public void step(double seconds) {
        if (!moving()) return;
        clock += seconds * fps;
        if (clock < 1) return;
        int steps = (int) clock; clock -= steps;
        int next = once ? Math.min(frames.length - 1, frame + steps) : (frame + steps) % frames.length;
        if (next != frame) { frame = next; invalidate(); }
    }
    public void hold() { if (frame != still && !once && frames.length > 0) { frame = still; invalidate(); } }
    public double ring() { return ring; }
    public void ring(double percent, int page, int track, int muted) {
        double shown = ClaudeText.contextLevel(percent) == 0 ? Double.NaN : Math.round(percent);
        if ((Double.isNaN(shown) && Double.isNaN(ring)) || (shown == ring && page == ringPage && muted == ringMuted)) return;
        ring = shown; ringPage = page; ringTrack = track; ringMuted = muted;
        invalidate();
    }

    @Override protected void onDraw(Canvas canvas) {
        if (frames.length == 0 || frames[frame] == null) return;
        Bitmap bitmap = frames[frame];
        source.set(0, 0, bitmap.getWidth(), bitmap.getHeight());
        target.set(0, 0, getWidth(), getHeight());
        canvas.drawBitmap(bitmap, source, target, PIXELS);
        int level = ClaudeText.contextLevel(ring);
        if (level == 0) return;
        float size = Math.max(10, Math.min(getHeight() * 0.5f, getWidth() * 0.26f)), stroke = Math.max(1.5f, size / 6), r = size / 2 - stroke / 2 - 0.5f;
        float cx = getWidth() - size / 2, cy = size / 2;
        int ink = level == 1 ? ringMuted : com.alialyami.widgetscreen.core.Theme.WARNING;
        ringPaint.setStyle(Paint.Style.FILL); ringPaint.setColor(ringPage); canvas.drawCircle(cx, cy, size / 2, ringPaint);
        ringPaint.setStyle(Paint.Style.STROKE); ringPaint.setStrokeWidth(stroke); ringPaint.setStrokeCap(Paint.Cap.ROUND);
        ringPaint.setColor(ringTrack); canvas.drawCircle(cx, cy, r, ringPaint);
        ringBox.set(cx - r, cy - r, cx + r, cy + r);
        ringPaint.setColor(ink); canvas.drawArc(ringBox, -90, (float) (Math.min(99.9, ring) * 3.6), false, ringPaint);
        if (level == 3) { ringPaint.setStyle(Paint.Style.FILL); canvas.drawCircle(cx, cy, Math.max(1, r - stroke), ringPaint); }
    }

    // ----- Making the frames -----

    // A car's frames for a box this wide. The view is the frame's full height: the car's 140 × 60 box plus a quarter
    // of its height above and below for lights, smoke and the fuel lamp.
    public static Bitmap[] carFrames(int color, int state, PitArt.Look look, double width) {
        int px = PitArt.pixelSize(width), nw = Math.max(8, (int) Math.round(width / px)), nh = Math.max(4, (int) Math.round(width * 60 / 140 / px)), pad = (int) Math.ceil(nh * 0.25);
        double u = 60.0 / nh;
        String name = "car|" + Integer.toHexString(color) + "|" + state + "|" + look.key() + "|" + nw + "x" + nh;
        Bitmap[] result = new Bitmap[8];
        for (int f = 0; f < 8; f++) result[f] = frame(name + "|" + f, PitArt.car(color, state, look, f), 0, -pad * u, 140, 60 + 2 * pad * u, nw, nh + 2 * pad);
        return result;
    }
    // A car's view height for a box this wide (the frame's full height).
    public static double carHeight(double width) { return width * 60 / 140 * 1.5; }
    public static Bitmap[] garageFrames(int color, int state, boolean closed, int count, double width) {
        int n = Math.max(8, (int) Math.round(width / PitArt.pixelSize(width)));
        String name = "garage|" + Integer.toHexString(color) + "|" + state + "|" + closed + "|" + count + "|" + n;
        return new Bitmap[] { frame(name + "|0", PitArt.garage(color, state, closed, count, 0), 0, 0, 100, 100, n, n), frame(name + "|1", PitArt.garage(color, state, closed, count, 1), 0, 0, 100, 100, n, n) };
    }

    public static Bitmap frame(String key, List<PitArt.Op> ops, double x, double y, double w, double h, int width, int height) {
        Bitmap bitmap = CACHE.get(key);
        if (bitmap != null) return bitmap;
        if (CACHE.size() > 1500) CACHE.clear();
        bitmap = render(ops, x, y, w, h, Math.max(1, width), Math.max(1, height));
        CACHE.put(key, bitmap);
        return bitmap;
    }

    // Draws the steps at the bitmap's own size, then makes every pixel opaque or clear (decision 017).
    public static Bitmap render(List<PitArt.Op> ops, double x, double y, double w, double h, int width, int height) {
        Bitmap bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
        Canvas canvas = new Canvas(bitmap);
        canvas.scale((float) (width / w), (float) (height / h));
        canvas.translate((float) -x, (float) -y);
        Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG); fill.setStyle(Paint.Style.FILL);
        Paint stroke = new Paint(Paint.ANTI_ALIAS_FLAG); stroke.setStyle(Paint.Style.STROKE); stroke.setStrokeJoin(Paint.Join.MITER);
        int pushed = 0;
        for (PitArt.Op op : ops) {
            switch (op.kind) {
                case PitArt.Op.PUSH: {
                    canvas.save(); pushed++;
                    Matrix m = new Matrix(); m.setValues(new float[] { (float) op.matrix[0], (float) op.matrix[2], (float) op.matrix[4], (float) op.matrix[1], (float) op.matrix[3], (float) op.matrix[5], 0, 0, 1 });
                    canvas.concat(m);
                    break;
                }
                case PitArt.Op.POP: if (pushed > 0) { canvas.restore(); pushed--; } break;
                case PitArt.Op.ELLIPSE: {
                    RectF oval = new RectF((float) (op.x - op.w), (float) (op.y - op.h), (float) (op.x + op.w), (float) (op.y + op.h));
                    if (op.fill != 0) { fill.setColor(op.fill); canvas.drawOval(oval, fill); }
                    if (stroked(stroke, op)) canvas.drawOval(oval, stroke);
                    break;
                }
                case PitArt.Op.RECT: {
                    RectF box = new RectF((float) op.x, (float) op.y, (float) (op.x + Math.max(0, op.w)), (float) (op.y + Math.max(0, op.h)));
                    float r = (float) op.radius;
                    if (op.fill != 0) { fill.setColor(op.fill); canvas.drawRoundRect(box, r, r, fill); }
                    if (stroked(stroke, op)) canvas.drawRoundRect(box, r, r, stroke);
                    break;
                }
                case PitArt.Op.TEXT: {
                    Paint words = new Paint(Paint.ANTI_ALIAS_FLAG); words.setColor(op.fill); words.setTextSize((float) op.size); words.setTextAlign(Paint.Align.CENTER); words.setTypeface(Typeface.DEFAULT_BOLD);
                    canvas.drawText(op.text, (float) op.x, (float) op.y, words);
                    break;
                }
                default: {
                    Path path = path(op.path);
                    path.setFillType(op.evenOdd ? Path.FillType.EVEN_ODD : Path.FillType.WINDING);
                    if (op.fill != 0) { fill.setColor(op.fill); canvas.drawPath(path, fill); }
                    if (stroked(stroke, op)) canvas.drawPath(path, stroke);
                }
            }
        }
        while (pushed-- > 0) canvas.restore();
        int[] pixels = new int[width * height];
        bitmap.getPixels(pixels, 0, width, 0, 0, width, height);
        crisp(pixels);
        bitmap.setPixels(pixels, 0, width, 0, 0, width, height);
        return bitmap;
    }
    private static boolean stroked(Paint stroke, PitArt.Op op) {
        if (op.stroke == 0 || op.strokeWidth <= 0) return false;
        stroke.setColor(op.stroke); stroke.setStrokeWidth((float) op.strokeWidth);
        stroke.setStrokeCap(op.roundCap ? Paint.Cap.ROUND : Paint.Cap.BUTT);
        return true;
    }
    private static Path path(String data) {
        Path path = new Path();
        for (float[] s : PitPath.parse(data)) {
            switch ((int) s[0]) {
                case PitPath.MOVE: path.moveTo(s[1], s[2]); break;
                case PitPath.LINE: path.lineTo(s[1], s[2]); break;
                case PitPath.QUAD: path.quadTo(s[1], s[2], s[3], s[4]); break;
                default: path.close();
            }
        }
        return path;
    }
    // Alpha above 80 becomes opaque, the rest clear; colours snap to 17-unit steps (getPixels gives straight colours).
    public static void crisp(int[] pixels) {
        for (int i = 0; i < pixels.length; i++) {
            int c = pixels[i], a = (c >>> 24) & 0xFF;
            if (a <= 80) { pixels[i] = 0; continue; }
            int r = snap((c >> 16) & 0xFF), g = snap((c >> 8) & 0xFF), b = snap(c & 0xFF);
            pixels[i] = 0xFF000000 | (r << 16) | (g << 8) | b;
        }
    }
    private static int snap(int v) { return (int) Math.round(v / 17.0) * 17; }
}
