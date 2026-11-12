using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared pieces of the sightline compression (ConstellationReveal, the
/// post-solve reward, and ConstellationIntro, the puzzle-start intro).
///
/// The Earth view is a perspective camera at the Earth observer. A star slid
/// along its own ray from that camera keeps its pixel, so flattening every
/// star onto one plane square to the camera's forward axis removes depth
/// without changing the Earth view image.
/// </summary>
public static class SightlineCompression
{
    /// <summary>Where the ray from origin through point meets the plane at distance along forward.</summary>
    public static Vector3 FlattenAlongSightline(Vector3 point, Vector3 origin, Vector3 forward, float distance)
    {
        Vector3 ray = point - origin;
        float depth = Vector3.Dot(ray, forward);
        if (depth <= 1e-6f)
        {
            return point; // behind the observer: not in the Earth view
        }

        return origin + ray * (distance / depth);
    }
}

/// <summary>
/// A generated constellation's stars and connection rods under one root, with
/// their authored local poses, so they can be moved and put back exactly.
/// Rods are paired with the two stars they were built between.
/// </summary>
public sealed class ConstellationGeometry
{
    public struct Star
    {
        public Transform transform;
        public Vector3 baseLocal;
        public Vector3 baseLocalScale;
    }

    public struct Connection
    {
        public Transform transform;
        public int a;
        public int b;
        public Vector3 baseLocalPosition;
        public Quaternion baseLocalRotation;
        public Vector3 baseLocalScale;
    }

    public Transform Root { get; private set; }
    public Star[] Stars { get; private set; } = new Star[0];
    public Connection[] Connections { get; private set; } = new Connection[0];

    /// <summary>Stars must share one parent (the root); rods are the root's children named "Connection".</summary>
    public static ConstellationGeometry Collect(IList<Transform> starTransforms)
    {
        if (starTransforms == null || starTransforms.Count == 0 || starTransforms[0] == null)
        {
            return null;
        }

        var geometry = new ConstellationGeometry { Root = starTransforms[0].parent };
        var stars = new Star[starTransforms.Count];
        for (int i = 0; i < stars.Length; i++)
        {
            stars[i] = new Star
            {
                transform = starTransforms[i],
                baseLocal = starTransforms[i].localPosition,
                baseLocalScale = starTransforms[i].localScale
            };
        }

        geometry.Stars = stars;

        var links = new List<Connection>();
        foreach (Transform child in geometry.Root)
        {
            if (child.name != "Connection")
            {
                continue;
            }

            int bestA = -1;
            int bestB = -1;
            float bestError = float.MaxValue;
            for (int a = 0; a < stars.Length; a++)
            {
                for (int b = a + 1; b < stars.Length; b++)
                {
                    Vector3 pa = stars[a].baseLocal;
                    Vector3 pb = stars[b].baseLocal;
                    float error = ((pa + pb) * 0.5f - child.localPosition).magnitude +
                                  Mathf.Abs((pb - pa).magnitude - child.localScale.y * 2f);
                    if (error < bestError)
                    {
                        bestError = error;
                        bestA = a;
                        bestB = b;
                    }
                }
            }

            if (bestA >= 0)
            {
                links.Add(new Connection
                {
                    transform = child,
                    a = bestA,
                    b = bestB,
                    baseLocalPosition = child.localPosition,
                    baseLocalRotation = child.localRotation,
                    baseLocalScale = child.localScale
                });
            }
        }

        geometry.Connections = links.ToArray();
        return geometry;
    }

    /// <summary>
    /// Stretches each rod between its two stars' current local positions.
    /// thickness (optional, per rod) scales the rod's cross-section from its base.
    /// </summary>
    public void UpdateConnections(float[] thickness = null)
    {
        for (int i = 0; i < Connections.Length; i++)
        {
            Connection link = Connections[i];
            if (link.transform == null)
            {
                continue;
            }

            Vector3 pa = Stars[link.a].transform.localPosition;
            Vector3 pb = Stars[link.b].transform.localPosition;
            Vector3 span = pb - pa;
            float length = span.magnitude;
            link.transform.localPosition = (pa + pb) * 0.5f;
            if (length > 1e-7f)
            {
                link.transform.localRotation = Quaternion.FromToRotation(Vector3.up, span / length);
            }

            Vector3 scale = link.transform.localScale;
            if (thickness != null && i < thickness.Length)
            {
                scale.x = link.baseLocalScale.x * thickness[i];
                scale.z = link.baseLocalScale.z * thickness[i];
            }

            scale.y = length * 0.5f;
            link.transform.localScale = scale;
        }
    }

    /// <summary>Puts every star and rod back on its authored local pose.</summary>
    public void Restore()
    {
        foreach (Star star in Stars)
        {
            if (star.transform != null)
            {
                star.transform.localPosition = star.baseLocal;
                star.transform.localScale = star.baseLocalScale;
            }
        }

        foreach (Connection link in Connections)
        {
            if (link.transform != null)
            {
                link.transform.localPosition = link.baseLocalPosition;
                link.transform.localRotation = link.baseLocalRotation;
                link.transform.localScale = link.baseLocalScale;
            }
        }
    }
}

/// <summary>Look of the compression guides (ray paths and the destination plane).</summary>
[Serializable]
public sealed class SightlineGuideSettings
{
    [Tooltip("Draw each star's remaining path to its landing point.")]
    public bool showRays = true;

    public bool dashed = true;

    public Color rayColor = new Color(0.75f, 0.85f, 1f, 0.5f);

    [Tooltip("Line width (world m).")]
    [Min(0.0001f)]
    public float rayThickness = 0.0012f;

    [Tooltip("Dash length (world m).")]
    [Min(0.0005f)]
    public float dashLength = 0.006f;

    [Tooltip("Gap between dashes (world m).")]
    [Min(0f)]
    public float gapLength = 0.004f;

    [Tooltip("Outline the part of the compression plane the pattern lands on.")]
    public bool showPlaneOutline = true;

    public Color planeOutlineColor = new Color(0.75f, 0.85f, 1f, 0.35f);

    [Tooltip("Outline width (world m).")]
    [Min(0.0001f)]
    public float planeOutlineThickness = 0.001f;

    public bool showPlaneFill = false;

    public Color planeFillColor = new Color(0.75f, 0.85f, 1f, 0.06f);

    [Tooltip("Margin around the flattened stars' bounds (world m).")]
    [Min(0f)]
    public float planePadding = 0.012f;
}

/// <summary>
/// Dashed ray guides plus an optional plane outline / fill, on the Default
/// layer: drawn in the headset, never in the Earth view (which only renders
/// the sky's WindowMasked layer). Visual only.
/// </summary>
public sealed class SightlineGuides
{
    private GameObject guideRoot;
    private LineRenderer[] rays = new LineRenderer[0];
    private LineRenderer planeOutline;
    private MeshRenderer planeFill;
    private Mesh planeFillMesh;
    private Material rayMaterial;
    private Material outlineMaterial;
    private Material fillMaterial;

    public bool IsBuilt => guideRoot != null;

    /// <summary>Builds rayCount ray lines and the plane pieces. False if the shader is missing.</summary>
    public bool Build(string name, int rayCount, Shader shader, string context)
    {
        Destroy();
        if (shader == null)
        {
            shader = Shader.Find("CelestialWorkshop/GuideLine");
        }

        if (shader == null)
        {
            Debug.LogWarning($"{context}: no guide shader; guides skipped.");
            return false;
        }

        if (rayMaterial == null)
        {
            rayMaterial = new Material(shader) { name = name + " Ray Guide" };
            outlineMaterial = new Material(shader) { name = name + " Plane Outline" };
            fillMaterial = new Material(shader) { name = name + " Plane Fill" };
        }

        guideRoot = new GameObject(name + " Guides");
        guideRoot.layer = 0;

        rays = new LineRenderer[rayCount];
        for (int i = 0; i < rayCount; i++)
        {
            LineRenderer line = NewLine("Ray " + i, rayMaterial);
            line.positionCount = 2;
            line.textureMode = LineTextureMode.Tile;
            rays[i] = line;
        }

        planeOutline = NewLine("Plane Outline", outlineMaterial);
        planeOutline.positionCount = 4;
        planeOutline.loop = true;

        var fillObject = new GameObject("Plane Fill");
        fillObject.layer = 0;
        fillObject.transform.SetParent(guideRoot.transform, false);
        planeFillMesh = new Mesh { name = name + " Plane Fill" };
        fillObject.AddComponent<MeshFilter>().sharedMesh = planeFillMesh;
        planeFill = fillObject.AddComponent<MeshRenderer>();
        planeFill.sharedMaterial = fillMaterial;
        planeFill.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        planeFill.receiveShadows = false;
        return true;
    }

    private LineRenderer NewLine(string name, Material material)
    {
        var lineObject = new GameObject(name);
        lineObject.layer = 0;
        lineObject.transform.SetParent(guideRoot.transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.sharedMaterial = material;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.startColor = Color.white;
        line.endColor = Color.white;
        return line;
    }

    /// <summary>
    /// Ray i runs from landing[i] back to current[i] (only the stretch still
    /// ahead of the star; dashes start at the landing end, so they stay put
    /// while the line shortens). The plane is centered at planeCenter with
    /// half extents planeHalfSize along planeRight / planeUp.
    /// </summary>
    public void Update(SightlineGuideSettings settings, Vector3[] landing, Vector3[] current,
                       Vector3 planeCenter, Vector3 planeRight, Vector3 planeUp, Vector2 planeHalfSize)
    {
        if (guideRoot == null)
        {
            return;
        }

        float period = settings.dashLength + settings.gapLength;
        rayMaterial.SetColor("_BaseColor", settings.rayColor);
        rayMaterial.SetFloat("_DashFraction", settings.dashed && settings.gapLength > 0f ? settings.dashLength / period : 1f);
        outlineMaterial.SetColor("_BaseColor", settings.planeOutlineColor);
        outlineMaterial.SetFloat("_DashFraction", 1f);
        fillMaterial.SetColor("_BaseColor", settings.planeFillColor);
        fillMaterial.SetFloat("_DashFraction", 1f);

        for (int i = 0; i < rays.Length && i < landing.Length && i < current.Length; i++)
        {
            LineRenderer line = rays[i];
            if (line == null)
            {
                continue;
            }

            line.SetPosition(0, landing[i]);
            line.SetPosition(1, current[i]);
            line.enabled = settings.showRays;
            line.widthMultiplier = settings.rayThickness;
            line.textureScale = new Vector2(1f / Mathf.Max(1e-5f, period), 1f);
        }

        Vector2 half = planeHalfSize + Vector2.one * settings.planePadding;
        Vector3 x = planeRight * half.x;
        Vector3 y = planeUp * half.y;
        var corners = new[]
        {
            planeCenter - x - y,
            planeCenter - x + y,
            planeCenter + x + y,
            planeCenter + x - y
        };

        if (planeOutline != null)
        {
            planeOutline.enabled = settings.showPlaneOutline;
            planeOutline.widthMultiplier = settings.planeOutlineThickness;
            planeOutline.SetPositions(corners);
        }

        if (planeFill != null)
        {
            planeFill.enabled = settings.showPlaneFill;
            planeFillMesh.vertices = corners;
            planeFillMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            planeFillMesh.RecalculateBounds();
        }
    }

    public void Destroy()
    {
        if (guideRoot != null)
        {
            UnityEngine.Object.Destroy(guideRoot);
        }

        if (planeFillMesh != null)
        {
            UnityEngine.Object.Destroy(planeFillMesh);
        }

        guideRoot = null;
        planeFillMesh = null;
        planeOutline = null;
        planeFill = null;
        rays = new LineRenderer[0];
    }

    /// <summary>Also frees the materials; call from the owner's OnDestroy.</summary>
    public void DestroyAll()
    {
        Destroy();
        foreach (Material material in new[] { rayMaterial, outlineMaterial, fillMaterial })
        {
            if (material != null)
            {
                UnityEngine.Object.Destroy(material);
            }
        }

        rayMaterial = outlineMaterial = fillMaterial = null;
    }
}
