using System.Collections.Generic;
using System.IO;
using CW.Onboarding.EditorTools;
using UnityEngine;

namespace CW.FieldNote.EditorTools
{
    /// <summary>
    /// Procedural ink art for the Skycrafter's Field Note, the selection table
    /// and the box fittings, drawn with the noticeboard's InkCanvas in the
    /// noticeboard's ink palette (gold = the Credits / noticeboard star gold).
    /// Each texture is its own PNG, so any one can be replaced by hand later.
    ///
    /// The three diagrams use the real five stars of Cassiopeia's W (J2000
    /// RA/Dec and approximate distances), placed in 3D around Earth. The only
    /// liberty is a uniform angular magnification (<see cref="Spread"/>): every
    /// star's sideways offset from the line of sight is scaled by the same
    /// factor, so the pattern seen from Earth keeps its true shape (just
    /// larger), depth order and distances stay true, and nothing is flattened.
    /// </summary>
    public static class FieldNoteArt
    {
        // ---------------------------------------------------------------- Palette (shared with the noticeboard)
        static readonly Color Ink = NoticeboardArt.Ink;
        static readonly Color InkSoft = NoticeboardArt.InkSoft;
        static readonly Color Gold = NoticeboardArt.Gold;
        static readonly Color Pencil = new Color(0.36f, 0.24f, 0.14f, 0.45f);
        static readonly Color Brass = new Color(0.74f, 0.50f, 0.20f, 1f);

        // ---------------------------------------------------------------- Cassiopeia (J2000)
        public struct StarData
        {
            public string Name;
            public float RaDeg, DecDeg, DistanceLy;
            public float Size; // drawing weight (brightness hint)

            public StarData(string name, float raH, float raM, float raS, float decD, float decM, float decS, float ly, float size)
            {
                Name = name;
                RaDeg = (raH + raM / 60f + raS / 3600f) * 15f;
                DecDeg = decD + decM / 60f + decS / 3600f;
                DistanceLy = ly;
                Size = size;
            }
        }

        // In the order the W is drawn: Caph - Schedar - Gamma - Ruchbah - Segin.
        public static readonly StarData[] Cassiopeia =
        {
            new StarData("Caph", 0, 9, 11, 59, 8, 59, 54.7f, 0.9f),
            new StarData("Schedar", 0, 40, 30, 56, 32, 14, 228f, 1f),
            new StarData("Gamma Cas", 0, 56, 42, 60, 43, 0, 550f, 1f),
            new StarData("Ruchbah", 1, 25, 49, 60, 14, 7, 99.4f, 0.85f),
            new StarData("Segin", 1, 54, 24, 63, 40, 12, 410f, 0.75f),
        };

        /// <summary>Uniform angular magnification for the 3D diagrams (see the class notes).</summary>
        public static float Spread = 4f;

        /// <summary>
        /// Star positions in a diagram frame (light years): +Z = from Earth
        /// toward the constellation, +Y = celestial north (up on the sky), +X =
        /// west (right on the sky as seen from Earth with north up). Earth = origin.
        /// </summary>
        public static Vector3[] StarPositions(float spread)
        {
            var units = new Vector3[Cassiopeia.Length];
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < units.Length; i++)
            {
                float ra = Cassiopeia[i].RaDeg * Mathf.Deg2Rad, dec = Cassiopeia[i].DecDeg * Mathf.Deg2Rad;
                // Equatorial: x toward RA 0, y toward RA 6h, z north.
                units[i] = new Vector3(Mathf.Cos(dec) * Mathf.Cos(ra), Mathf.Cos(dec) * Mathf.Sin(ra), Mathf.Sin(dec));
                sum += units[i];
            }

            Vector3 f = sum.normalized;
            Vector3 north = new Vector3(0f, 0f, 1f);
            Vector3 up = (north - f * Vector3.Dot(north, f)).normalized;
            Vector3 east = Vector3.Cross(north, f).normalized;
            // Roll the sky frame so the W sits level (Caph and Segin, the two
            // ends, at the same height). Which way is "up" on the sky changes
            // through the night anyway, so this changes nothing physical.
            Vector2 OnSky(int i) => new Vector2(-Vector3.Dot(units[i], east), Vector3.Dot(units[i], up)) / Vector3.Dot(units[i], f);
            Vector2 ends = OnSky(units.Length - 1) - OnSky(0);
            float roll = -Mathf.Atan2(ends.y, ends.x);
            float cr = Mathf.Cos(roll), sr = Mathf.Sin(roll);
            if (ends.x < 0f) { cr = -cr; sr = -sr; }

            var result = new Vector3[units.Length];
            for (int i = 0; i < units.Length; i++)
            {
                Vector3 v = units[i] * Cassiopeia[i].DistanceLy;
                float depth = Vector3.Dot(v, f);
                float west = -Vector3.Dot(v, east);
                float n = Vector3.Dot(v, up);
                float x = west * cr - n * sr, y = west * sr + n * cr;
                result[i] = new Vector3(x * spread, y * spread, depth);
            }

            return result;
        }

        /// <summary>Where each star appears on the sky from Earth (gnomonic, x right, y up; unitless).</summary>
        public static Vector2[] SkyFromEarth()
        {
            var p = StarPositions(1f);
            var s = new Vector2[p.Length];
            for (int i = 0; i < p.Length; i++) s[i] = new Vector2(p[i].x / p[i].z, p[i].y / p[i].z);
            return s;
        }

        // ---------------------------------------------------------------- Output

