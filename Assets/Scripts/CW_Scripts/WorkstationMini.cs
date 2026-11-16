using System.Collections;
using UnityEngine;

/// <summary>
/// The player's workstation: where a constellation sculpture is placed and the
/// miniature is turned, close to the body, while the globe shows the result.
///
/// Placement: this object goes in front of the player's head (sampled when the
/// workshop is aligned, recentered or raised/lowered, never every frame),
/// along the line toward the globe: Forward Distance ahead, Height Below Eyes
/// down, Lateral Offset to the side. Children (drop zone, marker) come along.
///
/// The mini: each frame Mini Earth Reference is posed so the mini constellation
/// pivot sits here, at Mini Reach (farthest star from the pivot, metres).
/// Its rotation sets how the mini relates to the hidden sky: ConstellationSetup
/// mirrors the sky as S * E^-1 * mini, so with E = the player's view frame of
/// the mini and S = the Earth observer's view frame of the sky, the mini looks
/// to the player the way the sky looks to the Earth view camera. The sky,
/// observer and Earth view camera are never moved by this.
///
/// Mapping: <see cref="MiniToSky"/> carries a point near the mini into the sky
/// by the similarity mini pivot -> sky pivot (rotation S * E^-1, uniform scale
/// sky reach / mini reach). Exact when ConstellationSetup's Mini Matches Sky is on.
///
/// After the solve the mini doesn't drop to physics: it shrinks away at the
/// chosen moment (stamp or signed) and comes back on the next puzzle reset.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class WorkstationMini : MonoBehaviour
{
    public enum MiniOrientation
    {
        [Tooltip("The mini looks to the player as the sky looks to the Earth observer (solved mini = the blueprint, seen from the seat).")]
        PlayerView,

        [Tooltip("The mini has the same world orientation as the hidden sky constellation.")]
        SameAsSky
    }

    public enum VanishMoment
    {
        [Tooltip("Keep the original behaviour: the mini drops to physics after the solve settle.")]
        NeverDrop,

        [Tooltip("When the gold solve stamp plays (right after the settle).")]
        Stamp,

        [Tooltip("When the constellation has been signed.")]
        Signed
    }

    [Header("References")]
    [SerializeField]
    private ConstellationSetup setup;

    [Tooltip("Viewer camera (CenterEyeAnchor).")]
    [SerializeField]
    private Camera viewer;

    [Tooltip("The globe: the workstation lines up between the head and it. Empty = the head's forward.")]
    [SerializeField]
    private Transform volume;

    [Tooltip("Found automatically if empty. Placement waits for it to align and follows its rig moves.")]
    [SerializeField]
    private WorkshopPlacement placement;

    [SerializeField]
    private ConstellationSolveController solveController;

    [Tooltip("For the Signed vanish moment.")]
    [SerializeField]
    private ConstellationSigning signing;

    [Tooltip("The ring shows once a constellation box is opened. Found automatically if empty.")]
    [SerializeField]
    private BoxSelection boxes;

    [Tooltip("Found automatically if empty.")]
    [SerializeField]
    private GameFlow gameFlow;

    [Tooltip("The ring also shows while a title letter is held (practice is placed here too). Found automatically if empty.")]
    [SerializeField]
    private TitleShelf titleShelf;

    [Tooltip("The rotation axes at the mini follow this camera's axes in the sky. Found automatically if empty.")]
    [SerializeField]
    private Transform earthViewCamera;

    [Header("Placement (relative to the head at placement)")]
    [Tooltip("Horizontal distance from the eyes toward the globe (m). About 0.35-0.45 keeps the elbows bent.")]
    [SerializeField]
    [Range(0.15f, 0.8f)]
    private float forwardDistance = 0.4f;

    [Tooltip("How far below eye height the mini's center sits (m).")]
    [SerializeField]
    [Range(0f, 0.7f)]
    private float heightBelowEyes = 0.3f;

    [Tooltip("Sideways offset (m, + = right).")]
    [SerializeField]
    [Range(-0.4f, 0.4f)]
    private float lateralOffset;

    [Tooltip("Eye height above the rig when the head isn't tracked (Editor without a headset).")]
    [SerializeField]
    private float fallbackEyeHeight = 1.2f;

    [Tooltip("Wait this long (real s) for the workshop to align before placing anyway.")]
    [SerializeField]
    [Min(0f)]
    private float alignTimeout = 8f;

    [Header("Mini")]
    [Tooltip("World size of the mini: farthest star from its pivot (m). The old mini above the globe was about 0.06.")]
    [SerializeField]
    [Range(0.03f, 0.25f)]
    private float miniReach = 0.09f;

    [SerializeField]
    private MiniOrientation orientation = MiniOrientation.PlayerView;

    [Tooltip("A rotation pinch starts within this multiple of Mini Reach from the mini's center.")]
    [SerializeField]
    [Range(1f, 4f)]
    private float entryRadiusFactor = 1.7f;

    [Header("Placement zone")]
    [Tooltip("Where box sculptures are accepted. Follows this object; radius set here.")]
    [SerializeField]
    private SphereDropZone dropZone;

    [SerializeField]
    [Range(0.04f, 0.3f)]
    private float dropRadius = 0.13f;

    [Tooltip("Minimal ring under the mini (filled in at runtime). Optional.")]
    [SerializeField]
    private MeshFilter marker;

    [Tooltip("Ring sits this far below the mini's center, as a multiple of Mini Reach.")]
    [SerializeField]
    [Range(0f, 3f)]
    private float markerDrop = 1.3f;

    [SerializeField]
    [Range(0.02f, 0.3f)]
    private float markerRadius = 0.08f;

    [SerializeField]
    [Range(0.001f, 0.03f)]
    private float markerWidth = 0.004f;

    [Header("After the solve")]
    [SerializeField]
    private VanishMoment vanishOn = VanishMoment.Stamp;

    [SerializeField]
    [Min(0f)]
    private float vanishDuration = 0.5f;

    [SerializeField]
    private bool logPlacement = true;

    private Transform miniEarth;
    private Transform miniPivot;
    private Transform skyPivot;
    private Transform skyRotation;
    private OVRCameraRig rig;

    private Vector3 eyeAtPlacement;
    private float miniReachLocal;      // in Mini Earth Reference units
    private Vector3 containerBaseScale;
    private bool vanished;
    private Coroutine vanishRoutine;
    private SkyStamp3DVFX stamp;
    private Mesh markerMesh;
    private Renderer markerRenderer;
    private bool markerArmed;
    private Transform[] miniStars = new Transform[0];
    private Transform[] skyStars = new Transform[0];

    /// <summary>True once the workstation has been placed and the mini measured.</summary>
    public bool IsReady { get; private set; }

    /// <summary>The mini's rotation pivot (the center of the controls).</summary>
    public Vector3 Center => miniPivot != null ? miniPivot.position : transform.position;

    /// <summary>World reach of the mini (m).</summary>
    public float Reach => miniReach;

    /// <summary>A rotation pinch starts within this distance of <see cref="Center"/>.</summary>
    public float EntryRadius => miniReach * entryRadiusFactor;

    /// <summary>True while the mini is hidden after a solve.</summary>
    public bool IsVanished => vanished;

    /// <summary>Rotation that carries directions at the mini into the sky (S * E^-1).</summary>
    public Quaternion MiniToSkyRotation =>
        skyRotation != null && miniEarth != null ? skyRotation.rotation * Quaternion.Inverse(miniEarth.rotation) : Quaternion.identity;

    /// <summary>
    /// Rotation axes for turning the mini: the Earth view camera's own right /
    /// up / forward carried back to the mini, so each axis the hand turns the
    /// mini about turns the sky about the matching camera axis (the projection
    /// in the globe spins, tilts or pans exactly on-axis). It is within a few
    /// degrees of the mini's own frame, which faces the eyes. Without an Earth
    /// view camera it is the mini's frame.
    /// </summary>
    public Quaternion ControlFrame =>
        earthViewCamera != null && miniEarth != null
            ? Quaternion.Inverse(MiniToSkyRotation) * earthViewCamera.rotation
            : (miniEarth != null ? miniEarth.rotation : transform.rotation);

    /// <summary>Sky size / mini size (live: the sky is shrunk into the globe after startup).</summary>
    public float MiniToSkyScale { get; private set; } = 1f;

    /// <summary>A world point near the mini, carried to the matching point in the hidden sky.</summary>
    public Vector3 MiniToSky(Vector3 point)
    {
        return skyPivot.position + MiniToSkyRotation * ((point - miniPivot.position) * MiniToSkyScale);
    }

    private void Awake()
    {
        if (setup == null)
        {
            setup = FindFirstObjectByType<ConstellationSetup>();
        }

        if (placement == null)
        {
            placement = FindFirstObjectByType<WorkshopPlacement>();
        }

        if (boxes == null)
        {
            boxes = FindFirstObjectByType<BoxSelection>(FindObjectsInactive.Include);
        }

        if (gameFlow == null)
        {
            gameFlow = FindFirstObjectByType<GameFlow>();
        }

        if (titleShelf == null)
        {
            titleShelf = FindFirstObjectByType<TitleShelf>(FindObjectsInactive.Include);
        }

        if (earthViewCamera == null)
        {
            EarthViewCamera earthView = FindFirstObjectByType<EarthViewCamera>(FindObjectsInactive.Include);
            earthViewCamera = earthView != null ? earthView.transform : null;
        }

        rig = viewer != null ? viewer.GetComponentInParent<OVRCameraRig>() : null;

        // Hidden from the first frame; UpdateMarkerVisibility shows it once a box is opened.
        markerRenderer = marker != null ? marker.GetComponent<Renderer>() : null;
        if (markerRenderer != null)
        {
            markerRenderer.enabled = false;
        }
    }

    private void OnEnable()
    {
        if (setup != null)
        {
            setup.DropMiniAfterSolve = vanishOn == VanishMoment.NeverDrop;
            setup.Resetting += Restore;
            setup.ConstellationLoaded += Remeasure;
        }

        if (solveController != null)
        {
            solveController.PuzzleReset += Restore;
        }

        if (placement != null)
        {
            placement.RigMoved += HandleRigMoved;
        }

        if (signing != null)
        {
            signing.Signed += HandleSigned;
        }
    }

    private void OnDisable()
    {
        if (setup != null)
        {
            setup.DropMiniAfterSolve = true;
            setup.Resetting -= Restore;
            setup.ConstellationLoaded -= Remeasure;
        }

        if (solveController != null)
        {
            solveController.PuzzleReset -= Restore;
        }

        if (placement != null)
        {
            placement.RigMoved -= HandleRigMoved;
        }

        if (signing != null)
        {
            signing.Signed -= HandleSigned;
        }

        if (stamp != null)
        {
            stamp.Played -= HandleStamp;
            stamp = null;
        }
    }

    private IEnumerator Start()
    {
        if (setup == null || setup.MiniEarthReference == null || setup.MiniConstellationPivot == null)
        {
            Debug.LogWarning("WorkstationMini: missing ConstellationSetup references.", this);
            enabled = false;
            yield break;
        }

        miniEarth = setup.MiniEarthReference;
        miniPivot = setup.MiniConstellationPivot;
        skyPivot = setup.SkyConstellationPivot;
        skyRotation = setup.SkyRotationReference;
        containerBaseScale = setup.MiniVisualContainer.localScale;

        stamp = setup.SkyStampVFX;
        if (stamp != null)
        {
            stamp.Played += HandleStamp;
        }

        BuildMarker();

        // Let ConstellationSetup generate and center the mini (its Start).
        yield return null;

        if (placement != null)
        {
            float giveUpAt = Time.realtimeSinceStartup + alignTimeout;
            while (!placement.IsAligned && Time.realtimeSinceStartup < giveUpAt)
            {
                yield return null;
            }
        }

        Place("startup");
        Remeasure();
        IsReady = true;
    }

    private void HandleRigMoved(Pose before, Pose after)
    {
        if (miniEarth != null)
        {
            Place("rig moved");
        }
    }

    [ContextMenu("Place Now")]
    private void PlaceNow()
    {
        Place("manual");
    }

    private void Place(string reason)
    {
        bool tracked = OVRManager.tracker != null && OVRManager.tracker.isPositionTracked;
        Vector3 eye = viewer != null && (tracked || rig == null)
            ? viewer.transform.position
            : (rig != null ? rig.transform.position : transform.position) + Vector3.up * fallbackEyeHeight;

        Vector3 forward = volume != null
            ? Vector3.ProjectOnPlane(volume.position - eye, Vector3.up)
            : Vector3.ProjectOnPlane(viewer != null ? viewer.transform.forward : Vector3.forward, Vector3.up);
        forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Vector3 anchor = eye + forward * forwardDistance + right * lateralOffset + Vector3.down * heightBelowEyes;
        transform.SetPositionAndRotation(anchor, Quaternion.LookRotation(forward, Vector3.up));
        eyeAtPlacement = eye;

        if (dropZone != null)
        {
            dropZone.Center = transform;
            dropZone.Radius = dropRadius;
        }

        if (logPlacement)
        {
            Debug.Log($"WorkstationMini: placed ({reason}) at {anchor:F3}, {Vector3.Distance(eye, anchor):F2} m from the eyes " +
                      $"(head {(tracked ? "tracked" : "NOT tracked, fallback eye")}), reach {miniReach:F3} m", this);
        }

        ApplyMiniPose();
    }

    private void LateUpdate()
    {
        UpdateMarkerVisibility();
        if (!IsReady)
        {
            return;
        }

        ApplyMiniPose();
        UpdateScale();
        UpdateMarker();
    }

    private void UpdateScale()
    {
        if (vanished)
        {
            return;
        }

        float mini = StarReach(miniStars, miniPivot.position);
        float sky = StarReach(skyStars, skyPivot.position);
        if (mini > 1e-6f && sky > 1e-6f)
        {
            MiniToSkyScale = sky / mini;
        }
    }

    // Poses Mini Earth Reference (rotation, scale) so the mini pivot sits on the
    // workstation at Mini Reach. The pivot is its child, so its rotation relative
    // to E (what the sky mirrors) is untouched: the sky never jumps.
    private void ApplyMiniPose()
    {
        if (miniEarth == null || miniPivot == null)
        {
            return;
        }

        Vector3 anchor = transform.position;
        Quaternion frame = orientation == MiniOrientation.SameAsSky && skyRotation != null
            ? skyRotation.rotation
            : Quaternion.LookRotation(LookDirection(anchor), Vector3.up);

        miniEarth.rotation = frame;

        if (miniReachLocal > 1e-6f)
        {
            float parentScale = miniEarth.parent != null ? miniEarth.parent.lossyScale.x : 1f;
            miniEarth.localScale = Vector3.one * (miniReach / miniReachLocal / Mathf.Max(1e-6f, parentScale));
        }

        miniEarth.position += anchor - miniPivot.position;
    }

    private Vector3 LookDirection(Vector3 anchor)
    {
        Vector3 look = anchor - eyeAtPlacement;
        if (look.sqrMagnitude < 1e-6f || Vector3.Cross(look.normalized, Vector3.up).sqrMagnitude < 1e-4f)
        {
            look = transform.forward;
        }

        return look.normalized;
    }

    // Mini reach in E's units and the sky/mini scale. On startup and after each
    // constellation load (the stars change).
    private void Remeasure()
    {
        if (vanished)
        {
            RestoreNow();
        }

        miniStars = CollectStars(setup.MiniVisualContainer);
        skyStars = CollectStars(setup.SkyVisualContainer);
        float miniWorld = StarReach(miniStars, miniPivot.position);
        float skyWorld = StarReach(skyStars, skyPivot.position);
        float eScale = Mathf.Max(1e-6f, miniEarth.lossyScale.x);
        if (miniWorld > 1e-6f)
        {
            miniReachLocal = miniWorld / eScale;
        }

        ApplyMiniPose();
        UpdateScale();
        miniWorld = StarReach(miniStars, miniPivot.position);

        if (logPlacement)
        {
            Debug.Log($"WorkstationMini: mini reach {miniWorld:F3} m, sky reach {skyWorld:F3} m (x{MiniToSkyScale:F2})", this);
        }
    }

    // Generated stars under a container (stamp copies excluded).
    private static Transform[] CollectStars(Transform container)
    {
        var stars = new System.Collections.Generic.List<Transform>();
        if (container != null)
        {
            foreach (GeneratedConstellationStar star in container.GetComponentsInChildren<GeneratedConstellationStar>())
            {
                if (star.GetComponentInParent<SkyStamp3DVFX>() == null)
                {
                    stars.Add(star.transform);
                }
            }
        }

        return stars.ToArray();
    }

    // Farthest star from a pivot, world metres.
    private static float StarReach(Transform[] stars, Vector3 pivot)
    {
        float reach = 0f;
        foreach (Transform star in stars)
        {
            if (star != null)
            {
                reach = Mathf.Max(reach, Vector3.Distance(star.position, pivot));
            }
        }

        return reach;
    }

    // ---- After the solve ----

    private void HandleStamp()
    {
        if (vanishOn == VanishMoment.Stamp)
        {
            Vanish();
        }
    }

    private void HandleSigned()
    {
        if (vanishOn == VanishMoment.Signed)
        {
            Vanish();
        }
    }

    [ContextMenu("Vanish")]
    private void Vanish()
    {
        if (vanished || setup == null)
        {
            return;
        }

        vanished = true;
        if (vanishRoutine != null)
        {
            StopCoroutine(vanishRoutine);
        }

        vanishRoutine = StartCoroutine(Shrink());
    }

    private IEnumerator Shrink()
    {
        Transform container = setup.MiniVisualContainer;
        float elapsed = 0f;
        while (elapsed < vanishDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / vanishDuration);
            t = t * t * (3f - 2f * t);
            container.localScale = containerBaseScale * (1f - t);
            yield return null;
        }

        container.localScale = Vector3.zero;
        vanishRoutine = null;
    }

    private void Restore()
    {
        if (vanished)
        {
            RestoreNow();
        }
    }

    private void RestoreNow()
    {
        if (vanishRoutine != null)
        {
            StopCoroutine(vanishRoutine);
            vanishRoutine = null;
        }

        if (setup != null && setup.MiniVisualContainer != null)
        {
            setup.MiniVisualContainer.localScale = containerBaseScale;
        }

        vanished = false;
    }

    // ---- Marker ----

    private void BuildMarker()
    {
        if (marker == null)
        {
            return;
        }

        const int segments = 64;
        markerMesh = new Mesh { name = "Workstation Marker Ring" };
        var vertices = new Vector3[segments * 2];
        var colors = new Color[segments * 2];
        var triangles = new int[segments * 12];
        for (int i = 0; i < segments; i++)
        {
            float a = 2f * Mathf.PI * i / segments;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            vertices[i * 2] = dir * 0.5f;      // inner, rewritten in UpdateMarker
            vertices[i * 2 + 1] = dir;          // outer
            colors[i * 2] = new Color(1f, 1f, 1f, 1f);
            colors[i * 2 + 1] = new Color(1f, 1f, 1f, 1f);

            int n = (i + 1) % segments;
            int t = i * 12;
            int a0 = i * 2, a1 = i * 2 + 1, b0 = n * 2, b1 = n * 2 + 1;
            // Both faces, so it reads from above and below.
            triangles[t] = a0; triangles[t + 1] = b0; triangles[t + 2] = a1;
            triangles[t + 3] = a1; triangles[t + 4] = b0; triangles[t + 5] = b1;
            triangles[t + 6] = a0; triangles[t + 7] = a1; triangles[t + 8] = b0;
            triangles[t + 9] = a1; triangles[t + 10] = b1; triangles[t + 11] = b0;
        }

        markerMesh.vertices = vertices;
        markerMesh.colors = colors;
        markerMesh.triangles = triangles;
        markerMesh.RecalculateBounds();
        marker.sharedMesh = markerMesh;
        UpdateMarker();
    }

    private string markerKey;

    // The ring appears once a constellation box is opened, or while a title
    // letter is held, stays through the puzzle (main or practice) started from
    // it, and goes with the mini (vanish) or on leaving for the title / closed
    // boxes / noticeboard.
    private void UpdateMarkerVisibility()
    {
        if (markerRenderer == null)
        {
            return;
        }

        BoxSelection.State box = boxes != null ? boxes.Current : BoxSelection.State.Hidden;
        bool selecting = box == BoxSelection.State.PackOpen || box == BoxSelection.State.Launching;
        if (selecting || TitleLetterHeld())
        {
            markerArmed = true;
        }
        else if (gameFlow == null || gameFlow.Current != GameFlow.Stage.Puzzle)
        {
            markerArmed = false;
        }

        bool show = IsReady && markerArmed && !vanished;
        if (markerRenderer.enabled != show)
        {
            markerRenderer.enabled = show;
        }
    }

    private bool TitleLetterHeld()
    {
        if (titleShelf == null || !titleShelf.IsShown || gameFlow == null || gameFlow.Current != GameFlow.Stage.Title)
        {
            return false;
        }

        foreach (TitleLetterMini letter in titleShelf.Letters)
        {
            if (letter != null && letter.IsGrabbed)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateMarker()
    {
        if (marker == null || markerMesh == null)
        {
            return;
        }

        marker.transform.localPosition = Vector3.down * (miniReach * markerDrop);
        marker.transform.localRotation = Quaternion.identity;
        marker.transform.localScale = Vector3.one;

        string key = $"{markerRadius:F4}|{markerWidth:F4}";
        if (key == markerKey)
        {
            return;
        }

        markerKey = key;
        Vector3[] vertices = markerMesh.vertices;
        int segments = vertices.Length / 2;
        for (int i = 0; i < segments; i++)
        {
            float a = 2f * Mathf.PI * i / segments;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            vertices[i * 2] = dir * Mathf.Max(0f, markerRadius - markerWidth);
            vertices[i * 2 + 1] = dir * markerRadius;
        }

        markerMesh.vertices = vertices;
        markerMesh.RecalculateBounds();
    }

    private void OnDestroy()
    {
        if (markerMesh != null)
        {
            Destroy(markerMesh);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireSphere(Application.isPlaying ? Center : transform.position, EntryRadius);
        Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, dropRadius);
    }
}
