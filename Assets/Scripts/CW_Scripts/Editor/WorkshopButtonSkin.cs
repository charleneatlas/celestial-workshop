using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools > Celestial Workshop > Skin Navigation Buttons.
///
/// Gives the stock ISDK poke buttons (title Start/Recenter/Raise/Lower, the
/// post-puzzle menu, Back to title, the box-stage buttons — every
/// MomentaryToggleButton in the open scene) the workshop look: parchment
/// faces with ink lettering in Architects Daughter, a warm wood press state,
/// and a wooden plank behind each row. Behaviour is untouched: the buttons
/// keep their Toggle, poke surface and MomentaryToggleButton.
///
/// The ISDK Animator rewrites the face and label colours every frame, so the
/// colours live in an AnimatorOverrideController whose clips are recoloured
/// copies of the ISDK dark-theme clips (Assets/CW_UI/WorkshopButton). Safe to
/// run again: it refreshes the clips and reuses the planks it made.
/// </summary>
public static class WorkshopButtonSkin
{
    private const string Folder = "Assets/CW_UI/WorkshopButton";
    private const string OverridePath = Folder + "/WorkshopButton.overrideController";
    private const string FontPath = "Assets/CW_Art/Fonts/ArchitectsDaughter SDF.asset";
    private const string WoodPath = "Assets/CW_Onboarding/Art/Generated/Wood.png";
    private const string PlankName = "Workshop Plank";
    private const string PlankEdgeName = "Workshop Plank Edge";

    // Palette: cream parchment, ink, warm wood (matches the noticeboard).
    private static readonly Color Parchment = new Color(0.93f, 0.86f, 0.70f, 1f);
    private static readonly Color ParchmentBright = new Color(1f, 0.95f, 0.82f, 1f);
    private static readonly Color WoodPressed = new Color(0.50f, 0.32f, 0.17f, 1f);
    private static readonly Color DisabledFace = new Color(0.75f, 0.70f, 0.62f, 0.5f);
    private static readonly Color Ink = new Color(0.22f, 0.12f, 0.05f, 1f);
    private static readonly Color Cream = new Color(1f, 0.94f, 0.80f, 1f);
    private static readonly Color PlankEdge = new Color(0.30f, 0.20f, 0.12f, 1f);

    private static readonly Vector2 PlankPadding = new Vector2(10f, 8f);
    private const float EdgeWidth = 3f;

    // Taller than the ISDK's 16 so the handwriting can be read at arm's length
    // (the button is 84 units tall: 24 icon + 4 gap + this, inside its padding).
    private const float LabelSlotHeight = 24f;

    [MenuItem("Tools/Celestial Workshop/Skin Navigation Buttons")]
    public static void SkinFromMenu()
    {
        Skin(true);
    }

    public static string Skin(bool log)
    {
        MomentaryToggleButton[] buttons = Object.FindObjectsByType<MomentaryToggleButton>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (buttons.Length == 0)
        {
            return "No MomentaryToggleButton in the open scene.";
        }

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Texture wood = AssetDatabase.LoadAssetAtPath<Texture>(WoodPath);

        AnimatorOverrideController overrideController = null;
        var rows = new HashSet<RectTransform>();
        int skinned = 0;

        foreach (MomentaryToggleButton button in buttons)
        {
            var animator = button.GetComponent<Animator>();
            if (animator != null)
            {
                RuntimeAnimatorController current = animator.runtimeAnimatorController;
                if (overrideController == null)
                {
                    RuntimeAnimatorController baseController = current is AnimatorOverrideController o ? o.runtimeAnimatorController : current;
                    if (baseController != null)
                    {
                        overrideController = BuildOverride(baseController);
                    }
                }

                if (overrideController != null && current != overrideController)
                {
                    Undo.RecordObject(animator, "Skin Navigation Buttons");
                    animator.runtimeAnimatorController = overrideController;
                    Record(animator);
                }
            }

            SkinFace(button.transform, font);
            if (button.transform.parent is RectTransform row)
            {
                rows.Add(row);
            }

            skinned++;
        }

        int planks = 0;
        foreach (RectTransform row in rows)
        {
            if (row.parent is RectTransform canvas)
            {
                EnsurePlank(canvas, row, PlankEdgeName, null, PlankEdge, PlankPadding + Vector2.one * EdgeWidth);
                EnsurePlank(canvas, row, PlankName, wood, Color.white, PlankPadding);
                planks++;
            }
        }

        EditorSceneManager.MarkAllScenesDirty();
        string summary = $"WorkshopButtonSkin: {skinned} buttons skinned, {planks} planks, override {(overrideController != null ? AssetDatabase.GetAssetPath(overrideController) : "none")}.";
        if (log)
        {
            Debug.Log(summary);
        }

        return summary;
    }

    // ---- Colours (through the Animator) ----

