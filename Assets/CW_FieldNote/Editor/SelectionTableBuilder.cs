using System.Collections.Generic;
using System.IO;
using CW.FieldNote.EditorTools;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools > Celestial Workshop > Build Selection Table.
///
/// Turns the constellation-box selection into a small worktable:
///   1. Art: generates the Field Note / fitting textures (FieldNoteArt) into
///      Assets/CW_FieldNote/Art and makes FN_* materials on the noticeboard's
///      own surface shader (Credits / noticeboard look; gold = their star gold).
///   2. Box prefab: a brass nameplate behind the name label, brass corner
///      fittings and a padlock (locked boxes only). No colliders are added.
///   3. Scene, under the BoxSelection ("Constellation Boxes"):
///      - "Selection Table": wooden top with a star-chart cloth, and a rear step;
///        the old Box Stand's renderer is switched off (object kept).
///      - "Field Note (Folded)": the folded sheet on the table, titled.
///      - "Btn Field Note" in the box row (a copy of Back to title).
///      - "Field Note Reader": the open tri-fold sheet with a Back button
///        (FieldNoteReader, the Credits board pattern).
///      - SelectionTableLayout on the BoxSelection holds every position;
///        edit it there and use its "Apply Layout".
///
/// Safe to run again: art is regenerated, generated objects are rebuilt by
/// name, the layout component keeps its values once created.
/// Frames: sheet objects are built in quad space (+X right, +Y up, text reads
/// facing -Z), metres.
/// </summary>
public static class SelectionTableBuilder
{
    const string Root = "Assets/CW_FieldNote";
    const string ArtDir = Root + "/Art";
    const string MatDir = Root + "/Materials";
    const string BoxPrefab = "Assets/CW_Boxes/ConstellationBox.prefab";
    const string NB = "Assets/CW_Onboarding";
    const string FontTitle = "Assets/CW_Art/Fonts/Meddon SDF.asset";
    const string FontHand = "Assets/CW_Art/Fonts/Caveat SDF.asset";
    const string FontBody = "Assets/CW_Art/Fonts/ArchitectsDaughter SDF.asset";
    const string IconSheet = "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Textures/OCUIIconSet/OCUI_24_Filled_2x.png";

    // ---- Copy ----
    const string NoteTitle = "Skycrafter's Field Note";
    const string NoteTitleFolded = "Skycrafter's\nField Note";
    const string NoteSubtitle = "How Constellations Work";
    static readonly string[] Captions =
    {
        "Stars in a constellation are scattered through space.",
        "From Earth, they appear to form a familiar pattern.",
        "Change your viewpoint, and the pattern changes.",
    };
    const string Invitation = "Find the view from Earth to bring the constellation together.";

    // ---- Open sheet (metres) ----
    const float SheetW = 0.52f, SheetH = 0.29f;
    const float TitleCap = 0.0105f, CaptionCap = 0.0062f, InviteCap = 0.0058f;
    const float TitleY = 0.112f, CaptionY = 0.076f, ArtY = -0.024f, InviteY = -0.117f;
    const float ArtW = 0.168f;
    const float BackButtonGap = 0.036f;

    // ---- Folded note ----
    const float CoverW = 0.155f, CoverH = 0.11f;
    const float CoverTitleCap = 0.0088f, CoverSubCap = 0.0037f;

    // ---- Box fittings (box units; the box is scaled by the layout) ----
    const float PlateW = 0.24f, PlateH = 0.06f, PlateY = -0.004f;
    const float CornerSize = 0.032f;
    const float LabelSize = 0.34f;
    const float PadlockW = 0.042f, PadlockH = 0.0525f;

    static readonly Color InkText = new Color(0.22f, 0.12f, 0.05f, 1f);
    static readonly Color InkBody = new Color(0.27f, 0.16f, 0.08f, 1f);
    static readonly Color PlateInk = new Color(0.20f, 0.11f, 0.04f, 1f);
    static readonly Color PlateInkLocked = new Color(0.86f, 0.79f, 0.62f, 1f);
    const int SortShadow = 1, SortPaper = 2, SortInk = 3, SortText = 4;

