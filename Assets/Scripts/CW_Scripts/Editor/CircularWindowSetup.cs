using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// One-shot setup (and removal) for the CircularWindow experiment: the
/// WindowMasked layer, the stencil materials, the URP Render Objects features
/// on both renderers, and the Circular Window object in the open scene.
/// </summary>
public static class CircularWindowSetup
{
    private const string MenuRoot = "Celestial Workshop/Circular Window/";
    private const int PreferredLayerIndex = 6;
    private const int StencilRef = 1;

    private const string OpaqueFeatureName = "Window Masked Opaque";
    private const string TransparentFeatureName = "Window Masked Transparent";
    private const string WindowObjectName = "Circular Window";

    private const string MaskShaderName = "CelestialWorkshop/WindowStencilMask";
    private const string RimShaderName = "CelestialWorkshop/WindowRim";
    private const string MaskMaterialPath = "Assets/Materials/CW_Materials/WindowStencilMask.mat";
    private const string RimMaterialPath = "Assets/Materials/CW_Materials/WindowRim.mat";

    private const string PunchOutObjectName = "Passthrough Punch-Out";
    private const string PunchOutShaderName = "CelestialWorkshop/PassthroughPunchOut";
    private const string PunchOutMaterialPath = "Assets/Materials/CW_Materials/PassthroughPunchOut.mat";

    private static readonly string[] RendererPaths =
    {
        "Assets/Settings/Mobile_Renderer.asset",
        "Assets/Settings/PC_Renderer.asset"
    };

    [MenuItem(MenuRoot + "Setup (masks selected objects)")]
    private static void SetupFromSelection()
    {
        Transform[] targets = Selection.transforms;
        if (targets.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Circular Window",
                "Select the objects that should only be visible through the window " +
                "(e.g. the sky constellation container and the tiny land), then run Setup again.",
                "OK"
            );
            return;
        }

