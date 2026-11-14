using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CW.Onboarding.EditorTools
{
    /// <summary>
    /// Adds the noticeboard to the open scene (once) and wires it to the scene:
    /// GameFlow, the player camera, the sky volume, and the same hands and
    /// synthetic hands ConstellationSigning uses.
    /// Tools > Celestial Workshop > Hook Up Workshop Noticeboard.
    /// </summary>
    public static class NoticeboardSceneHookup
    {
        [MenuItem("Tools/Celestial Workshop/Hook Up Workshop Noticeboard")]
        public static void HookUpMenu()
        {
            Debug.Log(HookUp(true));
        }

        public static string HookUp(bool save)
        {
            var log = new System.Text.StringBuilder();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NoticeboardBuilder.PrefabPath);
            if (prefab == null)
            {
                return "Build the noticeboard prefab first.";
            }

            var gameFlow = Object.FindFirstObjectByType<GameFlow>(FindObjectsInactive.Include);
            var bridge = Object.FindFirstObjectByType<TitleFlowBridge>(FindObjectsInactive.Include);
            var signing = Object.FindFirstObjectByType<ConstellationSigning>(FindObjectsInactive.Include);
            var skyProxy = Object.FindFirstObjectByType<NearFieldSkyProxy>(FindObjectsInactive.Include);
            if (gameFlow == null || signing == null)
            {
                return "No GameFlow or ConstellationSigning in the open scene.";
            }

            var onboarding = Object.FindFirstObjectByType<WorkshopOnboarding>(FindObjectsInactive.Include);
            if (onboarding == null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, gameFlow.gameObject.scene);
                go.name = "Workshop Noticeboard";
                onboarding = go.GetComponent<WorkshopOnboarding>();
                log.AppendLine("Added the Workshop Noticeboard prefab.");
            }

            // Edit-mode spot in front of the rig's eye (runtime placement replaces it).
            var bridgeSo = bridge != null ? new SerializedObject(bridge) : null;
            var viewer = bridgeSo?.FindProperty("viewer").objectReferenceValue as Camera;
            var volume = bridgeSo?.FindProperty("volume").objectReferenceValue as CircularWindow;
            if (viewer != null)
            {
                Transform eye = viewer.transform;
                Vector3 fwd = Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;
                onboarding.transform.SetPositionAndRotation(eye.position + fwd * 0.55f + Vector3.down * 0.12f, Quaternion.LookRotation(fwd, Vector3.up));
            }

            var so = new SerializedObject(onboarding);
            so.FindProperty("gameFlow").objectReferenceValue = gameFlow;
            so.FindProperty("viewer").objectReferenceValue = viewer;
            so.FindProperty("skyProxy").objectReferenceValue = skyProxy;
            so.FindProperty("volume").objectReferenceValue = volume;
            // The sky globe stays hidden until the board is signed.
            SerializedProperty hide = so.FindProperty("hideWhileShowing");
            hide.arraySize = volume != null ? 1 : 0;
            if (volume != null)
            {
                hide.GetArrayElementAtIndex(0).objectReferenceValue = volume.transform;
            }

            so.FindProperty("thenOpen").enumValueIndex = (int)WorkshopOnboarding.Then.Title;
            so.ApplyModifiedProperties();
            log.AppendLine($"Onboarding: gameFlow {gameFlow.name}, viewer {(viewer ? viewer.name : "none")}, sky proxy {(skyProxy ? skyProxy.name : "none")}, volume {(volume ? volume.name : "none")}.");

            // Same hands as the constellation signing.
            var signingSo = new SerializedObject(signing);
            var reveal = onboarding.GetComponentInChildren<SignatureReveal>(true);
            var revealSo = new SerializedObject(reveal);
            SerializedProperty src = signingSo.FindProperty("hands");
            SerializedProperty dst = revealSo.FindProperty("hands");
            dst.arraySize = src.arraySize;
            for (int i = 0; i < src.arraySize; i++)
            {
                dst.GetArrayElementAtIndex(i).objectReferenceValue = src.GetArrayElementAtIndex(i).objectReferenceValue;
            }

            revealSo.FindProperty("leftSyntheticHand").objectReferenceValue = signingSo.FindProperty("leftSyntheticHand").objectReferenceValue;
            revealSo.FindProperty("rightSyntheticHand").objectReferenceValue = signingSo.FindProperty("rightSyntheticHand").objectReferenceValue;
            revealSo.ApplyModifiedProperties();
            log.AppendLine($"Signature: {dst.arraySize} hand(s) and both synthetic hands copied from ConstellationSigning.");

            var flowSo = new SerializedObject(gameFlow);
            flowSo.FindProperty("onboarding").objectReferenceValue = onboarding;
            flowSo.ApplyModifiedProperties();
            log.AppendLine("GameFlow waits for the noticeboard at launch.");

            EditorSceneManager.MarkSceneDirty(gameFlow.gameObject.scene);
            if (save)
            {
                EditorSceneManager.SaveScene(gameFlow.gameObject.scene);
                log.AppendLine("Scene saved.");
            }

            return log.ToString();
        }
    }
}
