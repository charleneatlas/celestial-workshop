using System.Collections.Generic;
using System.IO;
using CW.Onboarding.EditorTools;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Celestial Workshop > Build Guild Certificate.
///
///   1. Art (Assets/CW_Certificate/Art): the parchment (the noticeboard's own
///      generator, at the certificate's proportions), one ink layer (gold
///      frame, corner stars, the signing line with star ends and rays, the
///      dashes around "Hereby certifies", faint clouds, moon phases and a
///      dotted orbit) and the purple guild wax seal.
///   2. Materials on the noticeboard surface shader (lit like the Field Note).
///   3. Prefab Assets/CW_Certificate/GuildCertificate.prefab: wooden writing
///      board, parchment, ink, texts (the Field Note's fonts), the name line
///      anchor and the seal.
///   4. Scene: "Guild Certificate" under the Workstation, wired into
///      ConstellationSigning.
///
/// Safe to run again (art, materials and prefab are regenerated; the scene
/// instance is replaced). Frames: quad space (+X right, +Y up, read from -Z), metres.
/// </summary>
public static class GuildCertificateBuilder
{
    const string Root = "Assets/CW_Certificate";
    const string ArtDir = Root + "/Art";
    const string MatDir = Root + "/Materials";
    const string PrefabPath = Root + "/GuildCertificate.prefab";
    const string NB = "Assets/CW_Onboarding";
    const string FontCaps = "Assets/CW_Art/Fonts/ArchitectsDaughter SDF.asset";
    const string FontHand = "Assets/CW_Art/Fonts/Caveat SDF.asset";
    const string InstanceName = "Guild Certificate";

    // ---- Copy ----
    const string TitleText = "THE CELESTIAL WORKSHOP GUILD";
    const string CertifiesText = "Hereby certifies";
    const string ClosingText = "for the night sky of Earth";

    // ---- Layout (metres) ----
    const float PaperW = 0.26f, PaperH = 0.10f;
    const float BoardW = 0.285f, BoardH = 0.13f, BoardT = 0.012f;
    const float TitleY = 0.0305f, CertifiesY = 0.0168f, LineY = -0.012f, ClosingY = -0.0315f;
    const float TitleCap = 0.0056f, HandCap = 0.0068f;
    const float TitleMaxW = 0.17f;
    const float LineHalf = 0.075f;
    static readonly Vector2 SealCenter = new Vector2(-0.113f, 0.030f);
    const float SealSize = 0.036f;

    // ---- Art ----
    const int InkW = 1560, InkH = 600;        // 6000 px per metre
    const int PaperPxW = 1300, PaperPxH = 500;
    const int SealPx = 320;

    static readonly Color Ink = new Color(0.22f, 0.12f, 0.05f, 1f);
    static readonly Color InkBody = new Color(0.27f, 0.16f, 0.08f, 1f);
    static readonly Color Gold = new Color(0.78f, 0.56f, 0.20f, 1f);
    static readonly Color Lavender = new Color(0.50f, 0.46f, 0.76f, 1f);
    const int SortBoard = 0, SortShadow = 1, SortPaper = 2, SortInk = 3, SortText = 4, SortSeal = 6;

    [MenuItem("Tools/Celestial Workshop/Build Guild Certificate")]
    static void BuildMenu() => Debug.Log("GuildCertificateBuilder: " + Build(true));

