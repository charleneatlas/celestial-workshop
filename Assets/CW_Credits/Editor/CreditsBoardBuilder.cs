using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools > Celestial Workshop > Build Credits Board.
///   1. Adds a "Credits" button to the end of the title's button row (a copy of
///      Recenter, so it keeps the workshop button skin).
///   2. Rebuilds the Credits board under the title shelf from the noticeboard's
///      own wood, parchment, ink and fonts, with a Back button (a copy of the
///      title's button canvas holding one button), and saves it as
///      Assets/CW_Credits/CreditsBoard.prefab (the scene holds an instance).
///   3. Makes sure the scene has one "Background Music" object with both clips.
/// Safe to run again: the button is reused by name and the board is rebuilt.
/// The words come from Credits.txt at runtime; rebuilding is only needed for
/// layout changes.
/// </summary>
public static class CreditsBoardBuilder
{
    const string Root = "Assets/CW_Credits";
    const string PrefabPath = Root + "/CreditsBoard.prefab";
    const string TextPath = Root + "/Credits.txt";
    const string NB = "Assets/CW_Onboarding";
    const string FontTitle = "Assets/CW_Art/Fonts/Meddon SDF.asset";
    const string FontHand = "Assets/CW_Art/Fonts/Caveat SDF.asset";
    const string FontBody = "Assets/CW_Art/Fonts/ArchitectsDaughter SDF.asset";
    const string IconSheet = "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Textures/OCUIIconSet/OCUI_24_Filled_2x.png";
    const string WorkshopClip = "Assets/Audio/Music/The Britons.mp3";
    const string PuzzleClip = "Assets/Audio/Music/Morning.mp3";

    // Board layout, metres. The board's centre sits this far above the title
    // letters' centre; the Back button takes the Start row's place below it.
    const float BoardY = 0.075f;
    const float PaperW = 0.40f, PaperH = 0.26f;
    const float BackW = 0.44f, BackH = 0.30f;
    const float BackButtonY = -0.105f;
    const float TextZ = -0.0016f, InkZ = -0.0012f;

    // Body text: cap height range (autosized to fit the sheet).
    const float BodyCapMax = 0.0074f, BodyCapMin = 0.0052f;

    static readonly Color InkText = new Color(0.22f, 0.12f, 0.05f, 1f);
    static readonly Color InkBody = new Color(0.27f, 0.16f, 0.08f, 1f);
    const int SortWood = 0, SortShadow = 1, SortPaper = 2, SortInk = 3, SortText = 4, SortNails = 6;

    [MenuItem("Tools/Celestial Workshop/Build Credits Board")]
    static void BuildMenu() => Debug.Log("CreditsBoardBuilder: " + Build(false));

