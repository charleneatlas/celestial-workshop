using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// The workshop navigation icons (Assets/CW_UI/Icons, original artwork) and
/// the menu sizing pass.
///
/// Tools/Celestial Workshop/Apply Workshop Icons: sets the icon import
/// settings and puts the right icon on every menu button in the open scene
/// and in CreditsBoard.prefab (template tables under INTERACTABLES are left
/// alone).
///
/// Tools/Celestial Workshop/Apply Menu Sizing: larger button canvases and
/// fixed-range label sizes; absolute values, so running it twice is safe.
///
/// The builders (PlacementControlsHookup, CreditsBoardBuilder,
/// SelectionTableBuilder) take their icons from <see cref="All"/>, so
/// re-running them keeps these icons.
/// </summary>
public static class WorkshopIcons
{
    public const string Dir = "Assets/CW_UI/Icons";

    public const string Start = "cw_icon_start";
    public const string Recenter = "cw_icon_recenter";
    public const string Raise = "cw_icon_raise";
    public const string Lower = "cw_icon_lower";
    public const string Credits = "cw_icon_credits";
    public const string Back = "cw_icon_back";
    public const string BackToTitle = "cw_icon_back_to_title";
    public const string FieldNote = "cw_icon_field_note";
    public const string AllBoxes = "cw_icon_all_boxes";
    public const string TryAnother = "cw_icon_try_another";
    public const string AnotherBox = "cw_icon_another_box";
    public const string ViewConstellations = "cw_icon_view_constellations";