        /// <summary>
        /// Writes the PNGs to the folder in SessionState "FieldNoteArt.PreviewDir"
        /// (for look-dev outside Assets) and logs the report.
        /// </summary>
        [UnityEditor.MenuItem("Tools/Celestial Workshop/Field Note/Preview Art To Folder")]
        static void PreviewMenu()
        {
            string dir = UnityEditor.SessionState.GetString("FieldNoteArt.PreviewDir", "");
            if (string.IsNullOrEmpty(dir))
            {
                Debug.LogWarning("FieldNoteArt: set SessionState FieldNoteArt.PreviewDir first.");
                return;
            }

            string report;
            try
            {
                report = GenerateAll(dir);
                // Contact sheet of the three panels on parchment colour, for review.
                var panels = new[] { Panel1(PanelW, PanelH, 101), Panel2(PanelW, PanelH, 102), Panel3(PanelW, PanelH, 103) };
                var sheet = new InkCanvas(PanelW * 3 + 80, PanelH + 40);
                for (int i = 0; i < sheet.Px.Length; i++) sheet.Px[i] = new Color(0.92f, 0.82f, 0.64f, 1f);
                for (int k = 0; k < 3; k++)
                    for (int y = 0; y < PanelH; y++)
                        for (int x = 0; x < PanelW; x++)
                        {
                            Color p = panels[k].Px[y * PanelW + x];
                            if (p.a > 0f) sheet.Over(20 + k * (PanelW + 20) + x, 20 + y, new Color(p.r, p.g, p.b, 1f), p.a);
                        }

                sheet.Save(Path.Combine(dir, "_panels.png"));
            }
            catch (System.Exception e) { report = "ERR " + e; }
            File.WriteAllText(Path.Combine(dir, "_report.txt"), report);
        }

        public static string GenerateAll(string dir)
        {
            Directory.CreateDirectory(dir);
            var report = new System.Text.StringBuilder();
            void Save(string name, InkCanvas c)
            {
                c.Save(Path.Combine(dir, name + ".png"));
                report.Append(name).Append(' ').Append(c.W).Append('x').Append(c.H).Append('\n');
            }

            Save("FN_Panel1", Panel1(PanelW, PanelH, 101));
            Save("FN_Panel2", Panel2(PanelW, PanelH, 102));
            Save("FN_Panel3", Panel3(PanelW, PanelH, 103));
            Save("FN_SheetFrame", Frame(1600, 892, 104, true));
            Save("FN_CoverFrame", Frame(800, 572, 105, false));
            Save("FN_Fold", Fold(48, 512));
            Save("FN_Sprig", Sprig(320, 120, 106));
            Save("FN_Plate", Plate(640, 160, false, 107));
            Save("FN_PlateTarnished", Plate(640, 160, true, 108));
            Save("FN_Corner", Corner(128, 109));
            Save("FN_Padlock", Padlock(128, 160, 110));
            Save("FN_Cloth", Cloth(1024, 512, 111));
            Save("FN_GoldStar", GoldStarTex(128));
            // Parchment at each sheet's own proportions (so the torn edge and
            // fibres aren't stretched), from the noticeboard's own generator.
            Save("FN_SheetParchment", NoticeboardParchment(1600, 892, 112));
            Save("FN_CoverParchment", NoticeboardParchment(800, 572, 113));
            return report.ToString();
        }

        public const int PanelW = 960, PanelH = 800;

        /// <summary>Line / glyph weight for the diagrams (they're read at ~17 deg wide).</summary>
        public static float K = 2.3f;

        static InkCanvas NoticeboardParchment(int w, int h, int seed)
        {
            var m = typeof(NoticeboardArt).GetMethod("Parchment", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (m == null) throw new System.Exception("NoticeboardArt.Parchment not found");
            return (InkCanvas)m.Invoke(null, new object[] { w, h, seed });
        }

        static InkCanvas GoldStarTex(int s)
        {
            var c = new InkCanvas(s, s);
            Vector2 p = new Vector2(s * 0.5f, s * 0.5f);
            c.Star(p, s * 0.46f, 4, 0.18f, Gold, 0f);
            c.Star(p, s * 0.27f, 4, 0.24f, Gold, 45f);
            return c;
        }

        // ---------------------------------------------------------------- Diagram camera

        // A simple pinhole (or near-orthographic, with a long lens) camera over
        // the diagram frame. Project() gives canvas pixels after Fit().
        sealed class View
        {
            readonly Matrix4x4 world2cam;
            readonly bool ortho;
            float scale = 1f;
            Vector2 offset;

            public View(Vector3 eye, Vector3 target, bool orthographic)
            {
                world2cam = Matrix4x4.TRS(eye, Quaternion.LookRotation(target - eye, Vector3.up), Vector3.one).inverse;
                ortho = orthographic;
            }

            public Vector2 Raw(Vector3 p)
            {
                Vector3 c = world2cam.MultiplyPoint3x4(p);
                return ortho ? new Vector2(c.x, c.y) : new Vector2(c.x / c.z, c.y / c.z) * 1000f;
            }

            public float Depth(Vector3 p) => world2cam.MultiplyPoint3x4(p).z;

            public void Fit(IEnumerable<Vector3> pts, Rect area)
            {
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
                foreach (Vector3 p in pts)
                {
                    Vector2 r = Raw(p);
                    min = Vector2.Min(min, r);
                    max = Vector2.Max(max, r);
                }

                Vector2 size = max - min;
                scale = Mathf.Min(area.width / Mathf.Max(1e-4f, size.x), area.height / Mathf.Max(1e-4f, size.y));
                offset = area.center - (min + max) * 0.5f * scale;
            }

            public Vector2 P(Vector3 p) => Raw(p) * scale + offset;
        }

        public static float DiagramPitch = 22f, DiagramYaw = -62f;
        // Panel 2 looks more from behind Earth so the sky (and the W on it) is nearly face-on.
        public static float SkyPitch = 16f, SkyYaw = -40f;

        // Shared 3/4 view for panels 1 and 2 (same camera, so the stars sit in
        // the same places on both): from behind Earth, up and to the left.
        static View DiagramView(Vector3[] stars, IEnumerable<Vector3> extra, Rect area, float pitch, float yaw)
        {
            Vector3 target = new Vector3(0f, 0f, 300f);
            Vector3 eye = target + Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, 0f, -4000f);
            var view = new View(eye, target, false);
            var all = new List<Vector3>(stars) { Vector3.zero };
            all.AddRange(extra);
            view.Fit(all, area);
            return view;
        }

