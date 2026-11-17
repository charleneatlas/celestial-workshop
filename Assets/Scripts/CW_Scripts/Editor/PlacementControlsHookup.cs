using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds Recenter / Raise / Lower buttons beside the title's Start button
/// (copies of it, smaller) and wires them to WorkshopPlacement through a
/// PlacementControls component. Safe to run again: existing buttons are
/// reused by name.
/// </summary>
public static class PlacementControlsHookup
{
    const float SmallWidth = 84f;

    static readonly (string name, string label, string icon)[] Buttons =
    {
        ("Btn Recenter", "Recenter", WorkshopIcons.Recenter),
        ("Btn Raise", "Raise", WorkshopIcons.Raise),
        ("Btn Lower", "Lower", WorkshopIcons.Lower),
    };

    [MenuItem("Tools/Celestial Workshop/Hook Up Placement Buttons")]
    static void HookUpMenu() => HookUp(false);

    public static string HookUp(bool save)
    {
        var shelf = Object.FindFirstObjectByType<TitleShelf>(FindObjectsInactive.Include);
        if (shelf == null) return "no TitleShelf in the open scene";
        var root = (GameObject)new SerializedObject(shelf).FindProperty("startButtonRoot").objectReferenceValue;
        if (root == null) return "TitleShelf has no Start Button Root";
        Transform row = root.transform.Find("Menu Canvas/Buttons");
        Transform start = row != null ? row.Find("Btn Start") : null;
        if (start == null) return "Btn Start not found under Menu Canvas/Buttons";
        var placement = Object.FindFirstObjectByType<WorkshopPlacement>(FindObjectsInactive.Include);
        if (placement == null) return "no WorkshopPlacement in the open scene";

        Dictionary<string, Sprite> sprites = WorkshopIcons.All();

        var made = new List<MomentaryToggleButton>();
        foreach (var (name, label, icon) in Buttons)
        {
            Transform t = row.Find(name);
            if (t == null)
            {
                var go = Object.Instantiate(start.gameObject, row);
                go.name = name;
                Undo.RegisterCreatedObjectUndo(go, "Placement buttons");
                t = go.transform;
            }

            t.SetAsLastSibling();
            Resize(t, SmallWidth);
            foreach (TMP_Text text in t.GetComponentsInChildren<TMP_Text>(true))
            {
                Undo.RecordObject(text, "Placement buttons");
                text.text = label;
            }

            Transform iconT = t.Find("Content/Background/Elements/Icon");
            if (iconT != null && sprites.TryGetValue(icon, out Sprite sprite))
            {
                var img = iconT.GetComponent<Image>();
                Undo.RecordObject(img, "Placement buttons");
                img.sprite = sprite;
            }

            var button = t.GetComponent<MomentaryToggleButton>();
            var bso = new SerializedObject(button);
            bso.FindProperty("onPressed.m_PersistentCalls.m_Calls").ClearArray();
            bso.ApplyModifiedProperties();
            made.Add(button);
        }

        // Canvas width from the row's real layout, so every button sits inside
        // the canvas (and its poke surface, which follows the canvas rect).
        var canvasRect = (RectTransform)row.parent;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.MarkLayoutForRebuild((RectTransform)row);
        LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);
        float width = LayoutUtility.GetPreferredWidth((RectTransform)row);
        Undo.RecordObject(canvasRect, "Placement buttons");
        canvasRect.sizeDelta = new Vector2(Mathf.Ceil(width), canvasRect.sizeDelta.y);
        LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);

        // The Start hint rings the Start button itself, wherever it sits in the
        // row (it used to point at the row's center).
        int retargeted = 0;
        foreach (var director in Object.FindObjectsByType<CW.Guidance.GuidanceDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var dso = new SerializedObject(director);
            var steps = dso.FindProperty("steps");
            for (int i = 0; i < steps.arraySize; i++)
            {
                foreach (string field in new[] { "target", "pulseAround" })
                {
                    var prop = steps.GetArrayElementAtIndex(i).FindPropertyRelative(field);
                    if (prop != null && prop.objectReferenceValue == root.transform)
                    {
                        prop.objectReferenceValue = start;
                        retargeted++;
                    }
                }
            }

            dso.ApplyModifiedProperties();
        }

        // Poke-release firing: every button on the canvas.
        var poke = root.GetComponent<PokeReleaseAnywhere>();
        if (poke != null)
        {
            var pso = new SerializedObject(poke);
            var list = pso.FindProperty("buttons");
            var all = root.GetComponentsInChildren<MomentaryToggleButton>(true);
            list.arraySize = all.Length;
            for (int i = 0; i < all.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = all[i];
            pso.ApplyModifiedProperties();
        }

        var controls = root.GetComponent<PlacementControls>();
        if (controls == null) controls = Undo.AddComponent<PlacementControls>(root);
        var cso = new SerializedObject(controls);
        cso.FindProperty("placement").objectReferenceValue = placement;
        cso.FindProperty("recenterButton").objectReferenceValue = made[0];
        cso.FindProperty("raiseButton").objectReferenceValue = made[1];
        cso.FindProperty("lowerButton").objectReferenceValue = made[2];
        cso.ApplyModifiedProperties();

        // The Recenter countdown prompt lives with the placement authority.
        var prompt = placement.GetComponent<RecenterPrompt>();
        if (prompt == null) prompt = Undo.AddComponent<RecenterPrompt>(placement.gameObject);
        var rso = new SerializedObject(prompt);
        rso.FindProperty("placement").objectReferenceValue = placement;
        var rig = Object.FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
        if (rig != null && rig.centerEyeAnchor != null)
            rso.FindProperty("viewer").objectReferenceValue = rig.centerEyeAnchor.GetComponent<Camera>();
        var hand = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/CW_Art/Fonts/Caveat SDF.asset");
        if (hand != null) rso.FindProperty("font").objectReferenceValue = hand;
        rso.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(root.scene);
        if (save) EditorSceneManager.SaveScene(root.scene);
        return $"placement buttons: {made.Count} under {root.name}, canvas width {canvasRect.sizeDelta.x}, hint targets moved to Start: {retargeted}";
    }

    static void Resize(Transform button, float width)
    {
        var rt = (RectTransform)button;
        Undo.RecordObject(rt, "Placement buttons");
        rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
        var le = button.GetComponent<LayoutElement>();
        if (le != null)
        {
            Undo.RecordObject(le, "Placement buttons");
            le.preferredWidth = width;
            le.minWidth = width;
        }

        // The label's own preferred width would widen the button again.
        Transform label = button.Find("Content/Background/Elements/Label (1)");
        if (label != null && label.TryGetComponent(out LayoutElement labelLe) && labelLe.preferredWidth > width - 8f)
        {
            Undo.RecordObject(labelLe, "Placement buttons");
            labelLe.preferredWidth = width - 8f;
        }

        Transform bg = button.Find("Content/Background");
        if (bg != null)
        {
            var bgRt = (RectTransform)bg;
            Undo.RecordObject(bgRt, "Placement buttons");
            bgRt.sizeDelta = new Vector2(width, bgRt.sizeDelta.y);
        }
    }
}