    // Button GameObject name -> icon.
    static readonly Dictionary<string, string> ByButton = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Btn Start", Start },
        { "Btn Recenter", Recenter },
        { "Btn Raise", Raise },
        { "Btn Lower", Lower },
        { "Btn Credits", Credits },
        { "Btn Back", Back },
        { "Btn Back to title", BackToTitle },
        { "Btn Field Note", FieldNote },
        { "Btn All boxes", AllBoxes },
        { "Btn Choose Another Constellation", TryAnother },
        { "Btn Choose Another Box", AllBoxes },
        { "Btn View Constellations", ViewConstellations },
    };

    const string CreditsPrefab = "Assets/CW_Credits/CreditsBoard.prefab";
    const string IconPath = "Content/Background/Elements/Icon";
    const string LabelPath = "Content/Background/Elements/Label (1)";

    /// <summary>Icon name -> sprite, for the builders.</summary>
    public static Dictionary<string, Sprite> All()
    {
        ConfigureImporters();
        var sprites = new Dictionary<string, Sprite>();
        foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { Dir }))
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
            if (s != null) sprites[s.name] = s;
        }

        return sprites;
    }

    public static void ConfigureImporters()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;

            bool dirty = ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single ||
                         !ti.mipmapEnabled || ti.filterMode != FilterMode.Trilinear || ti.wrapMode != TextureWrapMode.Clamp ||
                         !ti.alphaIsTransparency || !ti.sRGBTexture || ti.maxTextureSize != 256;
            if (!dirty) continue;

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(settings);
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.alphaIsTransparency = true;
            ti.sRGBTexture = true;
            ti.maxTextureSize = 256;
            ti.SaveAndReimport();
        }
    }

    [MenuItem("Tools/Celestial Workshop/Apply Workshop Icons")]
    static void ApplyIconsMenu() => Debug.Log("WorkshopIcons: " + ApplyIcons());

    public static string ApplyIcons()
    {
        Dictionary<string, Sprite> sprites = All();
        if (sprites.Count == 0) return $"no sprites in {Dir}";

        int scene = 0;
        foreach (MomentaryToggleButton b in Object.FindObjectsByType<MomentaryToggleButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (EditorUtility.IsPersistent(b) || IsTemplate(b.transform)) continue;
            if (SetIcon(b.transform, sprites)) scene++;
        }

        int prefab = 0;
        GameObject root = PrefabUtility.LoadPrefabContents(CreditsPrefab);
        try
        {
            foreach (MomentaryToggleButton b in root.GetComponentsInChildren<MomentaryToggleButton>(true))
                if (SetIcon(b.transform, sprites)) prefab++;
            if (prefab > 0) PrefabUtility.SaveAsPrefabAsset(root, CreditsPrefab);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        EditorSceneManager.MarkAllScenesDirty();
        return $"{scene} scene button(s) and {prefab} in CreditsBoard.prefab now use workshop icons (save the scene).";
    }

    static bool SetIcon(Transform button, Dictionary<string, Sprite> sprites)
    {
        if (!ByButton.TryGetValue(button.name, out string icon) || !sprites.TryGetValue(icon, out Sprite sprite)) return false;
        Image image = button.Find(IconPath)?.GetComponent<Image>();
        if (image == null) return false;
        if (image.sprite == sprite && image.preserveAspect) return true;
        Undo.RecordObject(image, "Workshop icon");
        image.sprite = sprite;
        image.preserveAspect = true;
        PrefabUtility.RecordPrefabInstancePropertyModifications(image);
        return true;
    }

    static bool IsTemplate(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.name == "INTERACTABLES") return true;
        return false;
    }

    // ------------------------------------------------------------------ sizing

    // One entry per button menu. Values are absolute, so re-running is a no-op.
    //  scale     canvas scale (the ISDK sample default is 0.0005)
    //  keepTop   grow downward (title row: stays clear of the WORKSHOP letters)
    //  labelH    label box height; 20 lets autosize reach 14 (16 capped it at 12)
    //  wideLabel label width for 112-wide buttons (centred, may use the side padding)
    //  buttonW   button width (0 = leave), for long single-line labels
    //  noWrap    single line: autosize shrinks instead of wrapping into the icon
    struct Menu
    {
        public string path;
        public float scale;
        public bool keepTop;
        public float labelH, wideLabel, buttonW, min, max;
        public bool noWrap;
    }

    static readonly Menu[] Menus =
    {
        // Title row (~0.5 m away, title shelf x0.8 at runtime).
        new Menu { path = "Constellation Title/Title Start Button/Menu Canvas", scale = 0.00073f, keepTop = true, labelH = 20f, wideLabel = 96f, min = 12f, max = 14f },
        // Workshop table menus: close to the player, size unchanged; only the label box.
        new Menu { path = "WORKSHOP/TableItems/Puzzle Presentation/Constellation Boxes/Boxes Back To Title Button/Menu Canvas", scale = 0.000625f, labelH = 20f, wideLabel = 96f, min = 12f, max = 14f },
        new Menu { path = "WORKSHOP/TableItems/Puzzle Presentation/Constellation Boxes/All Boxes Button/Menu Canvas", scale = 0.000625f, labelH = 20f, wideLabel = 96f, min = 12f, max = 14f },
        new Menu { path = "WORKSHOP/TableItems/Puzzle Presentation/Constellation Boxes/Field Note Reader/Board/Back Button/Menu Canvas", scale = 0.000625f, labelH = 20f, wideLabel = 96f, min = 12f, max = 14f },
        // Back to title during the puzzle (~0.67 m away).
        new Menu { path = "WORKSHOP/TableItems/Puzzle Presentation/Puzzle Back Button/Menu Canvas", scale = 0.00085f, labelH = 20f, wideLabel = 96f, min = 12f, max = 14f },
        // Post-solve: wider buttons so each label is one line under its icon.
        new Menu { path = "WORKSHOP/TableItems/Puzzle Presentation/Post-Puzzle Menu/Menu Canvas", scale = 0.000625f, labelH = 20f, wideLabel = 136f, buttonW = 160f, min = 12f, max = 14f, noWrap = true },
    };

    const float CreditsBoardScale = 1.35f;
    const float CreditsBackCanvasScale = 0.00055f;

    [MenuItem("Tools/Celestial Workshop/Apply Menu Sizing")]
    static void ApplySizingMenu() => Debug.Log("WorkshopIcons: " + ApplySizing());

    public static string ApplySizing()
    {
        var log = new List<string>();
        foreach (Menu m in Menus)
        {
            GameObject go = FindPath(m.path);
            if (go == null)
            {
                log.Add("missing " + m.path);
                continue;
            }

            var rt = (RectTransform)go.transform;
            Undo.RecordObject(rt, "Menu sizing");
            float current = rt.localScale.x;
            if (!Mathf.Approximately(current, m.scale))
            {
                if (m.keepTop)
                {
                    // Keep the top edge: move down by half the added height.
                    float dy = rt.rect.height * (m.scale - current) * 0.5f;
                    rt.localPosition -= new Vector3(0f, dy, 0f);
                }

                rt.localScale = Vector3.one * m.scale;
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(rt);
            int n = 0;
            foreach (MomentaryToggleButton b in go.GetComponentsInChildren<MomentaryToggleButton>(true))
            {
                if (SizeButton(b, m.labelH, m.wideLabel, m.buttonW, m.min, m.max, m.noWrap)) n++;
            }

            log.Add($"{rt.parent.name}: scale {m.scale}, {n} button(s)");
        }

        // Credits board (prefab): the sheet 1.35x; its Back button canvas a
        // little larger and the label box 20 tall.
        GameObject root = PrefabUtility.LoadPrefabContents(CreditsPrefab);
        try
        {
            Transform board = root.transform.Find("Board");
            if (board != null) board.localScale = Vector3.one * CreditsBoardScale;
            Transform back = board != null ? board.Find("Back Button/Menu Canvas") : null;
            if (back != null) back.localScale = Vector3.one * CreditsBackCanvasScale;
            foreach (MomentaryToggleButton b in root.GetComponentsInChildren<MomentaryToggleButton>(true))
                SizeButton(b, 20f, 96f, 0f, 12f, 14f, false);
            PrefabUtility.SaveAsPrefabAsset(root, CreditsPrefab);
            log.Add($"Credits Board {CreditsBoardScale}, Back canvas {(back != null ? CreditsBackCanvasScale.ToString() : "missing")}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        EditorSceneManager.MarkAllScenesDirty();
        return string.Join("; ", log);
    }

    static bool SizeButton(MomentaryToggleButton b, float labelH, float wideLabel, float buttonW, float min, float max, bool noWrap)
    {
        if (buttonW > 0f)
        {
            var le = b.GetComponent<LayoutElement>();
            if (le != null)
            {
                Undo.RecordObject(le, "Menu sizing");
                le.preferredWidth = buttonW;
                le.minWidth = Mathf.Min(le.minWidth, buttonW);
                PrefabUtility.RecordPrefabInstancePropertyModifications(le);
            }
        }

        TMP_Text label = b.transform.Find(LabelPath)?.GetComponent<TMP_Text>();
        if (label == null) return false;
        var lr = label.rectTransform;
        float buttonWidth = buttonW > 0f ? buttonW : b.GetComponent<LayoutElement>()?.preferredWidth ?? 0f;
        Undo.RecordObject(lr, "Menu sizing");
        Undo.RecordObject(label, "Menu sizing");
        float w = buttonWidth >= 112f ? wideLabel : lr.sizeDelta.x;
        lr.sizeDelta = new Vector2(w, labelH);
        var lle = label.GetComponent<LayoutElement>();
        if (lle != null)
        {
            Undo.RecordObject(lle, "Menu sizing");
            lle.preferredWidth = w;
            lle.preferredHeight = labelH;
            lle.minHeight = labelH;
            PrefabUtility.RecordPrefabInstancePropertyModifications(lle);
        }

        label.enableAutoSizing = true;
        label.fontSizeMin = min;
        label.fontSizeMax = max;
        if (noWrap) label.textWrappingMode = TextWrappingModes.NoWrap;
        PrefabUtility.RecordPrefabInstancePropertyModifications(lr);
        PrefabUtility.RecordPrefabInstancePropertyModifications(label);
        return true;
    }

    static GameObject FindPath(string path)
    {
        int slash = path.IndexOf('/');
        string rootName = slash < 0 ? path : path.Substring(0, slash);
        foreach (GameObject r in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (r.name != rootName) continue;
            if (slash < 0) return r;
            Transform t = r.transform.Find(path.Substring(slash + 1));
            if (t != null) return t.gameObject;
        }

        return null;
    }
}