        // ---------------------------------------------------------------- Panels

        // Panel 1: stars at their real distances, with drop lines to a floor
        // grid to show depth. No pattern lines: just scattered stars.
        static InkCanvas Panel1(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            Vector3[] stars = StarPositions(Spread);
            float floorY = FloorY(stars);
            var grid = FloorCorners(stars, floorY);
            View view = DiagramView(stars, grid, new Rect(90, 80, w - 170, h - 150), DiagramPitch, DiagramYaw);

            DrawFloorGrid(c, view, stars, floorY, seed);
            for (int i = 0; i < stars.Length; i++)
            {
                Vector3 foot = new Vector3(stars[i].x, floorY, stars[i].z);
                Dashed(c, view.P(stars[i]), view.P(foot), K * 2f, Pencil, K * 9f, K * 7f, seed + i);
                c.Ellipse(view.P(foot), K * 9f, K * 3.5f, 0f, K * 1.6f, Pencil, 1f, seed + 20 + i);
            }

            Earth(c, view.P(Vector3.zero), K * 20f, seed + 40);
            for (int i = 0; i < stars.Length; i++) InkStar(c, view.P(stars[i]), K * 17f * Cassiopeia[i].Size, Ink);
            return c;
        }

        // Panel 2: the same stars, with sightlines from Earth through each star
        // out to the sky beyond, where they land in the familiar W (gold).
        static InkCanvas Panel2(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            Vector3[] stars = StarPositions(Spread);
            float skyZ = SkyDepth(stars);
            Vector3[] proj = ProjectOnto(stars, skyZ);
            var sheet = SkySheet(proj, skyZ);
            View view = DiagramView(stars, sheet, new Rect(110, 100, w - 170, h - 170), SkyPitch, SkyYaw);

            // The sky: a soft wash where the stars appear to be.
            var sheet2D = new List<Vector2>();
            foreach (Vector3 p in sheet) sheet2D.Add(view.P(p));
            c.FillPolygon(sheet2D, new Color(0.52f, 0.56f, 0.78f, 0.14f));
            Hatch(c, sheet2D, new Color(0.40f, 0.44f, 0.66f, 0.18f), seed);
            c.Stroke(Closed(sheet2D), K * 1.6f, Pencil, 0.05f, seed + 1);

            // Sightlines: solid-ish from Earth to the star, dashed beyond it.
            Vector2 earth = view.P(Vector3.zero);
            for (int i = 0; i < stars.Length; i++)
            {
                Dashed(c, earth, view.P(stars[i]), K * 1.8f, new Color(Ink.r, Ink.g, Ink.b, 0.55f), K * 14f, K * 6f, seed + 10 + i);
                Dashed(c, view.P(stars[i]), view.P(proj[i]), K * 1.5f, Pencil, K * 5f, K * 8f, seed + 20 + i);
            }

            // The W where the sightlines meet the sky.
            for (int i = 0; i < proj.Length - 1; i++) GoldLine(c, view.P(proj[i]), view.P(proj[i + 1]), seed + 30 + i);
            for (int i = 0; i < proj.Length; i++) GoldStar(c, view.P(proj[i]), K * 13f * Cassiopeia[i].Size);

            Earth(c, earth, K * 20f, seed + 40);
            for (int i = 0; i < stars.Length; i++) InkStar(c, view.P(stars[i]), K * 17f * Cassiopeia[i].Size, Ink);
            return c;
        }

        // Panel 3: the same five stars seen from somewhere else (a quarter turn
        // round the group): joined in the same order they no longer make a W.
        static InkCanvas Panel3(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            Vector3[] stars = StarPositions(Spread);
            Vector3 centre = Vector3.zero;
            foreach (Vector3 s in stars) centre += s;
            centre /= stars.Length;
            Vector3 eye = centre + OtherViewpoint(stars, centre) * (Vector3.zero - centre);
            var view = new View(eye, centre, false);
            view.Fit(stars, new Rect(90, 330, w - 200, h - 420));

            var pts = new List<Vector2>();
            foreach (Vector3 s in stars) pts.Add(view.P(s));

            for (int i = 0; i < stars.Length - 1; i++) GoldLine(c, pts[i], pts[i + 1], seed + 30 + i);
            for (int i = 0; i < stars.Length; i++) GoldStar(c, pts[i], K * 15f * Cassiopeia[i].Size);

            // Earth bottom right, with the viewpoint swinging away from it.
            Vector2 e = new Vector2(w - 230f, 140f);
            Earth(c, e, K * 20f, seed + 40);
            var arc = new List<Vector2>();
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f;
                float a = Mathf.Lerp(-20f, 75f, t) * Mathf.Deg2Rad;
                arc.Add(e + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * K * 58f);
            }

