package com.alialyami.widgetscreen.core;

import java.util.ArrayList;
import java.util.List;

// Reads the small SVG path language the pit art uses (M, L, Q, H, V, Z and their lower-case relative forms) into
// absolute segments, so the phone draws the same shapes as the PC's Geometry.Parse. Android's own parser isn't
// public, and AndroidX isn't used. Each segment is { type, x1, y1, x2, y2 }: M and L use x1, y1; Q its control
// point then its end; Z nothing.
public final class PitPath {
    public static final int MOVE = 0, LINE = 1, QUAD = 2, CLOSE = 3;

    public static List<float[]> parse(String path) {
        List<float[]> result = new ArrayList<float[]>();
        if (path == null) return result;
        int i = 0, n = path.length();
        char command = 0;
        float x = 0, y = 0, startX = 0, startY = 0;
        while (i < n) {
            char c = path.charAt(i);
            if (Character.isWhitespace(c) || c == ',') { i++; continue; }
            if (Character.isLetter(c)) {
                command = c; i++;
                if (c == 'Z' || c == 'z') { result.add(new float[] { CLOSE, 0, 0, 0, 0 }); x = startX; y = startY; }
                continue;
            }
            // Numbers follow the last command (repeated pairs keep it, as in SVG; after M they are lines).
            float[] values = new float[4];
            int need = command == 'Q' || command == 'q' ? 4 : command == 'H' || command == 'h' || command == 'V' || command == 'v' ? 1 : 2;
            for (int k = 0; k < need; k++) {
                while (i < n && (Character.isWhitespace(path.charAt(i)) || path.charAt(i) == ',')) i++;
                int start = i;
                if (i < n && (path.charAt(i) == '-' || path.charAt(i) == '+')) i++;
                boolean dot = false;
                while (i < n && (Character.isDigit(path.charAt(i)) || (path.charAt(i) == '.' && !dot))) { if (path.charAt(i) == '.') dot = true; i++; }
                if (i < n && (path.charAt(i) == 'e' || path.charAt(i) == 'E')) { i++; if (i < n && (path.charAt(i) == '-' || path.charAt(i) == '+')) i++; while (i < n && Character.isDigit(path.charAt(i))) i++; }
                if (start == i) return result;
                values[k] = Float.parseFloat(path.substring(start, i));
            }
            boolean relative = Character.isLowerCase(command);
            switch (Character.toUpperCase(command)) {
                case 'M':
                    x = relative ? x + values[0] : values[0]; y = relative ? y + values[1] : values[1];
                    startX = x; startY = y;
                    result.add(new float[] { MOVE, x, y, 0, 0 });
                    command = relative ? 'l' : 'L';
                    break;
                case 'L':
                    x = relative ? x + values[0] : values[0]; y = relative ? y + values[1] : values[1];
                    result.add(new float[] { LINE, x, y, 0, 0 });
                    break;
                case 'H':
                    x = relative ? x + values[0] : values[0];
                    result.add(new float[] { LINE, x, y, 0, 0 });
                    break;
                case 'V':
                    y = relative ? y + values[0] : values[0];
                    result.add(new float[] { LINE, x, y, 0, 0 });
                    break;
                case 'Q': {
                    float cx = relative ? x + values[0] : values[0], cy = relative ? y + values[1] : values[1];
                    x = relative ? x + values[2] : values[2]; y = relative ? y + values[3] : values[3];
                    result.add(new float[] { QUAD, cx, cy, x, y });
                    break;
                }
                default: return result;
            }
        }
        return result;
    }
}
