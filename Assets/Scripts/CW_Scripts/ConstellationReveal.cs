using System;
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
///
/// Display Hold mode (the swapped layout, where the compression plays as the
/// puzzle-start intro instead, see ConstellationIntro): no compression or
/// tilt; after the same waits it holds at once, presenting the pattern where
/// it shows on the Earth view display.
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

    public enum PostSolveMode
    {
        [Tooltip("Hold on the Earth view display: no compression or tilt (the compression plays at puzzle start instead).")]
        DisplayHold,

        [Tooltip("The original reward: compress the solved sky along the sightlines, then tilt it toward the player.")]
        CompressAndTilt
    }

    [Tooltip("Play this reveal after each solve.")]
    [SerializeField]
    private bool revealEnabled = true;

    [Tooltip("What happens after the solve. Existing scenes keep Compress And Tilt.")]
    [SerializeField]
    private PostSolveMode mode = PostSolveMode.CompressAndTilt;

    [Tooltip("Display Hold: the Earth view display the pattern is presented on (normal = local +Y, picture up = local +Z, like the Earth View screens).")]
    [SerializeField]
    private Renderer presentationDisplay;

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

    private StarRecord[] stars = new StarRecord[0];
    private ConstellationGeometry geometry;
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

    private readonly SightlineGuides guides = new SightlineGuides();
    private SightlineGuideSettings guideSettings;

    public Phase CurrentPhase { get; private set; } = Phase.Idle;

    /// <summary>Raised once the pattern has finished turning toward the player and is holding still.</summary>
    public event Action Presented;

    /// <summary>Raised at the end of every ResetReveal (reset, re-preview, disable).</summary>
    public event Action Cleared;

    /// <summary>
    /// The held presentation, in world space: the pattern's center, its
    /// right/up axes as the player reads it, the direction back toward the
    /// player, and its half extents along right/up. Only valid while Holding.
    /// </summary>
    public bool TryGetPresentation(out Vector3 center, out Vector3 right, out Vector3 up, out Vector3 towardPlayer, out Vector2 halfSize)
    {
        if (mode == PostSolveMode.DisplayHold)
        {
            return TryGetDisplayPresentation(out center, out right, out up, out towardPlayer, out halfSize);
        }

        bool valid = CurrentPhase == Phase.Holding && root != null;
        center = valid ? root.TransformPoint(centerLocal) : Vector3.zero;
        right = presentRight;
        up = presentUp;
        towardPlayer = -presentForward;
        halfSize = planeHalfSize;
        return valid;
    }

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
        guides.DestroyAll();
    }

    private void LateUpdate()
    {
        if (guides.IsBuilt)
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
        Cleared?.Invoke();
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

        if (mode == PostSolveMode.DisplayHold)
        {
            // The pattern already faces the player on the display; the Earth
            // view camera stays live.
            CurrentPhase = Phase.Holding;
            routine = null;
            if (logChecks && TryGetDisplayPresentation(out Vector3 c, out _, out _, out _, out Vector2 h))
            {
                Debug.Log($"ConstellationReveal: holding on the display (pattern center {c}, half size {h.x:F3} x {h.y:F3} m).", this);
            }

            Presented?.Invoke();
            yield break;
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
        Presented?.Invoke();
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
    public static Vector3 FlattenAlongSightline(Vector3 point, Vector3 origin, Vector3 forward, float distance) =>
        SightlineCompression.FlattenAlongSightline(point, origin, forward, distance);

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

        var found = new List<Transform>();
        foreach (GeneratedConstellationStar star in setup.SkyVisualContainer.GetComponentsInChildren<GeneratedConstellationStar>(true))
        {
            if (star.GetComponentInParent<SkyStamp3DVFX>(true) == null)
            {
                found.Add(star.transform);
            }
        }

        geometry = ConstellationGeometry.Collect(found);
        if (geometry == null)
        {
            return false;
        }

        root = geometry.Root;
        stars = new StarRecord[geometry.Stars.Length];
        for (int i = 0; i < stars.Length; i++)
        {
            stars[i] = new StarRecord { transform = geometry.Stars[i].transform, baseLocal = geometry.Stars[i].baseLocal };
        }

        return true;
    }

    private void UpdateConnections()
    {
        geometry?.UpdateConnections();
    }

    private void RestoreBaseline()
    {
        if (!modified)
        {
            return;
        }

        geometry?.Restore();
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
        guides.Build("Reveal", stars.Length, guideShader, "ConstellationReveal");
        UpdateGuides();
    }

    // Applies the Inspector settings each frame, so they can be compared live.
    private void UpdateGuides()
    {
        guideSettings ??= new SightlineGuideSettings();
        guideSettings.showRays = showRayGuides;
        guideSettings.dashed = dashedRayGuides;
        guideSettings.rayColor = rayGuideColor;
        guideSettings.rayThickness = rayGuideThickness;
        guideSettings.dashLength = dashLength;
        guideSettings.gapLength = gapLength;
        guideSettings.showPlaneOutline = showPlaneOutline;
        guideSettings.planeOutlineColor = planeOutlineColor;
        guideSettings.planeOutlineThickness = planeOutlineThickness;
        guideSettings.showPlaneFill = showPlaneFill;
        guideSettings.planeFillColor = planeFillColor;
        guideSettings.planePadding = planePadding;

        var landing = new Vector3[stars.Length];
        var current = new Vector3[stars.Length];
        for (int i = 0; i < stars.Length; i++)
        {
            landing[i] = stars[i].flatWorld;
            current[i] = stars[i].transform != null ? stars[i].transform.position : stars[i].flatWorld;
        }

        guides.Update(guideSettings, landing, current, planeCenter, planeRight, planeUp, planeHalfSize);
    }

    private void DestroyGuides()
    {
        guides.Destroy();
    }

    // ---- Display Hold presentation ----

    // The pattern as it shows on the display: the bounds of the stars' Earth
    // view positions, mapped through the display's UVs (viewport (u, v) lands
    // at local ((u - 0.5) * width, surface, (v - 0.5) * width)).
    private bool TryGetDisplayPresentation(out Vector3 center, out Vector3 right, out Vector3 up, out Vector3 towardPlayer, out Vector2 halfSize)
    {
        center = right = up = towardPlayer = Vector3.zero;
        halfSize = Vector2.zero;
        if (CurrentPhase != Phase.Holding || presentationDisplay == null || earthView == null || setup == null ||
            setup.SkyVisualContainer == null || !CollectGeometry())
        {
            return false;
        }

        Camera cam = earthView.GetComponent<Camera>();
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (StarRecord star in stars)
        {
            if (star.transform == null)
            {
                continue;
            }

            Vector2 viewport = cam.WorldToViewportPoint(star.transform.position);
            min = Vector2.Min(min, viewport);
            max = Vector2.Max(max, viewport);
        }

        if (min.x > max.x)
        {
            return false;
        }

        Transform display = presentationDisplay.transform;
        Bounds local = presentationDisplay.localBounds;
        Vector2 mid = (min + max) * 0.5f;
        center = display.TransformPoint(new Vector3(
            local.center.x + (mid.x - 0.5f) * local.size.x,
            local.center.y,
            local.center.z + (mid.y - 0.5f) * local.size.z));
        right = display.right;
        up = display.forward;
        towardPlayer = display.up;
        halfSize = new Vector2(
            (max.x - min.x) * 0.5f * local.size.x * display.lossyScale.x,
            (max.y - min.y) * 0.5f * local.size.z * display.lossyScale.z);
        return true;
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