            c.Stroke(arc, K * 3f, Ink, 0.2f, seed + 50);
            ArrowHead(c, arc[arc.Count - 3], arc[arc.Count - 1], K * 15f, Ink, seed + 51);
            // A small eye at the arrow's end: the new viewpoint.
            Eye(c, arc[arc.Count - 1] + new Vector2(-6f, K * 30f), K * 20f, seed + 52);
            return c;
        }

        /// <summary>
        /// A quarter-turn-or-more away from Earth's view (yaw 60..120 deg round
        /// the group, pitch -35..35) whose pattern is as compact (least
        /// sliver-like) as possible, so the change reads as a new shape rather
        /// than a squashed W.
        /// </summary>
        static Quaternion OtherViewpoint(Vector3[] stars, Vector3 centre)
        {
            Quaternion best = Quaternion.Euler(0f, 90f, 0f);
            float bestScore = -1f;
            for (float yaw = 60f; yaw <= 120f; yaw += 5f)
            {
                foreach (float sign in new[] { -1f, 1f })
                {
                    for (float pitch = -35f; pitch <= 35f; pitch += 5f)
                    {
                        Quaternion q = Quaternion.Euler(pitch, yaw * sign, 0f);
                        var v = new View(centre + q * (Vector3.zero - centre), centre, false);
                        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
                        foreach (Vector3 s in stars)
                        {
                            Vector2 r = v.Raw(s);
                            min = Vector2.Min(min, r);
                            max = Vector2.Max(max, r);
                        }

                        Vector2 size = max - min;
                        float aspect = Mathf.Min(size.x, size.y) / Mathf.Max(size.x, size.y);
                        // Prefer landscape-ish shapes about 0.6 tall, and no two stars on top of each other.
                        float minGap = float.MaxValue;
                        for (int i = 0; i < stars.Length; i++)
                            for (int j = i + 1; j < stars.Length; j++)
                                minGap = Mathf.Min(minGap, (v.Raw(stars[i]) - v.Raw(stars[j])).magnitude / Mathf.Max(size.x, size.y));
                        float score = -Mathf.Abs(aspect - 0.6f) + Mathf.Min(minGap, 0.2f) * 2f;
                        if (size.x < size.y) score -= 0.3f;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = q;
                        }
                    }
                }
            }

            return best;
        }

        static void Hatch(InkCanvas c, List<Vector2> poly, Color col, int seed)
        {
            Vector2 min = poly[0], max = poly[0];
            foreach (Vector2 p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            int k = 0;
            for (float x = min.x - (max.y - min.y); x < max.x; x += K * 9f)
            {
                var seg = new List<Vector2>();
                for (float t = 0f; t <= 1f; t += 0.02f)
                {
                    Vector2 q = new Vector2(x + t * (max.y - min.y) * 0.6f, min.y + t * (max.y - min.y));
                    if (Inside(poly, q)) seg.Add(q);
                    else if (seg.Count > 1) break;
                }

                if (seg.Count > 1) c.Stroke(seg, K * 1.2f, col, 0.5f, seed + k++);
            }
        }

        // ---------------------------------------------------------------- Diagram helpers

        static float FloorY(Vector3[] stars)
        {
            float y = 0f;
            foreach (Vector3 s in stars) y = Mathf.Min(y, s.y);
            return y - 120f;
        }

        static List<Vector3> FloorCorners(Vector3[] stars, float y)
        {
            float x0 = -80f, x1 = 80f, z1 = 0f;
            foreach (Vector3 s in stars)
            {
                x0 = Mathf.Min(x0, s.x - 60f);
                x1 = Mathf.Max(x1, s.x + 60f);
                z1 = Mathf.Max(z1, s.z + 60f);
            }

            return new List<Vector3> { new Vector3(x0, y, -60f), new Vector3(x1, y, -60f), new Vector3(x1, y, z1), new Vector3(x0, y, z1) };
        }

        static void DrawFloorGrid(InkCanvas c, View view, Vector3[] stars, float y, int seed)
        {
            var k = FloorCorners(stars, y);
            float x0 = k[0].x, x1 = k[1].x, z0 = k[0].z, z1 = k[2].z;
            var faint = new Color(Pencil.r, Pencil.g, Pencil.b, 0.28f);
            int n = 0;
            for (float z = 0f; z <= z1; z += 100f)
            {
                c.Stroke(new List<Vector2> { view.P(new Vector3(x0, y, z)), view.P(new Vector3(x1, y, z)) }, K * 1.2f, faint, 0.3f, seed + n++);
            }

            for (float x = Mathf.Ceil(x0 / 100f) * 100f; x <= x1; x += 100f)
            {
                c.Stroke(new List<Vector2> { view.P(new Vector3(x, y, z0)), view.P(new Vector3(x, y, z1)) }, K * 1.2f, faint, 0.3f, seed + n++);
            }
        }

        static float SkyDepth(Vector3[] stars)
        {
            float z = 0f;
            foreach (Vector3 s in stars) z = Mathf.Max(z, s.z);
            return z + 150f;
        }

        static Vector3[] ProjectOnto(Vector3[] stars, float z)
        {
            var r = new Vector3[stars.Length];
            for (int i = 0; i < stars.Length; i++) r[i] = stars[i] * (z / stars[i].z);
            return r;
        }

        static List<Vector3> SkySheet(Vector3[] proj, float z)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (Vector3 p in proj)
            {
                x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
                y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
            }

            float mx = (x1 - x0) * 0.12f, my = (y1 - y0) * 0.25f + 30f;
            return new List<Vector3>
            {
                new Vector3(x0 - mx, y0 - my, z), new Vector3(x1 + mx, y0 - my, z),
                new Vector3(x1 + mx, y1 + my, z), new Vector3(x0 - mx, y1 + my, z)
            };
        }

        static void InkStar(InkCanvas c, Vector2 p, float r, Color col)
        {
            c.Star(p, r, 4, 0.22f, col, 0f);
            c.Star(p, r * 0.55f, 4, 0.3f, col, 45f);
        }

        static void GoldStar(InkCanvas c, Vector2 p, float r)
        {
            c.Dot(p, r * 0.9f, new Color(Gold.r, Gold.g, Gold.b, 0.22f));
            c.Star(p, r, 4, 0.22f, Gold, 0f);
            c.Star(p, r * 0.55f, 4, 0.3f, Gold, 45f);
            c.Dot(p, r * 0.18f, new Color(1f, 0.93f, 0.7f, 1f));
        }

        static void GoldLine(InkCanvas c, Vector2 a, Vector2 b, int seed)
        {
            Vector2 d = (b - a).normalized;
            c.Stroke(Wavy(a + d * K * 15f, b - d * K * 15f, 0.6f, seed), K * 3f, Gold, 0.3f, seed);
        }

        static void Dashed(InkCanvas c, Vector2 a, Vector2 b, float width, Color col, float dash, float gap, int seed)
        {
            float len = (b - a).magnitude;
            if (len < 1f) return;
            Vector2 d = (b - a) / len;
            int k = 0;
            for (float s = 0f; s < len; s += dash + gap)
            {
                float e = Mathf.Min(len, s + dash);
                c.Stroke(new List<Vector2> { a + d * s, a + d * e }, width, col, 0f, seed + k++);
            }
        }

        static void Wash(InkCanvas c, List<Vector2> poly, Color col, int seed)
        {
            c.FillPolygon(poly, col);
            // Uneven watercolour: a few soft overlapping blots inside.
            var rnd = new System.Random(seed);
            Vector2 min = poly[0], max = poly[0];
            foreach (Vector2 p in poly) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            for (int i = 0; i < 14; i++)
            {
                var centre = new Vector2(Mathf.Lerp(min.x, max.x, (float)rnd.NextDouble()), Mathf.Lerp(min.y, max.y, (float)rnd.NextDouble()));
                if (!Inside(poly, centre)) continue;
                float r = 20f + (float)rnd.NextDouble() * 40f;
                c.Dot(centre, r, new Color(col.r, col.g, col.b, col.a * 0.45f));
            }
        }

        static void Cloud(InkCanvas c, List<Vector2> pts, int seed)
        {
            var rnd = new System.Random(seed);
            var col = new Color(0.52f, 0.56f, 0.78f, 0.07f);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                for (int k = 0; k < 6; k++)
                {
                    Vector2 p = Vector2.Lerp(pts[i], pts[i + 1], (float)rnd.NextDouble()) + new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 60f;
                    c.Dot(p, 30f + (float)rnd.NextDouble() * 35f, col);
                }
            }
        }

        static bool Inside(List<Vector2> poly, Vector2 q)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > q.y) != (b.y > q.y) && q.x < (b.x - a.x) * (q.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }

            return inside;
        }

        // Small hand-tinted Earth: blue wash, two green land blots, ink rim and a meridian.
        static void Earth(InkCanvas c, Vector2 p, float r, int seed)
        {
            c.Dot(p, r, new Color(0.50f, 0.66f, 0.80f, 1f));
            c.Dot(p + new Vector2(-r * 0.3f, r * 0.3f), r * 0.55f, new Color(0.62f, 0.76f, 0.86f, 0.6f));
            var land1 = Blob(p + new Vector2(-r * 0.25f, r * 0.15f), r * 0.42f, r * 0.55f, seed);
            var land2 = Blob(p + new Vector2(r * 0.38f, -r * 0.3f), r * 0.3f, r * 0.36f, seed + 1);
            c.FillPolygon(Clip(land1, p, r * 0.96f), new Color(0.50f, 0.58f, 0.34f, 0.95f));
            c.FillPolygon(Clip(land2, p, r * 0.96f), new Color(0.50f, 0.58f, 0.34f, 0.95f));
            c.Ellipse(p, r * 0.45f, r, 0f, r * 0.04f, new Color(Ink.r, Ink.g, Ink.b, 0.5f), 1f, seed + 2);
            c.Ellipse(p, r, r, 0f, r * 0.09f, Ink, 1.04f, seed + 3);
        }

        static List<Vector2> Blob(Vector2 centre, float rx, float ry, int seed)
        {
            var l = new List<Vector2>();
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                float k = 0.75f + 0.5f * Mathf.PerlinNoise(Mathf.Cos(a) + seed * 0.71f, Mathf.Sin(a) + seed * 0.37f);
                l.Add(centre + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry) * k);
            }

            return l;
        }

        static List<Vector2> Clip(List<Vector2> poly, Vector2 centre, float r)
        {
            var l = new List<Vector2>();
            foreach (Vector2 p in poly)
            {
                Vector2 d = p - centre;
                l.Add(d.magnitude > r ? centre + d.normalized * r : p);
            }

            return l;
        }

        static void Eye(InkCanvas c, Vector2 p, float r, int seed)
        {
            var top = new List<Vector2>();
            var bottom = new List<Vector2>();
            for (int i = 0; i <= 20; i++)
            {
                float t = i / 20f;
                float x = Mathf.Lerp(-r, r, t);
                float y = Mathf.Sin(t * Mathf.PI) * r * 0.5f;
                top.Add(p + new Vector2(x, y));
                bottom.Add(p + new Vector2(x, -y));
            }

            c.Stroke(top, r * 0.12f, Ink, 0.2f, seed);
            c.Stroke(bottom, r * 0.12f, Ink, 0.2f, seed + 1);
            c.Dot(p, r * 0.3f, Ink);
        }

        static void ArrowHead(InkCanvas c, Vector2 from, Vector2 tip, float size, Color col, int seed)
        {
            Vector2 d = (tip - from).normalized;
            Vector2 n = new Vector2(-d.y, d.x);
            c.Stroke(new List<Vector2> { tip - d * size + n * size * 0.6f, tip, tip - d * size - n * size * 0.6f }, size * 0.2f, col, 0.2f, seed);
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

        static List<Vector2> Closed(List<Vector2> pts)
        {
            var l = new List<Vector2>(pts) { pts[0] };
            return l;
        }

        // ---------------------------------------------------------------- Sheet ornaments

        // A fine gold rule just inside the paper edge, a hairline ink rule inside
        // that, and small gold corner stars. Transparent elsewhere.
        static InkCanvas Frame(int w, int h, int seed, bool sheet)
        {
            var c = new InkCanvas(w, h);
            float inset = sheet ? 70f : 52f;
            var gold = new Color(Gold.r, Gold.g, Gold.b, 0.85f);
            Rect r = new Rect(inset, inset, w - inset * 2f, h - inset * 2f);
            float cut = 18f; // corners are left open for the stars
            void Side(Vector2 a, Vector2 b, float width, Color col, int s)
            {
                Vector2 d = (b - a).normalized;
                c.Stroke(Wavy(a + d * cut, b - d * cut, 0.5f, s), width, col, 0.04f, s);
            }

            Vector2 bl = new Vector2(r.xMin, r.yMin), br = new Vector2(r.xMax, r.yMin), tr = new Vector2(r.xMax, r.yMax), tl = new Vector2(r.xMin, r.yMax);
            Side(bl, br, 2.6f, gold, seed);
            Side(br, tr, 2.6f, gold, seed + 1);
            Side(tr, tl, 2.6f, gold, seed + 2);
            Side(tl, bl, 2.6f, gold, seed + 3);
            float i2 = 9f;
            var ink = new Color(Ink.r, Ink.g, Ink.b, 0.35f);
            Side(bl + new Vector2(i2, i2), br + new Vector2(-i2, i2), 1.3f, ink, seed + 4);
            Side(br + new Vector2(-i2, i2), tr + new Vector2(-i2, -i2), 1.3f, ink, seed + 5);
            Side(tr + new Vector2(-i2, -i2), tl + new Vector2(i2, -i2), 1.3f, ink, seed + 6);
            Side(tl + new Vector2(i2, -i2), bl + new Vector2(i2, i2), 1.3f, ink, seed + 7);
            foreach (Vector2 p in new[] { bl, br, tr, tl })
            {
                c.Star(p, 13f, 4, 0.2f, Gold, 0f);
                c.Star(p, 7f, 4, 0.25f, Gold, 45f);
            }

            return c;
        }

        // The fold: a soft valley shadow with a faint highlight beside it.
        static InkCanvas Fold(int w, int h)
        {
            var c = new InkCanvas(w, h);
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);
                float ends = Mathf.Clamp01(Mathf.Min(v, 1f - v) * 12f);
                for (int x = 0; x < w; x++)
                {
                    float u = (x / (float)(w - 1)) * 2f - 1f;
                    float shadow = Mathf.Exp(-Mathf.Pow((u + 0.08f) * 6f, 2f)) * 0.38f;
                    float light = Mathf.Exp(-Mathf.Pow((u - 0.28f) * 7f, 2f)) * 0.22f;
                    if (shadow >= light)
                        c.Px[y * w + x] = new Color(0.35f, 0.22f, 0.10f, shadow * ends);
                    else
                        c.Px[y * w + x] = new Color(1f, 0.97f, 0.88f, light * ends);
                }
            }

            return c;
        }

        // A gold laurel sprig (for the left of the cover title; mirrored for the right).
        static InkCanvas Sprig(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            var stem = new List<Vector2>();
            for (int i = 0; i <= 40; i++)
            {
                float t = i / 40f;
                stem.Add(new Vector2(Mathf.Lerp(w - 14f, 24f, t), h * 0.32f + Mathf.Sin(t * Mathf.PI * 0.9f) * h * 0.38f));
            }

            c.Stroke(stem, 3f, Gold, 0.4f, seed);
            for (int i = 4; i < stem.Count - 2; i += 5)
            {
                Vector2 p = stem[i];
                Vector2 d = (stem[i + 1] - stem[i - 1]).normalized;
                Vector2 n = new Vector2(-d.y, d.x);
                foreach (int side in new[] { -1, 1 })
                {
                    Vector2 tip = p + (n * side * 0.9f - d * 0.7f).normalized * 26f;
                    var leaf = new List<Vector2>();
                    Vector2 mid = (p + tip) * 0.5f;
                    Vector2 nn = new Vector2(-(tip - p).y, (tip - p).x).normalized * 7f;
                    leaf.Add(p); leaf.Add(mid + nn); leaf.Add(tip); leaf.Add(mid - nn);
                    c.FillPolygon(leaf, new Color(Gold.r, Gold.g, Gold.b, 0.9f));
                }
            }

            return c;
        }

        // ---------------------------------------------------------------- Box fittings

        // A brass nameplate: rounded-corner plate with a soft vertical sheen, a
        // bevel, an engraved inner line and two pins. Tarnished = darker, flatter.
        static InkCanvas Plate(int w, int h, bool tarnished, int seed)
        {
            var c = new InkCanvas(w, h);
            Color hi = tarnished ? new Color(0.52f, 0.42f, 0.26f) : new Color(0.98f, 0.82f, 0.46f);
            Color mid = tarnished ? new Color(0.40f, 0.31f, 0.18f) : new Color(0.80f, 0.56f, 0.20f);
            Color lo = tarnished ? new Color(0.24f, 0.18f, 0.10f) : new Color(0.50f, 0.32f, 0.10f);
            float rad = h * 0.22f, pad = 4f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float d = RoundRect(x + 0.5f, y + 0.5f, pad, pad, w - pad, h - pad, rad);
                    float a = Mathf.Clamp01(-d + 0.5f);
                    if (a <= 0f) continue;
                    float v = y / (float)(h - 1);
                    // Sheen: brightest just above the middle, darker at the edges.
                    float sheen = Mathf.Exp(-Mathf.Pow((v - 0.62f) * 2.6f, 2f));
                    Color col = Color.Lerp(lo, mid, Mathf.Clamp01(v * 1.4f));
                    col = Color.Lerp(col, hi, sheen * 0.75f);
                    // Brushed grain + tarnish blotches.
                    float grain = Mathf.PerlinNoise(x * 0.9f, y * 0.04f + seed);
                    col *= 0.94f + 0.1f * grain;
                    if (tarnished)
                    {
                        float blot = Mathf.PerlinNoise(x * 0.02f + seed, y * 0.05f);
                        col = Color.Lerp(col, new Color(0.22f, 0.24f, 0.18f), Mathf.Clamp01((blot - 0.5f) * 1.6f) * 0.5f);
                    }

                    // Bevel: light top-left edge, dark bottom-right edge.
                    float edge = Mathf.Clamp01(1f + d / 7f);
                    if (edge > 0f)
                    {
                        bool lit = y > h * 0.5f;
                        col = Color.Lerp(col, lit ? hi * 1.08f : lo * 0.7f, edge * 0.7f);
                    }

                    c.Px[y * w + x] = new Color(col.r, col.g, col.b, a);
                }
            }

            // Engraved inner line (dark groove with a light lip below it).
            float ins = 18f;
            var groove = RoundRectPath(ins, ins, w - ins, h - ins, rad * 0.6f);
            c.Stroke(groove, 2.4f, new Color(lo.r * 0.6f, lo.g * 0.6f, lo.b * 0.6f, 0.85f), 0f, seed, keepAlpha: true);
            var lip = RoundRectPath(ins + 1.5f, ins - 1.5f, w - ins + 1.5f, h - ins - 1.5f, rad * 0.6f);
            c.Stroke(lip, 1.2f, new Color(hi.r, hi.g, hi.b, 0.5f), 0f, seed + 1, keepAlpha: true);
            // Pins.
            foreach (float px in new[] { 30f, w - 30f })
            {
                Vector2 p = new Vector2(px, h * 0.5f);
                c.Dot(p, 7f, lo * 0.8f);
                c.Dot(p + new Vector2(-1.5f, 1.5f), 4.5f, hi);
            }

            return c;
        }

        static float RoundRect(float x, float y, float x0, float y0, float x1, float y1, float r)
        {
            float cx = Mathf.Clamp(x, x0 + r, x1 - r), cy = Mathf.Clamp(y, y0 + r, y1 - r);
            float dx = x - cx, dy = y - cy;
            float outside = Mathf.Sqrt(dx * dx + dy * dy) - r;
            return outside;
        }

        static List<Vector2> RoundRectPath(float x0, float y0, float x1, float y1, float r)
        {
            var l = new List<Vector2>();
            Vector2[] centres = { new Vector2(x1 - r, y1 - r), new Vector2(x0 + r, y1 - r), new Vector2(x0 + r, y0 + r), new Vector2(x1 - r, y0 + r) };
            for (int k = 0; k < 4; k++)
            {
                for (int i = 0; i <= 8; i++)
                {
                    float a = (k * 90f + i / 8f * 90f) * Mathf.Deg2Rad;
                    l.Add(centres[k] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            }

            l.Add(l[0]);
            return l;
        }

        // An L-shaped brass corner fitting (bottom-left corner of the texture;
        // mirror the quad for the other corners), with one pin.
        static InkCanvas Corner(int s, int seed)
        {
            var c = new InkCanvas(s, s);
            float arm = s * 0.34f, len = s - 4f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    bool inL = (x < arm && y < len) || (y < arm && x < len);
                    if (!inL) continue;
                    // Rounded arm ends.
                    float d = Mathf.Min(DistToSegment(new Vector2(x, y), new Vector2(arm * 0.5f, arm * 0.5f), new Vector2(arm * 0.5f, len - arm * 0.5f)),
                                        DistToSegment(new Vector2(x, y), new Vector2(arm * 0.5f, arm * 0.5f), new Vector2(len - arm * 0.5f, arm * 0.5f)));
                    float a = Mathf.Clamp01(arm * 0.5f - d + 0.5f);
                    if (a <= 0f) continue;
                    float t = Mathf.Clamp01(1f - d / (arm * 0.5f));
                    Color col = Color.Lerp(new Color(0.48f, 0.30f, 0.10f), new Color(0.95f, 0.78f, 0.42f), t * 0.85f);
                    col *= 0.94f + 0.1f * Mathf.PerlinNoise(x * 0.3f + seed, y * 0.3f);
                    c.Px[y * s + x] = new Color(col.r, col.g, col.b, a);
                }
            }

            Vector2 pin = new Vector2(arm * 0.5f, arm * 0.5f);
            c.Dot(pin, 6f, new Color(0.35f, 0.22f, 0.08f));
            c.Dot(pin + new Vector2(-1f, 1f), 3.6f, new Color(1f, 0.86f, 0.5f));
            return c;
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return (p - (a + ab * t)).magnitude;
        }

        // A small brass padlock (shackle + body + keyhole).
        static InkCanvas Padlock(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            Color dark = new Color(0.36f, 0.22f, 0.08f, 1f);
            Color hi = new Color(0.98f, 0.82f, 0.46f, 1f);
            // Shackle.
            var arc = new List<Vector2>();
            Vector2 sc = new Vector2(w * 0.5f, h * 0.56f);
            for (int i = 0; i <= 30; i++)
            {
                float a = Mathf.Lerp(0f, Mathf.PI, i / 30f);
                arc.Add(sc + new Vector2(Mathf.Cos(a) * w * 0.26f, Mathf.Sin(a) * h * 0.28f));
            }

            arc.Insert(0, arc[0] - new Vector2(0, 12));
            arc.Add(arc[arc.Count - 1] - new Vector2(0, 12));
            c.Stroke(arc, 15f, dark, 0f, seed);
            c.Stroke(arc, 8f, new Color(0.85f, 0.66f, 0.32f), 0f, seed + 1);
            // Body.
            float x0 = w * 0.12f, x1 = w * 0.88f, y0 = h * 0.05f, y1 = h * 0.58f;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float d = RoundRect(x + 0.5f, y + 0.5f, x0, y0, x1, y1, 10f);
                    float a = Mathf.Clamp01(-d + 0.5f);
                    if (a <= 0f) continue;
                    float v = (y - y0) / (y1 - y0);
                    Color col = Color.Lerp(new Color(0.55f, 0.36f, 0.12f), hi, Mathf.Exp(-Mathf.Pow((v - 0.7f) * 2.5f, 2f)) * 0.8f);
                    col = Color.Lerp(col, dark, Mathf.Clamp01(1f + d / 4f) * 0.8f);
                    c.Over(x, y, new Color(col.r, col.g, col.b, 1f), a);
                }
            }

            // Keyhole.
            Vector2 k = new Vector2(w * 0.5f, h * 0.36f);
            c.Dot(k, 8f, new Color(0.12f, 0.07f, 0.03f));
            c.FillPolygon(new List<Vector2> { k + new Vector2(-4, 0), k + new Vector2(4, 0), k + new Vector2(6, -22), k + new Vector2(-6, -22) }, new Color(0.12f, 0.07f, 0.03f));
            return c;
        }

        // ---------------------------------------------------------------- Table cloth

        // A deep-blue star-chart cloth: faint gold circles and rays, scattered
        // small stars, a darker worn edge. Restrained, so the boxes stay in front.
        static InkCanvas Cloth(int w, int h, int seed)
        {
            var c = new InkCanvas(w, h);
            var rnd = new System.Random(seed);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    float n = Mathf.PerlinNoise(u * 6f + seed, v * 3f) * 0.6f + Mathf.PerlinNoise(u * 40f, v * 20f + seed) * 0.4f;
                    Color col = Color.Lerp(new Color(0.09f, 0.11f, 0.22f), new Color(0.16f, 0.19f, 0.36f), n);
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u) * w, Mathf.Min(v, 1f - v) * h);
                    col = Color.Lerp(new Color(0.05f, 0.06f, 0.12f), col, Mathf.Clamp01(edge / 26f));
                    c.Px[y * w + x] = new Color(col.r, col.g, col.b, 1f);
                }
            }

            var faintGold = new Color(Gold.r, Gold.g, Gold.b, 0.32f);
            // Gold border line.
            Vector2 a0 = new Vector2(22, 22), a1 = new Vector2(w - 22, h - 22);
            c.Stroke(new List<Vector2> { a0, new Vector2(a1.x, a0.y), a1, new Vector2(a0.x, a1.y), a0 }, 2f, faintGold, 0f, seed);
            // A star-chart rosette at the centre (mostly hidden under the note, peeking out).
            Vector2 centre = new Vector2(w * 0.5f, h * 0.5f);
            foreach (float r in new[] { 120f, 190f, 205f })
            {
                c.Ellipse(centre, r * 1.6f, r, 0f, 1.6f, faintGold, 1f, seed + (int)r);
            }

            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                Vector2 d = new Vector2(Mathf.Cos(a) * 1.6f, Mathf.Sin(a));
                c.Stroke(new List<Vector2> { centre + d * 120f, centre + d * 205f }, 1.2f, faintGold, 0f, seed + 40 + i);
            }

            for (int i = 0; i < 70; i++)
            {
                var p = new Vector2(30f + (float)rnd.NextDouble() * (w - 60f), 30f + (float)rnd.NextDouble() * (h - 60f));
                float r = 1.2f + (float)rnd.NextDouble() * 2.2f;
                if (rnd.NextDouble() < 0.15)
                    c.Star(p, r * 3.2f, 4, 0.25f, new Color(Gold.r, Gold.g, Gold.b, 0.55f), 0f);
                else
                    c.Dot(p, r, new Color(0.85f, 0.80f, 0.62f, 0.45f));
            }

            return c;
        }
    }
}
