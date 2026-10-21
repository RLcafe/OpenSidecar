package com.alialyami.widgetscreen.views;

import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;

import java.util.HashMap;
import java.util.Map;

// Line icons drawn on a 24-unit grid. Android has no Segoe MDL2 Assets, and the dashboard doesn't use
// images or emoji for icons, so each glyph the Windows app uses is drawn here as a path.
public final class Glyphs {
    private static final class Glyph { final Path stroke = new Path(), fill = new Path(); }
    private static final Map<String, Glyph> CACHE = new HashMap<String, Glyph>();
    private static final Paint STROKE = new Paint(Paint.ANTI_ALIAS_FLAG), FILL = new Paint(Paint.ANTI_ALIAS_FLAG);

    static {
        STROKE.setStyle(Paint.Style.STROKE); STROKE.setStrokeWidth(2f);
        STROKE.setStrokeCap(Paint.Cap.ROUND); STROKE.setStrokeJoin(Paint.Join.ROUND);
        FILL.setStyle(Paint.Style.FILL);
    }

    public static void draw(Canvas canvas, String name, float centerX, float centerY, float size, int color) {
        Glyph glyph = get(name);
        canvas.save();
        canvas.translate(centerX - size / 2, centerY - size / 2);
        canvas.scale(size / 24f, size / 24f);
        STROKE.setColor(color); FILL.setColor(color);
        canvas.drawPath(glyph.fill, FILL);
        canvas.drawPath(glyph.stroke, STROKE);
        canvas.restore();
    }

    private static synchronized Glyph get(String name) {
        Glyph glyph = CACHE.get(name);
        if (glyph == null) { glyph = build(name == null ? "app" : name); CACHE.put(name, glyph); }
        return glyph;
    }

    private static void line(Path p, float x1, float y1, float x2, float y2) { p.moveTo(x1, y1); p.lineTo(x2, y2); }
    private static void poly(Path p, boolean close, float... xy) {
        p.moveTo(xy[0], xy[1]);
        for (int i = 2; i + 1 < xy.length; i += 2) p.lineTo(xy[i], xy[i + 1]);
        if (close) p.close();
    }
    private static void round(Path p, float left, float top, float right, float bottom, float radius) { p.addRoundRect(new RectF(left, top, right, bottom), radius, radius, Path.Direction.CW); }
    private static void circle(Path p, float x, float y, float r) { p.addCircle(x, y, r, Path.Direction.CW); }

    // A circular arrow: an arc from start sweeping clockwise, with a filled head at its end.
    private static void circularArrow(Glyph g, float r, float start, float sweep) {
        g.stroke.addArc(new RectF(12 - r, 12 - r, 12 + r, 12 + r), start, sweep);
        double end = Math.toRadians(start + sweep);
        float ex = 12 + r * (float) Math.cos(end), ey = 12 + r * (float) Math.sin(end);
        float tx = -(float) Math.sin(end), ty = (float) Math.cos(end), nx = (float) Math.cos(end), ny = (float) Math.sin(end);
        poly(g.fill, true, ex + tx * 3.2f, ey + ty * 3.2f, ex + nx * 3f - tx * 0.6f, ey + ny * 3f - ty * 0.6f, ex - nx * 3f - tx * 0.6f, ey - ny * 3f - ty * 0.6f);
    }