    public static string Build(bool save)
    {
        var shelf = Object.FindFirstObjectByType<TitleShelf>(FindObjectsInactive.Include);
        if (shelf == null) return "no TitleShelf in the open scene";
        var startRoot = (GameObject)new SerializedObject(shelf).FindProperty("startButtonRoot").objectReferenceValue;
        if (startRoot == null) return "TitleShelf has no Start Button Root";
        Transform row = startRoot.transform.Find("Menu Canvas/Buttons");
        Transform template = row != null ? row.Find("Btn Recenter") : null;
        if (template == null) return "Btn Recenter not found under Menu Canvas/Buttons (run Hook Up Placement Buttons first)";

        var sprites = new Dictionary<string, Sprite>();
        foreach (Object a in AssetDatabase.LoadAllAssetsAtPath(IconSheet))
            if (a is Sprite s) sprites[s.name] = s;

        // 1. Credits button at the end of the title row.
        Transform creditsT = row.Find("Btn Credits");
        if (creditsT == null)
        {
            var go = Object.Instantiate(template.gameObject, row);
            go.name = "Btn Credits";
            Undo.RegisterCreatedObjectUndo(go, "Credits button");
            creditsT = go.transform;
        }

        creditsT.SetAsLastSibling();
        MomentaryToggleButton creditsButton = SetupButton(creditsT, "Credits", sprites, "icon_info_24_Filled");
        FitCanvas(row);
        SetPokeButtons(startRoot);

        // 2. The board (rebuilt from scratch).
        Transform old = shelf.transform.Find("Credits");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        var creditsGo = new GameObject("Credits");
        Undo.RegisterCreatedObjectUndo(creditsGo, "Credits board");
        creditsGo.transform.SetParent(shelf.transform, false);
        var credits = creditsGo.AddComponent<CreditsBoard>();

        Transform board = Node(creditsGo.transform, "Board");
        board.localPosition = new Vector3(0f, BoardY, 0f);

        var m = new Dictionary<string, Material>();
        foreach (string n in new[] { "Wood", "Parchment", "ParchmentShadow", "NailHead", "NailDent", "TitleStars", "Divider", "Flourish" })
        {
            m[n] = AssetDatabase.LoadAssetAtPath<Material>($"{NB}/Materials/NB_{n}.mat");
            if (m[n] == null) return $"missing noticeboard material NB_{n} (run Build Workshop Noticeboard)";
        }

        var titleInk = AssetDatabase.LoadAssetAtPath<Material>($"{NB}/Materials/Title_Ink.mat");

        BuildWood(Node(board, "Wood"), m["Wood"]);
        Quad(board, "Parchment Shadow", m["ParchmentShadow"], new Vector2(0.006f, -0.007f), new Vector2(PaperW * 1.1f, PaperH * 1.1f), SortShadow, 0.0012f);
        var paper = MeshNode(board, "Parchment", Mesh($"{NB}/Meshes/NB_Parchment.asset"), m["Parchment"], SortPaper);
        paper.transform.localScale = new Vector3(PaperW / 0.47f, PaperH / 0.52f, 1f);
        BuildNails(Node(board, "Nails"), m["NailHead"], m["NailDent"]);

        Transform content = Node(board, "Content");
        Quad(content, "Title Stars", m["TitleStars"], new Vector2(0f, 0.100f), new Vector2(0.30f, 0.0586f), SortInk);
        var title = Text(content, "Title", "Celestial<space=0.85em>Workshop", FontTitle, 0.019f, InkText, TextAlignmentOptions.Baseline, new Vector2(0f, 0.083f), 0.36f, maxWidth: 0.30f);
        if (titleInk != null) title.fontSharedMaterial = titleInk;
        var heading = Text(content, "Heading", "Credits", FontHand, 0.0115f, InkText, TextAlignmentOptions.Baseline, new Vector2(0f, 0.058f), 0.2f, FontStyles.Bold);
        float hw = heading.GetPreferredValues("Credits").x * 0.5f;
        Quad(content, "Flourish Left", m["Flourish"], new Vector2(-hw - 0.040f, 0.0625f), new Vector2(0.06f, 0.0131f), SortInk);
        Quad(content, "Flourish Right", m["Flourish"], new Vector2(hw + 0.040f, 0.0625f), new Vector2(0.06f, 0.0131f), SortInk, flipX: true);
        Quad(content, "Divider", m["Divider"], new Vector2(0f, 0.040f), new Vector2(0.32f, 0.0175f), SortInk);

        // Body: one autosized block, top-centre at y 0.027, down to the sheet's margin.
        const float bodyTop = 0.027f, bodyBottom = -0.112f;
        var body = Text(content, "Credits Text", "", FontBody, BodyCapMax, InkBody, TextAlignmentOptions.Top, new Vector2(0f, (bodyTop + bodyBottom) * 0.5f), 0.36f);
        body.rectTransform.sizeDelta = new Vector2(0.36f, bodyTop - bodyBottom);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.overflowMode = TextOverflowModes.Overflow;
        body.lineSpacing = -8f;
        body.enableAutoSizing = true;
        body.fontSizeMax = body.fontSize;
        body.fontSizeMin = body.fontSize * BodyCapMin / BodyCapMax;

        // Back button: a copy of the title's button canvas with one button.
        var backRoot = Object.Instantiate(startRoot, board);
        backRoot.name = "Back Button";
        backRoot.SetActive(true);
        backRoot.transform.localPosition = new Vector3(0f, BackButtonY - BoardY, -0.012f);
        backRoot.transform.localRotation = Quaternion.identity;
        backRoot.transform.localScale = Vector3.one;
        var placement = backRoot.GetComponent<PlacementControls>();
        if (placement != null) Object.DestroyImmediate(placement);
        Transform backRow = backRoot.transform.Find("Menu Canvas/Buttons");
        for (int i = backRow.childCount - 1; i >= 0; i--)
        {
            Transform c = backRow.GetChild(i);
            if (c.name != "Btn Credits") Object.DestroyImmediate(c.gameObject);
        }

        Transform backT = backRow.Find("Btn Credits");
        backT.name = "Btn Back";
        MomentaryToggleButton backButton = SetupButton(backT, "Back", sprites, "icon_arrow-left_24_Filled");
        FitCanvas(backRow);
        SetPokeButtons(backRoot);

        var text = AssetDatabase.LoadAssetAtPath<TextAsset>(TextPath);
        var so = new SerializedObject(credits);
        so.FindProperty("shelf").objectReferenceValue = shelf;
        so.FindProperty("gameFlow").objectReferenceValue = Object.FindFirstObjectByType<GameFlow>(FindObjectsInactive.Include);
        so.FindProperty("openButton").objectReferenceValue = creditsButton;
        so.FindProperty("backButton").objectReferenceValue = backButton;
        so.FindProperty("board").objectReferenceValue = board.gameObject;
        so.FindProperty("creditsSource").objectReferenceValue = text;
        so.FindProperty("creditsText").objectReferenceValue = body;
        so.ApplyModifiedPropertiesWithoutUndo();
        credits.ApplyText();
        body.ForceMeshUpdate();
        float bodyCap = body.fontSize * 0.1f * body.font.faceInfo.scale * body.font.faceInfo.capLine / body.font.faceInfo.pointSize;
        bool overflow = body.isTextOverflowing || body.preferredHeight > body.rectTransform.rect.height + 0.0005f;

        board.gameObject.SetActive(false);
        PrefabUtility.SaveAsPrefabAssetAndConnect(creditsGo, PrefabPath, InteractionMode.AutomatedAction);

        // Button-press clicks for the two new buttons.
        AddFeedbackButtons(creditsButton, backButton);

        // 3. Background music.
        string music = EnsureMusic();

        EditorSceneManager.MarkSceneDirty(shelf.gameObject.scene);
        if (save) EditorSceneManager.SaveScene(shelf.gameObject.scene);
        return $"credits board built (body cap {bodyCap * 1000f:F2} mm{(overflow ? ", TEXT OVERFLOWS" : "")}); title row width {((RectTransform)row.parent).sizeDelta.x}; {music}";
    }

