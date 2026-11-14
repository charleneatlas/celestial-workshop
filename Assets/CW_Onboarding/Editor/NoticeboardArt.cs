using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CW.Onboarding.EditorTools
{
    /// <summary>
    /// Procedural art for the workshop noticeboard: wood, parchment, nails and
    /// every ink illustration, each written to its own PNG so any one can be
    /// replaced by hand-made art later (keep the file name, or reassign the
    /// material's texture). Deterministic: the same seed gives the same art.
    ///
    /// The controller and hands illustrations are pen-and-ink renders of the
    /// real meshes (Meta Quest Touch Plus from the Core SDK, the ISDK OpenXR
    /// hand), drawn by a small CPU rasteriser: outlines from depth/normal
    /// breaks plus hatching on the shadow side.
    /// </summary>
    public static class NoticeboardArt
    {
        public const string OutDir = "Assets/CW_Onboarding/Art/Generated";

        // Ink palette (baked into the textures; materials tint white).
        public static readonly Color Ink = new Color(0.24f, 0.13f, 0.06f, 1f);
        public static readonly Color InkSoft = new Color(0.36f, 0.22f, 0.11f, 1f);
        public static readonly Color Gold = new Color(0.80f, 0.52f, 0.16f, 1f);
        public static readonly Color RedInk = new Color(0.72f, 0.20f, 0.08f, 1f);
        public static readonly Color Wash = new Color(0.98f, 0.94f, 0.85f, 1f);

        const string TouchLeft = "Packages/com.meta.xr.sdk.core/Meshes/MetaQuestTouchPlus/MetaQuestTouchPlus_Left.fbx";
        const string TouchRight = "Packages/com.meta.xr.sdk.core/Meshes/MetaQuestTouchPlus/MetaQuestTouchPlus_Right.fbx";
        const string HandRight = "Packages/com.meta.xr.sdk.interaction/Runtime/Meshes/OpenXRRightHand.fbx";

        // Controller/hand view tuning (degrees), exposed for iteration.
        public static Vector3 ControllerViewEuler = new Vector3(-40f, 160f, 0f);
        public static float ControllerTilt = 22f;
        public static Vector3 HandViewEuler = new Vector3(-90f, 0f, 0f);

        // Pointing hand on the step 3 sketch: index out, other fingers curled
        // (degrees about each finger bone's local X), seen from PointViewEuler.
        public static float PointCurl = 80f;
        public static float PointThumbCurl = 35f;
        // Seen from the back of the hand: the palm faces the page and the
        // curled fingers fold away from the viewer, behind the knuckles.
        public static Vector3 PointViewEuler = new Vector3(-105f, 0f, 0f);
        public static float PointViewTurn = 180f;
        public static float PointTilt = 18f;
        public static int PointSize = 128;
        public static float PointGap = 7f;

        /// <summary>Renders only the pointing hand (for tuning its pose).</summary>
        public static void PointingHandTest(string path, int size)
        {
            var c = new InkCanvas(size, size);
            MeshInk.Render(c, new RectInt(8, 8, size - 16, size - 16), PointingHandParts(), Quaternion.Euler(0, 0, PointTilt) * Quaternion.Euler(0, PointViewTurn, 0) * Quaternion.Euler(PointViewEuler), false, 50);
            c.Save(path);
        }

        /// <summary>The ISDK right hand posed to point (baked skinned mesh).</summary>
        public static List<MeshInk.Part> PointingHandParts()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(HandRight);
            var go = UnityEngine.Object.Instantiate(asset);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name;
                    bool curled = n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little");
                    bool joint = n.EndsWith("Proximal") || n.EndsWith("Intermediate") || n.EndsWith("Distal");
                    if (curled && joint)
                    {
                        t.localRotation *= Quaternion.Euler(PointCurl, 0f, 0f);
                    }
                    else if (n.Contains("Thumb") && (n.EndsWith("Proximal") || n.EndsWith("Distal")))
                    {
                        t.localRotation *= Quaternion.Euler(PointThumbCurl, 0f, 0f);
                    }
                }

                var parts = new List<MeshInk.Part>();
                int id = 0;
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    smr.BakeMesh(baked, true);
                    parts.Add(new MeshInk.Part(baked, go.transform.worldToLocalMatrix * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one), id++));
                }

                return parts;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>Writes every texture into dir. Returns a short report.</summary>
        public static string GenerateAll(string dir)
        {
            Directory.CreateDirectory(dir);
            var report = new System.Text.StringBuilder();
            void Save(string name, InkCanvas c)
            {
                string path = Path.Combine(dir, name + ".png");
                c.Save(path);
                report.Append(name).Append(' ').Append(c.W).Append('x').Append(c.H).Append('\n');
            }

            Save("Wood", Wood(512, 1024, 11));
            Save("Parchment", Parchment(1024, 1136, 7));
            Save("ParchmentShadow", SoftRect(256, 288, 0.16f, 0.62f));
            Save("NailHead", NailHead(128));
            Save("NailDent", NailDent(64));
            Save("TitleStars", TitleStars(1024, 200, 3));
            Save("Ribbon", Ribbon(1024, 128, 5));
            Save("Divider", Divider(1024, 56, true, 9));
            Save("RuleSeparator", Divider(1024, 20, false, 10));
            Save("Flourish", Flourish(256, 56, 12));
            for (int i = 0; i < 3; i++)
            {
                Save("NumberOval" + (i + 1), NumberOval(128, 112, 20 + i));
            }

            Save("Controllers", Controllers(512, 280, 30));
            Save("WM", WM(512, 300, 31));
            Save("SketchGlobe", SketchGlobe(512, 280, 32));
            Save("Hands", Hands(256, 160, 33));
            Save("Underline", Underline(256, 24, 40));
            Save("SignatureSmudge", Smudge(1024, 180, 41));
            Save("GuideLine", GuideLine(1024, 20));
            Save("TraceFlourish", TraceFlourish(640, 48, 42));
            Save("GlowDot", GlowDot(64));
            Save("SparkleStar", SparkleStar(64));
            return report.ToString();
        }

        // ================= Materials =================

        static float Fbm(float x, float y, int octaves = 4)
        {
            float a = 0.5f, f = 1f, s = 0f, n = 0f;
            for (int i = 0; i < octaves; i++)
            {
                s += a * Mathf.PerlinNoise(x * f + 17.3f * i, y * f + 9.1f * i);
                n += a;
                a *= 0.5f;
                f *= 2.03f;
            }

            return s / n;
        }

        static InkCanvas Wood(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            var rnd = new System.Random(seed);
            float ox = (float)rnd.NextDouble() * 100f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    // Grain runs along v; warped stripes plus knots of noise.
                    float warp = Fbm(u * 3f + ox, v * 1.2f, 3) * 2.2f;
                    float grain = Mathf.Sin((u * 38f + warp * 6f) * Mathf.PI);
                    float fine = Fbm(u * 60f + ox, v * 4f, 2);
                    float blotch = Fbm(u * 2.5f, v * 3f + ox, 3);
                    float t = 0.5f + 0.25f * grain + 0.35f * (fine - 0.5f) + 0.4f * (blotch - 0.5f);
                    Color dark = new Color(0.13f, 0.075f, 0.045f);
                    Color light = new Color(0.36f, 0.22f, 0.12f);
                    Color col = Color.Lerp(dark, light, Mathf.Clamp01(t));
                    // Weathered grey streaks.
                    float weather = Mathf.Clamp01((Fbm(u * 8f + 3f, v * 0.8f + ox, 3) - 0.62f) * 4f);
                    col = Color.Lerp(col, new Color(0.42f, 0.36f, 0.30f), weather * 0.35f);
                    c.Px[y * w + x] = new Color(col.r, col.g, col.b, 1f);
                }
            }

            // Dark cracks along the grain.
            for (int i = 0; i < 9; i++)
            {
                float x0 = (float)rnd.NextDouble() * w;
                float y0 = (float)rnd.NextDouble() * h;
                float len = h * (0.1f + (float)rnd.NextDouble() * 0.3f);
                var pts = new List<Vector2>();
                for (int k = 0; k <= 24; k++)
                {
                    float t = k / 24f;
                    pts.Add(new Vector2(x0 + (Mathf.PerlinNoise(t * 3f, i) - 0.5f) * 10f, y0 + t * len));
                }

                c.Stroke(pts, 1.6f + (float)rnd.NextDouble() * 1.5f, new Color(0.06f, 0.035f, 0.02f, 0.8f), 0.3f, seed + i);
            }

            return c;
        }

        static InkCanvas Parchment(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            var rnd = new System.Random(seed);
            float o = (float)rnd.NextDouble() * 50f;
            Color baseCol = new Color(0.92f, 0.82f, 0.64f);
            Color stain = new Color(0.78f, 0.60f, 0.38f);
            Color burnt = new Color(0.45f, 0.27f, 0.12f);
            float margin = 28f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    // Distance to the paper edge (px), pushed in by a torn-edge noise.
                    float dEdge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                    float along = (x < margin * 2f || x > w - margin * 2f) ? v * 9f : u * 9f;
                    float tear = Fbm(along + o, dEdge * 0.02f + o, 4) * 26f + Fbm(along * 6f, o, 2) * 7f;
                    float edge = dEdge - tear - 4f;
                    // Corners are a bit more ragged.
                    float corner = Mathf.Min(new Vector2(x, y).magnitude, new Vector2(w - x, y).magnitude,
                        new Vector2(x, h - y).magnitude, new Vector2(w - x, h - y).magnitude);
                    edge = Mathf.Min(edge, corner - 46f - Fbm(u * 7f, v * 7f + o, 2) * 30f);
                    float alpha = Mathf.Clamp01(edge / 1.5f + 0.5f);

                    float blot = Fbm(u * 4f + o, v * 4f, 4);
                    float speck = Fbm(u * 40f, v * 40f + o, 2);
                    Color col = Color.Lerp(baseCol, stain, Mathf.Clamp01((blot - 0.45f) * 1.6f));
                    col = Color.Lerp(col, baseCol * 1.04f, Mathf.Clamp01((0.5f - speck) * 1.5f) * 0.3f);
                    // Fibers.
                    float fiber = Mathf.PerlinNoise(u * 300f + o, v * 18f);
                    col *= 0.97f + 0.05f * fiber;
                    // Darkened rim near the tear, plus an overall vignette.
                    float rim = Mathf.Clamp01(1f - edge / 38f);
                    col = Color.Lerp(col, burnt, rim * rim * 0.75f);
                    float vig = Mathf.Clamp01(Mathf.Max(Mathf.Abs(u - 0.5f) * 2f, Mathf.Abs(v - 0.5f) * 2f));
                    col = Color.Lerp(col, stain, Mathf.Pow(vig, 4f) * 0.35f);
                    c.Px[y * w + x] = new Color(col.r, col.g, col.b, alpha);
                }
            }

            return c;
        }

        static InkCanvas SoftRect(int w, int h, float edge, float strength)
        {
            var c = new InkCanvas(w, h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = Mathf.Abs(x / (float)(w - 1) - 0.5f) * 2f;
                    float v = Mathf.Abs(y / (float)(h - 1) - 0.5f) * 2f;
                    float a = Mathf.Clamp01((1f - u) / edge) * Mathf.Clamp01((1f - v) / edge);
                    a = a * a * (3f - 2f * a);
                    c.Px[y * w + x] = new Color(0.04f, 0.02f, 0.01f, a * strength);
                }
            }

            return c;
        }

        static InkCanvas NailHead(int s)
        {
            var c = new InkCanvas(s, s);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)(s - 1) * 2f - 1f, v = y / (float)(s - 1) * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float dents = Fbm(u * 3f + 4f, v * 3f + 1f, 3);
                    float hi = Mathf.Clamp01(1f - new Vector2(u + 0.35f, v - 0.35f).magnitude * 1.4f);
                    Color col = Color.Lerp(new Color(0.11f, 0.09f, 0.08f), new Color(0.50f, 0.44f, 0.38f), hi * 0.85f + dents * 0.3f);
                    col = Color.Lerp(col, new Color(0.35f, 0.18f, 0.08f), Mathf.Clamp01((Fbm(u * 5f, v * 5f, 2) - 0.55f) * 3f) * 0.6f);
                    col *= Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((r - 0.75f) * 4f));
                    c.Px[y * s + x] = new Color(col.r, col.g, col.b, 1f);
                }
            }

            return c;
        }

        static InkCanvas NailDent(int s)
        {
            var c = new InkCanvas(s, s);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)(s - 1) * 2f - 1f, v = y / (float)(s - 1) * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * 0.55f;
                    // Rust streak running down.
                    float streak = Mathf.Clamp01(1f - Mathf.Abs(u) * 4f) * Mathf.Clamp01(-v) * 0.25f;
                    c.Px[y * s + x] = new Color(0.25f, 0.13f, 0.05f, Mathf.Clamp01(a + streak));
                }
            }

            return c;
        }

        // ================= Ornaments =================

        static InkCanvas TitleStars(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            var rnd = new System.Random(seed);
            // Thin arc over the title.
            var arc = new List<Vector2>();
            for (int i = 0; i <= 80; i++)
            {
                float t = i / 80f;
                float x = Mathf.Lerp(330f, 760f, t);
                float y = h - 14f - Mathf.Pow((t - 0.5f) * 2f, 2f) * 16f;
                arc.Add(new Vector2(x, y));
            }

            c.Stroke(arc, 2.2f, Ink, 0.45f, seed);
            // Feature star (gold, 8 points) on the arc's right.
            c.Star(new Vector2(800f, h - 22f), 20f, 4, 0.18f, Gold, 0f);
            c.Star(new Vector2(800f, h - 22f), 12f, 4, 0.22f, Gold, 45f);
            c.Star(new Vector2(745f, h - 14f), 7f, 4, 0.25f, Ink, 0f);
            // Scattered small stars around the edges, avoiding the lettering.
            var spots = new[]
            {
                new Vector2(70, 150), new Vector2(110, 60), new Vector2(40, 95), new Vector2(160, 178),
                new Vector2(250, 186), new Vector2(940, 160), new Vector2(985, 80), new Vector2(905, 40),
                new Vector2(870, 186), new Vector2(560, 192), new Vector2(430, 188), new Vector2(980, 130),
                new Vector2(30, 30), new Vector2(660, 190)
            };
            foreach (Vector2 p in spots)
            {
                float r = 5f + (float)rnd.NextDouble() * 6f;
                bool eight = rnd.NextDouble() < 0.35;
                c.Star(p, r, 4, 0.22f, Ink, 0f);
                if (eight)
                {
                    c.Star(p, r * 0.6f, 4, 0.25f, Ink, 45f);
                }
            }

            for (int i = 0; i < 18; i++)
            {
                var p = new Vector2((float)rnd.NextDouble() * w, (float)rnd.NextDouble() * h);
                if (p.x > 150 && p.x < 880 && p.y < 160)
                {
                    continue;
                }

                c.Dot(p, 1.4f + (float)rnd.NextDouble() * 1.4f, Ink);
            }

            return c;
        }

        static InkCanvas Ribbon(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            Color fill = new Color(0.90f, 0.78f, 0.56f, 1f);
            Color fold = new Color(0.72f, 0.55f, 0.33f, 1f);
            float midY = h * 0.5f;
            float bandH = h * 0.56f;
            float x0 = 120f, x1 = w - 120f;
            Func<float, float> wave = x => Mathf.Sin((x / w) * Mathf.PI * 2f) * 6f;

            // Tails (behind), then folds, then the band.
            foreach (int side in new[] { -1, 1 })
            {
                float inner = side < 0 ? x0 + 40f : x1 - 40f;
                float outer = side < 0 ? 8f : w - 8f;
                float y = midY - 14f + wave(inner);
                var tail = new List<Vector2>
                {
                    new Vector2(inner, y + bandH * 0.42f),
                    new Vector2(outer, y + bandH * 0.42f + 4f),
                    new Vector2(outer + side * -34f, y),
                    new Vector2(outer, y - bandH * 0.42f - 4f),
                    new Vector2(inner, y - bandH * 0.42f)
                };
                c.FillPolygon(tail, fill * 0.93f);
                c.Stroke(Close(tail), 2.4f, Ink, 0.1f, seed + side);
                float edgeX = side < 0 ? x0 : x1;
                var foldPoly = new List<Vector2>
                {
                    new Vector2(edgeX, midY + bandH * 0.5f + wave(edgeX)),
                    new Vector2(inner, y + bandH * 0.42f),
                    new Vector2(inner, y - bandH * 0.42f),
                    new Vector2(edgeX, midY - bandH * 0.5f + wave(edgeX))
                };
                c.FillPolygon(foldPoly, fold);
                c.Stroke(Close(foldPoly), 2f, Ink, 0.1f, seed + 3 + side);
            }

            var top = new List<Vector2>();
            var bottom = new List<Vector2>();
            for (int i = 0; i <= 60; i++)
            {
                float x = Mathf.Lerp(x0, x1, i / 60f);
                top.Add(new Vector2(x, midY + bandH * 0.5f + wave(x)));
                bottom.Add(new Vector2(x, midY - bandH * 0.5f + wave(x)));
            }

            var band = new List<Vector2>(top);
            for (int i = bottom.Count - 1; i >= 0; i--)
            {
                band.Add(bottom[i]);
            }

            c.FillPolygon(band, fill);
            c.Stroke(top, 2.6f, Ink, 0.05f, seed + 7);
            c.Stroke(bottom, 2.6f, Ink, 0.05f, seed + 8);
            // A few shading strokes along the lower edge.
            for (int i = 0; i < 14; i++)
            {
                float x = Mathf.Lerp(x0 + 20f, x1 - 20f, i / 13f);
                float y = midY - bandH * 0.5f + wave(x) + 5f;
                c.Stroke(new List<Vector2> { new Vector2(x, y), new Vector2(x + 18f, y + 2f) }, 1.2f, new Color(Ink.r, Ink.g, Ink.b, 0.35f), 0.4f, seed + 20 + i);
            }

            return c;
        }

        static List<Vector2> Close(List<Vector2> pts)
        {
            var l = new List<Vector2>(pts) { pts[0] };
            return l;
        }

        static InkCanvas Divider(int w, int h, bool star, int seed)
        {
            var c = new InkCanvas(w, h);
            float y = h * 0.5f;
            float gap = star ? 34f : 0f;
            Color ink = star ? Ink : new Color(Ink.r, Ink.g, Ink.b, 0.45f);
            float width = star ? 2.2f : 1.6f;
            c.Stroke(Wavy(new Vector2(14f, y), new Vector2(w * 0.5f - gap, y), 0.8f, seed), width, ink, 0.5f, seed);
            c.Stroke(Wavy(new Vector2(w * 0.5f + gap, y), new Vector2(w - 14f, y), 0.8f, seed + 1), width, ink, 0.5f, seed + 1);
            if (star)
            {
                c.Star(new Vector2(w * 0.5f, y), 22f, 4, 0.16f, Gold, 0f);
                c.Star(new Vector2(w * 0.5f, y), 11f, 4, 0.25f, Gold, 45f);
                c.Dot(new Vector2(w * 0.5f - gap - 10f, y), 2.2f, Ink);
                c.Dot(new Vector2(w * 0.5f + gap + 10f, y), 2.2f, Ink);
            }

            return c;
        }

        static List<Vector2> Wavy(Vector2 a, Vector2 b, float amp, int seed)
        {
            var pts = new List<Vector2>();
            int n = Mathf.Max(8, Mathf.CeilToInt((b - a).magnitude / 6f));
            Vector2 nrm = new Vector2(-(b - a).y, (b - a).x).normalized;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                pts.Add(Vector2.Lerp(a, b, t) + nrm * ((Mathf.PerlinNoise(t * 6f, seed * 1.37f) - 0.5f) * 2f * amp));
            }

            return pts;
        }

        static InkCanvas Flourish(int w, int h, int seed)
        {
            // A scroll for the left of a heading: long tapered line ending in a curl at the left.
            var c = new InkCanvas(w, h);
            var pts = new List<Vector2>();
            float y = h * 0.5f;
            for (int i = 0; i <= 50; i++)
            {
                float t = i / 50f;
                pts.Add(new Vector2(Mathf.Lerp(w - 8f, 70f, t), y + Mathf.Sin(t * Mathf.PI) * 5f));
            }

            // Curl: spiral inward.
            Vector2 cc = new Vector2(52f, y + 4f);
            for (int i = 0; i <= 60; i++)
            {
                float t = i / 60f;
                float ang = Mathf.PI * 1.5f - t * Mathf.PI * 2.1f;
                float r = Mathf.Lerp(18f, 5f, t);
                pts.Add(cc + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r + new Vector2(0, -2f));
            }

            c.Stroke(pts, 3.2f, Ink, 0.35f, seed);
            c.Dot(new Vector2(w - 4f, y), 2.4f, Ink);
            return c;
        }

        static InkCanvas NumberOval(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            var rnd = new System.Random(seed);
            float tilt = ((float)rnd.NextDouble() - 0.5f) * 18f;
            c.Ellipse(new Vector2(w * 0.5f, h * 0.5f), w * 0.4f, h * 0.38f, tilt, 3.2f, Ink, 1.12f + (float)rnd.NextDouble() * 0.1f, seed);
            return c;
        }

        static InkCanvas Underline(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            var pts = new List<Vector2>();
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f;
                pts.Add(new Vector2(Mathf.Lerp(6f, w - 6f, t), h * 0.5f + Mathf.Sin(t * Mathf.PI * 1.3f + 0.4f) * 2.5f));
            }

            c.Stroke(pts, 2.6f, Ink, 0.35f, seed);
            return c;
        }

        static InkCanvas Smudge(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)(w - 1) * 2f - 1f, v = y / (float)(h - 1) * 2f - 1f;
                    float n = Fbm(x * 0.01f + seed, y * 0.05f, 4);
                    float streak = Fbm(x * 0.002f, y * 0.12f + seed, 3);
                    float r = Mathf.Sqrt(u * u * 0.95f + v * v * 1.2f) + (n - 0.5f) * 0.5f;
                    float a = Mathf.Clamp01(1f - r);
                    a = Mathf.Pow(a, 0.8f) * (0.7f + 0.6f * streak) * 0.5f;
                    c.Px[y * w + x] = new Color(0.30f, 0.18f, 0.08f, Mathf.Clamp01(a));
                }
            }

            return c;
        }

        static InkCanvas GuideLine(int w, int h)
        {
            var c = new InkCanvas(w, h);
            for (float x = 10f; x < w - 10f; x += 16f)
            {
                c.Dot(new Vector2(x, h * 0.5f), 2.1f, new Color(Ink.r, Ink.g, Ink.b, 0.55f));
            }

            return c;
        }

        static InkCanvas TraceFlourish(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            foreach (int side in new[] { -1, 1 })
            {
                var pts = new List<Vector2>();
                float y = h * 0.5f;
                float cx = side < 0 ? 70f : w - 70f;
                for (int i = 0; i <= 60; i++)
                {
                    float t = i / 60f;
                    float x = cx + side * (Mathf.Lerp(-55f, 55f, t));
                    pts.Add(new Vector2(x, y + Mathf.Sin(t * Mathf.PI * 2f) * 7f * (1f - t)));
                }

                c.Stroke(pts, 2.4f, Ink, 0.45f, seed + side);
                c.Dot(new Vector2(cx + side * -62f, y), 2.2f, Ink);
            }

            return c;
        }

        static InkCanvas GlowDot(int s)
        {
            var c = new InkCanvas(s, s);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)(s - 1) * 2f - 1f, v = y / (float)(s - 1) * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.Exp(-r * r * 6f) * Mathf.Clamp01((1f - r) * 4f);
                    c.Px[y * s + x] = new Color(1f, 1f, 1f, a);
                }
            }

            return c;
        }

        static InkCanvas SparkleStar(int s)
        {
            var c = new InkCanvas(s, s);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float u = x / (float)(s - 1) * 2f - 1f, v = y / (float)(s - 1) * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float rays = Mathf.Exp(-Mathf.Abs(u) * 22f) * Mathf.Clamp01(1f - Mathf.Abs(v)) +
                                 Mathf.Exp(-Mathf.Abs(v) * 22f) * Mathf.Clamp01(1f - Mathf.Abs(u));
                    float core = Mathf.Exp(-r * r * 30f);
                    float halo = Mathf.Exp(-r * r * 5f) * 0.25f;
                    c.Px[y * s + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(rays * 0.9f + core + halo) * Mathf.Clamp01((1f - r) * 3f));
                }
            }

            return c;
        }

        // ================= Illustrations =================

        static InkCanvas WM(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            // Soft hatched sky clouds behind each constellation.
            HatchCloud(c, new Rect(20, 160, 300, 130), seed);
            HatchCloud(c, new Rect(190, 15, 310, 130), seed + 1);

            var W = new[] { new Vector2(40, 262), new Vector2(102, 192), new Vector2(165, 245), new Vector2(226, 186), new Vector2(290, 258) };
            var M = new[] { new Vector2(222, 40), new Vector2(282, 122), new Vector2(344, 70), new Vector2(404, 126), new Vector2(466, 42) };
            foreach (var pts in new[] { W, M })
            {
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    Vector2 a = pts[i], b = pts[i + 1];
                    Vector2 d = (b - a).normalized;
                    c.Stroke(Wavy(a + d * 13f, b - d * 13f, 0.7f, seed + i), 3f, Ink, 0.25f, seed + i);
                }

                foreach (Vector2 p in pts)
                {
                    c.Star(p, 12f, 4, 0.22f, Ink, 0f);
                    c.Star(p, 7f, 4, 0.3f, Ink, 45f);
                }
            }

            // Curved double arrow between them.
            var arrow = Bezier(new Vector2(178, 165), new Vector2(140, 120), new Vector2(150, 75), new Vector2(196, 62), 40);
            c.Stroke(arrow, 3.2f, Ink, 0.15f, seed + 9);
            ArrowHead(c, arrow[arrow.Count - 2], arrow[arrow.Count - 1], 15f, Ink, seed + 10);
            ArrowHead(c, arrow[1], arrow[0], 15f, Ink, seed + 11);
            return c;
        }

        static void HatchCloud(InkCanvas c, Rect r, int seed)
        {
            var col = new Color(InkSoft.r, InkSoft.g, InkSoft.b, 0.16f);
            for (float x = r.xMin; x < r.xMax; x += 7f)
            {
                float t = (x - r.xMin) / r.width;
                float hgt = Mathf.Sin(t * Mathf.PI) * r.height * (0.6f + 0.4f * Mathf.PerlinNoise(t * 4f, seed));
                float y0 = r.center.y - hgt * 0.5f;
                c.Stroke(new List<Vector2> { new Vector2(x, y0), new Vector2(x + hgt * 0.35f, y0 + hgt) }, 1.4f, col, 0.4f, seed + (int)x);
            }
        }

        static void ArrowHead(InkCanvas c, Vector2 from, Vector2 tip, float size, Color col, int seed)
        {
            Vector2 d = (tip - from).normalized;
            Vector2 n = new Vector2(-d.y, d.x);
            c.Stroke(new List<Vector2> { tip - d * size + n * size * 0.6f, tip, tip - d * size - n * size * 0.6f }, 3f, col, 0.2f, seed);
        }

        static List<Vector2> Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int n)
        {
            var l = new List<Vector2>();
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n, u = 1f - t;
                l.Add(u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3);
            }

            return l;
        }

        static InkCanvas SketchGlobe(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            // Pinned sketch sheet, tilted.
            Vector2 sc = new Vector2(150, 150);
            float ang = 8f * Mathf.Deg2Rad;
            Vector2 R(Vector2 p) => sc + new Vector2(p.x * Mathf.Cos(ang) - p.y * Mathf.Sin(ang), p.x * Mathf.Sin(ang) + p.y * Mathf.Cos(ang));
            var sheet = new List<Vector2> { R(new Vector2(-120, -85)), R(new Vector2(118, -95)), R(new Vector2(122, 88)), R(new Vector2(-116, 96)) };
            c.FillPolygon(sheet, new Color(Wash.r, Wash.g, Wash.b, 0.85f));
            c.Stroke(Close(sheet), 2.6f, Ink, 0.05f, seed);
            // Curled corner shading.
            c.Stroke(new List<Vector2> { R(new Vector2(118, -95)), R(new Vector2(96, -70)), R(new Vector2(122, -62)) }, 1.8f, Ink, 0.1f, seed + 1);
            foreach (Vector2 p in new[] { new Vector2(-104, 80), new Vector2(104, 72), new Vector2(-100, -72) })
            {
                c.Dot(R(p), 4f, Ink);
            }

            // Constellation sketch on the sheet (pencil-ish).
            var cs = new[] { new Vector2(-80, -40), new Vector2(-30, 10), new Vector2(20, -5), new Vector2(70, 50) };
            for (int i = 0; i < cs.Length - 1; i++)
            {
                c.Stroke(Wavy(R(cs[i]), R(cs[i + 1]), 0.8f, seed + i), 2.6f, Ink, 0.2f, seed + 5 + i);
            }

            foreach (Vector2 p in cs)
            {
                c.Dot(R(p), 5.5f, Ink);
            }

            // A hand reaching in from below, its index finger on the second star.
            PointAt(c, R(cs[1]), PointSize, PointTilt, seed + 60);

            // Globe (armillary): rings around a center, on a stand.
            Vector2 g = new Vector2(400, 158);
            float gr = 78f;
            Color brass = new Color(0.74f, 0.50f, 0.20f, 1f);
            c.Ellipse(g, gr, gr, 0f, 3.2f, Ink, 1.02f, seed + 20);
            c.Ellipse(g, gr, gr * 0.32f, -20f, 3.6f, brass, 1.0f, seed + 21);
            c.Ellipse(g, gr, gr * 0.32f, -20f, 1.4f, Ink, 1.0f, seed + 25);
            c.Ellipse(g, gr * 0.42f, gr, 15f, 2.6f, Ink, 1.0f, seed + 22);
            c.Ellipse(g, gr * 0.85f, gr * 0.85f, 0f, 1.4f, new Color(Ink.r, Ink.g, Ink.b, 0.5f), 1f, seed + 23);
            c.Dot(g, 6f, brass);
            // Stand.
            c.Stroke(new List<Vector2> { g + new Vector2(0, -gr), g + new Vector2(0, -gr - 34) }, 4f, Ink, 0.05f, seed + 30);
            c.Ellipse(g + new Vector2(0, -gr - 40), 46f, 10f, 0f, 3f, Ink, 1.0f, seed + 31);
            c.FillPolygon(EllipsePoly(g + new Vector2(0, -gr - 40), 44f, 8f), new Color(brass.r, brass.g, brass.b, 0.55f));
            // Rotation arrow over the globe.
            var rot = new List<Vector2>();
            for (int i = 0; i <= 30; i++)
            {
                float t = i / 30f;
                float a = Mathf.Lerp(60f, 5f, t) * Mathf.Deg2Rad;
                rot.Add(g + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (gr + 22f));
            }

            c.Stroke(rot, 3f, Ink, 0.15f, seed + 40);
            ArrowHead(c, rot[rot.Count - 2], rot[rot.Count - 1], 13f, Ink, seed + 41);
            // Arrow from sheet to globe.
            var link = Bezier(new Vector2(250, 60), new Vector2(275, 30), new Vector2(305, 30), new Vector2(325, 52), 20);
            c.Stroke(link, 2.8f, Ink, 0.2f, seed + 42);
            ArrowHead(c, link[link.Count - 2], link[link.Count - 1], 12f, Ink, seed + 43);
            return c;
        }

        // Draws the posed hand (PointingHandParts) so its index fingertip stops
        // just short of target. tiltDeg turns it counter-clockwise from pointing up.
        static void PointAt(InkCanvas c, Vector2 target, int size, float tiltDeg, int seed)
        {
            var hand = new InkCanvas(size, size);
            Quaternion view = Quaternion.Euler(0, 0, tiltDeg) * Quaternion.Euler(0, PointViewTurn, 0) * Quaternion.Euler(PointViewEuler);
            MeshInk.Render(hand, new RectInt(0, 0, size, size), PointingHandParts(), view, false, seed, lineScale: 0.8f);

            // Fingertip = the inked pixel farthest along the pointing direction.
            float t = tiltDeg * Mathf.Deg2Rad;
            var dir = new Vector2(-Mathf.Sin(t), Mathf.Cos(t));
            Vector2 tip = Vector2.zero;
            float best = float.NegativeInfinity;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (hand.Px[y * size + x].a > 0.5f)
                    {
                        float d = Vector2.Dot(new Vector2(x, y), dir);
                        if (d > best)
                        {
                            best = d;
                            tip = new Vector2(x, y);
                        }
                    }
                }
            }

            Vector2 offset = target - dir * PointGap - tip;
            int ox = Mathf.RoundToInt(offset.x), oy = Mathf.RoundToInt(offset.y);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color p = hand.Px[y * size + x];
                    if (p.a > 0f)
                    {
                        c.Over(x + ox, y + oy, new Color(p.r, p.g, p.b, 1f), p.a);
                    }
                }
            }
        }

        static List<Vector2> EllipsePoly(Vector2 c, float rx, float ry)
        {
            var l = new List<Vector2>();
            for (int i = 0; i < 40; i++)
            {
                float a = i / 40f * Mathf.PI * 2f;
                l.Add(c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry));
            }

            return l;
        }

        static InkCanvas Controllers(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            var left = LoadParts(TouchLeft);
            var right = LoadParts(TouchRight);
            Quaternion view = Quaternion.Euler(ControllerViewEuler);
            // Two controllers tilted toward each other, like the concept.
            MeshInk.Render(c, new RectInt(36, 20, 220, 240), left, Quaternion.Euler(0, 0, ControllerTilt) * view, false, seed);
            MeshInk.Render(c, new RectInt(256, 20, 220, 240), right, Quaternion.Euler(0, 0, -ControllerTilt) * view, false, seed + 1);
            // Red X across both.
            c.Stroke(Wavy(new Vector2(150, 40), new Vector2(372, 240), 1.6f, seed + 2), 9f, RedInk, 0.35f, seed + 2);
            c.Stroke(Wavy(new Vector2(140, 236), new Vector2(380, 44), 1.6f, seed + 3), 9f, RedInk, 0.35f, seed + 3);
            // Emphasis ticks.
            Tick(c, new Vector2(30, 230), new Vector2(52, 214), seed + 4);
            Tick(c, new Vector2(18, 196), new Vector2(42, 192), seed + 5);
            Tick(c, new Vector2(482, 230), new Vector2(460, 214), seed + 6);
            Tick(c, new Vector2(494, 196), new Vector2(470, 192), seed + 7);
            Tick(c, new Vector2(52, 256), new Vector2(64, 238), seed + 8);
            Tick(c, new Vector2(460, 256), new Vector2(448, 238), seed + 9);
            return c;
        }

        static void Tick(InkCanvas c, Vector2 a, Vector2 b, int seed)
        {
            c.Stroke(new List<Vector2> { a, b }, 2.6f, Ink, 0.4f, seed);
        }

        static InkCanvas Hands(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            var hand = LoadParts(HandRight);
            Quaternion view = Quaternion.Euler(HandViewEuler);
            MeshInk.Render(c, new RectInt(8, 4, 116, 130), hand, Quaternion.Euler(0, 0, 12f) * view, true, seed, lineScale: 0.8f);
            MeshInk.Render(c, new RectInt(132, 4, 116, 130), hand, Quaternion.Euler(0, 0, -12f) * view, false, seed + 1, lineScale: 0.8f);
            // Little "hello" marks above.
            Tick(c, new Vector2(52, 156), new Vector2(58, 140), seed + 2);
            Tick(c, new Vector2(30, 148), new Vector2(42, 136), seed + 3);
            Tick(c, new Vector2(76, 154), new Vector2(74, 140), seed + 4);
            Tick(c, new Vector2(204, 156), new Vector2(198, 140), seed + 5);
            Tick(c, new Vector2(226, 148), new Vector2(214, 136), seed + 6);
            Tick(c, new Vector2(180, 154), new Vector2(182, 140), seed + 7);
            return c;
        }

        public static List<MeshInk.Part> LoadParts(string path)
        {
            var parts = new List<MeshInk.Part>();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null)
            {
                throw new Exception("Model not found: " + path);
            }

            int id = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null)
                {
                    parts.Add(new MeshInk.Part(mf.sharedMesh, root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix, id++));
                }
            }

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh != null)
                {
                    parts.Add(new MeshInk.Part(smr.sharedMesh, root.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix, id++));
                }
            }

            return parts;
        }
    }

    /// <summary>Pen-and-ink render of meshes on the CPU (outlines + hatching).</summary>
    public static class MeshInk
    {
        public readonly struct Part
        {
            public readonly Mesh Mesh;
            public readonly Matrix4x4 Matrix;
            public readonly int Id;

            public Part(Mesh mesh, Matrix4x4 matrix, int id)
            {
                Mesh = mesh;
                Matrix = matrix;
                Id = id;
            }
        }

        public static void Render(InkCanvas canvas, RectInt area, List<Part> parts, Quaternion view, bool mirror, int seed, float lineScale = 1f)
        {
            const int ss = 3;
            // Transform everything into view space (viewer on +Z looking toward -Z).
            var tris = new List<(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, int id)>();
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (Part p in parts)
            {
                Vector3[] v = p.Mesh.vertices;
                Vector3[] n = p.Mesh.normals;
                bool hasN = n != null && n.Length == v.Length;
                var tv = new Vector3[v.Length];
                var tn = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 q = view * p.Matrix.MultiplyPoint3x4(v[i]);
                    if (mirror) q.x = -q.x;
                    tv[i] = q;
                    Vector3 nn = hasN ? view * p.Matrix.MultiplyVector(n[i]).normalized : Vector3.forward;
                    if (mirror) nn.x = -nn.x;
                    tn[i] = nn;
                    min = Vector3.Min(min, q);
                    max = Vector3.Max(max, q);
                }

                for (int sm = 0; sm < p.Mesh.subMeshCount; sm++)
                {
                    int[] t = p.Mesh.GetTriangles(sm);
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        tris.Add((tv[t[i]], tv[t[i + 1]], tv[t[i + 2]], tn[t[i]], tn[t[i + 1]], tn[t[i + 2]], p.Id * 8 + sm));
                    }
                }
            }

            int gw = area.width * ss, gh = area.height * ss;
            float sx = (max.x - min.x), sy = (max.y - min.y);
            float scale = Mathf.Min((gw - 8f * ss) / Mathf.Max(1e-6f, sx), (gh - 8f * ss) / Mathf.Max(1e-6f, sy));
            float ox = (gw - sx * scale) * 0.5f, oy = (gh - sy * scale) * 0.5f;
            Vector2 S(Vector3 q) => new Vector2((q.x - min.x) * scale + ox, (q.y - min.y) * scale + oy);

            var z = new float[gw * gh];
            var nrm = new Vector3[gw * gh];
            var ids = new int[gw * gh];
            for (int i = 0; i < z.Length; i++)
            {
                z[i] = float.NegativeInfinity;
                ids[i] = -1;
            }

            foreach (var t in tris)
            {
                Vector2 a = S(t.a), b = S(t.b), c = S(t.c);
                float area2 = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (Mathf.Abs(area2) < 1e-6f) continue;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
                int x1 = Mathf.Min(gw - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
                int y1 = Mathf.Min(gh - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w0 = ((b.x - px) * (c.y - py) - (b.y - py) * (c.x - px)) / area2;
                        float w1 = ((c.x - px) * (a.y - py) - (c.y - py) * (a.x - px)) / area2;
                        float w2 = 1f - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                        float d = w0 * t.a.z + w1 * t.b.z + w2 * t.c.z;
                        int k = y * gw + x;
                        if (d <= z[k]) continue;
                        z[k] = d;
                        nrm[k] = (w0 * t.na + w1 * t.nb + w2 * t.nc).normalized;
                        ids[k] = t.id;
                    }
                }
            }

            float depthEdge = (max.z - min.z) * 0.06f;
            Vector3 light = new Vector3(-0.45f, 0.65f, 0.6f).normalized;
            var ink = new float[gw * gh];
            var cover = new float[gw * gh];
            float hatchSpacing = 5.5f * ss;
            for (int y = 1; y < gh - 1; y++)
            {
                for (int x = 1; x < gw - 1; x++)
                {
                    int k = y * gw + x;
                    if (ids[k] < 0) continue;
                    cover[k] = 1f;
                    float e = 0f;
                    foreach (int o in new[] { k - 1, k + 1, k - gw, k + gw })
                    {
                        if (ids[o] < 0) { e = 1f; break; }
                        if (Mathf.Abs(z[o] - z[k]) > depthEdge) e = Mathf.Max(e, 1f);
                        if (ids[o] != ids[k]) e = Mathf.Max(e, 0.85f);
                        float dn = Vector3.Dot(nrm[o], nrm[k]);
                        if (dn < 0.82f) e = Mathf.Max(e, 0.7f);
                    }

                    float lit = Vector3.Dot(nrm[k], light);
                    float hatch = 0f;
                    float jitter = (Mathf.PerlinNoise(x * 0.05f, y * 0.05f + seed) - 0.5f) * 3f * ss;
                    if (lit < 0.25f)
                    {
                        float f = Mathf.Repeat((x - y) + jitter, hatchSpacing);
                        hatch = f < 1.1f * ss ? Mathf.Lerp(0.55f, 0.35f, Mathf.Clamp01(lit + 0.2f)) : 0f;
                    }

                    if (lit < -0.2f)
                    {
                        float f = Mathf.Repeat((x + y) + jitter, hatchSpacing);
                        hatch = Mathf.Max(hatch, f < 1.1f * ss ? 0.5f : 0f);
                    }

                    ink[k] = Mathf.Max(e, hatch);
                }
            }

            // Thicken outlines (dilate the strong edges), then downsample.
            int dil = Mathf.Max(1, Mathf.RoundToInt(1.2f * ss * lineScale));
            var thick = new float[gw * gh];
            for (int y = 0; y < gh; y++)
            {
                for (int x = 0; x < gw; x++)
                {
                    float m = 0f;
                    for (int dy = -dil; dy <= dil; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= gh) continue;
                        for (int dx = -dil; dx <= dil; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= gw || dx * dx + dy * dy > dil * dil) continue;
                            float v = ink[yy * gw + xx];
                            if (v >= 0.69f) m = Mathf.Max(m, v);
                        }
                    }

                    thick[y * gw + x] = Mathf.Max(m, ink[y * gw + x]);
                }
            }

            for (int y = 0; y < area.height; y++)
            {
                for (int x = 0; x < area.width; x++)
                {
                    float iv = 0f, cv = 0f;
                    for (int dy = 0; dy < ss; dy++)
                    {
                        for (int dx = 0; dx < ss; dx++)
                        {
                            int k = (y * ss + dy) * gw + x * ss + dx;
                            iv += thick[k];
                            cv += cover[k];
                        }
                    }

                    iv /= ss * ss;
                    cv /= ss * ss;
                    int cx = area.x + x, cy = area.y + y;
                    if (cv > 0f)
                    {
                        canvas.Over(cx, cy, new Color(NoticeboardArt.Wash.r, NoticeboardArt.Wash.g, NoticeboardArt.Wash.b, 0.8f), cv);
                    }

                    if (iv > 0f)
                    {
                        canvas.Over(cx, cy, NoticeboardArt.Ink, Mathf.Clamp01(iv * 1.15f));
                    }
                }
            }
        }
    }

    /// <summary>A small anti-aliased ink canvas (straight alpha, origin bottom-left).</summary>
    public sealed class InkCanvas
    {
        public readonly int W, H;
        public readonly Color[] Px;
        private float[] mask;

        public InkCanvas(int w, int h)
        {
            W = w;
            H = h;
            Px = new Color[w * h];
            for (int i = 0; i < Px.Length; i++) Px[i] = new Color(1f, 1f, 1f, 0f);
        }

        public void Over(int x, int y, Color c, float coverage)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            float a = Mathf.Clamp01(c.a * coverage);
            if (a <= 0f) return;
            int k = y * W + x;
            Color d = Px[k];
            float na = a + d.a * (1f - a);
            if (na < 1e-6f) return;
            float r = (c.r * a + d.r * d.a * (1f - a)) / na;
            float g = (c.g * a + d.g * d.a * (1f - a)) / na;
            float b = (c.b * a + d.b * d.a * (1f - a)) / na;
            Px[k] = new Color(r, g, b, na);
        }

        // ---- mask helpers ----
        int mx0, my0, mx1, my1;

        void BeginMask(float x0, float y0, float x1, float y1)
        {
            if (mask == null) mask = new float[W * H];
            mx0 = Mathf.Max(0, Mathf.FloorToInt(x0));
            my0 = Mathf.Max(0, Mathf.FloorToInt(y0));
            mx1 = Mathf.Min(W - 1, Mathf.CeilToInt(x1));
            my1 = Mathf.Min(H - 1, Mathf.CeilToInt(y1));
            for (int y = my0; y <= my1; y++)
                for (int x = mx0; x <= mx1; x++)
                    mask[y * W + x] = 0f;
        }

        void EndMask(Color c, bool grain, bool keepAlpha = false)
        {
            for (int y = my0; y <= my1; y++)
            {
                for (int x = mx0; x <= mx1; x++)
                {
                    float m = mask[y * W + x];
                    if (m <= 0f) continue;
                    if (grain) m *= 0.84f + 0.16f * Mathf.PerlinNoise(x * 0.35f, y * 0.35f);
                    if (keepAlpha)
                    {
                        int k = y * W + x;
                        Color d = Px[k];
                        float a = Mathf.Clamp01(c.a * m);
                        Px[k] = new Color(Mathf.Lerp(d.r, c.r, a), Mathf.Lerp(d.g, c.g, a), Mathf.Lerp(d.b, c.b, a), d.a);
                    }
                    else
                    {
                        Over(x, y, c, m);
                    }
                }
            }
        }

        /// <summary>Tapered, slightly pressure-varying pen stroke along a polyline.</summary>
        public void Stroke(IList<Vector2> pts, float width, Color c, float taper, int seed, bool keepAlpha = false)
        {
            if (pts.Count < 2) return;
            float total = 0f;
            var cum = new float[pts.Count];
            for (int i = 1; i < pts.Count; i++)
            {
                total += (pts[i] - pts[i - 1]).magnitude;
                cum[i] = total;
            }

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vector2 p in pts)
            {
                minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y);
            }

            float pad = width + 2f;
            BeginMask(minX - pad, minY - pad, maxX + pad, maxY + pad);
            float taperLen = Mathf.Max(1e-3f, taper * 0.5f);
            for (int i = 1; i < pts.Count; i++)
            {
                Vector2 a = pts[i - 1], b = pts[i];
                Vector2 ab = b - a;
                float len2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - pad));
                int x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + pad));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - pad));
                int y1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + pad));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                        float d = (p - (a + ab * t)).magnitude;
                        float s = total > 0f ? (cum[i - 1] + t * Mathf.Sqrt(len2)) / total : 0.5f;
                        float press = taper > 0f ? Mathf.Clamp01(Mathf.Min(s, 1f - s) / taperLen) : 1f;
                        press = Mathf.Lerp(0.35f, 1f, Mathf.Sqrt(press));
                        press *= 0.85f + 0.3f * Mathf.PerlinNoise(s * 5f, seed * 0.731f);
                        float hw = width * 0.5f * press;
                        float cov = Mathf.Clamp01(hw - d + 0.5f);
                        int k = y * W + x;
                        if (cov > mask[k]) mask[k] = cov;
                    }
                }
            }

            EndMask(c, true, keepAlpha);
        }

        public void Dot(Vector2 center, float radius, Color c)
        {
            BeginMask(center.x - radius - 2, center.y - radius - 2, center.x + radius + 2, center.y + radius + 2);
            for (int y = my0; y <= my1; y++)
                for (int x = mx0; x <= mx1; x++)
                {
                    float d = (new Vector2(x + 0.5f, y + 0.5f) - center).magnitude;
                    mask[y * W + x] = Mathf.Clamp01(radius - d + 0.5f);
                }

            EndMask(c, false);
        }

        /// <summary>Star with the given number of points (4 = the sparkle shape).</summary>
        public void Star(Vector2 center, float radius, int points, float innerRatio, Color c, float rotationDeg)
        {
            var poly = new List<Vector2>();
            for (int i = 0; i < points * 2; i++)
            {
                float a = (i / (float)(points * 2)) * Mathf.PI * 2f + (rotationDeg + 90f) * Mathf.Deg2Rad;
                float r = (i % 2 == 0) ? radius : radius * innerRatio;
                poly.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }

            FillPolygon(poly, c);
        }

        public void FillPolygon(IList<Vector2> poly, Color c)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vector2 p in poly)
            {
                minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y);
            }

            BeginMask(minX - 1, minY - 1, maxX + 1, maxY + 1);
            const int n = 4;
            for (int y = my0; y <= my1; y++)
                for (int x = mx0; x <= mx1; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < n; sy++)
                        for (int sx = 0; sx < n; sx++)
                            if (Inside(poly, x + (sx + 0.5f) / n, y + (sy + 0.5f) / n)) inside++;
                    mask[y * W + x] = inside / (float)(n * n);
                }

            EndMask(c, false);
        }

        static bool Inside(IList<Vector2> poly, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }

            return inside;
        }

        /// <summary>Hand-drawn ellipse: turns > 1 overlaps the ends like a pen loop.</summary>
        public void Ellipse(Vector2 center, float rx, float ry, float tiltDeg, float width, Color c, float turns, int seed, bool keepAlpha = false)
        {
            var pts = new List<Vector2>();
            int n = Mathf.Max(24, Mathf.CeilToInt(Mathf.Max(rx, ry) * turns * 0.7f));
            float start = Mathf.PerlinNoise(seed * 0.37f, 0.5f) * Mathf.PI * 2f;
            float tilt = tiltDeg * Mathf.Deg2Rad;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float a = start + t * Mathf.PI * 2f * turns;
                float wob = 1f + (Mathf.PerlinNoise(t * 4f, seed * 1.3f) - 0.5f) * 0.06f + (turns > 1.01f ? t * 0.04f : 0f);
                Vector2 p = new Vector2(Mathf.Cos(a) * rx * wob, Mathf.Sin(a) * ry * wob);
                pts.Add(center + new Vector2(p.x * Mathf.Cos(tilt) - p.y * Mathf.Sin(tilt), p.x * Mathf.Sin(tilt) + p.y * Mathf.Cos(tilt)));
            }

            Stroke(pts, width, c, turns > 1.01f ? 0.25f : 0f, seed, keepAlpha);
        }

        public void Save(string path)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
            tex.SetPixels(Px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }

    /// <summary>Entry for previews: writes the PNGs to any folder (e.g. outside Assets).</summary>
    public static class NoticeboardArtPreview
    {
        public static string Run(string dir)
        {
            return NoticeboardArt.GenerateAll(dir);
        }

    }
}
