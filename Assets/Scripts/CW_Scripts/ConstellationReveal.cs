using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Solve reward, part 2: flattens the solved sky constellation along the Earth
/// observer's sightlines, then tilts the flat pattern toward the player.
///
/// Compression: the Earth view is a perspective camera at the Earth observer
/// (EarthViewCamera). Each star slides along its own ray from that camera
/// through its solved position, to where the ray meets one plane square to
/// the camera's forward axis. Any point on a ray projects to the same pixel,
/// so the Earth view image stays fixed through the whole move; only depth
/// goes away.
///
/// Rotation: the flat pattern turns rigidly about the center of its bounds so
/// it faces the player's head (captured once). The Earth view's right/up
/// carry over to the player's right/up, so it reads upright and unmirrored.
///
/// Presentation only: star transforms are moved in their own local space and
/// put back exactly on reset; the constellation data is never touched.
/// Starts after ConstellationSetup's canonical settle, once the solve stamp
/// and the diorama celebration (if any) have finished.
/// </summary>
public sealed class ConstellationReveal : MonoBehaviour
{
    public enum Phase
    {
        Idle,
        Waiting,
        Compressing,
        Paused,
        Rotating,
        Holding
    }

    [Tooltip("Play this reveal after each solve.")]
    [SerializeField]
    private bool revealEnabled = true;

    [Header("References")]
    [SerializeField]
    private ConstellationSetup setup;

    [SerializeField]
    private ConstellationSolveController solveController;

    [Tooltip("The Earth view camera; its projection defines the sightlines.")]
    [SerializeField]
    private EarthViewCamera earthView;

    [Tooltip("Optional: waits for its hops to finish before starting.")]
    [SerializeField]
    private DioramaCelebration celebration;

    [Tooltip("Optional: the spherical volume, to report stars that end up outside it.")]
    [SerializeField]
    private CircularWindow volume;

    [Tooltip("The player's head (CenterEyeAnchor). Empty = Camera.main.")]
    [SerializeField]
    private Transform playerHead;

    [Tooltip("The Earth View Disc's screen: powers off (CRT style) when the rotation starts and back on at reset. The disc itself stays on the table.")]
    [SerializeField]
    private ScreenPower earthViewScreen;

    [Tooltip("Optional: objects to hide outright when the rotation starts; restored on reset.")]
    [SerializeField]
    private GameObject[] hideWhenRotating = new GameObject[0];

    [Header("Start")]
    [Tooltip("Extra wait after a real solve, once the settle, solve stamp and diorama hops have all finished, before compression begins (s). Preview Reveal skips it.")]
    [SerializeField]
    [Min(0f)]
    private float startDelay = 0.75f;

    [Header("Compression")]
    [Tooltip("Distance of the flattening plane from the Earth observer, measured along the Earth view's forward axis, as a multiple of the distance to the constellation's pivot (its star centroid). 1 = plane through the centroid; less = nearer the observer, so the flat pattern is smaller. Relative, so it holds at any sky scale; the world distance is logged.")]
    [SerializeField]
    [Min(0.05f)]
    private float planeDistance = 1f;

    [SerializeField]
    [Min(0f)]
    private float compressionDuration = 1.2f;

    [Tooltip("Progress (0..1) over normalized time (0..1).")]
    [SerializeField]
    private AnimationCurve compressionEasing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Pause")]
    [Tooltip("Wait between compressing and rotating (s).")]
    [SerializeField]
    [Min(0f)]
    private float pauseDuration = 0.5f;

    [Tooltip("Debug: stay flattened (after the pause) until this is unticked, to inspect the result and the Earth view.")]
    [SerializeField]
    private bool holdAfterCompression = false;

    [Header("Rotation")]
    [SerializeField]
    [Min(0f)]
    private float rotationDuration = 1.5f;

    [Tooltip("Progress (0..1) over normalized time (0..1).")]
    [SerializeField]
    private AnimationCurve rotationEasing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Guides: Ray Paths")]
    [Tooltip("During compression (and the pause / hold), draw each star's path from its solved position to the plane. Visual only; hidden before the rotation. Not drawn in the Earth view.")]
    [SerializeField]
    private bool showRayGuides = true;

