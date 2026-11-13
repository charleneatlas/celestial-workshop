using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CW.Guidance.Editor
{
    /// <summary>
    /// Tools/Celestial Workshop/Build Guidance Preview: (re)builds
    /// Assets/CW_Guidance/Guidance.prefab and Assets/CW_Guidance/GuidancePreview.unity.
    /// The scene is created additively, saved and closed; the open production
    /// scene is never replaced or modified. Safe to run repeatedly.
    /// </summary>
    public static class GuidancePreviewBuilder
    {
        private const string Folder = "Assets/CW_Guidance";
        private const string ScenePath = Folder + "/GuidancePreview.unity";
        private const string PrefabPath = Folder + "/Guidance.prefab";
        private const string FontPath = "Assets/CW_Art/Fonts/Caveat SDF.asset";
        private const string PulseShaderName = "Universal Render Pipeline/Unlit";

        [MenuItem("Tools/Celestial Workshop/Build Guidance Preview")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[Guidance] Exit Play mode before building the guidance preview.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", "CW_Guidance");
            }

            // An earlier preview scene that is still open: close it first.
            Scene existing = SceneManager.GetSceneByPath(ScenePath);
            if (existing.IsValid() && existing.isLoaded)
            {
                if (SceneManager.sceneCount <= 1)
                {
                    Debug.LogError("[Guidance] GuidancePreview.unity is the only open scene; open another scene, then rebuild.");
                    return;
                }

                EditorSceneManager.CloseScene(existing, true);
            }

            // Unity refuses an additive new scene while an untitled scene is open.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                {
                    Debug.LogError("[Guidance] Save or close the untitled scene first.");
                    return;
                }
            }

            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            try
            {
                // New objects go to the active scene: make it the preview, so
                // the production scene is never touched.
                SceneManager.SetActiveScene(scene);

                GameObject prefab = BuildPrefab();
                if (prefab == null)
                {
                    return;
                }

                Populate(scene, prefab);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                {
                    Debug.LogError("[Guidance] Could not save " + ScenePath);
                    return;
                }

                Debug.Log("[Guidance] Built " + PrefabPath + " and " + ScenePath + ". Open the scene and press Play: Space completes, N shows next, arrows turn the camera.");
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActive);
                }

                EditorSceneManager.CloseScene(scene, true);
            }

            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
        }

        private static GameObject BuildPrefab()
        {
            var root = new GameObject("Guidance");
            try
            {
                GuidanceDirector director = root.AddComponent<GuidanceDirector>();

                var labelObject = new GameObject("HintLabel");
                labelObject.transform.SetParent(root.transform, false);
                HintLabel label = labelObject.AddComponent<HintLabel>();
                label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
                if (label.font == null)
                {
                    Debug.LogWarning("[Guidance] " + FontPath + " not found; the label will use TMP's default font.");
                }

                var pulseObject = new GameObject("TargetPulse");
                pulseObject.transform.SetParent(root.transform, false);
                TargetPulse pulse = pulseObject.AddComponent<TargetPulse>();
                pulse.shader = Shader.Find(PulseShaderName);
                if (pulse.shader == null)
                {
                    Debug.LogWarning("[Guidance] Shader '" + PulseShaderName + "' not found; the pulse will search for a fallback at runtime.");
                }

                director.label = label;
                director.pulse = pulse;

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (!success || saved == null)
                {
                    Debug.LogError("[Guidance] Could not save " + PrefabPath);
                    return null;
                }

                return saved;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Populate(Scene scene, GameObject prefab)
        {
            // Camera from the default objects: eye height, looking +Z, dark sky.
            Camera camera = null;
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                camera = rootObject.GetComponentInChildren<Camera>();
                if (camera != null) break;
            }

            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = cameraObject.AddComponent<Camera>();
            }

            camera.transform.SetPositionAndRotation(new Vector3(0f, 1.4f, 0f), Quaternion.identity);
            camera.nearClipPlane = 0.05f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.02f, 0.03f, 0.06f, 1f);
            camera.fieldOfView = 50f;

            // Three dummy targets about 0.6 m away.
            Transform cube = MakeTarget(PrimitiveType.Cube, "Target Cube", new Vector3(-0.22f, 1.3f, 0.62f), Vector3.one * 0.09f);
            Transform sphere = MakeTarget(PrimitiveType.Sphere, "Target Sphere", new Vector3(0.24f, 1.34f, 0.7f), Vector3.one * 0.14f);
            Transform panel = MakeTarget(PrimitiveType.Cube, "Target Panel", new Vector3(0f, 1.2f, 0.55f), new Vector3(0.18f, 0.11f, 0.01f));
            panel.rotation = Quaternion.Euler(-20f, 0f, 0f);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            GuidanceDirector director = instance.GetComponent<GuidanceDirector>();
            director.viewer = camera.transform;
            director.logSteps = true;
            director.steps = new List<GuidanceStep>
            {
                new GuidanceStep { id = "cube", text = "Pinch a letter, place it in the globe", target = cube, labelOffset = new Vector3(0f, 0.04f, 0f), pulseTarget = true },
                new GuidanceStep { id = "sphere", text = "Pinch inside the globe and turn", target = sphere, labelOffset = new Vector3(0f, 0.04f, 0f), pulseTarget = true },
                new GuidanceStep { id = "panel", text = "Choose another sky", target = panel, labelOffset = new Vector3(0f, 0.03f, 0f), pulseTarget = true, onlyOnce = true },
            };
            PrefabUtility.RecordPrefabInstancePropertyModifications(director);

            var driverObject = new GameObject("Guidance Preview Driver");
            GuidancePreviewDriver driver = driverObject.AddComponent<GuidancePreviewDriver>();
            driver.director = director;
            driver.cameraToTurn = camera.transform;

            // Objects created while the preview is the active scene already live
            // there; move explicitly anyway in case the active scene differs.
            MoveToScene(camera.transform.root.gameObject, scene);
            MoveToScene(cube.gameObject, scene);
            MoveToScene(sphere.gameObject, scene);
            MoveToScene(panel.gameObject, scene);
            MoveToScene(driverObject, scene);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static Transform MakeTarget(PrimitiveType type, string name, Vector3 position, Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            return go.transform;
        }

        private static void MoveToScene(GameObject go, Scene scene)
        {
            if (go.scene != scene)
            {
                SceneManager.MoveGameObjectToScene(go, scene);
            }
        }
    }
}
