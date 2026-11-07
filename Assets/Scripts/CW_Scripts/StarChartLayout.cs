using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lays out a round star chart (the Star Chart Blueprint) from the star data,
/// framed exactly like the Earth view disc: same circle, same star positions.
/// Laid over the Earth view later as a lens, the chart's stars land on the
/// rendered ones.
///
/// Positions the BlueprintStar children (the poke targets) and builds the
/// printed marks (compass ring, grid, constellation lines, star dots) as one
/// generated mesh. The paper underneath is a separate renderer, so a lens
/// mode can hide it and keep the marks.
///
/// Local frame: chart face up along +Y, image top along +Z, right along +X.
/// </summary>
[ExecuteAlways]
public sealed class StarChartLayout : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The sky's Constellation Generator: star positions and the line order.")]
    [SerializeField]
    private ConstellationGenerator constellationGenerator;

    [Tooltip("Earth View Camera: its circle framing decides where stars land on the chart.")]
    [SerializeField]
    private EarthViewCamera earthViewCamera;

    [Tooltip("Parent of the BlueprintStar poke targets; each is placed on its star by Star Id.")]
    [SerializeField]
    private Transform starTargets;

    [Tooltip("Renders the generated marks (compass ring, grid, lines, star dots).")]
    [SerializeField]
    private MeshFilter marks;

    [Header("Chart")]
    [Tooltip("Radius of the printed circle (local units). Match the Earth view disc's screen radius so a lens lines up.")]
    [SerializeField]
    [Min(0.01f)]
    private float chartRadius = 0.083f;

    [Tooltip("Height of the marks above the root (just above the paper).")]
    [SerializeField]
    private float markHeight = 0.0085f;

    [Header("Stars")]
    [SerializeField]
    [Min(0.0005f)]
    private float starDotRadius = 0.003f;

    [Tooltip("Thin circle drawn around each star dot, blueprint style. 0 hides it.")]
    [SerializeField]
    [Min(0f)]
    private float starRingRadius = 0.0055f;

    [SerializeField]
    private Color starColor = new Color(1f, 1f, 1f, 1f);

    [Header("Lines")]
    [SerializeField]
    [Min(0.0002f)]
    private float lineWidth = 0.0012f;

    [Tooltip("Lines stop this far from each star's center.")]
    [SerializeField]
    [Min(0f)]
    private float lineGap = 0.0075f;

    [SerializeField]
    private Color lineColor = new Color(0.8f, 0.92f, 1f, 0.9f);

    [Header("Compass And Grid")]
    [SerializeField]
    private bool showCompass = true;

    [SerializeField]
    private Color compassColor = new Color(0.8f, 0.92f, 1f, 0.55f);

    public enum GridStyle
    {
        [Tooltip("Concentric rings and spokes.")]
        Polar,

        [Tooltip("Blueprint squares, clipped to the circle.")]
        Squares
    }

    [SerializeField]
    private bool showGrid = true;

    [SerializeField]
    private GridStyle gridStyle = GridStyle.Squares;

    [SerializeField]
    private Color gridColor = new Color(0.8f, 0.92f, 1f, 0.18f);

    [Tooltip("Thickness of the grid lines (local units).")]
    [SerializeField]
    [Min(0.0001f)]
    private float gridLineWidth = 0.0006f;

    [Tooltip("Squares: side of each square (local units). Lines run through the chart's center.")]
    [SerializeField]
    [Min(0.002f)]
    private float gridSquareSize = 0.01f;

    [Tooltip("Squares: every Nth line is a major line (drawn with Major Line Color and twice the width). 0 = no major lines.")]
    [SerializeField]
    [Range(0, 10)]
    private int gridMajorEvery = 0;

    [SerializeField]
    private Color gridMajorColor = new Color(0.8f, 0.92f, 1f, 0.32f);

    [Tooltip("Polar: number of concentric rings.")]
    [SerializeField]
    [Range(0, 6)]
    private int gridRings = 2;

    [Tooltip("Polar: number of spokes (each is a full diameter).")]
    [SerializeField]
    [Range(0, 12)]
    private int gridSpokes = 4;

    private const float Thin = 0.0006f;
    private const int CircleSegments = 96;

    private Mesh marksMesh;
    private bool dirty = true;

    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> triangles = new List<int>();

    public float ChartRadius => chartRadius;

    private void OnEnable()
    {
        dirty = true;
        Refresh();
    }

    private void OnValidate()
    {
        // Mesh assignment isn't allowed during OnValidate; rebuild next update.
        dirty = true;
    }

    private void OnDestroy()
    {
        if (marksMesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(marksMesh);
        }
        else
        {
            DestroyImmediate(marksMesh);
        }

        marksMesh = null;
    }

    private void LateUpdate()
    {
        Refresh();
    }

    [ContextMenu("Rebuild Chart")]
    public void Rebuild()
    {
        dirty = true;
        Refresh();
    }

    /// <summary>A star's position on the chart, in this transform's local space.</summary>
    public bool TryGetLocalStarPosition(string starId, out Vector3 localPosition)
    {
        localPosition = default;
        if (constellationGenerator == null || earthViewCamera == null ||
            !constellationGenerator.TryGetStarData(starId, out ConstellationGenerator.StarData star))
        {
            return false;
        }

        localPosition = LocalStarPosition(star);
        return true;
    }

    private Vector3 LocalStarPosition(ConstellationGenerator.StarData star)
    {
        Vector2 disc = earthViewCamera.ArtboardToDisc(star.affinityPosition);
        return new Vector3(disc.x * chartRadius, markHeight, disc.y * chartRadius);
    }

    private void Refresh()
    {
        if (!dirty || constellationGenerator == null || earthViewCamera == null)
        {
            return;
        }

        dirty = false;
        PlaceStarTargets();
        BuildMarks();
    }

    private void PlaceStarTargets()
    {
        if (starTargets == null)
        {
            return;
        }

        foreach (BlueprintStar target in starTargets.GetComponentsInChildren<BlueprintStar>(true))
        {
            if (!TryGetLocalStarPosition(target.StarId, out Vector3 local))
            {
                Debug.LogWarning($"StarChartLayout: no star data named '{target.StarId}'.", target);
                continue;
            }

            Vector3 world = transform.TransformPoint(local);
            if ((target.transform.position - world).sqrMagnitude > 1e-12f)
            {
                target.transform.position = world;
            }
        }
    }

    private void BuildMarks()
    {
        if (marks == null)
        {
            return;
        }

        vertices.Clear();
        colors.Clear();
        triangles.Clear();

        // Drawn back to front within the mesh: grid, compass, lines, stars.
        if (showGrid && gridStyle == GridStyle.Polar)
        {
            for (int i = 1; i <= gridRings; i++)
            {
                AddRing(Vector3.zero, chartRadius * i / (gridRings + 1f), gridLineWidth, gridColor, CircleSegments);
            }

            for (int i = 0; i < gridSpokes; i++)
            {
                float angle = Mathf.PI * i / Mathf.Max(1, gridSpokes);
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * chartRadius * 0.9f;
                AddLine(-direction, direction, gridLineWidth, gridColor);
            }
        }
        else if (showGrid && gridStyle == GridStyle.Squares)
        {
            // Inside the compass ring when it's shown, else the whole chart.
            float limit = showCompass ? chartRadius * 0.95f : chartRadius;
            int count = Mathf.FloorToInt(limit / gridSquareSize);
            for (int k = -count; k <= count; k++)
            {
                float offset = k * gridSquareSize;
                float half = Mathf.Sqrt(Mathf.Max(0f, limit * limit - offset * offset));
                if (half <= 0f)
                {
                    continue;
                }

                bool major = gridMajorEvery > 0 && k % gridMajorEvery == 0;
                Color color = major ? gridMajorColor : gridColor;
                float width = major ? gridLineWidth * 2f : gridLineWidth;
                AddLine(new Vector3(offset, 0f, -half), new Vector3(offset, 0f, half), width, color);
                AddLine(new Vector3(-half, 0f, offset), new Vector3(half, 0f, offset), width, color);
            }
        }

        if (showCompass)
        {
            float ring = chartRadius * 0.95f;
            AddRing(Vector3.zero, ring, Thin * 1.5f, compassColor, CircleSegments);

            // Ticks every 5 degrees, longer every 30, pointing inward from the ring.
            for (int i = 0; i < 72; i++)
            {
                float angle = 2f * Mathf.PI * i / 72f;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                float length = i % 6 == 0 ? chartRadius * 0.07f : chartRadius * 0.03f;
                AddLine(direction * ring, direction * (ring - length), Thin, compassColor);
            }

            // Small triangle at the top (image up), like a north mark.
            Vector3 tip = Vector3.forward * (ring - chartRadius * 0.1f);
            Vector3 baseCenter = Vector3.forward * (ring - chartRadius * 0.17f);
            AddTriangle(tip, baseCenter + Vector3.right * chartRadius * 0.03f, baseCenter - Vector3.right * chartRadius * 0.03f, compassColor);
        }

        List<Vector3> starPositions = new List<Vector3>();
        foreach (ConstellationGenerator.StarData star in constellationGenerator.stars)
        {
            Vector3 position = LocalStarPosition(star);
            position.y = 0f;
            starPositions.Add(position);
        }

        // Same order as the sky's connection rods: each star to the next.
        for (int i = 0; i < starPositions.Count - 1; i++)
        {
            Vector3 a = starPositions[i];
            Vector3 b = starPositions[i + 1];
            Vector3 direction = b - a;
            float length = direction.magnitude;
            if (length <= lineGap * 2f)
            {
                continue;
            }

            direction /= length;
            AddLine(a + direction * lineGap, b - direction * lineGap, lineWidth, lineColor);
        }

        foreach (Vector3 position in starPositions)
        {
            AddDisc(position, starDotRadius, starColor, 24);
            if (starRingRadius > starDotRadius)
            {
                AddRing(position, starRingRadius, Thin, starColor, 32);
            }
        }

        // Into the marks object's space, at mark height.
        Matrix4x4 toMarks = marks.transform.worldToLocalMatrix * transform.localToWorldMatrix;
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 v = vertices[i];
            v.y += markHeight;
            vertices[i] = toMarks.MultiplyPoint3x4(v);
        }

        if (marksMesh == null)
        {
            marksMesh = new Mesh { name = "Star Chart Marks (generated)", hideFlags = HideFlags.DontSave };
        }

        marksMesh.Clear();
        marksMesh.SetVertices(vertices);
        marksMesh.SetColors(colors);
        marksMesh.SetTriangles(triangles, 0);
        marksMesh.RecalculateBounds();
        marks.sharedMesh = marksMesh;
    }

    // Flat primitives in the chart plane (y = 0 here, raised to mark height later).
    // The marks shader draws both sides, so winding doesn't matter.

    private void AddLine(Vector3 a, Vector3 b, float width, Color color)
    {
        Vector3 side = Vector3.Cross(Vector3.up, b - a).normalized * (width * 0.5f);
        AddQuad(a - side, a + side, b + side, b - side, color);
    }

    private void AddRing(Vector3 center, float radius, float width, Color color, int segments)
    {
        float inner = radius - width * 0.5f;
        float outer = radius + width * 0.5f;
        for (int i = 0; i < segments; i++)
        {
            float a0 = 2f * Mathf.PI * i / segments;
            float a1 = 2f * Mathf.PI * (i + 1) / segments;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            Vector3 d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            AddQuad(center + d0 * inner, center + d0 * outer, center + d1 * outer, center + d1 * inner, color);
        }
    }

    private void AddDisc(Vector3 center, float radius, Color color, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            float a0 = 2f * Mathf.PI * i / segments;
            float a1 = 2f * Mathf.PI * (i + 1) / segments;
            AddTriangle(
                center,
                center + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius,
                center + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius,
                color
            );
        }
    }

    private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        for (int i = 0; i < 4; i++)
        {
            colors.Add(color);
        }

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
    }
}