    public static string Build(bool save)
    {
        var log = new List<string>();
        log.Add(GenerateArt());

        var wood = AssetDatabase.LoadAssetAtPath<Material>($"{NB}/Materials/NB_Wood.mat");
        var shadow = AssetDatabase.LoadAssetAtPath<Material>($"{NB}/Materials/NB_ParchmentShadow.mat");
        if (wood == null || shadow == null) return "missing NB_Wood / NB_ParchmentShadow (run Build Workshop Noticeboard)";

        Directory.CreateDirectory(MatDir);
        Material paper = Surface("GC_Parchment", 0.45f, true, false);
        Material ink = Surface("GC_Ink", 0.1f, false, true);
        Material seal = Surface("GC_Seal", 0.55f, false, false);
        AssetDatabase.SaveAssets();

        // ---- Prefab ----
        var root = new GameObject(InstanceName);
        try
        {
            var certificate = root.AddComponent<GuildCertificate>();
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            MeshRenderer board = Cube(visual, "Writing Board", wood);
            // Turned a quarter so the wood's grain runs along the board.
            board.transform.localPosition = new Vector3(0f, 0f, BoardT * 0.5f + 0.0015f);
            board.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            board.transform.localScale = new Vector3(BoardH, BoardW, BoardT);
            board.sortingOrder = SortBoard;

            Quad(visual, "Parchment Shadow", shadow, new Vector2(0.003f, -0.004f), new Vector2(PaperW * 1.06f, PaperH * 1.14f), SortShadow, 0.0008f);
            Quad(visual, "Parchment", paper, Vector2.zero, new Vector2(PaperW, PaperH), SortPaper, 0f);
            Quad(visual, "Ink", ink, Vector2.zero, new Vector2(PaperW, PaperH), SortInk, -0.0004f);

            var placed = new List<(TextMeshPro, Vector3)>();
            TextMeshPro title = Text(visual, "Title", TitleText, FontCaps, TitleCap, Ink, new Vector2(0f, TitleY), 0.2f, placed, TitleMaxW, spacing: 14f);
            Text(visual, "Hereby Certifies", CertifiesText, FontHand, HandCap, InkBody, new Vector2(0f, CertifiesY), 0.1f, placed);
            Text(visual, "For The Night Sky", ClosingText, FontHand, HandCap, InkBody, new Vector2(0f, ClosingY), 0.15f, placed);

            Transform nameLine = new GameObject("Name Line").transform;
            nameLine.SetParent(visual, false);
            nameLine.localPosition = new Vector3(0f, LineY, -0.0015f);

            MeshRenderer sealQuad = Quad(visual, "Wax Seal", seal, SealCenter, new Vector2(SealSize, SealSize), SortSeal, -0.0025f);

            var so = new SerializedObject(certificate);
            so.FindProperty("visual").objectReferenceValue = visual;
            so.FindProperty("nameLine").objectReferenceValue = nameLine;
            so.FindProperty("seal").objectReferenceValue = sealQuad.transform;
            so.FindProperty("lineWidth").floatValue = LineHalf * 2f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // TMP RectTransforms: re-assert (see SelectionTableBuilder).
            foreach (var (text, pos) in placed) text.rectTransform.anchoredPosition3D = pos;
            log.Add($"title {title.GetPreferredValues(TitleText).x * title.transform.lossyScale.x:F3} m wide");

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            log.Add("prefab saved");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        log.Add(HookUpScene(save));
        return string.Join("; ", log);
    }

    // ================================================================ Scene

    static string HookUpScene(bool save)
    {
        var station = Object.FindFirstObjectByType<WorkstationMini>(FindObjectsInactive.Include);
        var signing = Object.FindFirstObjectByType<ConstellationSigning>(FindObjectsInactive.Include);
        if (station == null || signing == null) return "scene: missing WorkstationMini / ConstellationSigning";

        // Every earlier instance (whatever it was renamed to) is replaced.
        int removed = 0;
        foreach (GuildCertificate old in station.GetComponentsInChildren<GuildCertificate>(true))
        {
            Undo.DestroyObjectImmediate(old.gameObject);
            removed++;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, station.transform);
        instance.name = InstanceName;
        Undo.RegisterCreatedObjectUndo(instance, "Guild Certificate");
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        var certificate = instance.GetComponent<GuildCertificate>();
        Camera viewer = (Camera)new SerializedObject(station).FindProperty("viewer").objectReferenceValue;
        var cso = new SerializedObject(certificate);
        cso.FindProperty("workstation").objectReferenceValue = station;
        cso.FindProperty("viewer").objectReferenceValue = viewer;
        cso.FindProperty("signing").objectReferenceValue = signing;
        cso.FindProperty("gameFlow").objectReferenceValue = Object.FindFirstObjectByType<GameFlow>(FindObjectsInactive.Include);
        cso.FindProperty("solveController").objectReferenceValue = Object.FindFirstObjectByType<ConstellationSolveController>(FindObjectsInactive.Include);
        cso.ApplyModifiedProperties();

        var sso = new SerializedObject(signing);
        sso.FindProperty("certificate").objectReferenceValue = certificate;
        sso.ApplyModifiedProperties();

        // The "Draw along the line to sign" hint points at the certificate.
        string hint = "";
        var director = Object.FindFirstObjectByType<CW.Guidance.GuidanceDirector>(FindObjectsInactive.Include);
        if (director != null)
        {
            var dso = new SerializedObject(director);
            SerializedProperty steps = dso.FindProperty("steps");
            for (int i = 0; i < steps.arraySize; i++)
            {
                SerializedProperty step = steps.GetArrayElementAtIndex(i);
                if (step.FindPropertyRelative("id").stringValue != "sign") continue;
                step.FindPropertyRelative("target").objectReferenceValue = instance.transform.Find("Visual");
                step.FindPropertyRelative("labelOffset").vector3Value = new Vector3(0f, 0.03f, 0f);
                hint = ", sign hint retargeted";
            }

            dso.ApplyModifiedProperties();
        }

        EditorSceneManager.MarkSceneDirty(station.gameObject.scene);
        if (save) EditorSceneManager.SaveScene(station.gameObject.scene);
        return $"scene: certificate under the Workstation (replaced {removed}), signing wired" + hint;
    }

    // ================================================================ Art

    static string GenerateArt()
    {
        Directory.CreateDirectory(ArtDir);
        var parchment = typeof(NoticeboardArt).GetMethod("Parchment", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (parchment == null) return "art: NoticeboardArt.Parchment not found";
        ((InkCanvas)parchment.Invoke(null, new object[] { PaperPxW, PaperPxH, 211 })).Save($"{ArtDir}/GC_Parchment.png");
        InkLayer().Save($"{ArtDir}/GC_Ink.png");
        SealTexture().Save($"{ArtDir}/GC_Seal.png");
        AssetDatabase.Refresh();
        foreach (string name in new[] { "GC_Parchment", "GC_Ink", "GC_Seal" })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath($"{ArtDir}/{name}.png");
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.anisoLevel = 4;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.maxTextureSize = 2048;
            ti.sRGBTexture = true;
            ti.SaveAndReimport();
        }

        return "art generated";
    }

    // Paper metres -> ink layer pixels.
    static Vector2 P(float x, float y) => new Vector2((x / PaperW + 0.5f) * InkW, (y / PaperH + 0.5f) * InkH);
    static float Px(float metres) => metres / PaperW * InkW;

    static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

    static InkCanvas InkLayer()
    {
        var c = new InkCanvas(InkW, InkH);

        // Frame: a gold rule, an ink hairline inside it, gold corner stars.
        Rect outer = Rect.MinMaxRect(P(-0.121f, 0f).x, P(0f, -0.0435f).y, P(0.121f, 0f).x, P(0f, 0.0435f).y);
        Rect inner = Rect.MinMaxRect(outer.xMin + Px(0.0018f), outer.yMin + Px(0.0018f), outer.xMax - Px(0.0018f), outer.yMax - Px(0.0018f));
        RectLines(c, outer, Px(0.0004f), A(Gold, 0.9f), Px(0.004f), 1);
        RectLines(c, inner, Px(0.00022f), A(Ink, 0.55f), Px(0.004f), 5);
        foreach (Vector2 corner in new[] { new Vector2(outer.xMin, outer.yMin), new Vector2(outer.xMax, outer.yMin), new Vector2(outer.xMin, outer.yMax), new Vector2(outer.xMax, outer.yMax) })
        {
            c.Star(corner, Px(0.0034f), 4, 0.2f, Gold, 0f);
            c.Star(corner, Px(0.0019f), 4, 0.25f, Gold, 45f);
        }

        // Title: a small ink star either side.
        foreach (float x in new[] { -0.093f, 0.093f })
        {
            c.Star(P(x, TitleY), Px(0.0022f), 4, 0.25f, A(Ink, 0.85f), 0f);
        }

        // "Hereby certifies": a short rule either side.
        foreach (float s in new[] { -1f, 1f })
        {
            c.Stroke(new[] { P(s * 0.034f, CertifiesY - 0.0004f), P(s * 0.056f, CertifiesY - 0.0004f) }, Px(0.0003f), A(Ink, 0.75f), 0.2f, 21);
        }

        // The signing line, star ends and rays.
        c.Stroke(new[] { P(-LineHalf, LineY), P(LineHalf, LineY) }, Px(0.00035f), A(Ink, 0.9f), 0.05f, 31);
        foreach (float s in new[] { -1f, 1f })
        {
            Vector2 end = P(s * (LineHalf + 0.004f), LineY);
            c.Star(end, Px(0.0036f), 4, 0.18f, A(Ink, 0.95f), 0f);
            for (int i = 0; i < 5; i++)
            {
                float angle = (s > 0 ? 0f : 180f) + (i - 2) * 30f;
                Vector2 d = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                float r0 = Px(0.0056f), r1 = Px(i == 2 ? 0.0105f : 0.0085f);
                c.Stroke(new[] { end + d * r0, end + d * r1 }, Px(0.00025f), A(Ink, 0.7f), 0.5f, 40 + i);
            }
        }

        // Faint lavender clouds in the lower corners.
        Cloud(c, -0.093f, -0.037f, 1f, 51);
        Cloud(c, 0.093f, -0.037f, -1f, 52);

        // Moon phases arcing down the upper right, clear of the title.
        for (int i = 0; i < 5; i++)
        {
            float t = i / 4f;
            float x = Mathf.Lerp(0.098f, 0.1155f, t);
            float y = Mathf.Lerp(0.0225f, 0.0115f, t) + Mathf.Sin(t * Mathf.PI) * 0.003f;
            Moon(c, P(x, y), Px(0.0019f), Mathf.Lerp(-0.85f, 0.85f, t));
        }

        // A dotted orbit with a star, on the left below the seal, and one on the right.
        DottedArc(c, P(-0.098f, 0.002f), Px(0.016f), Px(0.0075f), -18f, 200f, 470f, 61);
        c.Star(P(-0.087f, 0.0085f), Px(0.0028f), 4, 0.2f, A(Ink, 0.8f), 0f);
        DottedArc(c, P(0.099f, -0.001f), Px(0.014f), Px(0.0062f), 14f, -20f, 240f, 62);
        c.Star(P(0.1095f, -0.006f), Px(0.0022f), 4, 0.22f, A(Ink, 0.75f), 0f);
        foreach (Vector2 sparkle in new[] { new Vector2(-0.108f, 0.016f), new Vector2(-0.079f, -0.004f), new Vector2(0.086f, 0.010f), new Vector2(0.090f, 0.021f) })
        {
            c.Star(P(sparkle.x, sparkle.y), Px(0.0013f), 4, 0.25f, A(Ink, 0.65f), 0f);
        }

        return c;
    }

    static void RectLines(InkCanvas c, Rect r, float width, Color col, float cut, int seed)
    {
        Vector2 bl = new Vector2(r.xMin, r.yMin), br = new Vector2(r.xMax, r.yMin), tr = new Vector2(r.xMax, r.yMax), tl = new Vector2(r.xMin, r.yMax);
        void Side(Vector2 a, Vector2 b, int s)
        {
            Vector2 d = (b - a).normalized;
            c.Stroke(new[] { a + d * cut, b - d * cut }, width, col, 0.03f, s);
        }

        Side(bl, br, seed);
        Side(br, tr, seed + 1);
        Side(tr, tl, seed + 2);
        Side(tl, bl, seed + 3);
    }

    // A row of soft scallops (cloud outline) with a faint wash under it.
    static void Cloud(InkCanvas c, float cx, float cy, float dir, int seed)
    {
        var rnd = new System.Random(seed);
        float[] radii = { 0.0045f, 0.0065f, 0.0085f, 0.006f, 0.004f };
        float x = cx - dir * 0.028f;
        int k = 0;
        foreach (float r in radii)
        {
            float jitter = (float)rnd.NextDouble() * 0.001f;
            Vector2 centre = new Vector2(x + dir * r, cy + r * 0.35f + jitter);
            c.Dot(P(centre.x, centre.y), Px(r * 0.95f), A(Lavender, 0.08f));
            // Each puff's upper arc, stopping where it meets the next one.
            var arc = new List<Vector2>();
            for (int i = 0; i <= 16; i++)
            {
                float a = Mathf.Lerp(15f, 165f, i / 16f) * Mathf.Deg2Rad;
                arc.Add(P(centre.x + Mathf.Cos(a) * r, centre.y + Mathf.Sin(a) * r));
            }

            c.Stroke(arc, Px(0.00028f), A(Lavender, 0.6f), 0.35f, seed + k++);
            x += dir * r * 1.55f;
        }

        c.Stroke(new[] { P(cx - dir * 0.03f, cy - 0.0005f), P(x + dir * 0.004f, cy - 0.0005f) }, Px(0.00022f), A(Lavender, 0.45f), 0.6f, seed + 9);
    }

    // phase: -1 = new (dark), 0 = half, 1 = full (outline only). A thin ink ring
    // with the shadowed part filled.
    static void Moon(InkCanvas c, Vector2 centre, float r, float phase)
    {
        float lit = Mathf.Clamp(phase, -1f, 1f);
        int x0 = Mathf.FloorToInt(centre.x - r - 2), x1 = Mathf.CeilToInt(centre.x + r + 2);
        int y0 = Mathf.FloorToInt(centre.y - r - 2), y1 = Mathf.CeilToInt(centre.y + r + 2);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                Vector2 q = new Vector2(x + 0.5f, y + 0.5f) - centre;
                float d = q.magnitude;
                float inside = Mathf.Clamp01(r - d + 0.5f);
                if (inside <= 0f) continue;
                // Terminator: an ellipse of half-width |lit| * r across the disc.
                float half = Mathf.Sqrt(Mathf.Max(0f, r * r - q.y * q.y));
                float edge = -lit * half;
                float shadow = Mathf.Clamp01(edge - q.x + 0.5f);
                float ring = Mathf.Clamp01(1f - Mathf.Abs(d - (r - 0.9f)) / 1.1f);
                float a = Mathf.Max(ring * 0.85f, shadow * inside * 0.7f);
                c.Over(x, y, Ink, a);
            }
    }

