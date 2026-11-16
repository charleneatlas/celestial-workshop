using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Celestial Workshop > Hook Up Chart Ghost Hand.
/// 1. Builds Assets/CW_Guidance/ChartPointingHand.prefab from the old
///    blueprint's tutorial hand (its pointing pose is baked into the bones),
///    without the hand-tracking visual, Animator or property-block editors,
///    so nothing drives or recolours it.
/// 2. Adds a ChartGhostHand next to the rotate demo's GuidanceGhostHand and
///    wires it (director, Star Chart, prefab, GhostHand material, viewer).
/// Safe to run again.
/// </summary>
public static class ChartGhostHandHookup
{
    const string PrefabPath = "Assets/CW_Guidance/ChartPointingHand.prefab";
    const string GhostMaterialPath = "Assets/CW_Onboarding/Materials/GhostHand.mat";

    [MenuItem("Tools/Celestial Workshop/Hook Up Chart Ghost Hand")]
    static void HookUpMenu() => Debug.Log("ChartGhostHandHookup: " + HookUp(true));

    public static string HookUp(bool save)
    {
        // ---- The old pointing hand ----
        BlueprintController old = Object.FindObjectsByType<BlueprintController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(b => b.name == "Blueprint");
        GameObject source = old != null
            ? (GameObject)typeof(BlueprintController).GetField("tutorialHandVisual", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(old)
            : null;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (source != null)
        {
            GameObject copy = Object.Instantiate(source);
            copy.name = "ChartPointingHand";
            copy.SetActive(true);
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            copy.transform.localScale = Vector3.one;
            foreach (MonoBehaviour m in copy.GetComponentsInChildren<MonoBehaviour>(true))
            {
                string type = m.GetType().Name;
                if (type == "HandVisual" || type == "MaterialPropertyBlockEditor")
                {
                    Object.DestroyImmediate(m);
                }
            }

            foreach (Animator a in copy.GetComponentsInChildren<Animator>(true))
            {
                Object.DestroyImmediate(a);
            }

            prefab = PrefabUtility.SaveAsPrefabAsset(copy, PrefabPath);
            Object.DestroyImmediate(copy);
        }

        if (prefab == null)
        {
            return "no pointing hand: the old Blueprint's tutorial hand wasn't found and no prefab exists";
        }

        // ---- The component, next to the rotate demo ----
        GuidanceGhostHand rotateDemo = Object.FindFirstObjectByType<GuidanceGhostHand>(FindObjectsInactive.Include);
        if (rotateDemo == null)
        {
            return "no GuidanceGhostHand to sit next to";
        }

        ChartGhostHand demo = rotateDemo.GetComponent<ChartGhostHand>();
        if (demo == null)
        {
            demo = Undo.AddComponent<ChartGhostHand>(rotateDemo.gameObject);
        }

        var rotateSo = new SerializedObject(rotateDemo);
        var so = new SerializedObject(demo);
        so.FindProperty("director").objectReferenceValue = rotateSo.FindProperty("director").objectReferenceValue;
        so.FindProperty("viewer").objectReferenceValue = rotateSo.FindProperty("viewer").objectReferenceValue;
        so.FindProperty("chart").objectReferenceValue = Object.FindFirstObjectByType<StarChartLayout>(FindObjectsInactive.Include);
        so.FindProperty("pointingHandPrefab").objectReferenceValue = prefab;
        so.FindProperty("ghostMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(rotateDemo.gameObject.scene);
        if (save)
        {
            EditorSceneManager.SaveScene(rotateDemo.gameObject.scene);
        }

        return $"prefab {PrefabPath}; ChartGhostHand on '{rotateDemo.name}'";
    }
}
