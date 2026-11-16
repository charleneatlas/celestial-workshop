using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Material-only "hand-carved wood" experiment for the Starter Pack's
/// constellation sculptures and the workstation mini they become (the
/// physical pieces; not the sky stars or the title letters, which share the
/// original MiniConstellation materials).
///
///   Generate Wood Textures  - (re)writes the tileable albedo + normal map.
///   Apply Wood              - creates the shared materials if missing and puts
///                             them on the sculptures (stars, rods, highlight).
///   Restore Original        - puts MiniConstellation_Mat / _Highlight_Mat back.
///
/// Both scene edits are undoable and only touch the sculptures' appearance
/// materials and renderers, and ConstellationSetup's mini appearance
/// (tableConstellationAppearance, used only to generate the workstation mini
/// at runtime); geometry and behaviour are unchanged.
/// Tune the look on SculptureWood_Mat (and _Highlight_Mat for the grab glow).
/// </summary>
public static class SculptureWoodTool
{
    private const string Menu = "Tools/Celestial Workshop/Sculpture Wood/";
    private const string TextureDir = "Assets/CW_Art/Textures/SculptureWood";
    private const string AlbedoPath = TextureDir + "/SculptureWood_Albedo.png";
    private const string NormalPath = TextureDir + "/SculptureWood_Normal.png";
    private const string WoodMatPath = "Assets/CW_Art/Materials/SculptureWood_Mat.mat";
    private const string WoodHighlightMatPath = "Assets/CW_Art/Materials/SculptureWood_Highlight_Mat.mat";
    private const string OriginalMatPath = "Assets/CW_Art/Materials/MiniConstellation_Mat.mat";
    private const string OriginalHighlightMatPath = "Assets/CW_Art/Materials/MiniConstellation_Highlight_Mat.mat";
    private const string ShaderName = "CelestialWorkshop/CarvedWood";

    private const int Size = 512;

    [MenuItem(Menu + "Apply Wood To Sculptures And Mini")]
    public static void ApplyWood()
    {
        if (!File.Exists(AlbedoPath) || !File.Exists(NormalPath))
        {
            GenerateTextures();
        }

        Material wood = EnsureMaterial(WoodMatPath, false);
        Material highlight = EnsureMaterial(WoodHighlightMatPath, true);
        int count = Swap(wood, wood, highlight);
        Debug.Log($"SculptureWoodTool: wood applied to {count} sculpture(s) and the workstation mini. Tune {WoodMatPath}.");
    }

    [MenuItem(Menu + "Restore Original Sculpture Materials")]
    public static void RestoreOriginal()
    {
        var original = AssetDatabase.LoadAssetAtPath<Material>(OriginalMatPath);
        var highlight = AssetDatabase.LoadAssetAtPath<Material>(OriginalHighlightMatPath);
        int count = Swap(original, original, highlight);
        Debug.Log($"SculptureWoodTool: original materials restored on {count} sculpture(s) and the workstation mini.");
    }

    // Starter Pack sculptures (BoxSelection's list) and the workstation mini.
    private static int Swap(Material star, Material line, Material highlight)
    {
        SwapMini(star, line, highlight);

        var boxes = Object.FindFirstObjectByType<BoxSelection>(FindObjectsInactive.Include);
        if (boxes == null || star == null || highlight == null)
        {
            Debug.LogWarning("SculptureWoodTool: no BoxSelection in the open scene, or materials missing.");
            return 0;
        }

        var field = typeof(BoxSelection).GetField("sculptures", BindingFlags.NonPublic | BindingFlags.Instance);
        var sculptures = (TitleLetterMini[])field.GetValue(boxes);

        // Every material the sculptures could currently wear, original or wood.
        var known = new HashSet<Material>
        {
            AssetDatabase.LoadAssetAtPath<Material>(OriginalMatPath),
            AssetDatabase.LoadAssetAtPath<Material>(OriginalHighlightMatPath),
            AssetDatabase.LoadAssetAtPath<Material>(WoodMatPath),
            AssetDatabase.LoadAssetAtPath<Material>(WoodHighlightMatPath)
        };

        int count = 0;
        foreach (TitleLetterMini sculpture in sculptures)
        {
            if (sculpture == null)
            {
                continue;
            }

            Undo.RecordObject(sculpture, "Sculpture material");
            ConstellationGenerator.AppearanceSettings appearance = sculpture.Appearance;
            appearance.starMaterial = star;
            appearance.lineMaterial = line;
            appearance.starHighlightMaterial = highlight;
            sculpture.Appearance = appearance;
            EditorUtility.SetDirty(sculpture);

            foreach (Renderer renderer in sculpture.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && known.Contains(materials[i]))
                    {
                        materials[i] = renderer.name == "Connection" ? line : star;
                        changed = true;
                    }
                }

                if (changed)
                {
                    Undo.RecordObject(renderer, "Sculpture material");
                    renderer.sharedMaterials = materials;
                }
            }