    static void DottedArc(InkCanvas c, Vector2 centre, float rx, float ry, float tilt, float fromDeg, float toDeg, int seed)
    {
        float t = tilt * Mathf.Deg2Rad;
        int dots = Mathf.CeilToInt(Mathf.Abs(toDeg - fromDeg) / 9f);
        for (int i = 0; i <= dots; i++)
        {
            float a = Mathf.Lerp(fromDeg, toDeg, i / (float)dots) * Mathf.Deg2Rad;
            Vector2 p = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
            p = new Vector2(p.x * Mathf.Cos(t) - p.y * Mathf.Sin(t), p.x * Mathf.Sin(t) + p.y * Mathf.Cos(t));
            c.Dot(centre + p, Px(0.00028f), A(Ink, 0.6f));
        }
    }

    // The guild seal: a purple wax blob with an irregular rim, a pressed inner
    // disc, a raised ring, and a gold laurel around an eight-point star.
    static InkCanvas SealTexture()
    {
        int s = SealPx;
        var c = new InkCanvas(s, s);
        Vector2 o = new Vector2(s * 0.5f, s * 0.5f);
        float R = s * 0.46f;
        Color wax = new Color(0.40f, 0.18f, 0.47f);
        Color deep = new Color(0.22f, 0.08f, 0.28f);
        Vector2 light = new Vector2(-0.6f, 0.75f).normalized;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                Vector2 q = new Vector2(x + 0.5f, y + 0.5f) - o;
                float ang = Mathf.Atan2(q.y, q.x);
                float rim = R * (0.93f + 0.045f * Mathf.Sin(ang * 7f + 0.6f) + 0.03f * Mathf.Sin(ang * 13f + 2.1f) +
                                 0.03f * (Mathf.PerlinNoise(Mathf.Cos(ang) * 2f + 3f, Mathf.Sin(ang) * 2f + 5f) - 0.5f));
                float d = q.magnitude;
                float alpha = Mathf.Clamp01(rim - d + 0.5f);
                if (alpha <= 0f) continue;

                float r = d / R;
                Vector2 n = d > 1e-3f ? q / d : Vector2.zero;
                // Bulge of the rim, the dip of the pressed disc, and the raised ring.
                float slope = 0f;
                if (r > 0.74f) slope = -Mathf.InverseLerp(0.74f, rim / R, r);                 // rim falls away outward
                else if (r > 0.66f) slope = Mathf.InverseLerp(0.74f, 0.66f, r) * 0.6f - 0.3f;  // raised ring
                float lightAmount = 0.75f + Vector2.Dot(n, light) * slope * 0.55f;
                Color col = Color.Lerp(deep, wax, Mathf.Clamp01(lightAmount));
                if (r < 0.66f) col = Color.Lerp(col, deep, 0.18f);
                // A soft sheen on the upper left.
                float sheen = Mathf.Exp(-((q - new Vector2(-0.38f, 0.42f) * R).sqrMagnitude) / (R * R * 0.05f)) * 0.22f;
                col += new Color(sheen, sheen * 0.85f, sheen, 0f);
                c.Over(x, y, new Color(col.r, col.g, col.b, 1f), alpha);
            }

