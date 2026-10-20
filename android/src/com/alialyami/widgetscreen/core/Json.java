package com.alialyami.widgetscreen.core;

import java.util.ArrayList;
import java.util.Collection;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

// A small JSON reader and writer: objects become maps, arrays lists, numbers Double. It runs on Android
// and on the desktop JVM alike, so Spotify and weather parsing can be checked without a device.
public final class Json {
    private final String text;
    private int at;

    private Json(String text) { this.text = text; }

    public static Object parse(String text) {
        if (text == null) throw new IllegalArgumentException("No JSON to read.");
        Json reader = new Json(text);
        reader.space(); Object value = reader.value(); reader.space();
        if (reader.at != text.length()) throw reader.error("Unexpected text after the JSON value");
        return value;
    }

    private IllegalArgumentException error(String message) { return new IllegalArgumentException(message + " (at " + at + ")."); }
    private void space() { while (at < text.length() && " \t\r\n".indexOf(text.charAt(at)) >= 0) at++; }
    private boolean peek(char c) { return at < text.length() && text.charAt(at) == c; }
    private void expect(char c) { if (!peek(c)) throw error("Expected '" + c + "'"); at++; }

    private Object value() {
        if (at >= text.length()) throw error("Unexpected end of JSON");
        switch (text.charAt(at)) {
            case '{': return object();
            case '[': return array();
            case '"': return string();
            case 't': return literal("true", Boolean.TRUE);
            case 'f': return literal("false", Boolean.FALSE);
            case 'n': return literal("null", null);
            default: return number();
        }
    }

    private Object literal(String word, Object value) {
        if (!text.startsWith(word, at)) throw error("Invalid value");
        at += word.length();
        return value;
    }

    private Map<String, Object> object() {
        Map<String, Object> result = new LinkedHashMap<String, Object>();
        at++; space();
        if (peek('}')) { at++; return result; }
        while (true) {
            space();
            if (!peek('"')) throw error("Expected a property name");
            String key = string(); space(); expect(':'); space();
            result.put(key, value()); space();
            if (peek(',')) { at++; continue; }
            expect('}');
            return result;
        }
    }

    private List<Object> array() {
        List<Object> result = new ArrayList<Object>();
        at++; space();
        if (peek(']')) { at++; return result; }
        while (true) {
            space(); result.add(value()); space();
            if (peek(',')) { at++; continue; }
            expect(']');
            return result;
        }
    }

    private String string() {
        at++;
        StringBuilder out = new StringBuilder();
        while (true) {
            if (at >= text.length()) throw error("Unterminated string");
            char c = text.charAt(at++);
            if (c == '"') return out.toString();
            if (c != '\\') { out.append(c); continue; }
            if (at >= text.length()) throw error("Unterminated string");
            char escape = text.charAt(at++);
            switch (escape) {
                case '"': case '\\': case '/': out.append(escape); break;
                case 'b': out.append('\b'); break;
                case 'f': out.append('\f'); break;
                case 'n': out.append('\n'); break;
                case 'r': out.append('\r'); break;
                case 't': out.append('\t'); break;
                case 'u':
                    if (at + 4 > text.length()) throw error("Invalid escape");
                    try { out.append((char) Integer.parseInt(text.substring(at, at + 4), 16)); }
                    catch (NumberFormatException e) { throw error("Invalid escape"); }
                    at += 4;
                    break;
                default: throw error("Invalid escape");
            }
        }
    }

    private Double number() {
        int start = at;
        while (at < text.length() && "+-0123456789.eE".indexOf(text.charAt(at)) >= 0) at++;
        if (start == at) throw error("Invalid value");
        try { return Double.valueOf(text.substring(start, at)); }
        catch (NumberFormatException e) { throw error("Invalid number"); }
    }

    public static String write(Object value) {
        StringBuilder out = new StringBuilder();
        write(out, value);
        return out.toString();
    }

    private static void write(StringBuilder out, Object value) {
        if (value == null) out.append("null");
        else if (value instanceof String) quote(out, (String) value);
        else if (value instanceof Boolean) out.append(value.toString());
        else if (value instanceof Number) {
            double number = ((Number) value).doubleValue();
            if (Double.isNaN(number) || Double.isInfinite(number)) out.append("null");
            else if (number == Math.rint(number) && Math.abs(number) < 1e15) out.append((long) number);
            else out.append(number);
        } else if (value instanceof Map) {
            out.append('{'); boolean first = true;
            for (Map.Entry<?, ?> entry : ((Map<?, ?>) value).entrySet()) {
                if (!first) out.append(',');
                first = false;
                quote(out, String.valueOf(entry.getKey())); out.append(':'); write(out, entry.getValue());
            }
            out.append('}');
        } else if (value instanceof Collection || value instanceof Object[]) {
            Collection<?> items = value instanceof Object[] ? java.util.Arrays.asList((Object[]) value) : (Collection<?>) value;
            out.append('['); boolean first = true;
            for (Object item : items) { if (!first) out.append(','); first = false; write(out, item); }
            out.append(']');
        } else quote(out, value.toString());
    }

    private static void quote(StringBuilder out, String value) {
        out.append('"');
        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);
            switch (c) {
                case '"': out.append("\\\""); break;
                case '\\': out.append("\\\\"); break;
                case '\n': out.append("\\n"); break;
                case '\r': out.append("\\r"); break;
                case '\t': out.append("\\t"); break;
                default:
                    if (c < 0x20) out.append(String.format("\\u%04x", (int) c));
                    else out.append(c);
            }
        }
        out.append('"');
    }

    // Lenient accessors in the style of the Windows SpotifyService helpers: a missing or mistyped value
    // reads as empty instead of throwing, so one odd field never breaks a whole list.
    @SuppressWarnings("unchecked")
    public static Map<String, Object> obj(Object value) { return value instanceof Map ? (Map<String, Object>) value : Collections.<String, Object>emptyMap(); }
    public static Object value(Map<String, Object> map, String key) { return map == null ? null : map.get(key); }
    public static String field(Map<String, Object> map, String key) { Object value = value(map, key); return value instanceof String ? (String) value : null; }
    public static double number(Map<String, Object> map, String key) { Object value = value(map, key); return value instanceof Number ? ((Number) value).doubleValue() : 0; }
    public static boolean has(Map<String, Object> map, String key) { return value(map, key) != null; }
    @SuppressWarnings("unchecked")
    public static List<Object> array(Map<String, Object> map, String key) { Object value = value(map, key); return value instanceof List ? (List<Object>) value : Collections.emptyList(); }
    public static Map<String, Object> map(Object... pairs) {
        Map<String, Object> result = new LinkedHashMap<String, Object>();
        for (int i = 0; i + 1 < pairs.length; i += 2) result.put((String) pairs[i], pairs[i + 1]);
        return result;
    }
}
