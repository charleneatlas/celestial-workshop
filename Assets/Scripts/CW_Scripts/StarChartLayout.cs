using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Lays out a round star chart (the Star Chart Blueprint) from the star data,
/// framed exactly like the Earth view disc: same circle, same star positions.
/// Laid over the Earth view later as a lens, the chart's stars land on the
/// rendered ones.
///
/// Positions the BlueprintStar children (the poke targets) and builds the
/// marks as one generated mesh. The paper underneath is a separate renderer,
/// so a lens mode can hide it and keep the marks.
///
/// Two styles: Blueprint (white print on blue, compass and grid) and Sketch
/// (the astronomer's own graphite sketch on aged graph paper: rough multi-
/// stroke lines, hatched star circles, handwritten names and a script title).
/// Labels are generated TextMeshPro objects that aren't saved with the scene.
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

    [Tooltip("Renders the generated marks (lines, stars, grid).")]
    [SerializeField]
    private MeshFilter marks;

    [Tooltip("The paper disc under the marks; its material follows the style.")]
    [SerializeField]
    private Renderer paper;

    public enum ChartStyle
    {
        [Tooltip("White print on blueprint blue, with compass ring and grid.")]
        Blueprint,

        [Tooltip("Graphite pencil sketch on aged graph paper, with handwritten names.")]
        Sketch
    }

    [Header("Style")]
    [SerializeField]
    private ChartStyle chartStyle = ChartStyle.Blueprint;

    [SerializeField]
    private Material blueprintPaperMaterial;

    [SerializeField]
    private Material blueprintMarksMaterial;

    [SerializeField]
    private Material sketchPaperMaterial;

    [Tooltip("PencilStroke material: graphite color, grain and edge softness live here.")]
    [SerializeField]
    private Material sketchMarksMaterial;

    public enum PaperLook
    {
        [Tooltip("The aged graph paper, warm and stained.")]
        Aged,
        [Tooltip("Pale, cool tracing paper; the grid and stains ghost through faintly.")]
        Tracing,
    }

    [Header("Sketch Paper")]
    [Tooltip("How solid the sketch paper is: 1 = opaque, lower = see-through like tracing paper. Needs the ChartPaper shader on the sketch paper material.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float paperOpacity = 0.75f;

    [SerializeField]
    private PaperLook paperLook = PaperLook.Aged;

    [Tooltip("Tracing look: the paper's color.")]
    [SerializeField]
    private Color tracingPaperColor = new Color(0.93f, 0.94f, 0.94f, 1f);

    [Tooltip("Tracing look: how much of the aged paper's grid and stains still shows.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float tracingDetail = 0.5f;

    [Header("Chart")]
    [Tooltip("Radius of the printed circle (local units). Match the Earth view disc's screen radius so a lens lines up.")]
    [SerializeField]
    [Min(0.01f)]
    private float chartRadius = 0.083f;

    [Tooltip("Height of the marks above the root (just above the paper).")]
    [SerializeField]
    private float markHeight = 0.0085f;

    [Header("Blueprint Stars")]
    [SerializeField]
    [Min(0.0005f)]
    private float starDotRadius = 0.003f;

    [Tooltip("Thin circle drawn around each star dot, blueprint style. 0 hides it.")]
    [SerializeField]
    [Min(0f)]
    private float starRingRadius = 0.0055f;

    [SerializeField]
    private Color starColor = new Color(1f, 1f, 1f, 1f);

    [Header("Blueprint Lines")]
    [SerializeField]
    [Min(0.0002f)]
    private float lineWidth = 0.0012f;

    [Tooltip("Lines stop this far from each star's center.")]
    [SerializeField]
    [Min(0f)]
    private float lineGap = 0.0075f;

    [SerializeField]
    private Color lineColor = new Color(0.8f, 0.92f, 1f, 0.9f);

    [Header("Blueprint Compass And Grid")]
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

    [Header("Sketch")]
    [Tooltip("Graphite color of the pencil marks (lines, circles, fill, underline). The PencilStroke material's color should stay white.")]
    [SerializeField]
    private Color pencilColor = new Color(0.14f, 0.14f, 0.16f, 1f);

    [Tooltip("Radius of each hand-drawn star circle.")]
    [SerializeField]
    [Min(0.001f)]
    private float sketchStarRadius = 0.0034f;

    [SerializeField]
    [Min(0.0001f)]
    private float sketchLineWidth = 0.0006f;

    [Tooltip("Overlapping strokes per constellation line.")]
    [SerializeField]
    [Range(1, 5)]
    private int strokesPerLine = 3;

    [Tooltip("How far repeat strokes stray from the line (sideways and at the ends).")]
    [SerializeField]
    [Min(0f)]
    private float strokeJitter = 0.00035f;

    [Tooltip("Gentle hand wobble along each stroke.")]
    [SerializeField]
    [Min(0f)]
    private float strokeWobble = 0.00012f;

    [Tooltip("How far strokes may run past where they should stop.")]
    [SerializeField]
    [Min(0f)]
    private float strokeOvershoot = 0.0012f;

    [Tooltip("Draw the pencil outline around each star circle. Off = just the fill.")]
    [SerializeField]
    private bool showStarOutline = true;

    [Tooltip("Pencil passes around each star circle.")]
    [SerializeField]
    [Range(1, 4)]
    private int circleStrokes = 2;

    public enum StarFill
    {
        [Tooltip("Small overlapping loops spiralling in, like filling a bubble on an answer sheet.")]
        Scribble,
        [Tooltip("Straight diagonal hatching.")]
        Hatch,
        [Tooltip("Outline only.")]
        None,
    }

    [Tooltip("How the star circles are filled in.")]
    [SerializeField]
    private StarFill starFill = StarFill.Scribble;

    [Tooltip("Scribble: size of each little loop, as a fraction of the star radius.")]
    [SerializeField]
    [Range(0.1f, 0.6f)]
    private float scribbleLoopSize = 0.38f;

    [Tooltip("Scribble: loops per trip around the circle.")]
    [SerializeField]
    [Range(4, 24)]
    private int scribbleLoopsPerTurn = 16;

    [Tooltip("Scribble: trips around the circle, spiralling to the middle. More = denser fill.")]
    [SerializeField]
    [Range(1f, 5f)]
    private float scribbleTurns = 3f;

    [Tooltip("Scribble: how hard the pencil presses (darkness).")]
    [SerializeField]
    [Range(0f, 1f)]
    private float scribbleStrength = 0.75f;

    [Tooltip("Scribble: how irregular the loops are.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float scribbleMessiness = 0.5f;

    [Tooltip("Hatch: gap between hatch lines inside the star circles. 0 = no hatching.")]
    [SerializeField]
    [Min(0f)]
    private float hatchSpacing = 0.0009f;

    [SerializeField]
    [Range(0f, 1f)]
    private float hatchStrength = 0.55f;

    [Tooltip("Pencil pressure (vertex alpha) for lines and circles.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float strokeStrength = 0.9f;

    [Tooltip("Change for a different hand-drawn variation.")]
    [SerializeField]
    private int sketchSeed = 7;

    [Header("Sketch Labels")]
    [SerializeField]
    private bool showLabels = true;

    [Tooltip("Handwritten caps font for the star names.")]
    [SerializeField]
    private TMP_FontAsset labelFont;

    [Tooltip("Height of the capital letters (local units).")]
    [SerializeField]
    [Min(0.0005f)]
    private float labelCapHeight = 0.0034f;

    [SerializeField]
    private Color labelColor = new Color(0.22f, 0.22f, 0.24f, 0.9f);

    [Tooltip("Thicker or thinner lettering (TMP face dilate), for both names and title.")]
    [SerializeField]
    [Range(-0.3f, 0.5f)]
    private float labelWeight = 0.08f;

    [Tooltip("A soft paper-colored glow behind names and title, so they stay readable over the see-through paper.")]
    [SerializeField]
    private bool labelHalo = true;

    [SerializeField]
    private Color labelHaloColor = new Color(0.97f, 0.93f, 0.84f, 0.75f);

    [Tooltip("How far the halo spreads around the letters.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float labelHaloSize = 0.45f;

    [Tooltip("Gap between a star circle and its name.")]
    [SerializeField]
    [Min(0f)]
    private float labelGap = 0.0018f;

    [Tooltip("Random tilt of each name, in degrees.")]
    [SerializeField]
    [Range(0f, 10f)]
    private float labelTilt = 3f;

    [SerializeField]
    private bool showTitle = true;

    [Tooltip("Script font for the constellation's name.")]
    [SerializeField]
    private TMP_FontAsset titleFont;

    [Tooltip("Empty: the generator's constellation name.")]
    [SerializeField]
    private string titleText = "";

    [Tooltip("Height of the title's capital letters (local units).")]
    [SerializeField]
    [Min(0.001f)]
    private float titleCapHeight = 0.0085f;

    [Tooltip("Title center, as a fraction of the chart radius (x right, y up).")]
    [SerializeField]
    private Vector2 titlePosition = new Vector2(0.12f, 0.7f);

    [SerializeField]
    private bool underlineTitle = true;

    [Header("Sketch Grid")]
    [Tooltip("Ruled squares drawn over the paper. For a paper texture without its own grid.")]
    [SerializeField]
    private bool showSketchGrid = false;

    [Tooltip("Side of each square (local units). Lines run through the chart's center.")]
    [SerializeField]
    [Min(0.001f)]
    private float sketchGridSquareSize = 0.01f;

    [Tooltip("Thickness of the grid lines (local units).")]
    [SerializeField]
    [Min(0.00005f)]
    private float sketchGridLineWidth = 0.00025f;

    [SerializeField]
    private Color sketchGridColor = new Color(0.33f, 0.6f, 0.62f, 0.35f);

    [Tooltip("Every Nth line is a major line (Major Color, 1.8x width). 0 = none.")]
    [SerializeField]
    [Min(0)]
    private int sketchGridMajorEvery = 0;

    [SerializeField]
    private Color sketchGridMajorColor = new Color(0.33f, 0.6f, 0.62f, 0.55f);

    [Header("Poke Highlight")]
    [Tooltip("Tint of the glow around a poked star (both styles).")]
    [SerializeField]
    private Color highlightColor = new Color(0.85f, 0.3f, 0.12f, 0.9f);

    private const float Thin = 0.0006f;
    private const int CircleSegments = 96;

    private Mesh marksMesh;
    private bool dirty = true;

    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> triangles = new List<int>();
    private readonly List<Vector2> uvs = new List<Vector2>();

    private Transform labelRoot;
    private readonly List<Vector3> starPositions = new List<Vector3>();
    private readonly List<Vector3> segmentA = new List<Vector3>();
    private readonly List<Vector3> segmentB = new List<Vector3>();

    public float ChartRadius => chartRadius;

    private bool IsSketch => chartStyle == ChartStyle.Sketch;

    private void OnEnable()
    {
        dirty = true;
        Refresh();
    }

    private void OnValidate()
    {
        // Mesh and object changes aren't allowed during OnValidate; rebuild next update.
        dirty = true;
    }

    private void OnDisable()
    {
        DestroyLabels();
    }

    private void OnDestroy()
    {
        DestroyLabels();
        DestroyLabelMaterials();
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
        ApplyMaterials();
        PlaceStarTargets();
        BuildMarks();
        BuildLabels();
    }

    private void ApplyMaterials()
    {
        Material paperMaterial = IsSketch ? sketchPaperMaterial : blueprintPaperMaterial;
        if (paper != null && paperMaterial != null && paper.sharedMaterial != paperMaterial)
        {
            paper.sharedMaterial = paperMaterial;
        }

        ApplyPaperLook();

        Material marksMaterial = IsSketch ? sketchMarksMaterial : blueprintMarksMaterial;
        if (marks != null && marksMaterial != null && marks.TryGetComponent(out MeshRenderer marksRenderer) &&
            marksRenderer.sharedMaterial != marksMaterial)
        {
            marksRenderer.sharedMaterial = marksMaterial;
        }
    }

    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int TracingId = Shader.PropertyToID("_Tracing");
    private static readonly int TracingColorId = Shader.PropertyToID("_TracingColor");
    private static readonly int TracingDetailId = Shader.PropertyToID("_TracingDetail");
    private MaterialPropertyBlock paperBlock;

    /// <summary>Sketch paper opacity and look, per renderer (the shared material stays untouched).</summary>
    private void ApplyPaperLook()
    {
        if (paper == null)
        {
            return;
        }

        if (!IsSketch)
        {
            paper.SetPropertyBlock(null);
            return;
        }

        paperBlock ??= new MaterialPropertyBlock();
        paper.GetPropertyBlock(paperBlock);
        paperBlock.SetFloat(OpacityId, paperOpacity);
        paperBlock.SetFloat(TracingId, paperLook == PaperLook.Tracing ? 1f : 0f);
        paperBlock.SetColor(TracingColorId, tracingPaperColor);
        paperBlock.SetFloat(TracingDetailId, tracingDetail);
        paper.SetPropertyBlock(paperBlock);
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

            // Highlight tint, per renderer so the shared material stays as is.
            highlightBlock ??= new MaterialPropertyBlock();
            foreach (Renderer highlight in target.GetComponentsInChildren<Renderer>(true))
            {
                highlight.GetPropertyBlock(highlightBlock);
                highlightBlock.SetColor(BaseColorId, highlightColor);
                highlight.SetPropertyBlock(highlightBlock);
            }
        }
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private MaterialPropertyBlock highlightBlock;

    private void BuildMarks()
    {
        if (marks == null)
        {
            return;
        }

        vertices.Clear();
        colors.Clear();
        triangles.Clear();
        uvs.Clear();

        CollectStarsAndSegments();

        if (IsSketch)
        {
            BuildSketchMarks();
        }
        else
        {
            BuildBlueprintMarks();
        }

        UploadMarks(0);
    }

    // Moves vertices from 'first' on into the marks object's space at mark
    // height, then uploads the whole mesh.
    private void UploadMarks(int first)
    {
        Matrix4x4 toMarks = marks.transform.worldToLocalMatrix * transform.localToWorldMatrix;
        for (int i = first; i < vertices.Count; i++)
        {
            Vector3 v = vertices[i];
            v.y += markHeight;
            vertices[i] = toMarks.MultiplyPoint3x4(v);
        }

        while (uvs.Count < vertices.Count)
        {
            uvs.Add(Vector2.zero);
        }

        if (marksMesh == null)
        {
            marksMesh = new Mesh { name = "Star Chart Marks (generated)", hideFlags = HideFlags.DontSave };
        }

        marksMesh.Clear();
        marksMesh.SetVertices(vertices);
        marksMesh.SetColors(colors);
        marksMesh.SetUVs(0, uvs);
        marksMesh.SetTriangles(triangles, 0);
        marksMesh.RecalculateBounds();
        marks.sharedMesh = marksMesh;
    }

    // Star centers (chart plane, y = 0) and the constellation's line segments,
    // in the same order as the sky's connection rods: each star to the next.
    private void CollectStarsAndSegments()
    {
        starPositions.Clear();
        segmentA.Clear();
        segmentB.Clear();
        foreach (ConstellationGenerator.StarData star in constellationGenerator.stars)
        {
            Vector3 position = LocalStarPosition(star);
            position.y = 0f;
            starPositions.Add(position);
        }

        for (int i = 0; i < starPositions.Count - 1; i++)
        {
            segmentA.Add(starPositions[i]);
            segmentB.Add(starPositions[i + 1]);
        }
    }

    // ------------------------------------------------------------- Blueprint

    private void BuildBlueprintMarks()
    {
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

        for (int i = 0; i < segmentA.Count; i++)
        {
            Vector3 a = segmentA[i];
            Vector3 b = segmentB[i];
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
    }

    // ---------------------------------------------------------------- Sketch

    private void BuildSketchMarks()
    {
        var random = new System.Random(sketchSeed);
        Color pencil = MeshColor(new Color(pencilColor.r, pencilColor.g, pencilColor.b, strokeStrength));

        if (showSketchGrid)
        {
            AddSketchGrid();
        }

        // Lines: a few rough passes, each stopping near (not exactly at) the circles.
        for (int i = 0; i < segmentA.Count; i++)
        {
            Vector3 a = segmentA[i];
            Vector3 b = segmentB[i];
            Vector3 direction = (b - a).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            for (int k = 0; k < strokesPerLine; k++)
            {
                float strength = k == 0 ? 1f : 0.45f + 0.3f * Next(random);
                Vector3 start = a + direction * (sketchStarRadius - strokeOvershoot * Next(random)) + side * Signed(random) * strokeJitter;
                Vector3 end = b - direction * (sketchStarRadius - strokeOvershoot * Next(random)) + side * Signed(random) * strokeJitter;
                float width = sketchLineWidth * (k == 0 ? 1f : 0.7f);
                AddPencilStroke(start, end, width, Fade(pencil, strength), random);
            }
        }

        // Star circles: filled in, then outlined in overlapping passes with a slight wobble.
        foreach (Vector3 center in starPositions)
        {
            if (starFill == StarFill.Scribble && scribbleStrength > 0f)
            {
                AddScribbleFill(center, sketchStarRadius, Fade(pencil, scribbleStrength / Mathf.Max(0.01f, strokeStrength)), random);
            }
            else if (starFill == StarFill.Hatch && hatchSpacing > 0f && hatchStrength > 0f)
            {
                AddHatching(center, sketchStarRadius * 0.92f, Fade(pencil, hatchStrength), random);
            }

            for (int k = 0; showStarOutline && k < circleStrokes; k++)
            {
                float start = 360f * Next(random);
                float sweep = 360f + 25f + 30f * Next(random);
                float radius = sketchStarRadius * (1f + 0.06f * Signed(random));
                AddPencilArc(center, radius, start, sweep, sketchLineWidth * (k == 0 ? 1.1f : 0.75f), Fade(pencil, k == 0 ? 1f : 0.55f), random);
            }
        }
    }

    // A stroke from a to b, as a ribbon of short quads: uv.x = distance along,
    // uv.y = 0..1 across. Wobbles gently and fades in and out at the ends.
    private void AddPencilStroke(Vector3 a, Vector3 b, float width, Color color, System.Random random)
    {
        Vector3 along = b - a;
        float length = along.magnitude;
        if (length < 1e-5f)
        {
            return;
        }

        Vector3 side = Vector3.Cross(Vector3.up, along / length);
        int segments = Mathf.Clamp(Mathf.CeilToInt(length / 0.002f), 2, 48);
        float phase = 10f * Next(random);
        float frequency = 1.5f + 2f * Next(random);

        int first = vertices.Count;
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float wobble = Mathf.Sin((t * frequency + phase) * Mathf.PI * 2f) * strokeWobble * Mathf.Sin(t * Mathf.PI);
            Vector3 center = a + along * t + side * wobble;
            float taper = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) / 0.12f);
            float halfWidth = width * 0.5f * Mathf.Lerp(0.55f, 1f, taper);
            Color c = Fade(color, Mathf.Lerp(0.25f, 1f, taper));

            vertices.Add(center - side * halfWidth);
            vertices.Add(center + side * halfWidth);
            colors.Add(c);
            colors.Add(c);
            uvs.Add(new Vector2(t * length, 0f));
            uvs.Add(new Vector2(t * length, 1f));
        }

        AddRibbonTriangles(first, segments);
    }

    // An open arc stroke around center (degrees; over 360 overlaps its start).
    private void AddPencilArc(Vector3 center, float radius, float startDegrees, float sweepDegrees, float width, Color color, System.Random random)
    {
        int segments = Mathf.Clamp(Mathf.CeilToInt(sweepDegrees / 8f), 8, 64);
        float wobblePhase = 10f * Next(random);
        float arcLength = radius * sweepDegrees * Mathf.Deg2Rad;

        int first = vertices.Count;
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = (startDegrees + sweepDegrees * t) * Mathf.Deg2Rad;
            Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float r = radius + Mathf.Sin(t * 5f + wobblePhase) * strokeWobble;
            float taper = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) / 0.1f);
            float halfWidth = width * 0.5f * Mathf.Lerp(0.5f, 1f, taper);
            Color c = Fade(color, Mathf.Lerp(0.2f, 1f, taper));

            vertices.Add(center + radial * (r - halfWidth));
            vertices.Add(center + radial * (r + halfWidth));
            colors.Add(c);
            colors.Add(c);
            uvs.Add(new Vector2(t * arcLength, 0f));
            uvs.Add(new Vector2(t * arcLength, 1f));
        }

        AddRibbonTriangles(first, segments);
    }

    // Diagonal pencil hatching clipped to a circle.
    private void AddHatching(Vector3 center, float radius, Color color, System.Random random)
    {
        Vector3 direction = new Vector3(1f, 0f, 1f).normalized;
        Vector3 across = Vector3.Cross(Vector3.up, direction);
        int count = Mathf.FloorToInt(radius / hatchSpacing);
        for (int k = -count; k <= count; k++)
        {
            float offset = k * hatchSpacing + hatchSpacing * 0.25f * Signed(random);
            float half = Mathf.Sqrt(Mathf.Max(0f, radius * radius - offset * offset));
            if (half < hatchSpacing * 0.5f)
            {
                continue;
            }

            Vector3 mid = center + across * offset;
            AddPencilStroke(mid - direction * half, mid + direction * half, sketchLineWidth * 0.55f, Fade(color, 0.7f + 0.3f * Next(random)), random);
        }
    }

    // Bubble-sheet fill: one continuous pencil line of small loops that travels
    // around the circle while spiralling to its middle. Loops overlap, so the
    // graphite builds up darker where they cross, like a hand-filled answer bubble.
    private void AddScribbleFill(Vector3 center, float radius, Color color, System.Random random)
    {
        float loopRadius = radius * scribbleLoopSize;
        // Loops reach the outline on the first trip around.
        float outer = Mathf.Max(0f, radius - loopRadius * 0.8f);
        // A non-whole loop count so successive trips don't line up into a rosette.
        float loops = Mathf.Max(1f, scribbleLoopsPerTurn * scribbleTurns + 0.37f);
        const int samplesPerLoop = 14;
        int count = Mathf.CeilToInt(loops * samplesPerLoop);
        float startAngle = Mathf.PI * 2f * Next(random);
        float direction = Next(random) < 0.5f ? 1f : -1f;
        float noiseX = 100f * Next(random);
        float noiseY = 100f * Next(random);

        scribblePoints.Clear();
        for (int i = 0; i <= count; i++)
        {
            float t = (float)i / count;
            float loopsSoFar = t * loops;

            // Smooth hand irregularity: loop size, drift off the path, and timing.
            float sizeNoise = Mathf.PerlinNoise(noiseX, loopsSoFar * 0.45f) - 0.5f;
            float driftNoise = Mathf.PerlinNoise(noiseY, loopsSoFar * 0.3f) - 0.5f;
            float timingNoise = Mathf.PerlinNoise(noiseX + 37f, loopsSoFar * 0.6f) - 0.5f;

            // Travel: around the circle, spiralling in (ending near the middle).
            float travelAngle = startAngle + direction * t * scribbleTurns * Mathf.PI * 2f;
            float travelRadius = outer * (1f - t * 0.95f) + loopRadius * scribbleMessiness * 0.8f * driftNoise;
            Vector3 travel = new Vector3(Mathf.Cos(travelAngle), 0f, Mathf.Sin(travelAngle)) * travelRadius;

            // Loop: circling the travel point, turning the same way as the travel.
            float loopAngle = startAngle + direction * (loopsSoFar * Mathf.PI * 2f + scribbleMessiness * 2.5f * timingNoise);
            float loopSize = loopRadius * (1f + scribbleMessiness * 1.4f * sizeNoise);
            Vector3 loop = new Vector3(Mathf.Cos(loopAngle), 0f, Mathf.Sin(loopAngle)) * loopSize;

            Vector3 point = travel + loop;
            float reach = radius * (1f + 0.08f * scribbleMessiness);
            if (point.magnitude > reach)
            {
                point *= reach / point.magnitude;
            }

            scribblePoints.Add(center + point);
        }

        AddPencilPolyline(scribblePoints, sketchLineWidth * 0.75f, color);
    }

    private readonly List<Vector3> scribblePoints = new List<Vector3>();

    // A continuous stroke through points (chart plane): uv.x = distance along,
    // uv.y = 0..1 across, fading in and out at the ends.
    private void AddPencilPolyline(List<Vector3> points, float width, Color color)
    {
        if (points.Count < 2)
        {
            return;
        }

        float total = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            total += (points[i] - points[i - 1]).magnitude;
        }

        int first = vertices.Count;
        float distance = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0)
            {
                distance += (points[i] - points[i - 1]).magnitude;
            }

            Vector3 tangent = points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)];
            Vector3 side = tangent.sqrMagnitude > 1e-12f ? Vector3.Cross(Vector3.up, tangent.normalized) : Vector3.right;
            float t = total > 0f ? distance / total : 0f;
            float taper = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) / 0.04f);
            float halfWidth = width * 0.5f * Mathf.Lerp(0.6f, 1f, taper);
            Color c = Fade(color, Mathf.Lerp(0.3f, 1f, taper));

            vertices.Add(points[i] - side * halfWidth);
            vertices.Add(points[i] + side * halfWidth);
            colors.Add(c);
            colors.Add(c);
            uvs.Add(new Vector2(distance, 0f));
            uvs.Add(new Vector2(distance, 1f));
        }

        AddRibbonTriangles(first, points.Count - 1);
    }

    // Ruled squares clipped to the chart circle, drawn first so everything sits on top.
    private void AddSketchGrid()
    {
        int count = Mathf.FloorToInt(chartRadius / sketchGridSquareSize);
        for (int k = -count; k <= count; k++)
        {
            float offset = k * sketchGridSquareSize;
            float half = Mathf.Sqrt(Mathf.Max(0f, chartRadius * chartRadius - offset * offset));
            if (half <= 0f)
            {
                continue;
            }

            bool major = sketchGridMajorEvery > 0 && k % sketchGridMajorEvery == 0;
            Color color = MeshColor(major ? sketchGridMajorColor : sketchGridColor);
            float width = major ? sketchGridLineWidth * 1.8f : sketchGridLineWidth;
            AddRuledLine(new Vector3(offset, 0f, -half), new Vector3(offset, 0f, half), width, color);
            AddRuledLine(new Vector3(-half, 0f, offset), new Vector3(half, 0f, offset), width, color);
        }
    }

    // A straight, even printed line (no wobble or taper), with the stroke UVs
    // the pencil shader needs for its soft edges.
    private void AddRuledLine(Vector3 a, Vector3 b, float width, Color color)
    {
        Vector3 along = b - a;
        float length = along.magnitude;
        if (length < 1e-5f)
        {
            return;
        }

        // The shader's soft edge eats into the ribbon; widen so the visible line matches 'width'.
        Vector3 side = Vector3.Cross(Vector3.up, along / length) * width;
        int first = vertices.Count;
        vertices.Add(a - side);
        vertices.Add(a + side);
        vertices.Add(b - side);
        vertices.Add(b + side);
        for (int i = 0; i < 4; i++)
        {
            colors.Add(color);
        }

        uvs.Add(new Vector2(0f, 0f));
        uvs.Add(new Vector2(0f, 1f));
        uvs.Add(new Vector2(length, 0f));
        uvs.Add(new Vector2(length, 1f));
        AddRibbonTriangles(first, 1);
    }

    private void AddRibbonTriangles(int first, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            int v = first + i * 2;
            triangles.Add(v);
            triangles.Add(v + 1);
            triangles.Add(v + 3);
            triangles.Add(v);
            triangles.Add(v + 3);
            triangles.Add(v + 2);
        }
    }

    private static float Next(System.Random random) => (float)random.NextDouble();

    private static float Signed(System.Random random) => (float)random.NextDouble() * 2f - 1f;

    // Inspector colors are sRGB; vertex colors reach the shader unconverted, so
    // convert them in a linear-space project to match what the picker shows.
    private static Color MeshColor(Color color)
    {
        return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
    }

    private static Color Fade(Color color, float factor)
    {
        color.a *= factor;
        return color;
    }

    // ---------------------------------------------------------------- Labels

    private void BuildLabels()
    {
        DestroyLabels();
        if (!IsSketch || marks == null)
        {
            return;
        }

        bool labels = showLabels && labelFont != null;
        bool title = showTitle && titleFont != null;
        if (!labels && !title)
        {
            return;
        }

        var holder = new GameObject("Sketch Labels (generated)") { hideFlags = HideFlags.DontSave };
        labelRoot = holder.transform;
        labelRoot.SetParent(transform, false);
        labelRoot.localPosition = new Vector3(0f, markHeight + 0.0002f, 0f);

        var random = new System.Random(sketchSeed + 101);

        if (labels)
        {
            List<ConstellationGenerator.StarData> stars = constellationGenerator.stars;
            for (int i = 0; i < stars.Count && i < starPositions.Count; i++)
            {
                TextMeshPro label = CreateText(stars[i].starName.ToUpperInvariant(), labelFont, labelCapHeight);
                Vector2 halfSize = HalfSize(label);
                PlaceText(label, ChooseLabelSpot(starPositions[i], halfSize), Signed(random) * labelTilt);
            }
        }

        if (title)
        {
            string text = string.IsNullOrEmpty(titleText) ? constellationGenerator.constellationName : titleText;
            if (!string.IsNullOrEmpty(text))
            {
                TextMeshPro titleLabel = CreateText(text, titleFont, titleCapHeight);
                Vector3 position = new Vector3(titlePosition.x, 0f, titlePosition.y) * chartRadius;
                PlaceText(titleLabel, position, -2f);

                if (underlineTitle)
                {
                    AddTitleUnderline(position, HalfSize(titleLabel));
                }
            }
        }
    }

    // The underline is a pencil stroke in the marks mesh: append and re-upload.
    private void AddTitleUnderline(Vector3 titleCenter, Vector2 half)
    {
        var random = new System.Random(sketchSeed + 202);
        int first = vertices.Count;
        Color pencil = MeshColor(new Color(pencilColor.r, pencilColor.g, pencilColor.b, strokeStrength));
        float z = titleCenter.z - half.y - titleCapHeight * 0.2f;
        for (int k = 0; k < 2; k++)
        {
            Vector3 a = new Vector3(titleCenter.x - half.x * (1.05f + 0.1f * Next(random)), 0f, z + 0.0005f * Signed(random) + 0.0006f);
            Vector3 b = new Vector3(titleCenter.x + half.x * (1.1f + 0.1f * Next(random)), 0f, z + 0.0005f * Signed(random) - 0.0004f);
            AddPencilStroke(a, b, sketchLineWidth * (k == 0 ? 1.4f : 0.8f), Fade(pencil, k == 0 ? 0.85f : 0.45f), random);
        }

        UploadMarks(first);
    }

    private TextMeshPro CreateText(string text, TMP_FontAsset font, float capHeight)
    {
        var go = new GameObject(text) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(labelRoot, false);
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.font = font;
        tmp.fontSharedMaterial = LabelMaterial(font);
        tmp.extraPadding = true;
        tmp.text = text;
        tmp.fontSize = 10f;
        tmp.color = labelColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(100f, 20f);
        if (go.TryGetComponent(out MeshRenderer textRenderer))
        {
            textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
        }

        // Scale so the capital letters come out capHeight tall: measure an "H".
        Vector2 h = tmp.GetPreferredValues("H");
        float capInText = h.y * (font.faceInfo.capLine / Mathf.Max(1e-4f, font.faceInfo.lineHeight));
        tmp.transform.localScale = Vector3.one * (capInText > 0f ? capHeight / capInText : 0.001f);
        return tmp;
    }

    private readonly Dictionary<TMP_FontAsset, Material> labelMaterials = new Dictionary<TMP_FontAsset, Material>();

    // One generated copy of each font's material, carrying the weight and halo
    // settings (the font asset's own material stays untouched).
    private Material LabelMaterial(TMP_FontAsset font)
    {
        if (!labelMaterials.TryGetValue(font, out Material material) || material == null)
        {
            material = new Material(font.material) { name = font.name + " (chart label)", hideFlags = HideFlags.DontSave };
            labelMaterials[font] = material;
        }

        material.SetFloat(ShaderUtilities.ID_FaceDilate, labelWeight);
        if (labelHalo)
        {
            material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            material.SetColor(ShaderUtilities.ID_UnderlayColor, labelHaloColor);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, labelHaloSize);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, Mathf.Lerp(0.3f, 0.8f, labelHaloSize));
        }
        else
        {
            material.DisableKeyword(ShaderUtilities.Keyword_Underlay);
        }

        ShaderUtilities.GetShaderPropertyIDs();
        ShaderUtilities.UpdateShaderRatios(material);
        return material;
    }

    private void DestroyLabelMaterials()
    {
        foreach (Material material in labelMaterials.Values)
        {
            if (material == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }

        labelMaterials.Clear();
    }

    // Half width and depth of the text on the chart (local units).
    private static Vector2 HalfSize(TextMeshPro tmp)
    {
        tmp.ForceMeshUpdate();
        Bounds bounds = tmp.textBounds;
        float scale = tmp.transform.localScale.x;
        return new Vector2(bounds.extents.x * scale, bounds.extents.y * scale);
    }

    private static void PlaceText(TextMeshPro tmp, Vector3 chartPosition, float tiltDegrees)
    {
        // Lying on the chart, reading upright with the image top at +Z.
        tmp.transform.localPosition = chartPosition;
        tmp.transform.localRotation = Quaternion.AngleAxis(tiltDegrees, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
    }

    // Above the star unless that's crowded by lines, stars or the rim; then
    // the clearest of the other sides.
    private Vector3 ChooseLabelSpot(Vector3 star, Vector2 half)
    {
        float reach = sketchStarRadius + labelGap;
        Vector3[] offsets =
        {
            new Vector3(0f, 0f, reach + half.y),
            new Vector3(0f, 0f, -(reach + half.y)),
            new Vector3(reach * 0.7f + half.x, 0f, reach * 0.7f + half.y * 0.6f),
            new Vector3(-(reach * 0.7f + half.x), 0f, reach * 0.7f + half.y * 0.6f),
            new Vector3(reach * 0.7f + half.x, 0f, -(reach * 0.7f + half.y * 0.6f)),
            new Vector3(-(reach * 0.7f + half.x), 0f, -(reach * 0.7f + half.y * 0.6f)),
        };
        float[] preference = { 0.0012f, 0.0006f, 0.0003f, 0.0003f, 0f, 0f };

        Vector3 best = star + offsets[0];
        float bestScore = float.MinValue;
        for (int c = 0; c < offsets.Length; c++)
        {
            Vector3 center = star + offsets[c];
            float clearance = Clearance(center, half);
            if (clearance == float.MinValue)
            {
                continue;
            }

            float score = Mathf.Min(clearance, 0.004f) + preference[c];
            if (score > bestScore)
            {
                bestScore = score;
                best = center;
            }
        }

        return best;
    }

    // Smallest distance from the label's box to any line or star circle;
    // float.MinValue if the box leaves the printable circle.
    private float Clearance(Vector3 center, Vector2 half)
    {
        float limit = chartRadius * 0.93f;
        float clearance = float.MaxValue;
        for (int ix = -2; ix <= 2; ix++)
        {
            for (int iz = -1; iz <= 1; iz++)
            {
                Vector3 p = center + new Vector3(half.x * ix * 0.5f, 0f, half.y * iz);
                if (p.magnitude > limit)
                {
                    return float.MinValue;
                }

                for (int s = 0; s < segmentA.Count; s++)
                {
                    clearance = Mathf.Min(clearance, DistanceToSegment(p, segmentA[s], segmentB[s]));
                }

                foreach (Vector3 other in starPositions)
                {
                    clearance = Mathf.Min(clearance, Vector3.Distance(p, other) - sketchStarRadius);
                }
            }
        }

        return clearance;
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-9f, ab.sqrMagnitude));
        return Vector3.Distance(p, a + ab * t);
    }

    private void DestroyLabels()
    {
        if (labelRoot == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(labelRoot.gameObject);
        }
        else
        {
            DestroyImmediate(labelRoot.gameObject);
        }

        labelRoot = null;
    }

    // ------------------------------------------------------------ Primitives
    // Flat shapes in the chart plane (y = 0 here, raised to mark height on
    // upload). The marks shaders draw both sides, so winding doesn't matter.

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