        Color gold = new Color(0.86f, 0.66f, 0.30f, 1f);
        // Eight-point star.
        c.Star(o, R * 0.30f, 4, 0.22f, gold, 0f);
        c.Star(o, R * 0.20f, 4, 0.30f, gold, 45f);
        c.Dot(o, R * 0.045f, new Color(1f, 0.9f, 0.65f, 1f));
        // Laurel: two branches curving up from the bottom.
        foreach (float side in new[] { -1f, 1f })
        {
            var stem = new List<Vector2>();
            for (int i = 0; i <= 24; i++)
            {
                float a = Mathf.Lerp(-80f, 55f, i / 24f) * Mathf.Deg2Rad;
                stem.Add(o + new Vector2(side * Mathf.Cos(a), Mathf.Sin(a)) * R * 0.5f);
            }

            c.Stroke(stem, R * 0.03f, gold, 0.3f, side > 0 ? 71 : 72);
            for (int i = 3; i < stem.Count - 1; i += 3)
            {
                Vector2 p = stem[i];
                Vector2 d = (stem[i + 1] - stem[i - 1]).normalized;
                Vector2 nrm = new Vector2(-d.y, d.x);
                foreach (int k in new[] { -1, 1 })
                {
                    Vector2 tip = p + (nrm * k * 0.8f + d * 0.9f).normalized * R * 0.13f;
                    Vector2 mid = (p + tip) * 0.5f;
                    Vector2 w = new Vector2(-(tip - p).y, (tip - p).x).normalized * R * 0.035f;
                    c.FillPolygon(new[] { p, mid + w, tip, mid - w }, gold);
                }
            }
        }

