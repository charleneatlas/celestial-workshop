using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Celestial Workshop > Build Title Preview
///
/// Builds Assets/CW_Title/TitlePreview.unity (camera, title shelf, stand-in
/// sphere drop zone, TitlePreviewDriver) in a NEW ADDITIVE scene, saves and
/// closes it, so the open production scene is never replaced. Also saves the
/// shelf as Assets/CW_Title/ConstellationTitle.prefab. Safe to run again: the
/// scene and prefab are overwritten (the prefab keeps its GUID).
///
/// "Title Preview: Full Title" toggles between just "C" (first proof) and the
/// full "CELESTIAL" / "WORKSHOP".
/// </summary>
public static class TitlePreviewBuilder
{
    private const string Folder = "Assets/CW_Title";
    private const string ScenePath = Folder + "/TitlePreview.unity";
    private const string PrefabPath = Folder + "/ConstellationTitle.prefab";

    private const string BuildMenu = "Tools/Celestial Workshop/Build Title Preview";
    private const string FullTitleMenu = "Tools/Celestial Workshop/Title Preview: Full Title";
    private const string FullTitlePref = "CW_Title.FullTitle";

    // Materials used by ConstellationSetup.tableConstellationAppearance in
    // CelestialWorkshop_VRGlasses_Prototype.unity.
    private const string MiniStarMaterialGuid = "023920514358d364296c66663a632662";        // MiniConstellation_Mat
    private const string MiniHighlightMaterialGuid = "27c28ca570396de4d99d4ac4cad0fcc1";   // MiniConstellation_Highlight_Mat
    private const string GlassMaterialGuid = "82263702bf1dec94f92abdb7a97ace9e";           // StarGimbalRing (white, 4% alpha)

    private static readonly Vector3 EyePosition = new Vector3(0f, 1.4f, 0f);
    private const float DropZoneRadius = 0.12f;

    [MenuItem(FullTitleMenu, false, 2001)]
    private static void ToggleFullTitle()
    {
        EditorPrefs.SetBool(FullTitlePref, !EditorPrefs.GetBool(FullTitlePref, false));
    }

    [MenuItem(FullTitleMenu, true)]
    private static bool ToggleFullTitleValidate()
    {
        Menu.SetChecked(FullTitleMenu, EditorPrefs.GetBool(FullTitlePref, false));
        return true;
    }

    [MenuItem(BuildMenu, false, 2000)]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("TitlePreviewBuilder: exit Play mode first.");
            return;
        }

        bool fullTitle = EditorPrefs.GetBool(FullTitlePref, false);

        if (!AssetDatabase.IsValidFolder(Folder))
        {
            AssetDatabase.CreateFolder("Assets", "CW_Title");
        }

        // If the preview scene is already open, close it first (unless it is
        // the only open scene: then there is nothing to switch to).
        Scene alreadyOpen = SceneManager.GetSceneByPath(ScenePath);
        if (alreadyOpen.IsValid() && alreadyOpen.isLoaded)
        {
            if (SceneManager.sceneCount <= 1)
            {
                Debug.LogError("TitlePreviewBuilder: TitlePreview.unity is the only open scene. Open another scene and run again.");
                return;
            }

            EditorSceneManager.CloseScene(alreadyOpen, true);
        }

        Scene previousActive = SceneManager.GetActiveScene();
        Scene preview = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);

        try
        {
            // New objects go into the active scene: make it the preview.
            SceneManager.SetActiveScene(preview);
            Populate(preview, fullTitle);

            if (!EditorSceneManager.SaveScene(preview, ScenePath))
            {
                Debug.LogError("TitlePreviewBuilder: could not save " + ScenePath);
            }
        }
        finally
        {
            if (previousActive.IsValid() && previousActive.isLoaded)
            {
                SceneManager.SetActiveScene(previousActive);
            }

            EditorSceneManager.CloseScene(preview, true);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"TitlePreviewBuilder: saved {ScenePath} and {PrefabPath} ({(fullTitle ? "full title" : "\"C\" only")}).");
    }

    private static void Populate(Scene scene, bool fullTitle)
    {
        Material star = LoadMaterial(MiniStarMaterialGuid);
        Material highlight = LoadMaterial(MiniHighlightMaterialGuid);
        Material glass = LoadMaterial(GlassMaterialGuid);

        // Camera (from DefaultGameObjects) at the eye, looking down +Z.
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Camera cam = root.GetComponent<Camera>();
            if (cam != null)
            {
                cam.transform.SetPositionAndRotation(EyePosition, Quaternion.identity);
                cam.nearClipPlane = 0.01f;
                cam.fieldOfView = 40f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.02f, 0.03f, 0.07f, 1f);
            }
        }

        // Title shelf: +Z away from the camera, at the viewing distance.
        GameObject shelfObject = new GameObject("Constellation Title");
        TitleShelf shelf = shelfObject.AddComponent<TitleShelf>();
        shelfObject.transform.SetPositionAndRotation(
            EyePosition + new Vector3(0f, 0.04f, shelf.ViewingDistance),
            Quaternion.identity);

        shelf.Rows = fullTitle ? new[] { "CELESTIAL", "WORKSHOP" } : new[] { "C" };
        shelf.Appearance = new ConstellationGenerator.AppearanceSettings
        {
            starMaterial = star,
            starHighlightMaterial = highlight,
            lineMaterial = star,
            starDiameter = 0.05f,
            lineWidth = 0.008f,
        };
        shelf.RebuildLetters();

        GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(
            shelfObject, PrefabPath, InteractionMode.AutomatedAction);
        if (prefab == null)
        {
            Debug.LogError("TitlePreviewBuilder: could not save " + PrefabPath);
        }

        // Stand-in for the puzzle sphere, below and in front of the title.
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Stand-in Sphere (Drop Zone)";
        sphere.transform.position = EyePosition + new Vector3(0f, -0.25f, 0.5f);
        sphere.transform.localScale = Vector3.one * DropZoneRadius * 2f;
        Object.DestroyImmediate(sphere.GetComponent<Collider>());
        if (glass != null)
        {
            sphere.GetComponent<Renderer>().sharedMaterial = glass;
        }

        SphereDropZone zone = sphere.AddComponent<SphereDropZone>();
        zone.Radius = DropZoneRadius;

        // Driver: logs events, keys 1..9 simulate placements.
        GameObject driverObject = new GameObject("Title Preview Driver");
        TitlePreviewDriver driver = driverObject.AddComponent<TitlePreviewDriver>();
        driver.shelf = shelf;
        driver.dropZone = zone;

        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static Material LoadMaterial(string guid)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        Material material = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Debug.LogWarning($"TitlePreviewBuilder: material {guid} not found; using defaults.");
        }

        return material;
    }
}