    // ---------------------------------------------------------------- Buttons

    static MomentaryToggleButton SetupButton(Transform t, string label, Dictionary<string, Sprite> sprites, string icon)
    {
        foreach (TMP_Text txt in t.GetComponentsInChildren<TMP_Text>(true))
        {
            Undo.RecordObject(txt, "Credits button");
            txt.text = label;
        }

        Transform iconT = t.Find("Content/Background/Elements/Icon");
        if (iconT != null && sprites.TryGetValue(icon, out Sprite sprite))
        {
            var img = iconT.GetComponent<Image>();
            Undo.RecordObject(img, "Credits button");
            img.sprite = sprite;
        }
        else
        {
            Debug.LogWarning($"CreditsBoardBuilder: icon {icon} not found; the copied icon stays.");
        }

        var button = t.GetComponent<MomentaryToggleButton>();
        var bso = new SerializedObject(button);
        bso.FindProperty("onPressed.m_PersistentCalls.m_Calls").ClearArray();
        bso.ApplyModifiedProperties();
        return button;
    }

    // Canvas width from the row's real layout, so every button sits inside the
    // canvas and its poke surface (same as Hook Up Placement Buttons).
    static void FitCanvas(Transform row)
    {
        var canvasRect = (RectTransform)row.parent;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.MarkLayoutForRebuild((RectTransform)row);
        LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);
        float width = LayoutUtility.GetPreferredWidth((RectTransform)row);
        Undo.RecordObject(canvasRect, "Credits button");
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

