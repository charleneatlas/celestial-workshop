using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Puzzle-start intro: shows how the Earth view display's image comes from
/// the 3D constellation in the globe.
///
/// 1. A visuals-only copy of the hidden sky constellation (the one the Earth
///    view camera renders) appears exactly where it is, in the puzzle's
///    starting (unsolved) orientation, on the Default layer, so the player
///    sees it and the Earth view camera doesn't.
/// 2. Compression in place: each star slides along its own ray from the
///    Earth observer onto one plane square to the camera's forward axis, at
///    the sky pivot's depth (ConstellationReveal's sightline compression). A
///    point anywhere on a ray keeps its Earth view pixel, and each star scales
///    by plane depth / its depth, so the Earth image, star sizes included,
///    never changes.
/// 3. Onto the display: the flat pattern moves by a similarity (rotation,
///    uniform scale, translation) that takes the camera's slice plane onto the
///    display's surface, so viewport (u, v) lands where the display's UVs show
///    it: local ((u - 0.5) * width, surface, (v - 0.5) * width).
/// 4. Handoff: the live display shows, the copy goes, and rotation and solve
///    detection come back.
///
/// The source sky is never touched. Everything is evaluated from live
/// transforms each frame, so a re-aim mid-intro moves source, copy and
/// display together. Plays on every puzzle start (ConstellationSolveController
/// .PuzzleReset); PlayIntro() is public for a future placement event.
/// </summary>
public sealed class ConstellationIntro : MonoBehaviour
{
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

    [Header("References")]
    [SerializeField]
    private ConstellationSetup setup;

    [SerializeField]
    private ConstellationSolveController solveController;

    [Tooltip("The Earth view camera; its projection defines the sightlines and the image.")]
    [SerializeField]
    private EarthViewCamera earthView;

    [Tooltip("The large Earth view display (EarthViewOverlay material; normal = local +Y, picture up = local +Z). Hidden through its _Intensity until the handoff.")]
    [SerializeField]
    private Renderer display;

    [Tooltip("Waits for the volume and sky to be placed before starting.")]
    [SerializeField]
    private NearFieldSkyProxy nearFieldSkyProxy;

    [Header("Start")]
    [Tooltip("Play on every puzzle start: startup, Choose Another, reset.")]
    [SerializeField]
    private bool playOnPuzzleStart = true;

    [Tooltip("Wait after the puzzle starts (and the sky is placed) before the copy appears (s).")]
    [SerializeField]
    [Min(0f)]
    private float startDelay = 0.75f;

    [Tooltip("Show the 3D copy this long before it starts compressing (s).")]
    [SerializeField]
    [Min(0f)]
    private float showDuration = 0.6f;

    [Header("Compression (in place)")]
    [Tooltip("Plane depth along the Earth view's forward axis, as a multiple of the sky pivot's depth. 1 = through the pivot.")]
    [SerializeField]
    [Min(0.05f)]
    private float planeDistance = 1f;

    [SerializeField]
    [Min(0f)]
    private float compressionDuration = 1.2f;

    [SerializeField]
    private AnimationCurve compressionEasing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Wait between compressing and moving onto the display (s).")]
    [SerializeField]
    [Min(0f)]
    private float pauseDuration = 0.4f;

    [Header("Onto the display")]
    [SerializeField]
    [Min(0f)]
    private float mapDuration = 1f;