    [SerializeField]
    private bool dashedRayGuides = true;

    [SerializeField]
    private Color rayGuideColor = new Color(0.75f, 0.85f, 1f, 0.5f);

    [Tooltip("Line width (world m).")]
    [SerializeField]
    [Min(0.0001f)]
    private float rayGuideThickness = 0.0012f;

    [Tooltip("Dash length (world m).")]
    [SerializeField]
    [Min(0.0005f)]
    private float dashLength = 0.006f;

    [Tooltip("Gap between dashes (world m).")]
    [SerializeField]
    [Min(0f)]
    private float gapLength = 0.004f;

    [Header("Guides: Destination Plane")]
    [Tooltip("During compression (and the pause / hold), outline the part of the compression plane the pattern lands on.")]
    [SerializeField]
    private bool showPlaneOutline = true;

    [SerializeField]
    private Color planeOutlineColor = new Color(0.75f, 0.85f, 1f, 0.35f);

    [Tooltip("Outline width (world m).")]
    [SerializeField]
    [Min(0.0001f)]
    private float planeOutlineThickness = 0.001f;

    [SerializeField]
    private bool showPlaneFill = false;

    [SerializeField]
    private Color planeFillColor = new Color(0.75f, 0.85f, 1f, 0.06f);

    [Tooltip("Margin around the flattened stars' bounds (world m).")]
    [SerializeField]
    [Min(0f)]
    private float planePadding = 0.012f;

    [Tooltip("CelestialWorkshop/GuideLine (referenced here so it ships in builds).")]
    [SerializeField]
    private Shader guideShader;

    [Header("Debug")]
    [Tooltip("Log the Earth-view drift check, the final straight-on check, and the volume containment.")]
    [SerializeField]
    private bool logChecks = true;

    private struct StarRecord
    {
        public Transform transform;
        public Vector3 baseLocal;
        public Vector3 startLocal;
        public Vector3 flatLocal;
        public Vector2 startViewport;
        public Vector3 startWorld;
        public Vector3 flatWorld;
    }

    private struct ConnectionRecord
    {
        public Transform transform;
        public int a;
        public int b;
        public Vector3 baseLocalPosition;
        public Quaternion baseLocalRotation;
        public Vector3 baseLocalScale;
    }

    private StarRecord[] stars = new StarRecord[0];
    private ConnectionRecord[] connections = new ConnectionRecord[0];
    private Transform root;
    private bool modified;
    private bool revealedThisSolve;
    private Coroutine routine;

    private bool previewOverride;
    private Quaternion savedPivotRotation;
    private bool savedMirror;

    private Vector3 centerLocal;
    private Quaternion rotationLocal = Quaternion.identity;
    private Vector3 headWorld;
    private Vector3 presentRight;
    private Vector3 presentUp;
    private Vector3 presentForward;

    // Compression plane, captured with the stars (world space).
    private Vector3 planeCenter;
    private Vector3 planeRight;
    private Vector3 planeUp;
    private Vector2 planeHalfSize;

    private readonly List<GameObject> hiddenByReveal = new List<GameObject>();

    private GameObject guideRoot;
    private LineRenderer[] rayGuides = new LineRenderer[0];
    private LineRenderer planeOutline;
    private MeshRenderer planeFill;
    private Mesh planeFillMesh;
    private Material rayMaterial;
    private Material outlineMaterial;
    private Material fillMaterial;

    public Phase CurrentPhase { get; private set; } = Phase.Idle;

    private void OnEnable()
    {
        if (setup != null)
        {
            setup.SolveSettled += OnSolveSettled;
        }
    }

    private void OnDisable()
    {
        if (setup != null)
        {
            setup.SolveSettled -= OnSolveSettled;
        }

        ResetReveal();
    }

    private void OnDestroy()
    {
        DestroyGuides();
        foreach (Material material in new[] { rayMaterial, outlineMaterial, fillMaterial })
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }

    private void LateUpdate()
    {
        if (guideRoot != null)
        {
            UpdateGuides();
        }
    }

    private void OnSolveSettled()
    {
        if (!revealEnabled || revealedThisSolve)
        {
            return;
        }

        if (solveController != null && !solveController.IsSolved)
        {
            return;
        }

        revealedThisSolve = true;
        StartSequence(true);
    }

    /// <summary>Reset hook: stops the reveal, restores the stars and connections, re-arms for the next solve.</summary>
    public void ResetReveal()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        DestroyGuides();
        RestoreHidden();
        if (earthViewScreen != null)
        {
            earthViewScreen.PowerOn();
        }

        RestoreBaseline();
        EndPreviewOverride();
        revealedThisSolve = false;
        CurrentPhase = Phase.Idle;
    }

    [ContextMenu("Preview Reveal")]
    private void PreviewReveal()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("ConstellationReveal: preview works in Play mode.", this);
            return;
        }

        bool wasRevealed = revealedThisSolve;
        ResetReveal();
        revealedThisSolve = wasRevealed;

        if (setup == null)
        {
            return;
        }

        if (solveController != null && !solveController.IsSolved)
        {
            // Pose the sky in its canonical solved orientation without solving:
            // detection and rotation mirroring pause until the preview resets.
            previewOverride = true;
            savedMirror = setup.MirrorRotation;
            savedPivotRotation = setup.SkyConstellationPivot.rotation;
            setup.MirrorRotation = false;
            solveController.SuspendDetection = true;
            setup.SkyConstellationPivot.rotation = setup.CanonicalSkyRotation;
        }

        StartSequence(false);
    }

    [ContextMenu("Reset Reveal")]
    private void ResetRevealFromMenu()
    {
        bool wasRevealed = revealedThisSolve;
        ResetReveal();
        revealedThisSolve = wasRevealed;
    }

    private void StartSequence(bool waitForOthers)
    {
        if (routine != null)
        {
            StopCoroutine(routine);
        }

        DestroyGuides();
        routine = StartCoroutine(Sequence(waitForOthers));
    }

    private IEnumerator Sequence(bool waitForOthers)
    {
        CurrentPhase = Phase.Waiting;
        while ((setup != null && setup.IsSettling) ||
               (waitForOthers && celebration != null && celebration.isActiveAndEnabled && celebration.IsPlaying) ||
               (waitForOthers && setup != null && setup.SkyStampVFX != null && setup.SkyStampVFX.IsPlaying))
        {
            yield return null;
        }

        if (waitForOthers && startDelay > 0f)
        {
            yield return new WaitForSeconds(startDelay);
        }

        if (!Capture())
        {
            CurrentPhase = Phase.Idle;
            routine = null;
            yield break;
        }

        // 1. Compress along the sightlines.
        CurrentPhase = Phase.Compressing;
        BuildGuides();
        float worstDriftPixels = 0f;
        float elapsed = 0f;
        while (true)
        {
            elapsed += Time.deltaTime;
            float t = compressionDuration > 0f ? Mathf.Clamp01(elapsed / compressionDuration) : 1f;
            float e = compressionEasing.Evaluate(t);
            for (int i = 0; i < stars.Length; i++)
            {
                stars[i].transform.localPosition = Vector3.LerpUnclamped(stars[i].startLocal, stars[i].flatLocal, e);
            }

            UpdateConnections();
            worstDriftPixels = Mathf.Max(worstDriftPixels, MeasureDriftPixels());
            if (t >= 1f)
            {
                break;
            }

            yield return null;
        }

        if (logChecks)
        {
            Debug.Log($"ConstellationReveal: compression done. Earth-view drift of star centers, worst over every frame: {worstDriftPixels:F4} px ({PixelSize().x}x{PixelSize().y}). {ContainmentReport()}", this);
        }

        // 2. Pause (and optional inspection hold).
        CurrentPhase = Phase.Paused;
        if (pauseDuration > 0f)
        {
            yield return new WaitForSeconds(pauseDuration);
        }

        while (holdAfterCompression)
        {
            yield return null;
        }

        // 3. Tilt toward the player, rigidly about the flat bounds' center.
        DestroyGuides();
        HideDisplays();
        if (earthViewScreen != null)
        {
            earthViewScreen.PowerOff();
        }

        CurrentPhase = Phase.Rotating;
        elapsed = 0f;
        while (true)
        {
            elapsed += Time.deltaTime;
            float t = rotationDuration > 0f ? Mathf.Clamp01(elapsed / rotationDuration) : 1f;
            Quaternion q = Quaternion.SlerpUnclamped(Quaternion.identity, rotationLocal, rotationEasing.Evaluate(t));
            for (int i = 0; i < stars.Length; i++)
            {
                stars[i].transform.localPosition = centerLocal + q * (stars[i].flatLocal - centerLocal);
            }

            UpdateConnections();
            if (t >= 1f)
            {
                break;
            }

            yield return null;
        }

        if (logChecks)
        {
            Debug.Log($"ConstellationReveal: rotation done. {StraightOnReport()} {ContainmentReport()}", this);
        }

        // 4. Hold.
        CurrentPhase = Phase.Holding;
        routine = null;
    }

    // ---- Capture ----

    private bool Capture()
    {
        if (setup == null || earthView == null || setup.SkyVisualContainer == null)
        {
            Debug.LogWarning("ConstellationReveal: missing references.", this);
            return false;
        }

        if (!CollectGeometry())
        {
            Debug.LogWarning("ConstellationReveal: no sky stars found.", this);
            return false;
        }

        RestoreBaseline();

        Camera cam = earthView.GetComponent<Camera>();
        Transform camTransform = cam.transform;
        Vector3 origin = camTransform.position;
        Vector3 forward = camTransform.forward;
        Vector3 right = camTransform.right;
        Vector3 up = camTransform.up;

        float pivotDepth = Vector3.Dot(setup.SkyConstellationPivot.position - origin, forward);
        float plane = planeDistance * pivotDepth;

        if (cam.orthographic)
        {
            Debug.LogWarning("ConstellationReveal: the Earth view camera is orthographic; compressing along its parallel sightlines.", this);
        }

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < stars.Length; i++)
        {
            Vector3 solved = stars[i].transform.position;
            Vector3 flat = cam.orthographic
                ? solved + forward * (plane - Vector3.Dot(solved - origin, forward))
                : FlattenAlongSightline(solved, origin, forward, plane);

            stars[i].startLocal = stars[i].baseLocal;
            stars[i].flatLocal = root.InverseTransformPoint(flat);
            stars[i].startViewport = cam.WorldToViewportPoint(solved);
            stars[i].startWorld = solved;
            stars[i].flatWorld = flat;

            Vector2 onPlane = new Vector2(Vector3.Dot(flat - origin, right), Vector3.Dot(flat - origin, up));
            min = Vector2.Min(min, onPlane);
            max = Vector2.Max(max, onPlane);
        }

        Vector2 mid = (min + max) * 0.5f;
        Vector3 centerWorld = origin + forward * plane + right * mid.x + up * mid.y;
        centerLocal = root.InverseTransformPoint(centerWorld);
        planeCenter = centerWorld;
        planeRight = right;
        planeUp = up;
        planeHalfSize = (max - min) * 0.5f;

        Transform head = playerHead != null ? playerHead : (Camera.main != null ? Camera.main.transform : null);
        headWorld = head != null ? head.position : centerWorld - forward;

        Vector3 toCenter = centerWorld - headWorld;
        Quaternion rotationWorld = PresentationRotation(forward, up, toCenter, Vector3.up);
        presentForward = rotationWorld * forward;
        presentUp = rotationWorld * up;
        presentRight = rotationWorld * right;

        // Same rotation in the root's local space (uniform scale), so the
        // motion stays attached to the sky if the group ever moves.
        rotationLocal = Quaternion.Inverse(root.rotation) * rotationWorld * root.rotation;

        if (logChecks)
        {
            Debug.Log($"ConstellationReveal: Earth view {(cam.orthographic ? "orthographic" : $"perspective, fov {cam.fieldOfView:F2}")}; " +
                      $"plane {plane:F4} m from the observer along its forward (x{planeDistance:F2} of the pivot depth {pivotDepth:F4} m); " +
                      $"head {Vector3.Distance(headWorld, centerWorld):F3} m from the pattern center; tilt {Quaternion.Angle(Quaternion.identity, rotationWorld):F1} deg.", this);
        }

        modified = true;
        return true;
    }

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

    /// <summary>
    /// Turns the Earth view's frame (forward, up) to face along toPresentation
    /// (from the viewer to the pattern), keeping it upright against worldUp:
    /// Earth-view right/up map onto the viewer's right/up, never mirrored.
    /// Falls back to the smallest turn when looking straight up or down.
    /// </summary>
    public static Quaternion PresentationRotation(Vector3 earthForward, Vector3 earthUp, Vector3 toPresentation, Vector3 worldUp)
    {
        if (toPresentation.sqrMagnitude < 1e-12f)
        {
            return Quaternion.identity;
        }

        Vector3 forward = toPresentation.normalized;
        Vector3 up = Vector3.ProjectOnPlane(worldUp, forward);
        if (up.sqrMagnitude < 1e-6f)
        {
            up = Quaternion.FromToRotation(earthForward, forward) * earthUp;
        }

        return Quaternion.LookRotation(forward, up.normalized) *
               Quaternion.Inverse(Quaternion.LookRotation(earthForward, earthUp));
    }

    // ---- Geometry ----

    private bool CollectGeometry()
    {
        if (root != null && stars.Length > 0 && stars[0].transform != null)
        {
            return true;
        }

        var found = new List<GeneratedConstellationStar>();
        foreach (GeneratedConstellationStar star in setup.SkyVisualContainer.GetComponentsInChildren<GeneratedConstellationStar>(true))
        {
            if (star.GetComponentInParent<SkyStamp3DVFX>(true) == null)
            {
                found.Add(star);
            }
        }

        if (found.Count == 0)
        {
            return false;
        }

        root = found[0].transform.parent;
        stars = new StarRecord[found.Count];
        for (int i = 0; i < found.Count; i++)
        {
            stars[i] = new StarRecord { transform = found[i].transform, baseLocal = found[i].transform.localPosition };
        }

        // Pair each connection rod with the two stars it was built between.
        var links = new List<ConnectionRecord>();
        foreach (Transform child in root)
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
                links.Add(new ConnectionRecord
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

        connections = links.ToArray();
        return true;
    }

    private void UpdateConnections()
    {
        foreach (ConnectionRecord link in connections)
        {
            if (link.transform == null)
            {
                continue;
            }

            Vector3 pa = stars[link.a].transform.localPosition;
            Vector3 pb = stars[link.b].transform.localPosition;
            Vector3 span = pb - pa;
            float length = span.magnitude;
            link.transform.localPosition = (pa + pb) * 0.5f;
            if (length > 1e-7f)
            {
                link.transform.localRotation = Quaternion.FromToRotation(Vector3.up, span / length);
            }

            Vector3 scale = link.transform.localScale;
            scale.y = length * 0.5f;
            link.transform.localScale = scale;
        }
    }

    private void RestoreBaseline()
    {
        if (!modified)
        {
            return;
        }

        foreach (StarRecord star in stars)
        {
            if (star.transform != null)
            {
                star.transform.localPosition = star.baseLocal;
            }
        }

        foreach (ConnectionRecord link in connections)
        {
            if (link.transform != null)
            {
                link.transform.localPosition = link.baseLocalPosition;
                link.transform.localRotation = link.baseLocalRotation;
                link.transform.localScale = new Vector3(link.transform.localScale.x, link.baseLocalScale.y, link.transform.localScale.z);
            }
        }

        modified = false;
    }

    private void EndPreviewOverride()
    {
        if (!previewOverride)
        {
            return;
        }

        previewOverride = false;
        if (setup != null)
        {
            setup.SkyConstellationPivot.rotation = savedPivotRotation;
            setup.MirrorRotation = savedMirror;
        }

        if (solveController != null)
        {
            solveController.SuspendDetection = false;
        }
    }

    // ---- Earth view display ----

    // Only objects that were showing are hidden (and later restored), so
    // reset brings back exactly the previous visibility.
    private void HideDisplays()
    {
        foreach (GameObject display in hideWhenRotating)
        {
            if (display != null && display.activeSelf)
            {
                display.SetActive(false);
                hiddenByReveal.Add(display);
            }
        }
    }

    private void RestoreHidden()
    {
        foreach (GameObject display in hiddenByReveal)
        {
            if (display != null)
            {
                display.SetActive(true);
            }
        }

        hiddenByReveal.Clear();
    }

    // ---- Guides (visual only; built from the captured rays and plane) ----

    private void BuildGuides()
    {
        DestroyGuides();
        if (guideShader == null)
        {
            guideShader = Shader.Find("CelestialWorkshop/GuideLine");
        }

        if (guideShader == null)
        {
            Debug.LogWarning("ConstellationReveal: no guide shader; guides skipped.", this);
            return;
        }

        if (rayMaterial == null)
        {
            rayMaterial = new Material(guideShader) { name = "Reveal Ray Guide" };
            outlineMaterial = new Material(guideShader) { name = "Reveal Plane Outline" };
            fillMaterial = new Material(guideShader) { name = "Reveal Plane Fill" };
        }

        // Default layer: drawn in the headset, never in the Earth view (which
        // only renders the sky's WindowMasked layer).
        guideRoot = new GameObject("Reveal Guides");
        guideRoot.layer = 0;

        rayGuides = new LineRenderer[stars.Length];
        for (int i = 0; i < stars.Length; i++)
        {
            LineRenderer line = NewLine("Ray " + stars[i].transform.name, rayMaterial);
            line.positionCount = 2;
            line.textureMode = LineTextureMode.Tile;
            rayGuides[i] = line;
        }

        planeOutline = NewLine("Plane Outline", outlineMaterial);
        planeOutline.positionCount = 4;
        planeOutline.loop = true;

        var fillObject = new GameObject("Plane Fill");
        fillObject.layer = 0;
        fillObject.transform.SetParent(guideRoot.transform, false);
        planeFillMesh = new Mesh { name = "Reveal Plane Fill" };
        fillObject.AddComponent<MeshFilter>().sharedMesh = planeFillMesh;
        planeFill = fillObject.AddComponent<MeshRenderer>();
        planeFill.sharedMaterial = fillMaterial;
        planeFill.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        planeFill.receiveShadows = false;

        UpdateGuides();
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

    // Applies the Inspector settings each frame, so they can be compared live.
    private void UpdateGuides()
    {
        float period = dashLength + gapLength;
        rayMaterial.SetColor("_BaseColor", rayGuideColor);
        rayMaterial.SetFloat("_DashFraction", dashedRayGuides && gapLength > 0f ? dashLength / period : 1f);
        outlineMaterial.SetColor("_BaseColor", planeOutlineColor);
        outlineMaterial.SetFloat("_DashFraction", 1f);
        fillMaterial.SetColor("_BaseColor", planeFillColor);
        fillMaterial.SetFloat("_DashFraction", 1f);

        for (int i = 0; i < rayGuides.Length; i++)
        {
            LineRenderer line = rayGuides[i];
            if (line == null)
            {
                continue;
            }

            // Only the stretch still ahead of the star: from its plane point
            // back to where the star is now. The dash pattern starts at the
            // plane end, so dashes stay put while the line shortens.
            line.SetPosition(0, stars[i].flatWorld);
            line.SetPosition(1, stars[i].transform.position);
            line.enabled = showRayGuides;
            line.widthMultiplier = rayGuideThickness;
            line.textureScale = new Vector2(1f / Mathf.Max(1e-5f, period), 1f);
        }

        Vector2 half = planeHalfSize + Vector2.one * planePadding;
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
            planeOutline.enabled = showPlaneOutline;
            planeOutline.widthMultiplier = planeOutlineThickness;
            planeOutline.SetPositions(corners);
        }

        if (planeFill != null)
        {
            planeFill.enabled = showPlaneFill;
            planeFillMesh.vertices = corners;
            planeFillMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            planeFillMesh.RecalculateBounds();
        }
    }

    private void DestroyGuides()
    {
        if (guideRoot != null)
        {
            Destroy(guideRoot);
        }

        if (planeFillMesh != null)
        {
            Destroy(planeFillMesh);
        }

        guideRoot = null;
        planeFillMesh = null;
        planeOutline = null;
        planeFill = null;
        rayGuides = new LineRenderer[0];
    }

    // ---- Checks ----

    private Vector2 PixelSize()
    {
        RenderTexture target = earthView != null ? earthView.GetComponent<Camera>().targetTexture : null;
        return target != null ? new Vector2(target.width, target.height) : new Vector2(1024f, 1024f);
    }

    private float MeasureDriftPixels()
    {
        Camera cam = earthView.GetComponent<Camera>();
        Vector2 pixels = PixelSize();
        float worst = 0f;
        foreach (StarRecord star in stars)
        {
            Vector2 now = cam.WorldToViewportPoint(star.transform.position);
            worst = Mathf.Max(worst, Vector2.Scale(now - star.startViewport, pixels).magnitude);
        }

        return worst;
    }

    // Compares the pattern seen straight on from the captured head position
    // with the Earth view image: same shape after removing scale, same handedness.
    private string StraightOnReport()
    {
        int n = stars.Length;
        var earth = new Vector2[n];
        var player = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            earth[i] = stars[i].startViewport;
            Vector3 q = stars[i].transform.position - headWorld;
            float depth = Vector3.Dot(q, presentForward);
            player[i] = new Vector2(Vector3.Dot(q, presentRight), Vector3.Dot(q, presentUp)) / Mathf.Max(1e-6f, depth);
        }

        Normalize(earth);
        Normalize(player);
        float worst = 0f;
        for (int i = 0; i < n; i++)
        {
            worst = Mathf.Max(worst, (earth[i] - player[i]).magnitude);
        }

        bool sameHandedness = Mathf.Sign(SignedArea(earth)) == Mathf.Sign(SignedArea(player));
        return $"Straight on from the captured head: pattern matches the Earth view within {worst * 100f:F3}% of its size, " +
               $"{(sameHandedness ? "same handedness (not mirrored)" : "MIRRORED")}.";
    }

    private static void Normalize(Vector2[] points)
    {
        Vector2 centroid = Vector2.zero;
        foreach (Vector2 p in points)
        {
            centroid += p;
        }

        centroid /= points.Length;
        float rms = 0f;
        foreach (Vector2 p in points)
        {
            rms += (p - centroid).sqrMagnitude;
        }

        rms = Mathf.Sqrt(rms / points.Length);
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = (points[i] - centroid) / Mathf.Max(1e-9f, rms);
        }
    }

    // Sum of cross products about the centroid: its sign flips under mirroring.
    private static float SignedArea(Vector2[] points)
    {
        float area = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Length];
            area += a.x * b.y - a.y * b.x;
        }

        return area;
    }

    private string ContainmentReport()
    {
        if (volume == null)
        {
            return "";
        }

        Vector3 center = volume.transform.position;
        float radius = volume.Radius * volume.transform.lossyScale.x;
        int outside = 0;
        float farthest = 0f;
        foreach (StarRecord star in stars)
        {
            float d = Vector3.Distance(star.transform.position, center);
            farthest = Mathf.Max(farthest, d);
            if (d > radius)
            {
                outside++;
            }
        }

        return outside > 0
            ? $"WARNING: {outside} star(s) outside the volume (farthest {farthest:F3} m, radius {radius:F3} m); try a smaller Plane Distance."
            : $"All stars inside the volume (farthest {farthest:F3} m of {radius:F3} m).";
    }
}