    [MenuItem("Tools/Celestial Workshop/Build Selection Table")]
    static void BuildMenu() => Debug.Log("SelectionTableBuilder: " + Build(false, true));

    [MenuItem("Tools/Celestial Workshop/Field Note/Rebuild Without Regenerating Art")]
    static void BuildNoArtMenu() => Debug.Log("SelectionTableBuilder: " + Build(false, false));

    public static string Build(bool save, bool regenerateArt)
    {
        var selection = Object.FindFirstObjectByType<BoxSelection>(FindObjectsInactive.Include);
        if (selection == null) return "no BoxSelection in the open scene";
        var sso = new SerializedObject(selection);
        var rowRoot = (GameObject)sso.FindProperty("backToTitleButtonRoot").objectReferenceValue;
        if (rowRoot == null) return "BoxSelection has no Back To Title Button Root";

        placedTexts.Clear();

        // 1. Art and materials.
        string art = regenerateArt ? GenerateArt() : "art kept";
        var m = Materials();
        if (m == null) return "missing noticeboard materials (run Build Workshop Noticeboard)";

        // 2. Box prefab fittings.
        string boxes = FitBoxPrefab(m);

        // 3. Scene. The selection and its row are inactive in edit mode;
        // layout (button row widths) only works on active objects.
        Transform sel = selection.transform;
        bool selWasActive = sel.gameObject.activeSelf, rowWasActive = rowRoot.activeSelf;
        sel.gameObject.SetActive(true);
        rowRoot.SetActive(true);
        try
        {
            return BuildScene(selection, rowRoot, save, art, boxes, m);
        }
        finally
        {
            rowRoot.SetActive(rowWasActive);
            sel.gameObject.SetActive(selWasActive);
        }
    }

    static string BuildScene(BoxSelection selection, GameObject rowRoot, bool save, string art, string boxes, Mats m)
    {
        Transform sel = selection.transform;
        var report = new List<string> { art, boxes };
        report.Add(BuildTable(sel, m));
        Transform folded = BuildFoldedNote(sel, m);
        MomentaryToggleButton openButton = BuildOpenButton(rowRoot);
        var reader = BuildReader(sel, rowRoot, openButton, m, out Transform readingBoard);
        ConfigureBoxInstances(selection);
        var layout = EnsureLayout(selection, folded, readingBoard);
        layout.Apply();
        report.Add(ReassertTexts());

        EditorSceneManager.MarkSceneDirty(sel.gameObject.scene);
        if (save) EditorSceneManager.SaveScene(sel.gameObject.scene);
        report.Add($"reader '{reader.name}', row width {((RectTransform)rowRoot.transform.Find("Menu Canvas")).sizeDelta.x}");
        return string.Join("; ", report);
    }

    // ================================================================ Art

    static string GenerateArt()
    {
        Directory.CreateDirectory(ArtDir);
        string report = FieldNoteArt.GenerateAll(ArtDir).Replace('\n', ' ');
        AssetDatabase.Refresh();
        foreach (string file in Directory.GetFiles(ArtDir, "*.png"))
        {
            string path = file.Replace('\\', '/');
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) continue;
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

        return "art: " + report.Trim();
    }

    sealed class Mats : Dictionary<string, Material>
    {
        public Material TitleInk;
    }

