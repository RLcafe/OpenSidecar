using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WidgetScreen
{
    // One drawing step of a pit sprite, in the sprite's own units (a car is 140 × 60, a garage 100 × 100, a car seen
    // from the front or rear 80 × 56): a path, ellipse, rectangle or text, or a pushed or popped transform. Colours are
    // ARGB with their opacity in the alpha. The phone's PitArt.java builds the same list.
    public sealed class PitOp
    {
        public const int PathOp = 0, EllipseOp = 1, RectOp = 2, TextOp = 3, PushOp = 4, PopOp = 5;
        public int Kind;
        public string Path = "", Text = "";
        public double X, Y, W, H, Radius, StrokeWidth, Size;
        public uint Fill, Stroke;
        public bool RoundCap, EvenOdd;
        // A pushed transform: [a, b, c, d, e, f] as in SVG's matrix().
        public double[] Matrix;
    }

    // The pit art (docs/design/claude-watcher-v2.html; decision 017): race cars seen from the side, front and rear, and
    // the pit garage, drawn as vectors that PitRaster turns into small bitmaps with hard pixels. No faces: a car's
    // status shows in its wheels, lights, smoke and how it moves. A port of the approved mock's carSVG, wheel,
    // carFrontSVG, carRearSVG, garageSVG, garageBackSVG and garageFrontSVG, the same numbers as PitArt.java.
    public static class PitArt
    {
        public const uint Amber = 0xFFF5C47D, Lime = 0xFFBAF28B, Page = 0xFF0C0E13, Text = 0xFFF2F4F8, Muted = 0xFFA0AABB;
        // Project colours: Lime, Glacier and Lavender, picked by a stable hash of the project's name. Amber is kept for
        // "needs you".
        public static readonly uint[] Accents = { 0xFFBAF28B, 0xFF8ED8F8, 0xFFC5AEFF };
        public static uint Hash(string project)
        {
            uint hash = 2166136261;
            foreach (char c in (project ?? "").Trim().ToLowerInvariant()) { hash ^= c; hash *= 16777619; }
            return hash;
        }
        public static uint Accent(string project) { return Accents[(int)(Hash(project) / 3 % 3)]; }
        public static uint Alpha(uint color, double opacity) { return ((uint)Math.Round(Math.Max(0, Math.Min(1, opacity)) * ((color >> 24) & 0xFF)) << 24) | (color & 0xFFFFFF); }

        // Side views, facing left: body, glass, door pillar, front and rear wheel centres, wheel radius, headlight and
        // tail light.
        private sealed class Shape { public string Body, Glass, Pillar; public double FrontWheel, RearWheel, Wheel, Roof, RoofX; public double[] Head, Tail; }
        private static readonly Shape[] Shapes = {
            new Shape { Body = "M4 41 Q4 34 14 32 L40 29 Q54 18 74 17 L92 17 Q110 18 124 28 L134 31 Q138 33 138 39 L138 43 Q138 46 134 46 L8 46 Q4 46 4 41Z", Glass = "M50 29 Q60 20 74 20 L90 20 Q104 21 114 29Z", Pillar = "M84 20 L83 29", FrontWheel = 28, RearWheel = 114, Wheel = 10.5, Roof = 17, RoofX = 84, Head = new double[] { 8, 35 }, Tail = new double[] { 134, 34 } },
            new Shape { Body = "M6 42 Q6 36 14 34 L36 31 Q50 19 70 17 L88 17 Q104 19 118 29 L130 32 Q136 34 136 40 L136 43 Q136 46 132 46 L10 46 Q6 46 6 42Z", Glass = "M46 31 Q58 21 72 20 L86 20 Q98 22 108 30Z", Pillar = "M80 20 L80 30", FrontWheel = 30, RearWheel = 110, Wheel = 11, Roof = 17, RoofX = 80, Head = new double[] { 10, 37 }, Tail = new double[] { 132, 35 } },
            new Shape { Body = "M8 43 Q8 35 16 33 L30 31 Q40 15 60 13 L112 13 Q124 13 128 23 L130 33 Q132 37 132 41 L132 43 Q132 46 128 46 L12 46 Q8 46 8 43Z", Glass = "M38 31 Q46 17 62 16 L108 16 Q118 16 122 25 L123 31Z", Pillar = "M84 16 L84 31", FrontWheel = 32, RearWheel = 108, Wheel = 11, Roof = 13, RoofX = 84, Head = new double[] { 11, 37 }, Tail = new double[] { 128, 30 } } };

        // What a side-view car frame shows besides its state.
        public struct CarLook
        {
            public PitCar Car; public bool Fuel, Hot, Fast, Lights, Reverse;
            public string Key { get { return ClaudePace.CarName(Car) + (Fuel ? "f" : "") + (Hot ? "h" : "") + (Fast ? "x" : "") + (Lights ? "l" : "") + (Reverse ? "r" : ""); } }
        }

        private static string N(double v) { return Math.Round(v, 2).ToString(CultureInfo.InvariantCulture); }
        private static PitOp P(string path, uint fill, uint stroke = 0, double width = 0, bool round = false) { return new PitOp { Kind = PitOp.PathOp, Path = path, Fill = fill, Stroke = stroke, StrokeWidth = width, RoundCap = round }; }
        private static PitOp E(double cx, double cy, double rx, double ry, uint fill, uint stroke = 0, double width = 0) { return new PitOp { Kind = PitOp.EllipseOp, X = cx, Y = cy, W = rx, H = ry, Fill = fill, Stroke = stroke, StrokeWidth = width }; }
        private static PitOp C(double cx, double cy, double r, uint fill, uint stroke = 0, double width = 0) { return E(cx, cy, r, r, fill, stroke, width); }
        private static PitOp R(double x, double y, double w, double h, uint fill, double radius = 0) { return new PitOp { Kind = PitOp.RectOp, X = x, Y = y, W = w, H = h, Fill = fill, Radius = radius }; }
        private static PitOp Push(double a, double b, double c, double d, double e, double f) { return new PitOp { Kind = PitOp.PushOp, Matrix = new[] { a, b, c, d, e, f } }; }
        private static PitOp Pop() { return new PitOp { Kind = PitOp.PopOp }; }
        private static PitOp Rotate(double degrees, double cx, double cy)
        {
            double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            return Push(cos, sin, -sin, cos, cx - cos * cx + sin * cy, cy - sin * cx - cos * cy);
        }

        // A wheel: tyre, rim (glowing orange when the brakes are hot), five spokes turned by rot, and the hub.
        private static void Wheel(List<PitOp> ops, double cx, double cy, double r, double rot, bool hot)
        {
            ops.Add(C(cx, cy, r, 0xFF1B1F27, Page, 1.5));
            ops.Add(C(cx, cy, r * 0.64, hot ? 0xFFF5A05A : Muted));
            if (hot) ops.Add(C(cx, cy, r * 0.64, Alpha(Amber, 0.35)));
            for (int i = 0; i < 5; i++)
            {
                double a = i * Math.PI * 2 / 5 - Math.PI / 2 - rot;
                ops.Add(P("M" + N(cx) + " " + N(cy) + "L" + N(cx + r * 0.6 * Math.Cos(a)) + " " + N(cy + r * 0.6 * Math.Sin(a)), 0, 0xFF2A303B, r * 0.2, true));
            }
            ops.Add(C(cx, cy, r * 0.16, 0xFF353D49));
        }

        // One frame (0–7) of a car seen from the side, facing left, in a 140 × 60 box.
        public static List<PitOp> Car(uint color, PitState state, CarLook look, int f)
        {
            var K = Shapes[(int)look.Car];
            const double cy = 46;
            var ops = new List<PitOp>();
            bool spins = state == PitState.Work || state == PitState.Stuck;
            double rot = spins ? f * Math.PI * 2 / 5 / 8 : 0;
            bool hazard = state == PitState.Need && f < 4, flash = state == PitState.Need && (f == 0 || f == 2);
            // Your turn: the brake lights stay on, their glow breathing (bigger on frames 0–3).
            bool brake = state == PitState.Done;
            var lights = new List<PitOp> {
                E(K.Head[0], K.Head[1], 3.4, 2.4, state == PitState.End || state == PitState.Idle ? 0xFF4A5361 : flash ? 0xFFFFFFFF : 0xFFDDE3EA),
                R(K.Tail[0] - 2, K.Tail[1] - 2, 4, 4.5, brake ? 0xFFFF8A8A : 0xFF7A3434),
                R(K.Head[0] + 1, K.Head[1] + 4, 5, 3, hazard ? Amber : 0xFF5E4A2A),
                R(K.Tail[0] - 4, K.Tail[1] + 4, 5, 3, hazard ? Amber : 0xFF5E4A2A) };
            if (flash) lights.Add(P("M" + N(K.Head[0] - 2) + " " + N(K.Head[1] - 2) + " L-16 " + N(K.Head[1] - 8) + " L-16 " + N(K.Head[1] + 8) + " L" + N(K.Head[0] - 2) + " " + N(K.Head[1] + 2) + "Z", Alpha(Text, 0.55)));
            if (hazard) { lights.Add(C(K.Head[0] + 3, K.Head[1] + 5, 8, Alpha(Amber, 0.45))); lights.Add(C(K.Tail[0] - 2, K.Tail[1] + 5, 8, Alpha(Amber, 0.45))); }
            // Needs you: an amber beacon on the roof, lit in every frame and flaring with the hazards.
            if (state == PitState.Need) { if (f < 4) lights.Add(C(K.RoofX, K.Roof - 4, 9, Alpha(Amber, 0.5))); lights.Add(R(K.RoofX - 5, K.Roof - 7, 10, 6, Amber, 2)); }
            if (look.Lights) { lights.Add(P("M" + N(K.Head[0] - 2) + " " + N(K.Head[1] - 2) + " L-12 " + N(K.Head[1] - 6) + " L-12 " + N(K.Head[1] + 6) + " L" + N(K.Head[0] - 2) + " " + N(K.Head[1] + 2) + "Z", Alpha(0xFFFFF6D8, 0.45))); lights.Add(E(K.Head[0], K.Head[1], 3.6, 2.6, 0xFFFFFFFF)); }
            if (look.Reverse) { lights.Add(C(K.Tail[0] + 1, K.Tail[1] + 1, 7, Alpha(0xFFFFFFFF, 0.4))); lights.Add(R(K.Tail[0] - 1, K.Tail[1] - 1, 4, 4, 0xFFFFFFFF)); }
            if (brake) lights.Add(C(K.Tail[0], K.Tail[1], f < 4 ? 8 : 6, Alpha(0xFFF09A9A, f < 4 ? 0.5 : 0.4)));

            var body = new List<PitOp> {
                P(K.Body, color, Alpha(Page, 0.7), 2),
                P("M8 " + N(cy - 5) + "H" + N(K.Tail[0] + 2) + "V" + N(cy) + "H8Z", Alpha(Page, 0.22)),
                P("M20 " + N(cy - 14) + "H" + N(K.Tail[0] - 8), 0, Alpha(0xFFFFFFFF, 0.35), 2),
                P(K.Glass, 0xFF141922) };
            double glassX = Double.Parse(K.Glass.Substring(1).Split(' ')[0], CultureInfo.InvariantCulture);
            body.Add(P("M" + N(glassX + 10) + " " + N(cy - 15) + "l8 -6", 0, Alpha(0xFF8ED8F8, 0.35), 2));
            body.Add(P(K.Pillar, 0, color, 3));
            body.Add(C(K.FrontWheel, cy, K.Wheel + 2, Page)); body.Add(C(K.RearWheel, cy, K.Wheel + 2, Page));
            body.AddRange(lights);

            ops.Add(E(70, 57, 62, 3, Alpha(0xFF000000, 0.4)));
            // Exhaust puffs on a long tool run or a heavy burn; tyre smoke rolling back when stuck.
            if (state == PitState.Rev || (state == PitState.Work && look.Fast)) { double k = (f % 4) / 4.0; ops.Add(C(K.Tail[0] + 5 + k * 14, cy - 2 - k * 5, 2 + k * 4, Alpha(Muted, 0.8 - k * 0.7))); }
            // Working: dust kicked up behind both tyres, drifting back and up and breaking up over four frames (a speck
            // when light, a trail when heavy; the frame rate follows the burn), and the ground dashes scrolling back.
            if (state == PitState.Work)
            {
                for (double x = -12 + (f % 3) * 4; x < 140; x += 12) ops.Add(R(x, cy + K.Wheel + 1, 6, 1, Alpha(0xFF606D81, 0.9)));
                int puffs = look.Fast ? 3 : 1;
                foreach (double wx in new[] { K.FrontWheel, K.RearWheel })
                    for (int j = 0; j < puffs; j++)
                    {
                        double q = ((f + j * 2) % 4) / 4.0;
                        ops.Add(C(wx + K.Wheel * 0.7 + q * 14, cy + K.Wheel - 2 - q * 5, 1.5 + q * 2.5, Alpha(0xFFB8A88E, 0.85 - q * 0.6)));
                    }
            }
            if (state == PitState.Stuck) for (int j = 0; j < 3; j++) { double q = ((f + j * 3) % 8) / 8.0; ops.Add(C(K.RearWheel + 6 + q * 22, cy + 6 - q * 12, 5 + q * 8, Alpha(0xFFC9D0DA, 0.85 - q * 0.75))); }
            double dy = spins && f % 2 == 1 ? 1 : 0;
            bool tilt = state == PitState.Rev && f < 2, sputter = look.Fuel && (f == 5 || f == 7);
            if (sputter) dy = 0;
            if (state == PitState.Pit)
            {
                // Up on the jack, the front wheel off and lying beside it.
                ops.Add(Push(1, 0, 0, 1, 0, f < 4 ? -6 : -5)); ops.AddRange(body); Wheel(ops, K.RearWheel, cy, K.Wheel, rot, look.Hot); ops.Add(Pop());
                ops.Add(E(K.FrontWheel - 18, cy + K.Wheel - 2, K.Wheel, 3.5, 0xFF1B1F27));
                ops.Add(E(K.FrontWheel - 18, cy + K.Wheel - 3, K.Wheel * 0.6, 2, Muted));
                ops.Add(P("M" + N(K.FrontWheel - 4) + " " + N(cy + 10) + "h14l-3 -10h-8z", 0xFF606D81));
            }
            else
            {
                ops.Add(Push(1, 0, 0, 1, sputter ? 1.5 : 0, -dy));
                if (tilt) ops.Add(Rotate(-2, K.RearWheel, cy));
                ops.AddRange(body);
                if (tilt) ops.Add(Pop());
                ops.Add(Pop());
                Wheel(ops, K.FrontWheel, cy, K.Wheel, rot, look.Hot); Wheel(ops, K.RearWheel, cy, K.Wheel, rot, look.Hot);
            }
            if (state == PitState.End)
            {
                // The cover drops over the car (frames 0–6), then stays.
                double drop = f < 7 ? -(7 - f) * 4 : 0;
                ops.Add(Push(1, 0, 0, 1, 0, drop));
                var cover = P("M4 46 Q2 30 20 28 Q44 12 72 12 Q112 12 132 26 Q140 32 138 46 Z", 0xFF4A5361, Alpha(Page, 0.6), 2);
                ops.Add(cover);
                ops.Add(P("M40 18 Q46 32 42 46M96 14 Q102 30 98 46", 0, Alpha(Page, 0.3), 2));
                ops.Add(Pop());
            }
            if (look.Fuel && f < 4) { ops.Add(R(62, 0, 8, 8, Amber)); ops.Add(R(60, -2, 12, 12, Alpha(Amber, 0.3))); }
            return ops;
        }

        // A car seen from the front, driving toward the viewer, in an 80 × 56 box; lit headlights for rolling out.
        public static List<PitOp> Front(uint color, bool lit)
        {
            var ops = new List<PitOp> {
                R(5, 40, 12, 15, 0xFF1B1F27, 2), R(63, 40, 12, 15, 0xFF1B1F27, 2),
                P("M18 29 L25 12 Q27 9 31 9 L49 9 Q53 9 55 12 L62 29Z", color, Alpha(Page, 0.7), 2), P("M23 28 L28 14 L52 14 L57 28Z", 0xFF141922), P("M30 16 L27 24", 0, Alpha(0xFF8ED8F8, 0.4), 2),
                R(11, 22, 7, 4, color), R(62, 22, 7, 4, color),
                P("M4 46 Q4 31 12 29 L68 29 Q76 31 76 46 L76 49 Q76 51 72 51 L8 51 Q4 51 4 49Z", color, Alpha(Page, 0.7), 2), R(5, 45, 70, 5, Alpha(Page, 0.25)),
                R(8, 33, 15, 6, lit ? 0xFFFFFFFF : 0xFFC9D0DA, 1), R(57, 33, 15, 6, lit ? 0xFFFFFFFF : 0xFFC9D0DA, 1), R(28, 37, 24, 8, 0xFF1B1F27, 1) };
            if (lit) { ops.Add(E(15, 36, 11, 7, Alpha(0xFFFFF6D8, 0.5))); ops.Add(E(65, 36, 11, 7, Alpha(0xFFFFF6D8, 0.5))); }
            return ops;
        }

        // A car seen from the rear, driving away, in an 80 × 56 box; brighter tail lights when braking.
        public static List<PitOp> Rear(uint color, bool brake)
        {
            uint tail = brake ? 0xFFFF8A8A : 0xFFE06A6A;
            double rx = brake ? 13 : 10, ry = brake ? 8 : 6;
            return new List<PitOp> {
                R(5, 40, 12, 15, 0xFF1B1F27, 2), R(63, 40, 12, 15, 0xFF1B1F27, 2),
                P("M18 29 L25 12 Q27 9 31 9 L49 9 Q53 9 55 12 L62 29Z", color, Alpha(Page, 0.7), 2), P("M24 27 L29 15 L51 15 L56 27Z", 0xFF141922),
                P("M4 46 Q4 31 12 29 L68 29 Q76 31 76 46 L76 49 Q76 51 72 51 L8 51 Q4 51 4 49Z", color, Alpha(Page, 0.7), 2), R(5, 45, 70, 5, Alpha(Page, 0.25)),
                R(7, 32, 18, 5, tail), R(55, 32, 18, 5, tail), R(25, 33, 30, 3, Alpha(Page, 0.35)),
                C(30, 49, 2.5, 0xFF353D49), C(50, 49, 2.5, 0xFF353D49),
                E(16, 34.5, rx, ry, Alpha(0xFFF09A9A, 0.45)), E(64, 34.5, rx, ry, Alpha(0xFFF09A9A, 0.45)) };
        }

        // The shutter's height (of 56) for a garage in each state: down when folded or all ended, half open when busy.
        public static double Shutter(PitState state, bool closed) { return closed || state == PitState.End ? 52 : state == PitState.Work ? 20 : state == PitState.Need ? 24 : 34; }

        // The pit garage seen from the front, in a 100 × 100 box, frame 0 or 1: the amber roof beacon blinks when a car
        // inside needs you (folded or not), light spills under the shutter while busy, and a folded garage shows its
        // car count on the sign.
        public static List<PitOp> Garage(uint color, PitState state, bool closed, int count, int f)
        {
            double shut = Shutter(state, closed);
            var ops = new List<PitOp>();
            if (state == PitState.Need)
            {
                if (f % 2 == 0) { ops.Add(C(50, 8, 12, Alpha(Amber, 0.45))); ops.Add(P("M38 8h-10M62 8h10", 0, Amber, 3)); }
                ops.Add(P("M44 14 Q44 4 50 4 Q56 4 56 14Z", Amber)); ops.Add(R(42, 13, 16, 4, 0xFF353D49, 1));
            }
            if (state == PitState.Work && !closed) ops.Add(R(20, 40 + shut, 60, 5, Alpha(Lime, f % 2 == 1 ? 0.45 : 0.8)));
            bool lit = !closed && (state == PitState.Work || state == PitState.Need || state == PitState.Done);
            ops.Add(new PitOp { Kind = PitOp.RectOp, X = 8, Y = 22, W = 84, H = 74, Radius = 3, Fill = color, Stroke = Alpha(Page, 0.5), StrokeWidth = 1.5 });
            ops.Add(R(8, 22, 12, 74, Alpha(Page, 0.12)));
            ops.Add(R(5, 16, 90, 16, 0xFF353D49, 2));
            ops.Add(R(30, 19, 40, 11, closed ? Text : Muted, 2));
            if (closed && count > 0) ops.Add(new PitOp { Kind = PitOp.TextOp, X = 50, Y = 29, Text = count > 99 ? "99" : count.ToString(CultureInfo.InvariantCulture), Size = 10, Fill = Page });
            ops.Add(R(20, 40, 60, 56, Page));
            if (lit) { ops.Add(R(20, 78, 60, 18, Alpha(color, 0.22))); ops.Add(P("M28 50h18M54 50h18M28 56h44", 0, 0xFF353D49, 2)); }
            ops.Add(R(20, 40, 60, shut, 0xFF7C8798));
            for (double y = 46; y < 42 + shut - 1; y += 5) ops.Add(P("M22 " + N(y) + "h56", 0, 0xFF4A5361, 1.2));
            ops.Add(R(44, 34, 12, 4, lit ? Text : 0xFF606D81, 1));
            ops.Add(R(4, 94, 92, 4, 0xFF353D49));
            return ops;
        }

        // For rolling in and out: the dark bay behind the cars, and the walls, sign and shutter (k of 8 down) in front
        // of them, with the door cut out so a car in the bay shows through it and hides behind the walls.
        public static List<PitOp> GarageBack(uint color)
        {
            return new List<PitOp> { R(20, 40, 60, 56, Page), R(20, 78, 60, 18, Alpha(color, 0.22)), P("M28 50h18M54 50h18M28 56h44", 0, 0xFF353D49, 2) };
        }
        public static List<PitOp> GarageFront(uint color, int k, int count, bool lit)
        {
            double shut = Math.Round(56.0 * k / 8);
            var ops = new List<PitOp> {
                new PitOp { Kind = PitOp.PathOp, Path = "M8 22h84v74H8Z M20 40h60v56H20Z", EvenOdd = true, Fill = color, Stroke = Alpha(Page, 0.5), StrokeWidth = 1.5 },
                R(8, 22, 12, 74, Alpha(Page, 0.12)), R(5, 16, 90, 16, 0xFF353D49, 2), R(30, 19, 40, 11, count > 0 ? Text : Muted, 2) };
            if (count > 0) ops.Add(new PitOp { Kind = PitOp.TextOp, X = 50, Y = 29, Text = count > 99 ? "99" : count.ToString(CultureInfo.InvariantCulture), Size = 10, Fill = Page });
            if (shut > 0)
            {
                ops.Add(R(20, 40, 60, shut, 0xFF7C8798));
                for (double y = 46; y < 40 + shut - 1; y += 5) ops.Add(P("M20 " + N(y) + "h60", 0, 0xFF4A5361, 1.2));
            }
            ops.Add(R(44, 34, 12, 4, lit ? Text : 0xFF606D81, 1));
            if (lit) ops.Add(P("M44 38 L36 50 H64 L56 38Z", Alpha(Text, 0.12)));
            return ops;
        }
    }

    // Turns a sprite's drawing into a small bitmap with hard pixels (decision 017): drawn at its native pixel size, then
    // every pixel either opaque (alpha above 80, its colour on a 16-step ramp) or clear. Shown scaled up with
    // nearest-neighbour, so the pixels stay square. Frames are cached by what they show.
    public static class PitRaster
    {
        private static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();
        public static int Cached { get { return cache.Count; } }

        // A sprite's pixel size for a box this wide: 2 units a pixel under 150 wide, 3 from there.
        public static int PixelSize(double width) { return width >= 150 ? 3 : 2; }

        public static BitmapSource Frame(string key, Func<List<PitOp>> draw, Rect box, int width, int height)
        {
            BitmapSource bitmap;
            if (cache.TryGetValue(key, out bitmap)) return bitmap;
            if (cache.Count > 1500) cache.Clear();
            bitmap = Render(draw(), box, Math.Max(1, width), Math.Max(1, height));
            cache[key] = bitmap;
            return bitmap;
        }

        public static BitmapSource Render(List<PitOp> ops, Rect box, int width, int height)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                double sx = width / box.Width, sy = height / box.Height;
                dc.PushTransform(new MatrixTransform(sx, 0, 0, sy, -box.X * sx, -box.Y * sy));
                int pushed = 0;
                foreach (var op in ops) Draw(dc, op, ref pushed);
                while (pushed-- > 0) dc.Pop();
                dc.Pop();
            }
            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var pixels = new byte[width * height * 4];
            target.CopyPixels(pixels, width * 4, 0);
            Crisp(pixels);
            var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, width * 4);
            result.Freeze();
            return result;
        }

        // Alpha above 80 becomes opaque, the rest clear; colours (un-premultiplied) snap to 17-unit steps.
        public static void Crisp(byte[] pixels)
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int a = pixels[i + 3];
                if (a <= 80) { pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 0; continue; }
                for (int c = 0; c < 3; c++) { int v = Math.Min(255, pixels[i + c] * 255 / a); pixels[i + c] = (byte)(Math.Round(v / 17.0) * 17); }
                pixels[i + 3] = 255;
            }
        }

        private static Brush Brush(uint color)
        {
            if (color == 0) return null;
            var brush = new SolidColorBrush(Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color));
            brush.Freeze();
            return brush;
        }
        private static Pen Pen(PitOp op)
        {
            if (op.Stroke == 0 || op.StrokeWidth <= 0) return null;
            var pen = new Pen(Brush(op.Stroke), op.StrokeWidth);
            if (op.RoundCap) { pen.StartLineCap = PenLineCap.Round; pen.EndLineCap = PenLineCap.Round; }
            pen.Freeze();
            return pen;
        }
        private static readonly Typeface Bold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

        private static void Draw(DrawingContext dc, PitOp op, ref int pushed)
        {
            switch (op.Kind)
            {
                case PitOp.PushOp: dc.PushTransform(new MatrixTransform(op.Matrix[0], op.Matrix[1], op.Matrix[2], op.Matrix[3], op.Matrix[4], op.Matrix[5])); pushed++; return;
                case PitOp.PopOp: if (pushed > 0) { dc.Pop(); pushed--; } return;
                case PitOp.EllipseOp: dc.DrawEllipse(Brush(op.Fill), Pen(op), new Point(op.X, op.Y), op.W, op.H); return;
                case PitOp.RectOp: dc.DrawRoundedRectangle(Brush(op.Fill), Pen(op), new Rect(op.X, op.Y, Math.Max(0, op.W), Math.Max(0, op.H)), op.Radius, op.Radius); return;
                case PitOp.TextOp:
                    {
                        var text = new FormattedText(op.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, op.Size, Brush(op.Fill), 1.0);
                        // Centred on X, with Y the baseline, like SVG's text-anchor="middle".
                        dc.DrawText(text, new Point(op.X - text.Width / 2, op.Y - text.Baseline));
                        return;
                    }
                default:
                    {
                        var geometry = Geometry.Parse((op.EvenOdd ? "F0 " : "F1 ") + op.Path);
                        dc.DrawGeometry(Brush(op.Fill), Pen(op), geometry);
                        return;
                    }
            }
        }
    }

    // A pit sprite on the card: a car or a garage, shown as its cached frames with hard pixels. The frame steps on the
    // card's PitClock at the sprite's own rate; Quiet mode and a hidden card hold it still. A car's box is 140 × 60 in
    // shape, and its frames reach a quarter of its height above and below so lights, smoke and the fuel lamp fit.
    public sealed class PitSprite : FrameworkElement
    {
        private BitmapSource[] frames = new BitmapSource[0];
        private int frame, still;
        private double over;
        public string Key { get; private set; }
        // Frames a second (0: still), and whether the frames play once and stop on the last (Done, Ended).
        public double Fps;
        public bool Once;
        public double Clock;
        public int Frame { get { return frame; } }
        public int FrameCount { get { return frames.Length; } }

        public PitSprite()
        {
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
            SnapsToDevicePixels = true; IsHitTestVisible = false;
        }

        public void Show(string key, BitmapSource[] next, double overflow, int stillFrame, bool restart)
        {
            if (key == Key && !restart) return;
            bool same = key == Key;
            Key = key; frames = next; over = overflow; still = Math.Max(0, Math.Min(next.Length - 1, stillFrame));
            frame = restart ? 0 : same ? Math.Min(frame, next.Length - 1) : still;
            Clock = 0;
            InvalidateVisual();
        }
        public void Step(double seconds)
        {
            if (frames.Length < 2 || Fps <= 0) return;
            if (Once && frame >= frames.Length - 1) return;
            Clock += seconds * Fps;
            if (Clock < 1) return;
            int steps = (int)Clock; Clock -= steps;
            int next = Once ? Math.Min(frames.Length - 1, frame + steps) : (frame + steps) % frames.Length;
            if (next != frame) { frame = next; InvalidateVisual(); }
        }
        // Back to the frame that stands for the state when nothing moves (Quiet mode, a hidden card).
        public void Hold() { if (frame != still && !Once && frames.Length > 0) { frame = still; InvalidateVisual(); } }
        public bool Moving { get { return frames.Length > 1 && Fps > 0 && !(Once && frame >= frames.Length - 1); } }

        protected override void OnRender(DrawingContext dc)
        {
            if (frames.Length == 0 || frames[frame] == null) return;
            double extra = ActualHeight * over;
            dc.DrawImage(frames[frame], new Rect(0, -extra, ActualWidth, ActualHeight + 2 * extra));
        }

        // ----- Making the frames -----

        public const double CarOverflow = 0.25;
        public static readonly int[] Frames8 = { 0, 1, 2, 3, 4, 5, 6, 7 };

        // A car's frames for a box this wide. Its frames per second follow its state (and its burn while working).
        public static BitmapSource[] CarFrames(uint color, PitState state, PitArt.CarLook look, double width, out string key)
        {
            int px = PitRaster.PixelSize(width), nw = Math.Max(8, (int)Math.Round(width / px)), nh = Math.Max(4, (int)Math.Round(width * 60 / 140 / px)), pad = (int)Math.Ceiling(nh * CarOverflow);
            double unit = 60.0 / nh;
            var box = new Rect(0, -pad * unit, 140, 60 + 2 * pad * unit);
            string name = "car|" + color.ToString("X8") + "|" + state + "|" + look.Key + "|" + nw + "x" + nh;
            key = name;
            return Frames8.Select(f => PitRaster.Frame(name + "|" + f, () => PitArt.Car(color, state, look, f), box, nw, nh + 2 * pad)).ToArray();
        }
        public static BitmapSource[] GarageFrames(uint color, PitState state, bool closed, int count, double width, out string key)
        {
            int px = PitRaster.PixelSize(width), n = Math.Max(8, (int)Math.Round(width / px));
            string name = "garage|" + color.ToString("X8") + "|" + state + "|" + closed + "|" + count + "|" + n;
            key = name;
            return new[] { 0, 1 }.Select(f => PitRaster.Frame(name + "|" + f, () => PitArt.Garage(color, state, closed, count, f), new Rect(0, 0, 100, 100), n, n)).ToArray();
        }
        // How fast a car's frames step in each state; working cars go with their burn.
        public static double CarFps(PitState state, double share, bool fuel)
        {
            switch (state)
            {
                case PitState.Work: return ClaudePace.WheelFps(share);
                case PitState.Stuck: return 24;
                case PitState.Need: return 6;
                case PitState.Rev: return 8;
                case PitState.Done: return 2;
                case PitState.End: return 12;
                case PitState.Pit: return 2;
                default: return fuel ? 6 : 0;
            }
        }
        // The frame that stands for a state when nothing moves: Ended's dropped cover, the first frame otherwise.
        public static int StillFrame(PitState state) { return state == PitState.End ? 7 : 0; }
        // Ended plays its cover drop once; everything else loops.
        public static bool PlaysOnce(PitState state) { return state == PitState.End; }
    }

    // The one frame clock for a card's pit sprites (the user asked for the motion; decision 017). It runs only while the
    // card is shown, the window isn't minimised, Quiet mode is off and something moves, at the rate the fastest sprite
    // needs (at most 24 frames a second), and stops otherwise, so it costs nothing during games.
    public sealed class PitClock
    {
        private readonly DispatcherTimer timer;
        private readonly List<PitSprite> sprites = new List<PitSprite>();
        private DateTime last;
        private bool live;
        public Action<double> Extra;
        public bool Running { get { return timer.IsEnabled; } }
        public bool Live { get { return live; } }

        public PitClock(Dispatcher dispatcher)
        {
            timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher);
            timer.Tick += (sender, e) => Tick();
        }
        public void Clear() { sprites.Clear(); Update(); }
        public void Add(PitSprite sprite) { sprites.Add(sprite); }
        public IEnumerable<PitSprite> Sprites { get { return sprites; } }

        // Whether the card may move at all: shown, window up, not Quiet, not rendering a picture.
        public void SetLive(bool next) { if (next == live) return; live = next; if (!live) foreach (var s in sprites) s.Hold(); Update(); }
        public void Update()
        {
            bool needed = live && !Program.Rendering && (Extra != null || sprites.Any(s => s.Moving));
            if (!needed) { timer.Stop(); return; }
            double fps = Extra != null ? 20 : Math.Min(24, Math.Max(4, sprites.Where(s => s.Moving).Select(s => s.Fps).DefaultIfEmpty(4).Max()));
            var interval = TimeSpan.FromMilliseconds(Math.Round(1000 / fps));
            if (timer.Interval != interval) timer.Interval = interval;
            if (!timer.IsEnabled) { last = DateTime.UtcNow; timer.Start(); }
        }
        private void Tick()
        {
            DateTime now = DateTime.UtcNow;
            double seconds = Math.Min(0.25, (now - last).TotalSeconds); last = now;
            foreach (var s in sprites) if (s.IsVisible) s.Step(seconds);
            if (Extra != null) Extra(seconds);
            if (Extra == null && !sprites.Any(s => s.Moving)) timer.Stop();
        }
    }
}
