using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Celestial Workshop > Hook Up Nov 16 Playtest Fixes.
/// Scene settings for the playtest fixes (the behaviour lives in the scripts):
///   - Title-letter hint: "Pick up a letter to practice."
///   - Signing: the noticeboard's gold-glow name material and gold face for the
///     certificate and the sphere copy; a second star trail follows the sphere copy.
///   - Practice completion labels: Try Another Letter / Enter the Workshop.
///   - The practice placement sound uses the same "begin" clip as Start
///     (the old impact clip read as a coin).
///   - The Earth View Camera is switched off at the title (its display is hidden there).
///   - Pinch-rejection logging off (it logged on every pinch, title letters included),
///     the Star Chart's per-star logging off, and the hidden per-star debug labels off.
///   - The rotate ghost hand demonstrates at the workstation, along the turn guide ring, left to right.
/// Safe to run again.
/// </summary>
public static class PlaytestFixesHookup
{
    const string SignatureGold = "Assets/CW_Onboarding/Materials/Signature_Gold.mat";
    const string BeginClip = "Assets/Audio/CW_Audio/ISDK/Interaction_Locomotion_Teleport.wav";
    static readonly Color SignatureFace = new Color(1f, 0.86f, 0.55f, 1f);   // the noticeboard's signature gold

    [MenuItem("Tools/Celestial Workshop/Hook Up Nov 16 Playtest Fixes")]
    static void HookUpMenu() => Debug.Log("PlaytestFixesHookup: " + HookUp(true));

    public static string HookUp(bool save)
    {
        var log = new List<string>();
        var flow = Find<GameFlow>();
        var signing = Find<ConstellationSigning>();
        if (flow == null || signing == null) return "missing GameFlow / ConstellationSigning";

        // ---- Hints ----
        var director = Find<CW.Guidance.GuidanceDirector>();
        if (director != null)
        {
            var so = new SerializedObject(director);
            SerializedProperty steps = so.FindProperty("steps");
            for (int i = 0; i < steps.arraySize; i++)
            {
                SerializedProperty step = steps.GetArrayElementAtIndex(i);
                if (step.FindPropertyRelative("id").stringValue == "title-letter")
                {
                    step.FindPropertyRelative("text").stringValue = "Pick up a letter to practice.";
                    log.Add("title hint");
                }
            }

            so.ApplyModifiedProperties();
        }

        var hooks = Find<GuidanceHooks>();
        if (hooks != null)
        {
            Set(hooks, "practiceMenuText", p => p.stringValue = "Try another letter, or enter the workshop");
            Set(hooks, "postPuzzleMenu", p => p.objectReferenceValue = Find<PostPuzzleMenu>());
        }

        // ---- Signing look ----
        var gold = AssetDatabase.LoadAssetAtPath<Material>(SignatureGold);
        var sso = new SerializedObject(signing);
        sso.FindProperty("nameMaterial").objectReferenceValue = gold;
        sso.FindProperty("textColor").colorValue = SignatureFace;
        sso.FindProperty("gameFlow").objectReferenceValue = flow;
        sso.FindProperty("signInPractice").boolValue = false;
        sso.FindProperty("revealInSphereToo").boolValue = true;
        sso.ApplyModifiedProperties();
        log.Add(gold != null ? "gold name material" : "Signature_Gold NOT found");

        // ---- Second star trail on the sphere copy ----
        SigningStarTrail lineTrail = null;
        bool hasSphereTrail = false;
        foreach (var t in Object.FindObjectsByType<SigningStarTrail>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (new SerializedObject(t).FindProperty("followSphereCopy").boolValue) hasSphereTrail = true;
            else lineTrail = t;
        }

        if (!hasSphereTrail && lineTrail != null)
        {
            GameObject copy = Object.Instantiate(lineTrail.gameObject, lineTrail.transform.parent);
            copy.name = lineTrail.gameObject.name + " (Sphere)";
            Undo.RegisterCreatedObjectUndo(copy, "Sphere star trail");
            // Components on the copied object keep their references to each other (remapped by Instantiate).
            Set(copy.GetComponent<SigningStarTrail>(), "followSphereCopy", p => p.boolValue = true);
            log.Add("sphere star trail added");
        }
        else
        {
            log.Add(hasSphereTrail ? "sphere star trail present" : "no SigningStarTrail found");
        }

        // ---- Practice completion labels ----
        var menu = Find<PostPuzzleMenu>();
        if (menu != null)
        {
            Set(menu, "practiceAnotherText", p => p.stringValue = "Try Another Letter");
            Set(menu, "practiceWorkshopText", p => p.stringValue = "Enter the Workshop");
            log.Add("practice menu labels");
        }

        // ---- Practice placement sound ----
        var audio = Find<CW.AudioFeedback.AudioFeedback>();
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(BeginClip);
        if (audio != null && clip != null)
        {
            var aso = new SerializedObject(audio);
            SerializedProperty entries = aso.FindProperty("entries");
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty e = entries.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("cue").enumValueIndex != (int)CW.AudioFeedback.FeedbackCue.Place) continue;
                SerializedProperty variants = e.FindPropertyRelative("variants");
                variants.arraySize = 1;
                variants.GetArrayElementAtIndex(0).objectReferenceValue = clip;
                log.Add("Place cue -> begin clip");
            }

            aso.ApplyModifiedProperties();
        }

        // ---- Performance ----
        var earthView = Find<EarthViewCamera>();
        Camera earthCam = earthView != null ? earthView.GetComponent<Camera>() : null;
        if (earthCam != null)
        {
            var fso = new SerializedObject(flow);
            SerializedProperty list = fso.FindProperty("titleDisabledBehaviours");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = earthCam;
            fso.ApplyModifiedProperties();
            log.Add("Earth View Camera off at the title");
        }

        // The Star Chart logged every star the finger entered and left (two log lines per change).
        foreach (var bp in Object.FindObjectsByType<BlueprintController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Set(bp, "showDebugLogs", p => p.boolValue = false);
        }

        log.Add("Star Chart logs off");

        // Every generated star instantiated a hidden debug text label (115 in a puzzle), all inactive.
        int generators = 0;
        foreach (var gen in Object.FindObjectsByType<ConstellationGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Set(gen, "showDebugLabels", p => p.boolValue = false);
            generators++;
        }

        log.Add($"star debug labels off ({generators} generators)");

        var pinch = Find<VolumePinchRotation>();
        if (pinch != null) Set(pinch, "logPinchRejections", p => p.boolValue = false);

        // ---- Rotate ghost hand at the workstation ----
        var ghost = Find<GuidanceGhostHand>();
        if (ghost != null)
        {
            Set(ghost, "workstation", p => p.objectReferenceValue = Find<WorkstationMini>());
            Set(ghost, "workstationRadiusFactor", p => p.floatValue = 1.1f);   // on the turn guide ring
            Set(ghost, "workstationHeightFraction", p => p.floatValue = 0f);
            log.Add("ghost hand at the workstation");
        }

        EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);
        if (save) EditorSceneManager.SaveScene(flow.gameObject.scene);
        return string.Join("; ", log);
    }

    static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);

    static void Set(Object target, string field, System.Action<SerializedProperty> apply)
    {
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null) { Debug.LogWarning($"PlaytestFixesHookup: {target.GetType().Name}.{field} not found"); return; }
        apply(p);
        so.ApplyModifiedProperties();
    }
}