    [SerializeField]
    private AnimationCurve mapEasing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("The copy lands this far in front of the display's surface, toward the player (m).")]
    [SerializeField]
    [Min(0f)]
    private float surfaceOffset = 0.002f;

    [Header("Handoff")]
    [Tooltip("0 = switch straight from the copy to the live display. Otherwise the display fades in over this long (s) before the copy goes.")]
    [SerializeField]
    [Min(0f)]
    private float handoffFade = 0f;

    [Header("Guides")]
    [SerializeField]
    private SightlineGuideSettings guides = new SightlineGuideSettings();

    [Tooltip("CelestialWorkshop/GuideLine (referenced here so it ships in builds).")]
    [SerializeField]
    private Shader guideShader;

    [Header("Debug")]
    [Tooltip("Log the Earth view drift during compression, the landing error on the display, and containment.")]
    [SerializeField]
    private bool logChecks = true;

    private Coroutine routine;
    private bool suspendedDetection;
    private bool heldMirror;
    private MaterialPropertyBlock displayBlock;

    // Source (never modified) and the copy, star i and rod j matched.
    private Transform sourceRoot;
    private Transform[] sourceStars = new Transform[0];
    private GameObject copyObject;
    private ConstellationGeometry copy;
    private float[] rodDepthRatio = new float[0];
    private float[] rodThickness = new float[0];

    private readonly SightlineGuides guideLines = new SightlineGuides();

    /// <summary>True from PlayIntro until the handoff (or a stop). Rotation and solving stand down meanwhile.</summary>
    public bool IsPlaying => routine != null;

    /// <summary>Raised after the handoff, once rotation and solving are back on.</summary>
    public event System.Action Finished;

    private void OnEnable()
    {
        if (solveController != null)
        {
            solveController.PuzzleReset += OnPuzzleReset;
        }

        // Keep the display dark from the very first frame; the intro reveals it.
        if (playOnPuzzleStart)
        {
            SetDisplayFade(0f);
        }
    }

    private void OnDisable()
    {
        if (solveController != null)
        {
            solveController.PuzzleReset -= OnPuzzleReset;
        }

        StopIntro();
    }

    private void OnDestroy()
    {
        guideLines.DestroyAll();
    }

    private void OnPuzzleReset()
    {
        if (playOnPuzzleStart)
        {
            PlayIntro();
        }
    }

    /// <summary>Plays the intro for the current puzzle orientation (restarts it if already playing).</summary>
    [ContextMenu("Play Intro")]
    public void PlayIntro()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        StopIntro();
        SetDisplayFade(0f);

        // No solve while the copy is out, even during the wait.
        if (solveController != null && !solveController.SuspendDetection)
        {
            solveController.SuspendDetection = true;
            suspendedDetection = true;
        }

        routine = StartCoroutine(Run());
    }

    /// <summary>Stops the intro and restores a playable state: no copy, display on, rotation and solving back.</summary>
    [ContextMenu("Stop Intro")]
    public void StopIntro()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        DestroyCopy();
        guideLines.Destroy();
        SetDisplayFade(1f);
        ReleaseLocks();
    }

    private void ReleaseLocks()
    {
        if (heldMirror && setup != null)
        {
            setup.MirrorRotation = true;
        }

        if (suspendedDetection && solveController != null)
        {
            solveController.SuspendDetection = false;
        }

        heldMirror = false;
        suspendedDetection = false;
    }

    private IEnumerator Run()
    {
        while (nearFieldSkyProxy != null && !nearFieldSkyProxy.IsPlaced)
        {
            yield return null;
        }

        // At least one frame, so the mirror has put the newly randomized
        // mini's rotation onto the sky.
        yield return null;
        if (startDelay > 0f)
        {
            yield return new WaitForSeconds(startDelay);
        }

        if (setup == null || earthView == null || display == null || setup.SkyVisualContainer == null || !BuildCopy())
        {
            Debug.LogWarning("ConstellationIntro: missing references or no sky stars; showing the display without the intro.", this);
            routine = null;
            StopIntro();
            yield break;
        }

        // Hold the puzzle orientation: source, mini and copy all stay put.
        if (setup.MirrorRotation)
        {
            setup.MirrorRotation = false;
            heldMirror = true;
        }

        Camera cam = earthView.GetComponent<Camera>();

        // 1. Show the 3D copy.
        PoseCopy(cam, 0f, 0f);
        if (showDuration > 0f)
        {
            float shown = 0f;
            while (shown < showDuration)
            {
                shown += Time.deltaTime;
                PoseCopy(cam, 0f, 0f);
                yield return null;
            }
        }

        // 2. Compress in place along the sightlines.
        guideLines.Build("Intro", sourceStars.Length, guideShader, "ConstellationIntro");
        float worstDrift = 0f;
        float elapsed = 0f;
        while (true)
        {
            elapsed += Time.deltaTime;
            float t = compressionDuration > 0f ? Mathf.Clamp01(elapsed / compressionDuration) : 1f;
            PoseCopy(cam, compressionEasing.Evaluate(t), 0f);
            UpdateGuides(cam);
            worstDrift = Mathf.Max(worstDrift, MeasureDriftPixels(cam));
            if (t >= 1f)
            {
                break;
            }

            yield return null;
        }

        if (logChecks)
        {
            Debug.Log($"ConstellationIntro: compressed. Earth view drift of the copy's star centers, worst over every frame: {worstDrift:F4} px. {ContainmentReport()}", this);
        }

        // 3. Pause, then onto the display.
        float paused = 0f;
        while (paused < pauseDuration)
        {
            paused += Time.deltaTime;
            PoseCopy(cam, 1f, 0f);
            UpdateGuides(cam);
            yield return null;
        }

        guideLines.Destroy();
        elapsed = 0f;
        while (true)
        {
            elapsed += Time.deltaTime;
            float t = mapDuration > 0f ? Mathf.Clamp01(elapsed / mapDuration) : 1f;
            PoseCopy(cam, 1f, mapEasing.Evaluate(t));
            if (t >= 1f)
            {
                break;
            }

            yield return null;
        }

        if (logChecks)
        {
            Debug.Log($"ConstellationIntro: on the display. {LandingReport(cam)}", this);
        }

        // 4. Handoff.
        if (handoffFade > 0f)
        {
            float faded = 0f;
            while (faded < handoffFade)
            {
                faded += Time.deltaTime;
                PoseCopy(cam, 1f, 1f);
                SetDisplayFade(Mathf.Clamp01(faded / handoffFade));
                yield return null;
            }
        }

        SetDisplayFade(1f);
        DestroyCopy();
        ReleaseLocks();
        routine = null;
        Finished?.Invoke();
    }

    // ---- Copy ----

    private bool BuildCopy()
    {
        DestroyCopy();

        var found = new List<Transform>();
        foreach (GeneratedConstellationStar star in setup.SkyVisualContainer.GetComponentsInChildren<GeneratedConstellationStar>(true))
        {
            if (star.GetComponentInParent<SkyStamp3DVFX>(true) == null)
            {
                found.Add(star.transform);
            }
        }

        if (found.Count == 0)
        {
            return false;
        }

        sourceRoot = found[0].parent;
        sourceStars = found.ToArray();

        // Visuals only: transforms, meshes and materials; no colliders, scripts or events.
        copyObject = new GameObject("Constellation Intro Copy");
        copyObject.layer = 0;
        var map = new Dictionary<Transform, Transform>();
        CopyVisuals(sourceRoot, copyObject.transform, map);

        var copyStars = new List<Transform>();
        foreach (Transform source in sourceStars)
        {
            copyStars.Add(map[source]);
        }

        copy = ConstellationGeometry.Collect(copyStars);
        rodDepthRatio = new float[copy.Connections.Length];
        rodThickness = new float[copy.Connections.Length];
        return true;
    }

    private static void CopyVisuals(Transform source, Transform target, Dictionary<Transform, Transform> map)
    {
        map[source] = target;
        target.gameObject.SetActive(source.gameObject.activeSelf);

        MeshFilter filter = source.GetComponent<MeshFilter>();
        MeshRenderer renderer = source.GetComponent<MeshRenderer>();
        if (filter != null && renderer != null)
        {
            target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            MeshRenderer copyRenderer = target.gameObject.AddComponent<MeshRenderer>();
            copyRenderer.sharedMaterials = renderer.sharedMaterials;
            copyRenderer.enabled = renderer.enabled;
            copyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            copyRenderer.receiveShadows = false;
            if (renderer.HasPropertyBlock())
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                copyRenderer.SetPropertyBlock(block);
            }
        }

        foreach (Transform child in source)
        {
            var childCopy = new GameObject(child.name);
            childCopy.layer = 0;
            childCopy.transform.SetParent(target, false);
            childCopy.transform.localPosition = child.localPosition;
            childCopy.transform.localRotation = child.localRotation;
            childCopy.transform.localScale = child.localScale;
            CopyVisuals(child, childCopy.transform, map);
        }
    }

    private void DestroyCopy()
    {
        if (copyObject != null)
        {
            Destroy(copyObject);
        }

        copyObject = null;
        copy = null;
    }

    // ---- Pose ----

    // compress: 0 = the source's 3D arrangement, 1 = flat on the plane.
    // map: 0 = where the source is, 1 = on the display (similarity S).
    private void PoseCopy(Camera cam, float compress, float map)
    {
        if (copy == null || sourceRoot == null)
        {
            return;
        }

        Frame(cam, out Vector3 origin, out Vector3 forward, out float plane);

        for (int i = 0; i < sourceStars.Length; i++)
        {
            Vector3 world = sourceStars[i].position;
            float depth = Vector3.Dot(world - origin, forward);
            Vector3 flat = SightlineCompression.FlattenAlongSightline(world, origin, forward, plane);
            float ratio = depth > 1e-6f ? plane / depth : 1f;

            Transform star = copy.Stars[i].transform;
            star.localPosition = Vector3.LerpUnclamped(sourceStars[i].localPosition, sourceRoot.InverseTransformPoint(flat), compress);
            star.localScale = copy.Stars[i].baseLocalScale * Mathf.LerpUnclamped(1f, ratio, compress);
        }

        for (int j = 0; j < copy.Connections.Length; j++)
        {
            ConstellationGeometry.Connection link = copy.Connections[j];
            float a = DepthRatio(sourceStars[link.a].position, origin, forward, plane);
            float b = DepthRatio(sourceStars[link.b].position, origin, forward, plane);
            rodDepthRatio[j] = (a + b) * 0.5f;
            rodThickness[j] = Mathf.LerpUnclamped(1f, rodDepthRatio[j], compress);
        }

        copy.UpdateConnections(rodThickness);

        // The copy root follows the source root, carried by S_map.
        Similarity(cam, map, out Vector3 fromPoint, out Vector3 toPoint, out Quaternion turn, out float scale);
        Transform root = copyObject.transform;
        root.SetPositionAndRotation(
            toPoint + turn * ((sourceRoot.position - fromPoint) * scale),
            turn * sourceRoot.rotation);
        root.localScale = sourceRoot.lossyScale * scale;
    }

    private static float DepthRatio(Vector3 world, Vector3 origin, Vector3 forward, float plane)
    {
        float depth = Vector3.Dot(world - origin, forward);
        return depth > 1e-6f ? plane / depth : 1f;
    }

    private void Frame(Camera cam, out Vector3 origin, out Vector3 forward, out float plane)
    {
        Transform camTransform = cam.transform;
        origin = camTransform.position;
        forward = camTransform.forward;
        float pivotDepth = Vector3.Dot(setup.SkyConstellationPivot.position - origin, forward);
        plane = planeDistance * pivotDepth;
    }

    // S_t(x) = toPoint + turn * ((x - fromPoint) * scale). At t = 1 it takes
    // the camera's slice plane at the compression depth onto the display's
    // surface: forward -> into the display, camera up -> picture up, camera
    // right -> picture right, and the slice's width (2 * depth * tan(fov/2),
    // the full viewport) -> the display's width. Every S_t is a similarity, so
    // the pattern's shape and its image never change on the way.
    private void Similarity(Camera cam, float t, out Vector3 fromPoint, out Vector3 toPoint, out Quaternion turn, out float scale)
    {
        Frame(cam, out Vector3 origin, out Vector3 forward, out float plane);
        Transform camTransform = cam.transform;
        fromPoint = origin + forward * plane;

        Transform screen = display.transform;
        Bounds local = display.localBounds;
        Vector3 surface = screen.TransformPoint(local.center) + screen.up * surfaceOffset;
        float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float fullScale = local.size.x * screen.lossyScale.x / Mathf.Max(1e-6f, 2f * plane * tanHalf);
        Quaternion fullTurn =
            Quaternion.LookRotation(-screen.up, screen.forward) *
            Quaternion.Inverse(Quaternion.LookRotation(forward, camTransform.up));

        toPoint = Vector3.LerpUnclamped(fromPoint, surface, t);
        turn = Quaternion.SlerpUnclamped(Quaternion.identity, fullTurn, t);
        scale = Mathf.LerpUnclamped(1f, fullScale, t);
    }

    // ---- Display ----

    private void SetDisplayFade(float fade)
    {
        if (display == null)
        {
            return;
        }

        if (fade >= 1f)
        {
            // Back to the material's own settings.
            display.SetPropertyBlock(null);
            return;
        }

        float intensity = display.sharedMaterial != null && display.sharedMaterial.HasProperty(IntensityId)
            ? display.sharedMaterial.GetFloat(IntensityId)
            : 1f;
        displayBlock ??= new MaterialPropertyBlock();
        display.GetPropertyBlock(displayBlock);
        displayBlock.SetFloat(IntensityId, intensity * fade);
        display.SetPropertyBlock(displayBlock);
    }

    // ---- Guides ----

    private void UpdateGuides(Camera cam)
    {
        if (!guideLines.IsBuilt || copy == null)
        {
            return;
        }

        Frame(cam, out Vector3 origin, out Vector3 forward, out float plane);
        Transform camTransform = cam.transform;
        Vector3 right = camTransform.right;
        Vector3 up = camTransform.up;

        var landing = new Vector3[sourceStars.Length];
        var current = new Vector3[sourceStars.Length];
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < sourceStars.Length; i++)
        {
            landing[i] = SightlineCompression.FlattenAlongSightline(sourceStars[i].position, origin, forward, plane);
            current[i] = copy.Stars[i].transform.position;
            Vector2 onPlane = new Vector2(Vector3.Dot(landing[i] - origin, right), Vector3.Dot(landing[i] - origin, up));
            min = Vector2.Min(min, onPlane);
            max = Vector2.Max(max, onPlane);
        }

        Vector2 mid = (min + max) * 0.5f;
        Vector3 center = origin + forward * plane + right * mid.x + up * mid.y;
        guideLines.Update(guides, landing, current, center, right, up, (max - min) * 0.5f);
    }

    // ---- Checks ----

    private Vector2 PixelSize(Camera cam)
    {
        RenderTexture target = cam.targetTexture;
        return target != null ? new Vector2(target.width, target.height) : new Vector2(1024f, 1024f);
    }

    // While the copy sits on the source (map = 0), each copy star should keep
    // its source star's Earth view pixel.
    private float MeasureDriftPixels(Camera cam)
    {
        Vector2 pixels = PixelSize(cam);
        float worst = 0f;
        for (int i = 0; i < sourceStars.Length; i++)
        {
            Vector2 source = cam.WorldToViewportPoint(sourceStars[i].position);
            Vector2 now = cam.WorldToViewportPoint(copy.Stars[i].transform.position);
            worst = Mathf.Max(worst, Vector2.Scale(now - source, pixels).magnitude);
        }

        return worst;
    }

    /// <summary>Where the live display shows star i: its Earth view viewport mapped through the display's UVs.</summary>
    public Vector3 DisplayPoint(Camera cam, Vector3 starWorld)
    {
        Vector2 viewport = cam.WorldToViewportPoint(starWorld);
        Transform screen = display.transform;
        Bounds local = display.localBounds;
        return screen.TransformPoint(new Vector3(
                   local.center.x + (viewport.x - 0.5f) * local.size.x,
                   local.center.y,
                   local.center.z + (viewport.y - 0.5f) * local.size.z)) +
               screen.up * surfaceOffset;
    }

    private string LandingReport(Camera cam)
    {
        float worst = 0f;
        for (int i = 0; i < sourceStars.Length; i++)
        {
            worst = Mathf.Max(worst, Vector3.Distance(copy.Stars[i].transform.position, DisplayPoint(cam, sourceStars[i].position)));
        }

        float pixelsPerMeter = PixelSize(cam).x / Mathf.Max(1e-6f, display.localBounds.size.x * display.transform.lossyScale.x);
        return $"Landing error vs the live display, worst star: {worst * 1000f:F3} mm ({worst * pixelsPerMeter:F2} RT px).";
    }

    private string ContainmentReport()
    {
        CircularWindow volume = nearFieldSkyProxy != null ? nearFieldSkyProxy.GetComponent<CircularWindow>() : null;
        if (volume == null || copy == null)
        {
            return "";
        }

        Vector3 center = volume.transform.position;
        float farthest = 0f;
        foreach (ConstellationGeometry.Star star in copy.Stars)
        {
            farthest = Mathf.Max(farthest, Vector3.Distance(star.transform.position, center));
        }

        return $"Copy's farthest star {farthest:F3} m from the globe center (radius {volume.Radius:F3} m).";
    }
}