            count++;
        }

        EditorSceneManager.MarkSceneDirty(boxes.gameObject.scene);
        return count;
    }

    // The mini is regenerated from this appearance whenever a constellation loads.
    private static void SwapMini(Material star, Material line, Material highlight)
    {
        var setup = Object.FindFirstObjectByType<ConstellationSetup>(FindObjectsInactive.Include);
        if (setup == null || star == null || highlight == null)
        {
            Debug.LogWarning("SculptureWoodTool: no ConstellationSetup in the open scene; the mini was not changed.");
            return;
        }

        var serialized = new SerializedObject(setup);
        serialized.FindProperty("tableConstellationAppearance.starMaterial").objectReferenceValue = star;
        serialized.FindProperty("tableConstellationAppearance.lineMaterial").objectReferenceValue = line;
        serialized.FindProperty("tableConstellationAppearance.starHighlightMaterial").objectReferenceValue = highlight;
        serialized.ApplyModifiedProperties();
    }

    private static Material EnsureMaterial(string path, bool highlight)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        material = new Material(Shader.Find(ShaderName));
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
        material.SetTextureScale("_BaseMap", Vector2.one);
        if (highlight)
        {
            // Grab / hover: the wood glows warm gold (the original highlight was yellow).
            material.SetColor("_BaseColor", new Color(1f, 0.92f, 0.7f, 1f));
            material.SetColor("_EmissionColor", new Color(0.55f, 0.42f, 0.08f, 1f));
        }

        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        return material;
    }

    // ---- Texture generation (tileable, deterministic) ----

    [MenuItem(Menu + "Generate Wood Textures")]
    public static void GenerateTextures()
    {
        Directory.CreateDirectory(TextureDir);
        var albedo = new Color[Size * Size];
        var normal = new Color[Size * Size];

        // Grain: lines run along v (the length of a rod, pole to pole on a
        // star). Facets: knife cuts, as shallow scooped Voronoi dishes.
        const int grainLines = 7;
        const int cellsU = 9, cellsV = 5;
        Color light = new Color(0.80f, 0.62f, 0.42f);
        Color dark = new Color(0.52f, 0.34f, 0.19f);
        float texel = 1f / Size;

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) * texel, v = (y + 0.5f) * texel;

                float grain = Grain(u, v, grainLines);
                float dgu = (Grain(u + texel, v, grainLines) - Grain(u - texel, v, grainLines)) / (2f * texel);
                float dgv = (Grain(u, v + texel, grainLines) - Grain(u, v - texel, grainLines)) / (2f * texel);

                Facet(u, v, cellsU, cellsV, out Vector2 facetGrad, out float edge, out float cellTone);

                float pores = Mathf.Clamp01((Fbm(u * 96f, v * 12f, 96, 12, 2) - 0.68f) * 6f);
                float tone = Fbm(u * 3f, v * 2f, 3, 2, 3);

                // Albedo: warm wood, darker latewood lines, pores, faint cut-face
                // variation, a little dirt in the carved creases.
                Color c = Color.Lerp(light, dark, grain * 0.55f);
                c *= 0.93f + 0.14f * tone;
                c *= 1f - 0.22f * pores;
                c *= 0.96f + 0.08f * cellTone;
                c *= 1f - 0.16f * (1f - Mathf.SmoothStep(0f, 0.06f, edge));
                c.a = 1f;
                albedo[y * Size + x] = c;

                // Normal: facet slopes (dominant), grain ridges, fine fuzz.
                float fu = (Fbm(u * 24f + texel * 8f, v * 6f, 24, 6, 2) - Fbm(u * 24f - texel * 8f, v * 6f, 24, 6, 2));
                float fv = (Fbm(u * 24f, v * 6f + texel * 8f, 24, 6, 2) - Fbm(u * 24f, v * 6f - texel * 8f, 24, 6, 2));
                Vector2 g = facetGrad * 1.0f + new Vector2(dgu, dgv) * 0.0035f + new Vector2(fu, fv) * 0.6f;
                Vector3 n = new Vector3(-g.x, -g.y, 1f).normalized;
                normal[y * Size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        }

        Write(AlbedoPath, albedo, false);
        Write(NormalPath, normal, true);
        Debug.Log($"SculptureWoodTool: textures written to {TextureDir}.");
    }

    // 0 = light earlywood, 1 = dark latewood line.
    private static float Grain(float u, float v, int lines)
    {
        // Wavy, unevenly spaced bands rather than regular stripes.
        float warp = 0.2f * (Fbm(u * 2f, v * 2f, 2, 2, 3) - 0.5f) + 0.05f * (Fbm(u * 5f, v * 3f, 5, 3, 2) - 0.5f);
        float phase = (u + warp) * lines;
        float width = 0.35f + 0.65f * Fbm(u * 4f, v * 1f, 4, 1, 2);
        float main = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * phase), 3f + 6f * (1f - width)) * width;
        float fine = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(2f * Mathf.PI * phase * 4f), 8f) * 0.25f;
        return Mathf.Clamp01(main + fine);
    }

    // Scooped facets: inside each Voronoi cell the surface is a shallow dish,
    // tilted a little at random, so neighbouring cuts meet in crisp ridges.
    private static void Facet(float u, float v, int cu, int cv, out Vector2 gradient, out float edge, out float tone)
    {
        Vector2 p = new Vector2(u * cu, v * cv);
        int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y);
        float f1 = float.MaxValue, f2 = float.MaxValue;
        Vector2 nearest = Vector2.zero;
        int nx = 0, ny = 0;
        for (int oy = -1; oy <= 1; oy++)
        {
            for (int ox = -1; ox <= 1; ox++)
            {
                int cx = ix + ox, cy = iy + oy;
                Vector2 site = new Vector2(cx + 0.15f + 0.7f * Hash(cx, cy, cu, cv, 1), cy + 0.15f + 0.7f * Hash(cx, cy, cu, cv, 2));
                float d = (p - site).sqrMagnitude;
                if (d < f1)
                {
                    f2 = f1;
                    f1 = d;
                    nearest = site;
                    nx = cx;
                    ny = cy;
                }
                else if (d < f2)
                {
                    f2 = d;
                }
            }
        }

        Vector2 offset = p - nearest;
        Vector2 tilt = new Vector2(Hash(nx, ny, cu, cv, 3) - 0.5f, Hash(nx, ny, cu, cv, 4) - 0.5f) * 0.9f;
        // Height = tilt . offset + 0.5 |offset|^2 (dish), in cell units -> uv.
        Vector2 cellGrad = tilt + offset;
        gradient = new Vector2(cellGrad.x * cu, cellGrad.y * cv) * 0.06f;
        edge = Mathf.Sqrt(f2) - Mathf.Sqrt(f1);
        tone = Hash(nx, ny, cu, cv, 5);
    }

    private static float Hash(int x, int y, int px, int py, int seed)
    {
        x = ((x % px) + px) % px;
        y = ((y % py) + py) % py;
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519u);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }

    // Periodic value noise: x in [0, px) repeats, y in [0, py) repeats.
    private static float Noise(float x, float y, int px, int py, int seed)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash(ix, iy, px, py, seed), b = Hash(ix + 1, iy, px, py, seed);
        float c = Hash(ix, iy + 1, px, py, seed), d = Hash(ix + 1, iy + 1, px, py, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float Fbm(float x, float y, int px, int py, int octaves)
    {
        float sum = 0f, amplitude = 0.5f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            int scale = 1 << o;
            sum += Noise(x * scale, y * scale, px * scale, py * scale, 11 + o) * amplitude;
            norm += amplitude;
            amplitude *= 0.5f;
        }

        return sum / norm;
    }

    private static void Write(string path, Color[] pixels, bool isNormal)
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, !isNormal);
        texture.SetPixels(pixels);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !isNormal;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 2;
        importer.maxTextureSize = Size;
        importer.SaveAndReimport();
    }
}