    // ---------------------------------------------------------------- Music

    static string EnsureMusic()
    {
        ImportMusic(WorkshopClip);
        ImportMusic(PuzzleClip);
        var workshop = AssetDatabase.LoadAssetAtPath<AudioClip>(WorkshopClip);
        var puzzle = AssetDatabase.LoadAssetAtPath<AudioClip>(PuzzleClip);

        var music = Object.FindFirstObjectByType<BackgroundMusic>(FindObjectsInactive.Include);
        if (music == null)
        {
            var go = new GameObject("Background Music");
            Undo.RegisterCreatedObjectUndo(go, "Background music");
            music = go.AddComponent<BackgroundMusic>();
            var feedback = Object.FindFirstObjectByType<CW.AudioFeedback.AudioFeedback>(FindObjectsInactive.Include);
            if (feedback != null) go.transform.SetSiblingIndex(feedback.transform.GetSiblingIndex() + 1);
        }

        var so = new SerializedObject(music);
        so.FindProperty("workshopMusic").objectReferenceValue = workshop;
        so.FindProperty("puzzleMusic").objectReferenceValue = puzzle;
        so.FindProperty("gameFlow").objectReferenceValue = Object.FindFirstObjectByType<GameFlow>(FindObjectsInactive.Include);
        so.ApplyModifiedProperties();
        return $"music: {(workshop != null ? workshop.length.ToString("F1") + " s" : "MISSING")} / {(puzzle != null ? puzzle.length.ToString("F1") + " s" : "MISSING")}";
    }

    // Long music: streamed from disk (small memory footprint on Quest), Vorbis.
    static void ImportMusic(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return;
        var s = importer.defaultSampleSettings;
        bool changed = s.loadType != AudioClipLoadType.Streaming || s.compressionFormat != AudioCompressionFormat.Vorbis || !Mathf.Approximately(s.quality, 0.6f);
        s.loadType = AudioClipLoadType.Streaming;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.6f;
        s.preloadAudioData = false;
        importer.defaultSampleSettings = s;
        if (importer.forceToMono)
        {
            importer.forceToMono = false;
            changed = true;
        }

        if (changed) importer.SaveAndReimport();
    }

    // ---------------------------------------------------------------- Board parts

    static void BuildWood(Transform wood, Material mat)
    {
        // The noticeboard's own planks and battens, scaled to this smaller board.
        var rnd = new System.Random(11);
        float r() => (float)rnd.NextDouble() * 2f - 1f;
        float plankW = BackW / 3f;
        for (int i = 0; i < 3; i++)
        {
            float x = -BackW * 0.5f + plankW * (i + 0.5f);
            var p = MeshNode(wood, "Plank " + (i + 1), Mesh($"{NB}/Meshes/NB_Plank_{i + 1}.asset"), mat, SortWood);
            p.transform.localPosition = new Vector3(x, r() * 0.002f, 0.031f);
            p.transform.localRotation = Quaternion.Euler(r() * 0.4f, r() * 0.4f, r() * 0.3f);
            p.transform.localScale = new Vector3(plankW / 0.178f, BackH / 0.585f, 1f);
        }

        var top = MeshNode(wood, "Batten Top", Mesh($"{NB}/Meshes/NB_Batten_Top.asset"), mat, SortWood);
        top.transform.localPosition = new Vector3(0.001f, BackH * 0.5f - 0.016f, 0.012f);
        top.transform.localRotation = Quaternion.Euler(0f, 0f, -0.5f);
        top.transform.localScale = new Vector3((BackW + 0.02f) / 0.565f, 0.75f, 1f);
        var bottom = MeshNode(wood, "Batten Bottom", Mesh($"{NB}/Meshes/NB_Batten_Bottom.asset"), mat, SortWood);
        bottom.transform.localPosition = new Vector3(-0.001f, -BackH * 0.5f + 0.016f, 0.012f);
        bottom.transform.localRotation = Quaternion.Euler(0f, 0f, 0.4f);
        bottom.transform.localScale = new Vector3((BackW + 0.02f) / 0.565f, 0.75f, 1f);
    }

