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
        { "Btn Choose Another Box", AnotherBox },
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

    // Canvas path -> (canvas scale, canvas y shift in its parent, label min, label max).
    // Title row grows downward (top edge kept clear of the WORKSHOP letters).
    static readonly (string path, float scale, float shiftY, float min, float max)[] Menus =
    {
        ("Constellation Title/Title Start Button/Menu Canvas", 0.00065f, -0.0075f, 12f, 14f),
        ("WORKSHOP/TableItems/Puzzle Presentation/Constellation Boxes/Boxes Back To Title Button/Menu Canvas", 0.000625f, 0f, 12f, 14f),
        ("WORKSHOP/TableItems/Puzzle Presentation/Constellation Boxes/All Boxes Button/Menu Canvas", 0.000625f, 0f, 12f, 14f),
        ("WORKSHOP/TableItems/Puzzle Presentation/Constellation Boxes/Field Note Reader/Board/Back Button/Menu Canvas", 0.000625f, 0f, 12f, 14f),
        ("WORKSHOP/TableItems/Puzzle Presentation/Puzzle Back Button/Menu Canvas", 0.000625f, 0f, 12f, 14f),
        ("WORKSHOP/TableItems/Puzzle Presentation/Post-Puzzle Menu/Menu Canvas", 0.000625f, 0f, 11f, 14f),
    };

    const float BaseCanvasScale = 0.0005f;
    const float CreditsBoardScale = 1.35f;

    [MenuItem("Tools/Celestial Workshop/Apply Menu Sizing")]
    static void ApplySizingMenu() => Debug.Log("WorkshopIcons: " + ApplySizing());

    public static string ApplySizing()
    {
        var log = new List<string>();
        foreach (var m in Menus)
        {
            GameObject go = FindPath(m.path);
            if (go == null)
            {
                log.Add("missing " + m.path);
                continue;
            }

            Transform t = go.transform;
            Undo.RecordObject(t, "Menu sizing");
            // Shift relative to the original scale, so a second run is a no-op.
            float current = t.localScale.x;
            if (!Mathf.Approximately(current, m.scale))
            {
                float alreadyShifted = Mathf.Approximately(current, BaseCanvasScale) ? 0f : m.shiftY;
                t.localPosition += new Vector3(0f, m.shiftY - alreadyShifted, 0f);
                t.localScale = new Vector3(m.scale, m.scale, m.scale);
            }

            int labels = 0;
            foreach (MomentaryToggleButton b in go.GetComponentsInChildren<MomentaryToggleButton>(true))
            {
                TMP_Text label = b.transform.Find(LabelPath)?.GetComponent<TMP_Text>();
                if (label == null) continue;
                Undo.RecordObject(label, "Menu sizing");
                label.enableAutoSizing = true;
                label.fontSizeMin = m.min;
                label.fontSizeMax = m.max;
                PrefabUtility.RecordPrefabInstancePropertyModifications(label);
                labels++;
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            log.Add($"{go.name} under {t.parent.name}: scale {m.scale}, {labels} label(s)");
        }

        // Credits board (prefab): the whole sheet 1.35x so the legal notices
        // read at ~0.66 deg after the title's 0.8 scale; its Back button too.
        GameObject root = PrefabUtility.LoadPrefabContents(CreditsPrefab);
        try
        {
            Transform board = root.transform.Find("Board");
            if (board != null)
            {
                board.localScale = Vector3.one * CreditsBoardScale;
                log.Add($"Credits Board scale {CreditsBoardScale}");
            }
            else
            {
                log.Add("missing Credits/Board in prefab");
            }

            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.name != "Label (1)") continue;
                label.enableAutoSizing = true;
                label.fontSizeMin = 12f;
                label.fontSizeMax = 14f;
            }

            PrefabUtility.SaveAsPrefabAsset(root, CreditsPrefab);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        EditorSceneManager.MarkAllScenesDirty();
        return string.Join("; ", log);
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
