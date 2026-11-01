using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WidgetScreen
{
    // An object whose keys keep the order they were read or added in, so Claude Code's settings.json comes back
    // with its keys where they were.
    public sealed class JsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly List<KeyValuePair<string, object>> items = new List<KeyValuePair<string, object>>();
        public int Count { get { return items.Count; } }
        public bool Contains(string key) { return IndexOf(key) >= 0; }
        public object Get(string key) { int i = IndexOf(key); return i < 0 ? null : items[i].Value; }
        public object this[string key]
        {
            get { return Get(key); }
            set { int i = IndexOf(key); if (i < 0) items.Add(new KeyValuePair<string, object>(key, value)); else items[i] = new KeyValuePair<string, object>(key, value); }
        }
        public bool Remove(string key) { int i = IndexOf(key); if (i < 0) return false; items.RemoveAt(i); return true; }
        private int IndexOf(string key) { for (int i = 0; i < items.Count; i++) if (items[i].Key == key) return i; return -1; }
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() { return items.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return items.GetEnumerator(); }
    }

    // A number as it was written, so 1e3 or 0.50 aren't rewritten as 1000 and 0.5.
    public sealed class JsonNumber
    {
        public readonly string Text;
        public JsonNumber(string text) { Text = text; }
        public JsonNumber(double value) { Text = value.ToString("R", CultureInfo.InvariantCulture); }
        public double Value { get { double v; return Double.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : Double.NaN; } }
        public override string ToString() { return Text; }
    }

    // A small JSON reader and writer for the Claude Code bridge (docs/design/claude-watcher.md). Objects are JsonObject,
    // arrays List<object>, numbers JsonNumber, and strings, booleans and null are themselves. The writer indents with
    // two spaces like Claude Code's own JSON.stringify(…, null, 2), so settings.json keeps its look.
    public static class ClaudeJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("No JSON.");
            var reader = new Reader(text); reader.Space();
            object value = reader.Value(0); reader.Space();
            if (!reader.End) throw new FormatException("Unexpected text after the JSON at " + reader.Position + ".");
            return value;
        }
        public static bool TryParse(string text, out object value)
        {
            try { value = Parse(text); return true; } catch (FormatException) { value = null; return false; }
        }

        public static string Write(object value)
        {
            var builder = new StringBuilder(); Write(builder, value, 0); return builder.ToString();
        }
        // The compact form, for files only Widget Screen reads.
        public static string Compact(object value)
        {
            var builder = new StringBuilder(); Write(builder, value, -1); return builder.ToString();
        }

        public static JsonObject Obj(object value) { return value as JsonObject; }
        public static JsonObject Obj(JsonObject parent, string key) { return parent == null ? null : parent.Get(key) as JsonObject; }
        public static List<object> List(JsonObject parent, string key) { return parent == null ? null : parent.Get(key) as List<object>; }
        public static string Str(JsonObject parent, string key)
        {
            object value = parent == null ? null : parent.Get(key);
            return value is string ? (string)value : value is JsonNumber ? ((JsonNumber)value).Text : null;
        }
        public static double Num(JsonObject parent, string key)
        {
            object value = parent == null ? null : parent.Get(key);
            if (value is JsonNumber) return ((JsonNumber)value).Value;
            double parsed; if (value is string && Double.TryParse((string)value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return parsed;
            return Double.NaN;
        }
        public static bool Bool(JsonObject parent, string key) { return parent != null && parent.Get(key) is bool && (bool)parent.Get(key); }

        private static void Write(StringBuilder b, object value, int depth)
        {
            bool pretty = depth >= 0;
            if (value == null) b.Append("null");
            else if (value is bool) b.Append((bool)value ? "true" : "false");
            else if (value is JsonNumber) b.Append(((JsonNumber)value).Text);
            else if (value is string) Quote(b, (string)value);
            else if (value is int || value is long || value is double || value is float) b.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
            else if (value is JsonObject)
            {
                var obj = (JsonObject)value;
                if (obj.Count == 0) { b.Append("{}"); return; }
                b.Append('{'); bool first = true;
                foreach (var pair in obj)
                {
                    if (!first) b.Append(','); first = false;
                    if (pretty) { b.Append('\n'); b.Append(' ', 2 * (depth + 1)); }
                    Quote(b, pair.Key); b.Append(pretty ? ": " : ":");
                    Write(b, pair.Value, pretty ? depth + 1 : -1);
                }
                if (pretty) { b.Append('\n'); b.Append(' ', 2 * depth); }
                b.Append('}');
            }
            else if (value is IList)
            {
                var list = (IList)value;
                if (list.Count == 0) { b.Append("[]"); return; }
                b.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) b.Append(',');
                    if (pretty) { b.Append('\n'); b.Append(' ', 2 * (depth + 1)); }
                    Write(b, list[i], pretty ? depth + 1 : -1);
                }
                if (pretty) { b.Append('\n'); b.Append(' ', 2 * depth); }
                b.Append(']');
            }
            else Quote(b, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        // Escapes like JSON.stringify: quotes, backslashes and control characters; everything else as it is.
        private static void Quote(StringBuilder b, string text)
        {
            b.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    case '\b': b.Append("\\b"); break;
                    case '\f': b.Append("\\f"); break;
                    default: if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); break;
                }
            }
            b.Append('"');
        }

        private sealed class Reader
        {
            private readonly string text; private int i;
            public Reader(string text) { this.text = text; if (text.Length > 0 && text[0] == '﻿') i = 1; }
            public bool End { get { return i >= text.Length; } }
            public int Position { get { return i; } }
            public void Space() { while (i < text.Length && (text[i] == ' ' || text[i] == '\t' || text[i] == '\n' || text[i] == '\r')) i++; }
            private FormatException Fail(string what) { return new FormatException(what + " at " + i + "."); }

            public object Value(int depth)
            {
                if (depth > 64) throw Fail("JSON nested too deeply");
                if (End) throw Fail("Unexpected end of JSON");
                char c = text[i];
                if (c == '{') return Object(depth);
                if (c == '[') return Array(depth);
                if (c == '"') return String();
                if (c == 't' && Word("true")) return true;
                if (c == 'f' && Word("false")) return false;
                if (c == 'n' && Word("null")) return null;
                if (c == '-' || (c >= '0' && c <= '9')) return Number();
                throw Fail("Unexpected character");
            }
            private bool Word(string word)
            {
                if (string.CompareOrdinal(text, i, word, 0, word.Length) != 0) throw Fail("Unexpected word");
                i += word.Length; return true;
            }
            private JsonObject Object(int depth)
            {
                var obj = new JsonObject(); i++; Space();
                if (!End && text[i] == '}') { i++; return obj; }
                while (true)
                {
                    Space(); if (End || text[i] != '"') throw Fail("Expected a key");
                    string key = String(); Space();
                    if (End || text[i] != ':') throw Fail("Expected ':'");
                    i++; Space();
                    obj[key] = Value(depth + 1); Space();
                    if (End) throw Fail("Unexpected end of JSON");
                    if (text[i] == ',') { i++; continue; }
                    if (text[i] == '}') { i++; return obj; }
                    throw Fail("Expected ',' or '}'");
                }
            }
            private List<object> Array(int depth)
            {
                var list = new List<object>(); i++; Space();
                if (!End && text[i] == ']') { i++; return list; }
                while (true)
                {
                    Space(); list.Add(Value(depth + 1)); Space();
                    if (End) throw Fail("Unexpected end of JSON");
                    if (text[i] == ',') { i++; continue; }
                    if (text[i] == ']') { i++; return list; }
                    throw Fail("Expected ',' or ']'");
                }
            }
            private string String()
            {
                i++; var b = new StringBuilder();
                while (true)
                {
                    if (End) throw Fail("Unterminated string");
                    char c = text[i++];
                    if (c == '"') return b.ToString();
                    if (c != '\\') { b.Append(c); continue; }
                    if (End) throw Fail("Unterminated string");
                    char e = text[i++];
                    switch (e)
                    {
                        case '"': b.Append('"'); break;
                        case '\\': b.Append('\\'); break;
                        case '/': b.Append('/'); break;
                        case 'b': b.Append('\b'); break;
                        case 'f': b.Append('\f'); break;
                        case 'n': b.Append('\n'); break;
                        case 'r': b.Append('\r'); break;
                        case 't': b.Append('\t'); break;
                        case 'u':
                            int code;
                            if (i + 4 > text.Length || !Int32.TryParse(text.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) throw Fail("Bad \\u escape");
                            b.Append((char)code); i += 4; break;
                        default: throw Fail("Bad escape");
                    }
                }
            }
            private JsonNumber Number()
            {
                int start = i;
                if (text[i] == '-') i++;
                while (i < text.Length && ((text[i] >= '0' && text[i] <= '9') || text[i] == '.' || text[i] == 'e' || text[i] == 'E' || text[i] == '+' || text[i] == '-')) i++;
                string token = text.Substring(start, i - start);
                double value;
                if (!Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) throw Fail("Bad number");
                return new JsonNumber(token);
            }
        }
    }
}