        return c;
    }

    // ================================================================ Helpers

    static Material Surface(string name, float shade, bool zwrite, bool twoSided)
    {
        Shader shader = Shader.Find("CelestialWorkshop/NoticeboardSurface");
        string path = $"{MatDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }

        mat.shader = shader;
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/{name}.png"));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetColor("_EmissionColor", Color.black);
        mat.SetFloat("_Shade", shade);
        mat.SetFloat("_ZWrite", zwrite ? 1f : 0f);
        mat.SetFloat("_Cull", twoSided ? 0f : 2f);
        mat.renderQueue = 3000;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static MeshRenderer MeshNode(Transform parent, string name, Mesh mesh, Material mat, int sort)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = t.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.sortingOrder = sort;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    static MeshRenderer Cube(Transform parent, string name, Material mat)
    {
        GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cube = probe.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(probe);
        return MeshNode(parent, name, cube, mat, SortBoard);
    }

    static MeshRenderer Quad(Transform parent, string name, Material mat, Vector2 center, Vector2 size, int sort, float z)
    {
        var mr = MeshNode(parent, name, AssetDatabase.LoadAssetAtPath<Mesh>($"{NB}/Meshes/NB_Quad.asset"), mat, sort);
        mr.transform.localPosition = new Vector3(center.x, center.y, z);
        mr.transform.localScale = new Vector3(size.x, size.y, 1f);
        return mr;
    }

    // Same sizing rule as the noticeboard, Credits and Field Note: cap = cap height in metres.
    static TextMeshPro Text(Transform parent, string name, string text, string fontPath, float cap, Color color,
        Vector2 pos, float width, List<(TextMeshPro, Vector3)> placed, float maxWidth = 0f, float spacing = 0f)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        var tmp = t.gameObject.AddComponent<TextMeshPro>();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        tmp.font = font;
        tmp.fontSharedMaterial = font.material;
        tmp.text = text;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.enableAutoSizing = false;
        tmp.characterSpacing = spacing;
        tmp.isOrthographic = false;
        var face = font.faceInfo;
        tmp.fontSize = cap / (0.1f * face.scale * face.capLine / face.pointSize);
        tmp.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        tmp.rectTransform.sizeDelta = new Vector2(width, 0.03f);
        if (maxWidth > 0f)
        {
            float w = tmp.GetPreferredValues(text).x;
            if (w > maxWidth) tmp.fontSize *= maxWidth / w;
        }

        tmp.rectTransform.anchoredPosition3D = new Vector3(pos.x, pos.y, -0.0012f);
        placed.Add((tmp, tmp.rectTransform.anchoredPosition3D));
        var mr = tmp.GetComponent<MeshRenderer>();
        mr.sortingOrder = SortText;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return tmp;
    }
}