    private static Glyph build(String name) {
        Glyph g = new Glyph();
        Path s = g.stroke, f = g.fill;
        switch (name) {
            case "play": poly(f, true, 7, 4.5f, 19.5f, 12, 7, 19.5f); break;
            case "pause": round(f, 6, 4.5f, 10, 19.5f, 1); round(f, 14, 4.5f, 18, 19.5f, 1); break;
            case "previous": poly(f, true, 18.5f, 5.5f, 9, 12, 18.5f, 18.5f); round(f, 5.5f, 5.5f, 7.5f, 18.5f, 0.6f); break;
            case "next": poly(f, true, 5.5f, 5.5f, 15, 12, 5.5f, 18.5f); round(f, 16.5f, 5.5f, 18.5f, 18.5f, 0.6f); break;
            case "close": line(s, 6, 6, 18, 18); line(s, 18, 6, 6, 18); break;
            case "plus": line(s, 12, 5, 12, 19); line(s, 5, 12, 19, 12); break;
            case "check": poly(s, false, 5, 12.5f, 10, 17.5f, 19, 7); break;
            case "settings":
                circle(s, 12, 12, 3);
                circle(s, 12, 12, 7);
                for (int i = 0; i < 8; i++) {
                    double a = Math.toRadians(i * 45);
                    line(s, 12 + 7 * (float) Math.cos(a), 12 + 7 * (float) Math.sin(a), 12 + 9.6f * (float) Math.cos(a), 12 + 9.6f * (float) Math.sin(a));
                }
                break;
            case "music": circle(f, 8.5f, 17, 3); line(s, 11.5f, 17, 11.5f, 4.5f); poly(s, false, 11.5f, 4.5f, 18, 6.5f, 18, 9.5f, 11.5f, 7.5f); break;
            case "globe": circle(s, 12, 12, 9); s.addOval(new RectF(8, 3, 16, 21), Path.Direction.CW); line(s, 3, 12, 21, 12); break;
            case "app": round(s, 4, 4, 10.5f, 10.5f, 1.5f); round(s, 13.5f, 4, 20, 10.5f, 1.5f); round(s, 4, 13.5f, 10.5f, 20, 1.5f); round(s, 13.5f, 13.5f, 20, 20, 1.5f); break;
            case "folder": poly(s, true, 3, 6, 9.5f, 6, 11.5f, 8.5f, 21, 8.5f, 21, 18.5f, 3, 18.5f); break;
            case "calculator":
                round(s, 5, 3, 19, 21, 2); round(s, 8, 6, 16, 9.5f, 0.5f);
                for (int row = 0; row < 2; row++) for (int col = 0; col < 3; col++) circle(f, 8.5f + col * 3.5f, 13.5f + row * 3.8f, 1.1f);
                break;
            case "notes": round(s, 5, 3, 19, 21, 2); line(s, 8.5f, 8, 15.5f, 8); line(s, 8.5f, 12, 15.5f, 12); line(s, 8.5f, 16, 12.5f, 16); break;
            case "volume":
                poly(f, true, 3.5f, 9, 7.5f, 9, 12.5f, 4.5f, 12.5f, 19.5f, 7.5f, 15, 3.5f, 15);
                s.addArc(new RectF(9.5f, 8.5f, 16.5f, 15.5f), -50, 100);
                s.addArc(new RectF(8, 5, 20, 19), -50, 100);
                break;
            case "mute":
                poly(f, true, 3.5f, 9, 7.5f, 9, 12.5f, 4.5f, 12.5f, 19.5f, 7.5f, 15, 3.5f, 15);
                line(s, 15.5f, 9.5f, 20.5f, 14.5f); line(s, 20.5f, 9.5f, 15.5f, 14.5f);
                break;
            case "timer": circle(s, 12, 13.5f, 7.5f); line(s, 12, 13.5f, 12, 9.5f); line(s, 9.5f, 3, 14.5f, 3); line(s, 18, 6.5f, 19.5f, 5); break;
            case "reset": circularArrow(g, 7, 120, 270); break;
            case "refresh": circularArrow(g, 7, 300, 300); break;
            case "heart":
                f.moveTo(12, 20.5f);
                f.cubicTo(4, 14.5f, 2.5f, 11, 2.5f, 8);
                f.cubicTo(2.5f, 5.2f, 4.7f, 3.2f, 7.4f, 3.2f);
                f.cubicTo(9.3f, 3.2f, 11, 4.3f, 12, 6);
                f.cubicTo(13, 4.3f, 14.7f, 3.2f, 16.6f, 3.2f);
                f.cubicTo(19.3f, 3.2f, 21.5f, 5.2f, 21.5f, 8);
                f.cubicTo(21.5f, 11, 20, 14.5f, 12, 20.5f);
                f.close();
                break;
            case "heart-outline":
                s.moveTo(12, 20);
                s.cubicTo(4.5f, 14.4f, 3, 11, 3, 8.2f);
                s.cubicTo(3, 5.6f, 5, 3.8f, 7.4f, 3.8f);
                s.cubicTo(9.2f, 3.8f, 11, 5, 12, 6.8f);
                s.cubicTo(13, 5, 14.8f, 3.8f, 16.6f, 3.8f);
                s.cubicTo(19, 3.8f, 21, 5.6f, 21, 8.2f);
                s.cubicTo(21, 11, 19.5f, 14.4f, 12, 20);
                s.close();
                break;
            case "chevron-left": poly(s, false, 15, 5, 8, 12, 15, 19); break;
            case "chevron-right": poly(s, false, 9, 5, 16, 12, 9, 19); break;
            case "chevron-down": poly(s, false, 6, 9, 12, 15, 18, 9); break;
            case "chevron-up": poly(s, false, 6, 15, 12, 9, 18, 15); break;
            case "search": circle(s, 10.5f, 10.5f, 6); line(s, 15, 15, 20, 20); break;
            case "back": line(s, 19, 12, 5, 12); poly(s, false, 11, 6, 5, 12, 11, 18); break;
            case "star": poly(s, true, 12, 3, 14.6f, 8.9f, 21, 9.4f, 16.1f, 13.6f, 17.6f, 19.9f, 12, 16.5f, 6.4f, 19.9f, 7.9f, 13.6f, 3, 9.4f, 9.4f, 8.9f); break;
            case "camera": poly(s, true, 3, 8, 7.5f, 8, 9.5f, 5, 14.5f, 5, 16.5f, 8, 21, 8, 21, 19, 3, 19); circle(s, 12, 13.2f, 3.4f); break;
            case "game": round(s, 2.5f, 7, 21.5f, 17.5f, 5); line(s, 7.5f, 10, 7.5f, 14.5f); line(s, 5.25f, 12.25f, 9.75f, 12.25f); circle(f, 15.5f, 11, 1.1f); circle(f, 17.8f, 13.5f, 1.1f); break;
            case "calendar": round(s, 4, 5, 20, 20, 2); line(s, 4, 9.5f, 20, 9.5f); line(s, 8.5f, 3, 8.5f, 6.5f); line(s, 15.5f, 3, 15.5f, 6.5f); break;
            case "playlist": line(s, 4, 6, 15, 6); line(s, 4, 11, 15, 11); line(s, 4, 16, 10, 16); circle(f, 16, 17.5f, 2.5f); line(s, 18.5f, 17.5f, 18.5f, 9); break;
            case "album": circle(s, 12, 12, 9); circle(f, 12, 12, 2.2f); break;
            case "output": round(s, 5, 3, 19, 21, 2.5f); circle(s, 12, 14.5f, 3.5f); circle(f, 12, 7, 1.2f); break;
            case "up": poly(s, false, 6, 14, 12, 8, 18, 14); break;
            case "microphone": round(s, 9, 3, 15, 14, 3); s.addArc(new RectF(5.5f, 6.5f, 18.5f, 17.5f), 0, 180); line(s, 12, 17.5f, 12, 21); line(s, 8.5f, 21, 15.5f, 21); break;
            case "keyboard":
                round(s, 2.5f, 6, 21.5f, 18, 2); line(s, 7, 14.5f, 17, 14.5f);
                for (int i = 0; i < 5; i++) circle(f, 6 + i * 3, 10, 0.9f);
                break;
            case "down": poly(s, false, 6, 10, 12, 16, 18, 10); break;
            default: round(s, 4, 4, 10.5f, 10.5f, 1.5f); round(s, 13.5f, 4, 20, 10.5f, 1.5f); round(s, 4, 13.5f, 10.5f, 20, 1.5f); round(s, 13.5f, 13.5f, 20, 20, 1.5f); break;
        }
        return g;
    }
}
