using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Celestial Workshop > Hook Up Comfortable Title.
/// Wires the close title and workstation practice into the open scene:
///   - GameFlow hides the globe (mask, rim, Earth view display) at the title.
///   - TitleFlowBridge places the title near the player and sends title
///     letters to the workstation's drop zone.
///   - The title-letter hint names the workstation.
/// Run Hook Up Workstation first. Safe to run again.
/// </summary>
public static class ComfortableTitleHookup
{
    static readonly string[] GlobeParts = { "Mask Sphere", "Rim Sphere", "Earth View Display (Large)" };

    [MenuItem("Tools/Celestial Workshop/Hook Up Comfortable Title")]
    static void HookUpMenu() => Debug.Log("ComfortableTitleHookup: " + HookUp(true));

    public static string HookUp(bool save)
    {
        var log = new List<string>();
        var flow = Find<GameFlow>();
        var bridge = Find<TitleFlowBridge>();
        var station = Find<WorkstationMini>();
        var volume = Find<CircularWindow>();
        if (flow == null || bridge == null || station == null || volume == null)
            return "missing GameFlow / TitleFlowBridge / WorkstationMini / CircularWindow";

        var zone = (SphereDropZone)new SerializedObject(station).FindProperty("dropZone").objectReferenceValue;
        if (zone == null) return "WorkstationMini has no drop zone (run Hook Up Workstation)";

        // ---- Globe hidden at the title ----
        var parts = new List<Transform>();
        foreach (string name in GlobeParts)
        {
            Transform t = volume.transform.Find(name);
            if (t != null) parts.Add(t);
            else log.Add($"globe part '{name}' not found");
        }

        var flowSo = new SerializedObject(flow);
        SerializedProperty roots = flowSo.FindProperty("titleHiddenRendererRoots");
        roots.arraySize = parts.Count;
        for (int i = 0; i < parts.Count; i++) roots.GetArrayElementAtIndex(i).objectReferenceValue = parts[i];
        flowSo.ApplyModifiedProperties();
        log.Add($"globe hidden at title ({parts.Count} parts)");

        // ---- Title near the player, letters to the workstation ----
        var bridgeSo = new SerializedObject(bridge);
        bridgeSo.FindProperty("letterDropZone").objectReferenceValue = zone;
        bridgeSo.FindProperty("layout").enumValueIndex = (int)TitleFlowBridge.TitleLayout.NearPlayer;
        bridgeSo.ApplyModifiedProperties();
        log.Add("title near player, letters placed at the workstation");

        // ---- Hint ----
        var director = Find<CW.Guidance.GuidanceDirector>();
        if (director != null)
        {
            var dirSo = new SerializedObject(director);
            SerializedProperty steps = dirSo.FindProperty("steps");
            for (int i = 0; i < steps.arraySize; i++)
            {
                SerializedProperty step = steps.GetArrayElementAtIndex(i);
                if (step.FindPropertyRelative("id").stringValue == "title-letter")
                {
                    step.FindPropertyRelative("text").stringValue = "Place a letter at your workstation to practice.";
                    log.Add("title-letter hint updated");
                }
            }

            dirSo.ApplyModifiedProperties();
        }

        EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);
        if (save) EditorSceneManager.SaveScene(flow.gameObject.scene);
        return string.Join("; ", log);
    }

    static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
}