    static Mats Materials()
    {
        Directory.CreateDirectory(MatDir);
        var m = new Mats();
        foreach (string n in new[] { "Wood", "Parchment", "ParchmentShadow", "Ribbon", "Flourish" })
        {
            var nb = AssetDatabase.LoadAssetAtPath<Material>($"{NB}/Materials/NB_{n}.mat");
            if (nb == null) return null;
            m["NB_" + n] = nb;
        }

        m.TitleInk = AssetDatabase.LoadAssetAtPath<Material>($"{NB}/Materials/Title_Ink.mat");

        // Paper: lit like the noticeboard's parchment. Ink and gold: barely lit (printed on it).
        m["SheetParchment"] = Surface("SheetParchment", 0.45f, true, Color.white);
        m["CoverParchment"] = Surface("CoverParchment", 0.45f, true, Color.white);
        m["CoverUnder"] = Surface("CoverUnder", 0.45f, true, new Color(0.86f, 0.80f, 0.70f, 1f), "FN_CoverParchment");
        foreach (string ink in new[] { "Panel1", "Panel2", "Panel3", "SheetFrame", "CoverFrame", "Fold", "Sprig", "GoldStar" })
        {
            m[ink] = Surface(ink, 0.1f, false, Color.white, twoSided: true);
        }

        // Brass: lit like the nail heads, with a touch of warm emission (gold leaf).
        m["Plate"] = Surface("Plate", 0.8f, true, Color.white, emission: new Color(0.10f, 0.06f, 0.01f));
        m["PlateTarnished"] = Surface("PlateTarnished", 0.8f, true, Color.white);
        m["Corner"] = Surface("Corner", 0.8f, true, Color.white, emission: new Color(0.06f, 0.035f, 0.005f));
        m["Padlock"] = Surface("Padlock", 0.8f, true, Color.white, emission: new Color(0.06f, 0.035f, 0.005f));
        m["Cloth"] = Surface("Cloth", 0.7f, true, Color.white);
        AssetDatabase.SaveAssets();
        return m;
    }

    static Material Surface(string name, float shade, bool zwrite, Color tint, string texture = null, bool twoSided = false, Color emission = default)
    {
        Shader s = Shader.Find("CelestialWorkshop/NoticeboardSurface");
        string path = $"{MatDir}/FN_{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(s) { name = "FN_" + name };
            AssetDatabase.CreateAsset(mat, path);
        }

        mat.shader = s;
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/{texture ?? "FN_" + name}.png"));
        mat.SetColor("_BaseColor", tint);
        mat.SetColor("_EmissionColor", emission);
        mat.SetFloat("_Shade", shade);
        mat.SetFloat("_ZWrite", zwrite ? 1f : 0f);
        mat.SetFloat("_Cull", twoSided ? 0f : 2f);
        mat.renderQueue = 3000;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ================================================================ Box prefab