    static void BuildNails(Transform nails, Material head, Material dent)
    {
        var spots = new[] { new Vector2(-0.178f, 0.108f), new Vector2(0.180f, 0.105f), new Vector2(-0.176f, -0.107f), new Vector2(0.178f, -0.104f) };
        var rnd = new System.Random(23);
        float r() => (float)rnd.NextDouble() * 2f - 1f;
        GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Mesh sphere = probe.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(probe);
        for (int i = 0; i < spots.Length; i++)
        {
            float size = 0.012f + r() * 0.0012f;
            var n = MeshNode(nails, "Nail " + (i + 1), sphere, head, SortNails);
            n.transform.localPosition = new Vector3(spots[i].x, spots[i].y, 0.0004f);
            n.transform.localRotation = Quaternion.Euler(r() * 12f, r() * 12f, r() * 180f);
            n.transform.localScale = new Vector3(size, size, size * 0.42f);
            Quad(nails, "Nail Dent " + (i + 1), dent, spots[i] + new Vector2(0.0013f, -0.0017f), Vector2.one * (size * 2.1f), SortInk, InkZ, r() * 30f);
        }
    }

    static Mesh Mesh(string path) => AssetDatabase.LoadAssetAtPath<Mesh>(path);

    static Transform Node(Transform parent, string name)
    {
        Transform t = parent.Find(name);
        if (t == null)
        {
            t = new GameObject(name).transform;
            t.SetParent(parent, false);
        }

        return t;
    }

    static MeshRenderer MeshNode(Transform parent, string name, Mesh mesh, Material mat, int sort)
    {
        Transform t = Node(parent, name);
        var mf = t.gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        var mr = t.gameObject.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.sortingOrder = sort;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    static MeshRenderer Quad(Transform parent, string name, Material mat, Vector2 center, Vector2 size, int sort, float z = InkZ, float rotZ = 0f, bool flipX = false)
    {
        var mr = MeshNode(parent, name, Mesh($"{NB}/Meshes/NB_Quad.asset"), mat, sort);
        mr.transform.localPosition = new Vector3(center.x, center.y, z);
        mr.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        mr.transform.localScale = new Vector3(size.x * (flipX ? -1f : 1f), size.y, 1f);
        return mr;
    }

    // Same sizing rule as the noticeboard: cap = cap height in metres.
    static TextMeshPro Text(Transform parent, string name, string text, string fontPath, float cap, Color color,
        TextAlignmentOptions align, Vector2 pos, float width, FontStyles style = FontStyles.Normal, float maxWidth = 0f)
    {
        Transform t = Node(parent, name);
        var tmp = t.gameObject.AddComponent<TextMeshPro>();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        tmp.font = font;
        tmp.fontSharedMaterial = font.material;
        tmp.text = text;
        tmp.fontStyle = style;
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

        tmp.transform.localPosition = new Vector3(pos.x, pos.y, TextZ);
        tmp.transform.localRotation = Quaternion.identity;
        tmp.transform.localScale = Vector3.one;
        var mr = tmp.GetComponent<MeshRenderer>();
        mr.sortingOrder = SortText;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return tmp;
    }
}
