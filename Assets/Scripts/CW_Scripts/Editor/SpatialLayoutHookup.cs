using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Celestial Workshop > Hook Up Spatial Layout.
/// The seated-ergonomics layout pass, arranged around the miniature's
/// comfortable spot (the workstation is never moved by any of this):
///   - WorkshopPlacement aligns the eye height too, so the globe and table sit
///     as authored for every seated player (root cause of the low globe/table).
///   - Globe presentation raised a little (NearFieldSkyProxy.presentationHeightOffset).
///   - The box selection is anchored at the placement ring (WorkstationAnchor),
///     its table just under the ring, the boxes and Field Note laid out around it.
///   - The Star Chart sits left of the miniature where it was placed in the headset (WorkstationAnchor).
///   - The Guild Certificate comes a little closer and lower, tilted like a desk.
/// All values stay editable in the Inspector for headset tuning. Safe to run again.
/// </summary>
public static class SpatialLayoutHookup
{
    // Ring-frame layout: x right, y up, z away from the player.
    static readonly Vector3 TableFromRing = new Vector3(0f, -0.009f, 0.365f);
    // Star Chart pose chosen in the headset (placed by hand, 2026-11-16).
    static readonly Vector3 ChartFromMini = new Vector3(-0.211f, -0.074f, 0.016f);
    static readonly Vector3 ChartRotation = new Vector3(313.2f, 348.0f, 347.4f);

    [MenuItem("Tools/Celestial Workshop/Hook Up Spatial Layout")]
    static void HookUpMenu() => Debug.Log("SpatialLayoutHookup: " + HookUp(true));

    public static string HookUp(bool save)
    {
        var log = new List<string>();
        var workstation = Find<WorkstationMini>();
        if (workstation == null) return "no WorkstationMini";

        // ---- Root cause: eye height ----
        var placement = Find<WorkshopPlacement>();
        if (placement != null)
        {
            Set(placement, "alignEyeHeight", p => p.boolValue = true);
            Set(placement, "designEyeHeight", p => p.floatValue = 1.2f);
            log.Add("eye-height align on (1.2 m)");
        }

        // ---- Globe ----
        var proxy = Find<NearFieldSkyProxy>();
        if (proxy != null)
        {
            Set(proxy, "presentationHeightOffset", p => p.floatValue = 0.03f);
            log.Add("globe +0.03 m");
        }

        // ---- Selection table around the ring ----
        var layout = Find<SelectionTableLayout>();
        if (layout != null)
        {
            Undo.RecordObject(layout, "Spatial layout");
            layout.starterPack.position = new Vector3(-0.215f, 0.0405f, -0.425f);
            layout.starterPack.yaw = -14f;
            layout.northernSky.position = new Vector3(-0.17f, 0.0755f, -0.19f);
            layout.northernSky.yaw = -7f;
            layout.southernSky.position = new Vector3(0f, 0.0755f, -0.18f);
            layout.southernSky.yaw = 0f;
            layout.zodiac.position = new Vector3(0.17f, 0.0755f, -0.19f);
            layout.zodiac.yaw = 7f;
            layout.notePosition = new Vector3(0.205f, 0.003f, -0.47f);
            layout.noteYaw = 14f;
            layout.tableCentre = new Vector3(0f, 0f, -0.3025f);
            layout.tableSize = new Vector2(0.66f, 0.405f);
            layout.riserCentre = new Vector3(0f, 0f, -0.185f);
            layout.riserSize = new Vector3(0.54f, 0.035f, 0.14f);
            layout.Apply();
            EditorUtility.SetDirty(layout);
            Anchor(layout.gameObject, "RingCenter", TableFromRing, Vector3.zero, workstation);
            log.Add("table anchored at the ring, boxes and Field Note around it");
        }

        // ---- Star Chart left of the miniature, facing the eyes ----
        var chart = Find<StarChartLayout>();
        if (chart != null)
        {
            Anchor(chart.gameObject, "MiniCenter", ChartFromMini, ChartRotation, workstation);
            log.Add("Star Chart at the headset-chosen spot left of the miniature");
        }

        // ---- Certificate: closer, lower, desk tilt ----
        var cert = Find<GuildCertificate>();
        if (cert != null)
        {
            Set(cert, "offset", p => p.vector3Value = new Vector3(0f, -0.03f, -0.07f));
            Set(cert, "extraTilt", p => p.floatValue = 18f);
            log.Add("certificate closer and lower");
        }

        EditorSceneManager.MarkSceneDirty(workstation.gameObject.scene);
        if (save) EditorSceneManager.SaveScene(workstation.gameObject.scene);
        return string.Join("; ", log);
    }

    static void Anchor(GameObject go, string reference, Vector3 offset, Vector3 rotation, WorkstationMini workstation)
    {
        var anchor = go.GetComponent<WorkstationAnchor>();
        if (anchor == null) anchor = Undo.AddComponent<WorkstationAnchor>(go);
        var so = new SerializedObject(anchor);
        so.FindProperty("workstation").objectReferenceValue = workstation;
        SerializedProperty r = so.FindProperty("reference");
        r.enumValueIndex = System.Array.IndexOf(r.enumNames, reference);
        so.FindProperty("offset").vector3Value = offset;
        so.FindProperty("rotation").vector3Value = rotation;
        so.ApplyModifiedProperties();
    }

    static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);

    static void Set(Object target, string field, System.Action<SerializedProperty> apply)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"SpatialLayoutHookup: {target.GetType().Name}.{field} not found"); return; }
        apply(p);
        so.ApplyModifiedProperties();
    }
}