    static string FitBoxPrefab(Mats m)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BoxPrefab);
        try
        {
            var box = root.GetComponent<ConstellationBox>();
            Transform baseT = root.transform.Find("BoxBase");
            Transform label = root.transform.Find("Name Label");
            Transform status = root.transform.Find("Status Label");
            if (box == null || baseT == null || label == null) return "box prefab: missing ConstellationBox / BoxBase / Name Label";

            // Front face and outline of the base, in the box's own frame.
            Bounds b = LocalBounds(root.transform, baseT.GetComponent<MeshFilter>());
            float face = b.min.z;

            Transform old = root.transform.Find("Fittings");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            Transform fit = new GameObject("Fittings").transform;
            fit.SetParent(root.transform, false);

            var plate = Quad(fit, "Nameplate", m["Plate"], new Vector2(0f, PlateY), new Vector2(PlateW, PlateH), SortInk, face - 0.003f);
            ((RectTransform)label).anchoredPosition3D = new Vector3(0f, PlateY, face - 0.006f);
            var tmp = label.GetComponent<TMP_Text>();
            tmp.fontStyle = FontStyles.Bold;
            tmp.rectTransform.sizeDelta = new Vector2(PlateW - 0.05f, PlateH);
            // The label's own size was 0.35 (box units); shrink to fit the plate if needed.
            tmp.enableAutoSizing = true;
            tmp.fontSizeMax = LabelSize;
            tmp.fontSizeMin = LabelSize * 0.6f;
            tmp.fontSize = LabelSize;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.GetComponent<MeshRenderer>().sortingOrder = SortText;
            if (status != null)
            {
                ((RectTransform)status).anchoredPosition3D = new Vector3(0f, PlateY - PlateH * 0.5f - 0.024f, face - 0.006f);
            }

            // Corner fittings on the base's front corners (the texture is a
            // bottom-left L; mirrored by negative scale for the others).
            float inset = 0.002f;
            foreach (var (x, y, name) in new[] { (-1, -1, "Corner BL"), (1, -1, "Corner BR"), (-1, 1, "Corner TL"), (1, 1, "Corner TR") })
            {
                Vector2 c = new Vector2(x < 0 ? b.min.x + inset : b.max.x - inset, y < 0 ? b.min.y + inset : b.max.y - inset);
                Vector2 centre = c - new Vector2(x, y) * (CornerSize * 0.5f);
                var q = Quad(fit, name, m["Corner"], centre, new Vector2(CornerSize, CornerSize), SortInk, face - 0.0025f);
                q.transform.localScale = new Vector3(CornerSize * -x, CornerSize * -y, 1f);
            }

            // Padlock: hangs from the lid seam, centred (locked boxes only).
            var lockGo = new GameObject("Locked Only");
            lockGo.transform.SetParent(fit, false);
            Quad(lockGo.transform, "Padlock", m["Padlock"], new Vector2(0f, b.max.y - 0.006f), new Vector2(PadlockW, PadlockH), SortText, face - 0.008f);

            var so = new SerializedObject(box);
            so.FindProperty("nameplate").objectReferenceValue = plate;
            so.FindProperty("nameplateMaterial").objectReferenceValue = m["Plate"];
            so.FindProperty("nameplateUnavailableMaterial").objectReferenceValue = m["PlateTarnished"];
            so.FindProperty("lockedOnly").objectReferenceValue = lockGo;
            so.ApplyModifiedPropertiesWithoutUndo();
            // Not ApplyLook(): that would also restyle the prefab's own defaults
            // (base material, lid tint). Each scene box applies its look itself.
            plate.sharedMaterial = m["Plate"];
            lockGo.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(root, BoxPrefab);
            return $"box prefab fitted (front face z {face:F3}, base {b.size.x:F3} x {b.size.y:F3})";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Bounds LocalBounds(Transform root, MeshFilter mf)
    {
        Bounds mb = mf.sharedMesh.bounds;
        Matrix4x4 toRoot = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
        var b = new Bounds(toRoot.MultiplyPoint3x4(mb.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            b.Encapsulate(toRoot.MultiplyPoint3x4(corner));
        }

        return b;
    }

    // Scene instances: ink on the bright plate, pale engraving on the tarnished one.
    static void ConfigureBoxInstances(BoxSelection selection)
    {
        var so = new SerializedObject(selection);
        var boxes = so.FindProperty("boxes");
        for (int i = 0; i < boxes.arraySize; i++)
        {
            var box = boxes.GetArrayElementAtIndex(i).objectReferenceValue as ConstellationBox;
            if (box == null) continue;
            var bso = new SerializedObject(box);
            bso.FindProperty("nameColor").colorValue = PlateInk;
            bso.FindProperty("unavailableNameColor").colorValue = PlateInkLocked;
            bso.ApplyModifiedProperties();
            box.ApplyLook();
        }
    }

    // ================================================================ Table

    static string BuildTable(Transform sel, Mats m)
    {
        Transform stand = sel.Find("Box Stand");
        if (stand != null)
        {
            var r = stand.GetComponent<Renderer>();
            if (r != null && r.enabled)
            {
                Undo.RecordObject(r, "Selection table");
                r.enabled = false;
            }
        }

        Rebuild(sel, "Selection Table", out Transform table);
        // Children live in a unit square scaled by the layout (x = width, z = depth).
        var top = Cube(table, "Top", m["NB_Wood"]);
        top.transform.localPosition = new Vector3(0f, -0.011f, 0f);
        top.transform.localScale = new Vector3(1f, 0.022f, 1f);
        var cloth = MeshNode(table, "Cloth", NBQuad(), m["Cloth"], 0);
        cloth.transform.localPosition = new Vector3(0f, 0.0006f, 0f);
        cloth.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        cloth.transform.localScale = new Vector3(0.93f, 0.88f, 1f);

        Rebuild(sel, "Rear Step", out Transform step);
        Cube(step, "Step", m["NB_Wood"]).transform.localScale = Vector3.one;
        return stand != null ? "table built (Box Stand renderer off)" : "table built";
    }

    // ================================================================ Folded note

    static Transform BuildFoldedNote(Transform sel, Mats m)
    {
        Rebuild(sel, "Field Note (Folded)", out Transform note);
        // Layers under the cover: the other two thirds, peeking out at the bottom edge.
        Quad(note, "Shadow", m["NB_ParchmentShadow"], new Vector2(0.004f, -0.006f), new Vector2(CoverW * 1.12f, CoverH * 1.14f), SortShadow, 0.0045f);
        Quad(note, "Third 3", m["CoverUnder"], new Vector2(0.0012f, -0.0042f), new Vector2(CoverW, CoverH), SortPaper, 0.0030f);
        Quad(note, "Third 2", m["CoverUnder"], new Vector2(-0.0008f, -0.0022f), new Vector2(CoverW, CoverH), SortPaper, 0.0015f);
        Quad(note, "Cover", m["CoverParchment"], Vector2.zero, new Vector2(CoverW, CoverH), SortPaper, 0f);
        Quad(note, "Frame", m["CoverFrame"], Vector2.zero, new Vector2(CoverW, CoverH), SortInk, -0.0004f);

        var title = Text(note, "Title", NoteTitleFolded, FontTitle, CoverTitleCap, InkText, TextAlignmentOptions.Center, new Vector2(0f, 0.019f), CoverW * 0.9f);
        title.lineSpacing = -18f;
        if (m.TitleInk != null) title.fontSharedMaterial = m.TitleInk;
        Quad(note, "Sprig Left", m["Sprig"], new Vector2(-0.038f, -0.021f), new Vector2(0.040f, 0.015f), SortInk, -0.0004f);
        Quad(note, "Sprig Right", m["Sprig"], new Vector2(0.038f, -0.021f), new Vector2(0.040f, 0.015f), SortInk, -0.0004f, flipX: true);
        Quad(note, "Star", m["GoldStar"], new Vector2(0f, -0.021f), new Vector2(0.016f, 0.016f), SortInk, -0.0004f);
        Text(note, "Subtitle", NoteSubtitle, FontBody, CoverSubCap, InkBody, TextAlignmentOptions.Center, new Vector2(0f, -0.039f), CoverW * 0.9f);
        return note;
    }

    // ================================================================ Field Note button

    static MomentaryToggleButton BuildOpenButton(GameObject rowRoot)
    {
        Transform row = rowRoot.transform.Find("Menu Canvas/Buttons");
        Transform template = row.Find("Btn Back to title");
        Transform t = row.Find("Btn Field Note");
        if (t == null)
        {
            var go = Object.Instantiate(template.gameObject, row);
            go.name = "Btn Field Note";
            Undo.RegisterCreatedObjectUndo(go, "Field Note button");
            t = go.transform;
        }

        t.SetAsLastSibling();
        var button = SetupButton(t, "Field Note", "icon_file_24_Filled");
        FitCanvas(row);
        SetPokeButtons(rowRoot);
        AddFeedbackButtons(button);
        return button;
    }

    // ================================================================ Reader

    static FieldNoteReader BuildReader(Transform sel, GameObject rowRoot, MomentaryToggleButton openButton, Mats m, out Transform board)
    {
        Rebuild(sel, "Field Note Reader", out Transform readerT);
        var reader = readerT.gameObject.AddComponent<FieldNoteReader>();
        board = new GameObject("Board").transform;
        board.SetParent(readerT, false);

        Quad(board, "Shadow", m["NB_ParchmentShadow"], new Vector2(0.007f, -0.009f), new Vector2(SheetW * 1.08f, SheetH * 1.12f), SortShadow, 0.003f);
        Quad(board, "Sheet", m["SheetParchment"], Vector2.zero, new Vector2(SheetW, SheetH), SortPaper, 0f);
        Quad(board, "Frame", m["SheetFrame"], Vector2.zero, new Vector2(SheetW, SheetH), SortInk, -0.0004f);
        float third = SheetW / 3f;
        Quad(board, "Fold Left", m["Fold"], new Vector2(-third * 0.5f, 0f), new Vector2(0.012f, SheetH * 0.985f), SortInk, -0.0003f);
        Quad(board, "Fold Right", m["Fold"], new Vector2(third * 0.5f, 0f), new Vector2(0.012f, SheetH * 0.985f), SortInk, -0.0003f, flipX: true);

        var title = Text(board, "Title", NoteTitle, FontTitle, TitleCap, InkText, TextAlignmentOptions.Center, new Vector2(0f, TitleY), third * 0.98f, maxWidth: third * 0.96f);
        if (m.TitleInk != null) title.fontSharedMaterial = m.TitleInk;
        Quad(board, "Sprig Left", m["Sprig"], new Vector2(-third * 0.5f - 0.040f, TitleY - 0.002f), new Vector2(0.056f, 0.021f), SortInk, -0.0004f);
        Quad(board, "Sprig Right", m["Sprig"], new Vector2(third * 0.5f + 0.040f, TitleY - 0.002f), new Vector2(0.056f, 0.021f), SortInk, -0.0004f, flipX: true);

        float artH = ArtW * FieldNoteArt.PanelH / FieldNoteArt.PanelW;
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * third;
            var cap = Text(board, $"Caption {i + 1}", Captions[i], FontBody, CaptionCap, InkText, TextAlignmentOptions.Center, new Vector2(x, CaptionY), third * 0.84f);
            cap.textWrappingMode = TextWrappingModes.Normal;
            cap.rectTransform.sizeDelta = new Vector2(third * 0.84f, 0.036f);
            cap.lineSpacing = -10f;
            cap.fontStyle = FontStyles.Bold;
            Quad(board, $"Illustration {i + 1}", m[$"Panel{i + 1}"], new Vector2(x, ArtY), new Vector2(ArtW, artH), SortInk, -0.0005f);
        }

        Quad(board, "Invitation Ribbon", m["NB_Ribbon"], new Vector2(0f, InviteY), new Vector2(0.44f, 0.055f), SortInk, -0.0006f);
        var invite = Text(board, "Invitation", Invitation, FontBody, InviteCap, InkText, TextAlignmentOptions.Center, new Vector2(0f, InviteY + 0.001f), 0.33f, maxWidth: 0.32f);
        invite.fontStyle = FontStyles.Bold;

        // Back button: a copy of the box row with one button (as Credits does with the title row).
        var backRoot = Object.Instantiate(rowRoot, board);
        backRoot.name = "Back Button";
        backRoot.SetActive(true);
        backRoot.transform.localPosition = new Vector3(0f, -SheetH * 0.5f - BackButtonGap, -0.012f);
        backRoot.transform.localRotation = Quaternion.identity;
        Transform backRow = backRoot.transform.Find("Menu Canvas/Buttons");
        for (int i = backRow.childCount - 1; i >= 0; i--)
        {
            Transform c = backRow.GetChild(i);
            if (c.name != "Btn Back to title") Object.DestroyImmediate(c.gameObject);
        }

        Transform backT = backRow.Find("Btn Back to title");
        backT.name = "Btn Back";
        var backButton = SetupButton(backT, "Back", "icon_arrow-left_24_Filled");
        FitCanvas(backRow);
        SetPokeButtons(backRoot);
        AddFeedbackButtons(backButton);

        var so = new SerializedObject(reader);
        so.FindProperty("selection").objectReferenceValue = sel.GetComponent<BoxSelection>();
        so.FindProperty("openButton").objectReferenceValue = openButton;
        so.FindProperty("backButton").objectReferenceValue = backButton;
        so.FindProperty("board").objectReferenceValue = board.gameObject;
        so.FindProperty("boxRow").objectReferenceValue = rowRoot;
        so.ApplyModifiedPropertiesWithoutUndo();
        board.gameObject.SetActive(false);
        return reader;
    }

    // ================================================================ Layout

    static SelectionTableLayout EnsureLayout(BoxSelection selection, Transform folded, Transform readingBoard)
    {
        Transform sel = selection.transform;
        var layout = selection.GetComponent<SelectionTableLayout>();
        bool fresh = layout == null;
        if (fresh) layout = Undo.AddComponent<SelectionTableLayout>(selection.gameObject);

        Undo.RecordObject(layout, "Selection table layout");
        SelectionTableLayout.Slot Slot(string name, Vector3 pos, float yaw, SelectionTableLayout.Slot current)
        {
            var s = fresh ? new SelectionTableLayout.Slot { position = pos, yaw = yaw } : current;
            s.target = sel.Find(name);
            return s;
        }

        // Box origins sit 0.09 x scale above their base; rear boxes stand on the step.
        float s = layout.boxScale;
        float lift = 0.09f * s;
        float stepTop = layout.riserSize.y;
        layout.starterPack = Slot("Box - Starter Pack", new Vector3(-0.165f, lift, -0.425f), -14f, layout.starterPack);
        layout.zodiac = Slot("Box - Zodiac", new Vector3(0.165f, lift, -0.425f), 14f, layout.zodiac);
        layout.northernSky = Slot("Box - Northern Sky", new Vector3(-0.10f, stepTop + lift, -0.29f), -7f, layout.northernSky);
        layout.southernSky = Slot("Box - Southern Sky", new Vector3(0.10f, stepTop + lift, -0.29f), 7f, layout.southernSky);
        layout.starterContents = sel.Find("Starter Pack Contents");
        layout.foldedNote = folded;
        layout.noteHeight = CoverH;
        layout.readingView = readingBoard;
        layout.table = sel.Find("Selection Table");
        layout.riser = sel.Find("Rear Step");
        EditorUtility.SetDirty(layout);
        return layout;
    }

    // ================================================================ Buttons (as the Credits builder)

    static MomentaryToggleButton SetupButton(Transform t, string label, string icon)
    {
        foreach (TMP_Text txt in t.GetComponentsInChildren<TMP_Text>(true))
        {
            Undo.RecordObject(txt, "Field Note button");
            txt.text = label;
        }

        var sprites = new Dictionary<string, Sprite>();
        foreach (Object a in AssetDatabase.LoadAllAssetsAtPath(IconSheet))
            if (a is Sprite sp) sprites[sp.name] = sp;

        Transform iconT = t.Find("Content/Background/Elements/Icon");
        if (iconT != null && sprites.TryGetValue(icon, out Sprite sprite))
        {
            var img = iconT.GetComponent<Image>();
            Undo.RecordObject(img, "Field Note button");
            img.sprite = sprite;
        }
        else
        {
            Debug.LogWarning($"SelectionTableBuilder: icon {icon} not found; the copied icon stays.");
        }

        var button = t.GetComponent<MomentaryToggleButton>();
        var bso = new SerializedObject(button);
        bso.FindProperty("onPressed.m_PersistentCalls.m_Calls").ClearArray();
        bso.ApplyModifiedProperties();
        return button;
    }

    static void FitCanvas(Transform row)
    {
        var canvasRect = (RectTransform)row.parent;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.MarkLayoutForRebuild((RectTransform)row);
        LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);
        float width = LayoutUtility.GetPreferredWidth((RectTransform)row);
        Undo.RecordObject(canvasRect, "Field Note button");
        canvasRect.sizeDelta = new Vector2(Mathf.Ceil(width), canvasRect.sizeDelta.y);
        LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);
        foreach (var plank in canvasRect.GetComponentsInChildren<WorkshopPlank>(true)) plank.Refit();
    }

    static void SetPokeButtons(GameObject root)
    {
        var poke = root.GetComponent<PokeReleaseAnywhere>();
        if (poke == null) return;
        var pso = new SerializedObject(poke);
        var list = pso.FindProperty("buttons");
        var all = root.GetComponentsInChildren<MomentaryToggleButton>(true);
        list.arraySize = all.Length;
        for (int i = 0; i < all.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = all[i];
        pso.ApplyModifiedProperties();
    }

    static void AddFeedbackButtons(params MomentaryToggleButton[] add)
    {
        var hooks = Object.FindFirstObjectByType<AudioFeedbackHooks>(FindObjectsInactive.Include);
        if (hooks == null) return;
        var so = new SerializedObject(hooks);
        var list = so.FindProperty("buttons");
        if (list == null) return;
        var have = new List<Object>();
        for (int i = 0; i < list.arraySize; i++)
        {
            Object o = list.GetArrayElementAtIndex(i).objectReferenceValue;
            if (o != null) have.Add(o);
        }

        foreach (var b in add)
            if (b != null && !have.Contains(b)) have.Add(b);

        list.arraySize = have.Count;
        for (int i = 0; i < have.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = have[i];
        so.ApplyModifiedProperties();
    }

    // ================================================================ Parts

    static void Rebuild(Transform parent, string name, out Transform t)
    {
        Transform old = parent.Find(name);
        Vector3 pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;
        Vector3 scale = Vector3.one;
        if (old != null)
        {
            pos = old.localPosition;
            rot = old.localRotation;
            scale = old.localScale;
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Selection table");
        t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localRotation = rot;
        t.localScale = scale;
    }

    static Mesh NBQuad() => AssetDatabase.LoadAssetAtPath<Mesh>($"{NB}/Meshes/NB_Quad.asset");

    static MeshRenderer Cube(Transform parent, string name, Material mat)
    {
        GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cube = probe.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(probe);
        var mr = MeshNode(parent, name, cube, mat, 0);
        return mr;
    }

    static MeshRenderer MeshNode(Transform parent, string name, Mesh mesh, Material mat, int sort)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        var mf = t.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = t.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.sortingOrder = sort;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    static MeshRenderer Quad(Transform parent, string name, Material mat, Vector2 center, Vector2 size, int sort, float z, bool flipX = false)
    {
        var mr = MeshNode(parent, name, NBQuad(), mat, sort);
        mr.transform.localPosition = new Vector3(center.x, center.y, z);
        mr.transform.localScale = new Vector3(size.x * (flipX ? -1f : 1f), size.y, 1f);
        return mr;
    }

    // Every text the build placed, with where it belongs. Once (after new glyphs
    // entered the dynamic font atlases) some came out at the origin, so the
    // build re-asserts them at the end and reports any that had moved.
    static readonly List<(TextMeshPro text, Vector3 pos)> placedTexts = new List<(TextMeshPro, Vector3)>();

    static string ReassertTexts()
    {
        int moved = 0;
        foreach (var (text, pos) in placedTexts)
        {
            if (text == null) continue;
            if ((text.rectTransform.anchoredPosition3D - pos).sqrMagnitude > 1e-8f) moved++;
            text.rectTransform.anchoredPosition3D = pos;
            EditorUtility.SetDirty(text.rectTransform);
        }

        placedTexts.Clear();
        return moved == 0 ? "texts in place" : $"{moved} text(s) had moved and were put back";
    }

    // Same sizing rule as the noticeboard and Credits: cap = cap height in metres.
    static TextMeshPro Text(Transform parent, string name, string text, string fontPath, float cap, Color color,
        TextAlignmentOptions align, Vector2 pos, float width, float maxWidth = 0f)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        var tmp = t.gameObject.AddComponent<TextMeshPro>();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        tmp.font = font;
        tmp.fontSharedMaterial = font.material;
        tmp.text = text;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.enableAutoSizing = false;
        tmp.richText = true;
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

        // anchoredPosition3D, not transform.localPosition: on a freshly added
        // TMP RectTransform the latter showed in the Editor but saved as 0.
        tmp.rectTransform.anchoredPosition3D = new Vector3(pos.x, pos.y, -0.0012f);
        placedTexts.Add((tmp, tmp.rectTransform.anchoredPosition3D));
        var mr = tmp.GetComponent<MeshRenderer>();
        mr.sortingOrder = SortText;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return tmp;
    }
}