        Setup(targets);
    }

    [MenuItem(MenuRoot + "Remove")]
    public static void Remove()
    {
        int layer = LayerMask.NameToLayer(CircularWindow.MaskedLayerName);

        foreach (CircularWindow window in Object.FindObjectsByType<CircularWindow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (layer >= 0)
            {
                foreach (Transform root in window.MaskedRoots.Where(root => root != null))
                {
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (child.gameObject.layer == layer)
                        {
                            Undo.RecordObject(child.gameObject, "Remove Circular Window");
                            child.gameObject.layer = 0;
                        }
                    }
                }
            }

            RemovePassthrough(window);
            Undo.DestroyObjectImmediate(window.gameObject);
        }

        foreach (UniversalRendererData rendererData in LoadRenderers())
        {
            foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures
                .Where(IsWindowFeature)
                .ToList())
            {
                rendererData.rendererFeatures.Remove(feature);
                AssetDatabase.RemoveObjectFromAsset(feature);
                Object.DestroyImmediate(feature, true);
            }

            SyncFeatureMap(rendererData);

            if (layer >= 0)
            {
                rendererData.opaqueLayerMask |= 1 << layer;
                rendererData.transparentLayerMask |= 1 << layer;
            }

            rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("CircularWindow: removed. The WindowMasked layer and materials are left in place.");
    }

    /// <summary>
    /// Entry point for menu use and for the Unity CLI (run_script). Safe to re-run.
    /// </summary>
    public static CircularWindow Setup(IReadOnlyList<Transform> targets)
    {
        int layer = EnsureLayer();
        if (layer < 0)
        {
            return null;
        }

        Material maskMaterial = EnsureMaterial(MaskMaterialPath, MaskShaderName);
        Material rimMaterial = EnsureMaterial(RimMaterialPath, RimShaderName);
        if (maskMaterial == null || rimMaterial == null)
        {
            return null;
        }

        foreach (UniversalRendererData rendererData in LoadRenderers())
        {
            ConfigureRenderer(rendererData, layer);
        }

        AssetDatabase.SaveAssets();

        CircularWindow window = EnsureWindowObject(maskMaterial, rimMaterial);
        AssignTargets(window, targets, layer);

        Camera viewer = Camera.main;
        if (viewer != null && window.FitToMaskedContent(viewer.transform.position))
        {
            Debug.Log(
                $"CircularWindow: placed at {window.transform.position}, radius {window.Radius:F2}. " +
                "Runtime-generated content (the sky constellation) isn't counted in Edit mode; " +
                "use the component's 'Fit To Masked Content' context menu in Play mode to refit.",
                window
            );
        }

        Selection.activeGameObject = window.gameObject;
        return window;
    }

    [MenuItem(MenuRoot + "Setup Passthrough")]
    private static void SetupPassthroughMenu()
    {
        SetupPassthrough();
    }

    /// <summary>
    /// Adds what the Passthrough surround needs: the camera-centered punch-out
    /// sphere and a disabled OVRPassthroughLayer on the camera rig, wired into
    /// the CircularWindow. Leaves the surround on VRSky. Safe to re-run.
    /// </summary>
    public static bool SetupPassthrough()
    {
        CircularWindow window = Object.FindFirstObjectByType<CircularWindow>(FindObjectsInactive.Include);
        if (window == null)
        {
            Debug.LogError("CircularWindow: run Setup first.");
            return false;
        }

        var serialized = new SerializedObject(window);
        var viewer = serialized.FindProperty("targetCamera").objectReferenceValue as Camera;
        if (viewer == null)
        {
            viewer = Camera.main;
            serialized.FindProperty("targetCamera").objectReferenceValue = viewer;
        }

        var rig = Object.FindFirstObjectByType<OVRCameraRig>(FindObjectsInactive.Include);
        if (viewer == null || rig == null)
        {
            Debug.LogError($"CircularWindow: missing camera ({viewer != null}) or OVRCameraRig ({rig != null}).");
            return false;
        }

        Material punchOutMaterial = EnsureMaterial(PunchOutMaterialPath, PunchOutShaderName);
        if (punchOutMaterial == null)
        {
            return false;
        }

        SerializedProperty punchOutProperty = serialized.FindProperty("punchOut");
        var punchOut = punchOutProperty.objectReferenceValue as GameObject;
        if (punchOut == null)
        {
            punchOut = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            punchOut.name = PunchOutObjectName;
            Object.DestroyImmediate(punchOut.GetComponent<Collider>());
            punchOut.transform.SetParent(viewer.transform, false);
            ConfigureEffectRenderer(punchOut.GetComponent<MeshRenderer>(), punchOutMaterial);
            punchOut.SetActive(false);
            Undo.RegisterCreatedObjectUndo(punchOut, "Create Passthrough Punch-Out");
            punchOutProperty.objectReferenceValue = punchOut;
        }

        SerializedProperty layerProperty = serialized.FindProperty("passthroughLayer");
        var passthroughLayer = layerProperty.objectReferenceValue as OVRPassthroughLayer;
        if (passthroughLayer == null)
        {
            passthroughLayer = rig.GetComponent<OVRPassthroughLayer>();
            if (passthroughLayer == null)
            {
                passthroughLayer = Undo.AddComponent<OVRPassthroughLayer>(rig.gameObject);
            }

            passthroughLayer.enabled = false;
            layerProperty.objectReferenceValue = passthroughLayer;
        }

        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(window);
        Debug.Log(
            "CircularWindow: passthrough ready. Set Surround to Passthrough, or press P in Play mode to toggle.",
            window
        );
        return true;
    }

    private static void RemovePassthrough(CircularWindow window)
    {
        var serialized = new SerializedObject(window);

        if (serialized.FindProperty("punchOut").objectReferenceValue is GameObject punchOut)
        {
            Undo.DestroyObjectImmediate(punchOut);
        }

        if (serialized.FindProperty("passthroughLayer").objectReferenceValue is OVRPassthroughLayer passthroughLayer)
        {
            Undo.DestroyObjectImmediate(passthroughLayer);
        }
    }

    private static int EnsureLayer()
    {
        int existing = LayerMask.NameToLayer(CircularWindow.MaskedLayerName);
        if (existing >= 0)
        {
            return existing;
        }

        var tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]
        );
        SerializedProperty layers = tagManager.FindProperty("layers");

        int index = string.IsNullOrEmpty(layers.GetArrayElementAtIndex(PreferredLayerIndex).stringValue)
            ? PreferredLayerIndex
            : -1;

        for (int i = 8; index < 0 && i < layers.arraySize; i++)
        {
            if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
            {
                index = i;
            }
        }

        if (index < 0)
        {
            Debug.LogError("CircularWindow: no free layer slot for WindowMasked.");
            return -1;
        }

        layers.GetArrayElementAtIndex(index).stringValue = CircularWindow.MaskedLayerName;
        tagManager.ApplyModifiedPropertiesWithoutUndo();
        return index;
    }

    private static Material EnsureMaterial(string path, string shaderName)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"CircularWindow: shader '{shaderName}' not found.");
            return null;
        }

        material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static IEnumerable<UniversalRendererData> LoadRenderers()
    {
        foreach (string path in RendererPaths)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (rendererData == null)
            {
                Debug.LogWarning($"CircularWindow: renderer not found at {path}.");
                continue;
            }

            yield return rendererData;
        }
    }

    private static void ConfigureRenderer(UniversalRendererData rendererData, int layer)
    {
        int layerBit = 1 << layer;
        rendererData.opaqueLayerMask &= ~layerBit;
        rendererData.transparentLayerMask &= ~layerBit;

        EnsureFeature(rendererData, OpaqueFeatureName, RenderPassEvent.AfterRenderingOpaques, RenderQueueType.Opaque, layerBit);
        EnsureFeature(rendererData, TransparentFeatureName, RenderPassEvent.AfterRenderingTransparents, RenderQueueType.Transparent, layerBit);

        SyncFeatureMap(rendererData);
        rendererData.SetDirty();
        EditorUtility.SetDirty(rendererData);
    }

    private static void EnsureFeature(
        UniversalRendererData rendererData,
        string featureName,
        RenderPassEvent passEvent,
        RenderQueueType queue,
        int layerMask)
    {
        var feature = rendererData.rendererFeatures.FirstOrDefault(f => f != null && f.name == featureName) as RenderObjects;
        if (feature == null)
        {
            feature = ScriptableObject.CreateInstance<RenderObjects>();
            feature.name = featureName;
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            rendererData.rendererFeatures.Add(feature);
        }

        RenderObjects.RenderObjectsSettings settings = feature.settings;
        settings.passTag = featureName;
        settings.Event = passEvent;
        settings.filterSettings.RenderQueueType = queue;
        settings.filterSettings.LayerMask = layerMask;
        settings.stencilSettings.overrideStencilState = true;
        settings.stencilSettings.stencilReference = StencilRef;
        settings.stencilSettings.stencilCompareFunction = CompareFunction.Equal;
        settings.stencilSettings.passOperation = StencilOp.Keep;
        settings.stencilSettings.failOperation = StencilOp.Keep;
        settings.stencilSettings.zFailOperation = StencilOp.Keep;
        feature.Create();
        EditorUtility.SetDirty(feature);
    }

    private static bool IsWindowFeature(ScriptableRendererFeature feature)
    {
        return feature != null && (feature.name == OpaqueFeatureName || feature.name == TransparentFeatureName);
    }

    private static void SyncFeatureMap(UniversalRendererData rendererData)
    {
        var serialized = new SerializedObject(rendererData);
        serialized.Update();

        SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
        SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
        map.arraySize = features.arraySize;

        for (int i = 0; i < features.arraySize; i++)
        {
            Object feature = features.GetArrayElementAtIndex(i).objectReferenceValue;
            long localId = 0;
            if (feature != null)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out localId);
            }

            map.GetArrayElementAtIndex(i).longValue = localId;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static CircularWindow EnsureWindowObject(Material maskMaterial, Material rimMaterial)
    {
        CircularWindow window = Object.FindFirstObjectByType<CircularWindow>(FindObjectsInactive.Include);
        if (window != null)
        {
            return window;
        }

        var root = new GameObject(WindowObjectName);
        Undo.RegisterCreatedObjectUndo(root, "Create Circular Window");
        window = root.AddComponent<CircularWindow>();

        GameObject mask = CreateQuad("Mask", root.transform, maskMaterial);
        GameObject rim = CreateQuad("Rim", root.transform, rimMaterial);

        var serialized = new SerializedObject(window);
        serialized.FindProperty("maskQuad").objectReferenceValue = mask.transform;
        serialized.FindProperty("rimRenderer").objectReferenceValue = rim.GetComponent<Renderer>();
        serialized.FindProperty("targetCamera").objectReferenceValue = Camera.main;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        window.ApplyShape();
        return window;
    }

    private static GameObject CreateQuad(string name, Transform parent, Material material)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        ConfigureEffectRenderer(quad.GetComponent<MeshRenderer>(), material);
        return quad;
    }

    private static void ConfigureEffectRenderer(MeshRenderer effectRenderer, Material material)
    {
        effectRenderer.sharedMaterial = material;
        effectRenderer.shadowCastingMode = ShadowCastingMode.Off;
        effectRenderer.receiveShadows = false;
        effectRenderer.lightProbeUsage = LightProbeUsage.Off;
        effectRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private static void AssignTargets(CircularWindow window, IReadOnlyList<Transform> targets, int layer)
    {
        var serialized = new SerializedObject(window);
        SerializedProperty roots = serialized.FindProperty("maskedRoots");

        var existing = new HashSet<Object>();
        for (int i = 0; i < roots.arraySize; i++)
        {
            existing.Add(roots.GetArrayElementAtIndex(i).objectReferenceValue);
        }

        foreach (Transform target in targets.Where(t => t != null && !existing.Contains(t)))
        {
            roots.arraySize++;
            roots.GetArrayElementAtIndex(roots.arraySize - 1).objectReferenceValue = target;
        }

        serialized.ApplyModifiedProperties();

        foreach (Transform target in targets.Where(t => t != null))
        {
            foreach (Transform child in target.GetComponentsInChildren<Transform>(true))
            {
                Undo.RecordObject(child.gameObject, "Assign WindowMasked Layer");
                child.gameObject.layer = layer;
            }
        }
    }
}
