using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wires the workstation interaction into the open scene:
///   - "Workstation" under WORKSHOP: WorkstationMini, a drop zone and a ring marker.
///   - ConstellationSetup: the mini is generated as a scaled copy of the sky.
///   - VolumePinchRotation: controls centered on the workstation (no rotating in the globe).
///   - ConstellationIntro: the intro copy starts on the workstation mini.
///   - BoxSelection: sculptures are placed at the workstation.
///   - "Projected Hands": the pinching hand, projected into the sky for the Earth view.
///   - Guidance: boxes-place and rotate hints point at the workstation.
/// Safe to run again: existing objects are reused by name.
/// </summary>
public static class WorkstationSceneHookup
{
    const string MarkerMaterialPath = "Assets/Materials/CW_Materials/WorkstationMarker_Mat.mat";
    const string GuideAdditivePath = "Assets/Materials/CW_Materials/VolumeRotationGuide_Additive_Mat.mat";

    [MenuItem("Tools/Celestial Workshop/Hook Up Workstation")]
    static void HookUpMenu() => Debug.Log("WorkstationSceneHookup: " + HookUp(true));

    public static string HookUp(bool save)
    {
        var log = new List<string>();
        var setup = Find<ConstellationSetup>();
        var pinch = Find<VolumePinchRotation>();
        var intro = Find<ConstellationIntro>();
        var boxes = Find<BoxSelection>();
        var placement = Find<WorkshopPlacement>();
        var solve = Find<ConstellationSolveController>();
        var signing = Find<ConstellationSigning>();
        var proxy = Find<NearFieldSkyProxy>();
        if (setup == null || pinch == null || placement == null || proxy == null)
            return "missing ConstellationSetup / VolumePinchRotation / WorkshopPlacement / NearFieldSkyProxy";

        Camera viewer = (Camera)new SerializedObject(pinch).FindProperty("viewer").objectReferenceValue;
        Transform workshop = placement.transform;

        // ---- Workstation object ----
        Transform station = workshop.Find("Workstation");
        if (station == null)
        {
            station = new GameObject("Workstation").transform;
            Undo.RegisterCreatedObjectUndo(station.gameObject, "Workstation");
            station.SetParent(workshop, false);
            log.Add("created Workstation");
        }

        // Edit-time spot for a nominal seated eye; runtime placement replaces it.
        if (viewer != null)
        {
            OVRCameraRig rig = viewer.GetComponentInParent<OVRCameraRig>();
            Vector3 eye = (rig != null ? rig.transform.position : viewer.transform.position) + Vector3.up * 1.2f;
            Vector3 fwd = Vector3.ProjectOnPlane(proxy.transform.position - eye, Vector3.up).normalized;
            station.SetPositionAndRotation(eye + fwd * 0.4f + Vector3.down * 0.3f, Quaternion.LookRotation(fwd, Vector3.up));
        }

        var mini = GetOrAdd<WorkstationMini>(station);

        Transform zoneT = station.Find("Workstation Drop Zone");
        if (zoneT == null)
        {
            zoneT = new GameObject("Workstation Drop Zone").transform;
            Undo.RegisterCreatedObjectUndo(zoneT.gameObject, "Workstation");
            zoneT.SetParent(station, false);
        }

        var zone = GetOrAdd<SphereDropZone>(zoneT);

        Transform markerT = station.Find("Workstation Marker");
        if (markerT == null)
        {
            markerT = new GameObject("Workstation Marker").transform;
            Undo.RegisterCreatedObjectUndo(markerT.gameObject, "Workstation");
            markerT.SetParent(station, false);
        }

        var markerFilter = GetOrAdd<MeshFilter>(markerT);
        var markerRenderer = GetOrAdd<MeshRenderer>(markerT);
        markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        markerRenderer.receiveShadows = false;
        markerRenderer.sharedMaterial = MarkerMaterial();

        var zoneSo = new SerializedObject(zone);
        zoneSo.FindProperty("center").objectReferenceValue = station;
        zoneSo.FindProperty("radius").floatValue = 0.13f;
        zoneSo.FindProperty("glowRenderer").objectReferenceValue = markerRenderer;
        zoneSo.FindProperty("glowColorProperty").stringValue = "_BaseColor";
        zoneSo.ApplyModifiedProperties();

        var miniSo = new SerializedObject(mini);
        miniSo.FindProperty("setup").objectReferenceValue = setup;
        miniSo.FindProperty("viewer").objectReferenceValue = viewer;
        miniSo.FindProperty("volume").objectReferenceValue = proxy.transform;
        miniSo.FindProperty("placement").objectReferenceValue = placement;
        miniSo.FindProperty("solveController").objectReferenceValue = solve;
        miniSo.FindProperty("signing").objectReferenceValue = signing;
        miniSo.FindProperty("dropZone").objectReferenceValue = zone;
        miniSo.FindProperty("marker").objectReferenceValue = markerFilter;
        miniSo.ApplyModifiedProperties();

        // ---- Existing systems ----
        Set(setup, "miniMatchesSky", so => so.boolValue = true);

        var pinchSo = new SerializedObject(pinch);
        pinchSo.FindProperty("controlCenter").enumValueIndex = (int)VolumePinchRotation.ControlCenterMode.Workstation;
        pinchSo.FindProperty("workstation").objectReferenceValue = mini;
        pinchSo.ApplyModifiedProperties();

        if (intro != null) Set(intro, "workstation", so => so.objectReferenceValue = mini);
        if (boxes != null) Set(boxes, "dropZone", so => so.objectReferenceValue = zone);
        log.Add("wired setup, pinch, intro, boxes");

        // ---- Projected hands ----
        Transform handsT = station.Find("Projected Hands");
        if (handsT == null)
        {
            handsT = new GameObject("Projected Hands").transform;
            Undo.RegisterCreatedObjectUndo(handsT.gameObject, "Workstation");
            handsT.SetParent(station, false);
        }

        var projected = GetOrAdd<ProjectedHands>(handsT);
        // Raw tracked hands first (OVRHandVisualLeft/Right), then the synthetic ones.
        HandVisual[] visuals = Object.FindObjectsByType<HandVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(v => v.GetComponentInParent<OVRCameraRig>() != null)
            .OrderBy(v => v.name.StartsWith("OVRHandVisual") ? 0 : 1)
            .ThenBy(v => v.name)
            .ToArray();
        var handsSo = new SerializedObject(projected);
        handsSo.FindProperty("pinchRotation").objectReferenceValue = pinch;
        handsSo.FindProperty("workstation").objectReferenceValue = mini;
        SerializedProperty list = handsSo.FindProperty("handVisuals");
        list.arraySize = visuals.Length;
        for (int i = 0; i < visuals.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = visuals[i];
        handsSo.ApplyModifiedProperties();
        log.Add($"projected hands from {visuals.Length} rig hand visual(s)");

        // ---- Guidance ----
        var director = Find<CW.Guidance.GuidanceDirector>();
        if (director != null)
        {
            Transform pivot = setup.MiniConstellationPivot;
            var dirSo = new SerializedObject(director);
            SerializedProperty steps = dirSo.FindProperty("steps");
            for (int i = 0; i < steps.arraySize; i++)
            {
                SerializedProperty step = steps.GetArrayElementAtIndex(i);
                string id = step.FindPropertyRelative("id").stringValue;
                if (id == "boxes-place")
                {
                    step.FindPropertyRelative("text").stringValue = "Place a constellation at your workstation.";
                    step.FindPropertyRelative("target").objectReferenceValue = station;
                    step.FindPropertyRelative("pulseAround").objectReferenceValue = markerT;
                    step.FindPropertyRelative("pulseRadius").floatValue = 0.11f;
                }
                else if (id == "rotate")
                {
                    step.FindPropertyRelative("text").stringValue = "Pinch the small constellation and turn it.";
                    step.FindPropertyRelative("target").objectReferenceValue = station;
                    step.FindPropertyRelative("pulseAround").objectReferenceValue = pivot;
                    step.FindPropertyRelative("pulseRadius").floatValue = 0.12f;
                }
            }

            dirSo.ApplyModifiedProperties();
            log.Add("guidance retargeted");
        }

        EditorSceneManager.MarkSceneDirty(station.gameObject.scene);
        if (save) EditorSceneManager.SaveScene(station.gameObject.scene);
        return string.Join("; ", log);
    }

    static Material MarkerMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MarkerMaterialPath);
        if (material != null) return material;
        var source = AssetDatabase.LoadAssetAtPath<Material>(GuideAdditivePath);
        if (source == null) return null;
        material = new Material(source) { name = "WorkstationMarker_Mat" };
        material.SetColor("_BaseColor", new Color(0.55f, 0.75f, 1f, 0.35f));
        AssetDatabase.CreateAsset(material, MarkerMaterialPath);
        return material;
    }

    static T GetOrAdd<T>(Component host) where T : Component
    {
        T existing = host.GetComponent<T>();
        return existing != null ? existing : Undo.AddComponent<T>(host.gameObject);
    }

    static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);

    static void Set(Object target, string field, System.Action<SerializedProperty> apply)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"WorkstationSceneHookup: {target.GetType().Name}.{field} not found"); return; }
        apply(p);
        so.ApplyModifiedProperties();
    }
}