    private static AnimatorOverrideController BuildOverride(RuntimeAnimatorController baseController)
    {
        EnsureFolder(Folder);

        var overrideController = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(OverridePath);
        if (overrideController == null)
        {
            overrideController = new AnimatorOverrideController(baseController) { name = "WorkshopButton" };
            AssetDatabase.CreateAsset(overrideController, OverridePath);
        }
        else if (overrideController.runtimeAnimatorController != baseController)
        {
            overrideController.runtimeAnimatorController = baseController;
        }

        var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        overrideController.GetOverrides(pairs);
        var made = new Dictionary<AnimationClip, AnimationClip>();
        for (int i = 0; i < pairs.Count; i++)
        {
            AnimationClip source = pairs[i].Key;
            if (source == null)
            {
                continue;
            }

            if (!made.TryGetValue(source, out AnimationClip copy))
            {
                copy = RecolouredClip(source);
                made[source] = copy;
            }

            pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(source, copy);
        }

        overrideController.ApplyOverrides(pairs);
        EditorUtility.SetDirty(overrideController);
        AssetDatabase.SaveAssets();
        return overrideController;
    }

    private static AnimationClip RecolouredClip(AnimationClip source)
    {
        // Stable names: one clip per source name (the ISDK controller reuses "Disabled").
        string path = $"{Folder}/WorkshopButton_{source.name}.anim";

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }

        EditorUtility.CopySerialized(source, clip);
        clip.name = Path.GetFileNameWithoutExtension(path);

        StateColours(source.name, out Color face, out Color letters);
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            bool isFace = binding.path.EndsWith("Background") && binding.propertyName.StartsWith("m_Color.");
            bool isLetters = (binding.path.EndsWith("Label") || binding.path.EndsWith("Label (1)") || binding.path.EndsWith("Icon")) &&
                             (binding.propertyName.StartsWith("m_fontColor.") || binding.propertyName.StartsWith("m_Color."));
            if (!isFace && !isLetters)
            {
                continue;
            }

            Color colour = isFace ? face : letters;
            char channel = binding.propertyName[binding.propertyName.Length - 1];
            float value = channel == 'r' ? colour.r : channel == 'g' ? colour.g : channel == 'b' ? colour.b : colour.a;

            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            Keyframe[] keys = curve.keys;
            for (int k = 0; k < keys.Length; k++)
            {
                keys[k].value = value;
                keys[k].inTangent = 0f;
                keys[k].outTangent = 0f;
            }

            curve.keys = keys;
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static void StateColours(string state, out Color face, out Color letters)
    {
        letters = Ink;
        switch (state)
        {
            case "Normal":
                face = Parchment;
                break;
            case "Pressed":
            case "SelectedPressed":
                face = WoodPressed;
                letters = Cream;
                break;
            case "Disabled":
                face = DisabledFace;
                break;
            default: // Highlighted, Selected and the Selected* variants
                face = ParchmentBright;
                break;
        }
    }

    // ---- Font and resting colours ----

    private static void SkinFace(Transform button, TMP_FontAsset font)
    {
        foreach (TMP_Text label in button.GetComponentsInChildren<TMP_Text>(true))
        {
            Undo.RecordObject(label, "Skin Navigation Buttons");
            if (font != null)
            {
                label.font = font;
                label.fontSharedMaterial = font.material;
            }

            label.fontStyle = FontStyles.Bold;
            label.color = Ink;

            // The visible label sits in a fixed-height slot: let long labels shrink to fit.
            if (label.gameObject.name == "Label (1)")
            {
                label.enableAutoSizing = true;
                label.fontSizeMin = 9f;
                label.fontSizeMax = 16f;

                var slot = label.GetComponent<LayoutElement>();
                if (slot != null)
                {
                    Undo.RecordObject(slot, "Skin Navigation Buttons");
                    slot.preferredHeight = LabelSlotHeight;
                    slot.minHeight = LabelSlotHeight;
                    Record(slot);
                }
            }

            Record(label);
        }

        foreach (Image image in button.GetComponentsInChildren<Image>(true))
        {
            Undo.RecordObject(image, "Skin Navigation Buttons");
            image.color = image.gameObject.name == "Background" ? Parchment : Ink;
            Record(image);
        }
    }

    // ---- Wooden plank behind the row ----

    private static void EnsurePlank(RectTransform canvas, RectTransform row, string name, Texture texture, Color colour, Vector2 padding)
    {
        Transform existing = canvas.Find(name);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(WorkshopPlank));
            Undo.RegisterCreatedObjectUndo(go, "Skin Navigation Buttons");
            go.transform.SetParent(canvas, false);
            go.layer = canvas.gameObject.layer;
        }

        var image = go.GetComponent<RawImage>();
        if (image == null)
        {
            image = go.AddComponent<RawImage>();
        }

        Undo.RecordObject(image, "Skin Navigation Buttons");
        image.texture = texture;
        image.color = colour;
        image.raycastTarget = false;

        var plank = go.GetComponent<WorkshopPlank>();
        if (plank == null)
        {
            plank = go.AddComponent<WorkshopPlank>();
        }

        Undo.RecordObject(plank, "Skin Navigation Buttons");
        plank.Configure(row, padding, texture != null);
        Record(plank);

        // Behind everything else on the canvas; the edge behind the plank.
        go.transform.SetSiblingIndex(0);
        if (name == PlankName)
        {
            Transform edge = canvas.Find(PlankEdgeName);
            if (edge != null)
            {
                edge.SetSiblingIndex(0);
            }
        }
    }

    private static void Record(Object target)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(target))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        EditorUtility.SetDirty(target);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
