using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Prototype: pinch anywhere inside the near-field volume to rotate the
/// constellation about one fixed workspace axis.
/// Where the hand is picks which two axes are offered (the axis the hand
/// sits on is dropped, since its circle would have no radius there). Before
/// pinching, short arcs preview that pair; the pinch commits it, and the first
/// deliberate hand movement locks one of them for the rest of the pinch.
/// Rotation follows the hand's angle around the locked axis, so the grabbed
/// spot stays under the fingers without the hand having to reach the guide.
/// Like the gimbal, it turns the hidden mini pivot about the remapped axis and
/// ConstellationSetup mirrors that to the sky, so solve, settle and reset are
/// unchanged. Pinch Mode off hands control back to the gimbal (A/B toggle).
/// </summary>
public sealed class VolumePinchRotation : MonoBehaviour
{
    public enum GuideStyle
    {
        LineRenderer,
        Tube
    }

    private enum State
    {
        Idle,
        Pending,
        Locked
    }

    [Header("References")]
    [SerializeField]
    private NearFieldSkyProxy nearFieldSkyProxy;

    [SerializeField]
    private CircularWindow volume;

    [Tooltip("Viewer camera (CenterEyeAnchor). Fixes the workspace axes the first time a hand enters.")]
    [SerializeField]
    private Camera viewer;

    [Tooltip("Rotation center (Sky Constellation Pivot).")]
    [SerializeField]
    private Transform skyConstellationPivot;

    [Tooltip("Rotated about the remapped axis; ConstellationSetup mirrors it to the sky.")]
    [SerializeField]
    private Transform miniConstellationPivot;

    [SerializeField]
    private Transform miniEarthReference;

    [SerializeField]
    private Transform skyRotationReference;

    [SerializeField]
    private ConstellationSolveController solveController;

    [Tooltip("Hidden while Pinch Mode is on.")]
    [SerializeField]
    private GameObject gimbalRoot;

    [Tooltip("Hands that can pinch (the rig's per-hand HandRef).")]
    [SerializeField]
    [Interface(typeof(IHand))]
    private List<Object> hands = new List<Object>();

    [Header("Mode")]
    [Tooltip("On: pinch inside the volume to rotate. Off: the gimbal is back.")]
    [SerializeField]
    private bool pinchMode = true;

    [Tooltip("Play mode key that flips Pinch Mode (Editor / Link testing).")]
    [SerializeField]
    private Key toggleModeKey = Key.G;

    [Header("Pinch")]
    [SerializeField]
    [Range(0f, 1f)]
    private float pinchOnStrength = 0.8f;

    [SerializeField]
    [Range(0f, 1f)]
    private float pinchOffStrength = 0.5f;

    [Tooltip("A pinch starts a rotation (and the preview shows) within this multiple of the volume's radius.")]
    [SerializeField]
    [Min(0.1f)]
    private float entryRadiusFactor = 1.1f;

    [Tooltip("Hold the pinch through tracking loss this short.")]
    [SerializeField]
    [Min(0f)]
    private float trackingGrace = 0.2f;

    [Header("Axis Offer")]
    [Tooltip("Inside this fraction of the radius the hand direction is noise: offer the front pair (X and Y).")]
    [SerializeField]
    [Range(0f, 1f)]
    private float centerFraction = 0.25f;

    [Tooltip("Weight on the front/back component when classifying. Above 1 keeps a deep front reach in the front pair; below 1 makes the sides and top easier to reach.")]
    [SerializeField]
    [Range(0.5f, 2f)]
    private float frontWeight = 1f;

    [Tooltip("A new region must beat the current one by this fraction before the offered pair switches.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float regionHysteresis = 0.15f;

    [Tooltip("Off: workspace up is world up. On: tilted with the view down to the volume, like the gimbal.")]
    [SerializeField]
    private bool tiltFrameWithView;

    [Header("Axis Selection")]
    [Tooltip("Hand travel before one of the offered axes is chosen.")]
    [SerializeField]
    [Min(0.001f)]
    private float selectDeadZone = 0.015f;

    [Header("Rotation")]
    [Tooltip("Degrees of constellation per degree of hand angle. 1 keeps the grabbed spot under the fingers.")]
    [SerializeField]
    [Min(0.1f)]
    private float gain = 1f;

    [Tooltip("Closer to the axis than this fraction of the radius, rotation is scaled down so jitter doesn't spin it.")]
    [SerializeField]
    [Range(0.05f, 1f)]
    private float minLeverFraction = 0.35f;

    [Header("Guides")]
    [Tooltip("LineRenderer: camera-facing ribbons. Tube: a thin 3D mesh with the same circle.")]
    [SerializeField]
    private GuideStyle guideStyle = GuideStyle.Tube;

    [Tooltip("LineRenderer style and grip dot material; tinted with _BaseColor.")]
    [SerializeField]
    private Material guideMaterial;

    [Tooltip("Tube style material; tinted with _BaseColor, vertex alpha fades arc ends and the back of rings (Particles/Unlit).")]
    [SerializeField]
    private Material tubeMaterial;

    [SerializeField]
    [Range(0.1f, 1f)]
    private float guideRadiusFraction = 0.85f;

    [Tooltip("Line width, or tube diameter.")]
    [SerializeField]
    [Min(0.0005f)]
    private float guideWidth = 0.002f;

    [SerializeField]
    [Range(3, 12)]
    private int tubeSides = 6;

    [Tooltip("Tube only: brightness of the half of each ring away from the viewer. 1 = full ring, 0 = front half only.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float backFade = 1f;

    [SerializeField]
    private Color guideColor = new Color(0.75f, 0.9f, 1f, 1f);

    [SerializeField]
    [Range(0f, 1f)]
    private float offeredAlpha = 0.35f;

    [SerializeField]
    [Range(0f, 1f)]
    private float selectedAlpha = 0.8f;

    [Tooltip("Dot that rides the selected guide at the hand's angle. 0 hides it.")]
    [SerializeField]
    [Min(0f)]
    private float gripMarkerSize = 0.008f;

    [Header("Preview")]
    [Tooltip("Before pinching, show short arcs of the two axes a pinch would offer.")]
    [SerializeField]
    private bool showPreview = true;

    [SerializeField]
    [Range(5f, 180f)]
    private float previewArcDegrees = 40f;

    [SerializeField]
    [Range(0f, 1f)]
    private float previewAlpha = 0.2f;

    [Tooltip("Preview brightness multiplier near the center, where the front pair is only the default.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float centerPreviewScale = 0.6f;

    private const int GuideSegments = 64;
    private const int ArcSegments = 16;
    private const float Hidden = -1f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private sealed class PinchHand
    {
        public IHand Hand;
        public bool Held;
    }

    private sealed class GuideVisual
    {
        public LineRenderer Line;
        public MeshRenderer Tube;
        public MeshFilter TubeFilter;
    }

    private readonly List<PinchHand> pinchHands = new List<PinchHand>();

    private State state;
    private PinchHand activeHand;
    private float untrackedTime;

    private bool frameCaptured;
    private Quaternion workspaceFrame;

    private Vector3 pinchStart;
    private readonly int[] offeredAxes = new int[2];
    private int lockedAxis = -1;
    private Vector3 previousDirection;

    private PinchHand previewHand;
    private int previewDropped = -1;

    // What each axis shows this frame (alpha, or Hidden); ApplyGuides draws it in the current style.
    private readonly float[] ringAlpha = { Hidden, Hidden, Hidden };
    private readonly float[] arcAlpha = { Hidden, Hidden, Hidden };
    private readonly Vector3[] arcDirection = new Vector3[3];

    private readonly GuideVisual[] rings = new GuideVisual[3];
    private readonly GuideVisual[] arcs = new GuideVisual[3];
    private readonly Mesh[] ringMeshes = new Mesh[3];
    private Mesh arcMesh;
    private string meshKey;

    private readonly Vector3[] ringPoints = new Vector3[GuideSegments];
    private readonly Vector3[] arcPoints = new Vector3[ArcSegments + 1];
    private Renderer gripMarker;
    private MaterialPropertyBlock guideProperties;

    private bool lastPinchMode;

    private void Start()
    {
        foreach (Object handObject in hands)
        {
            if (handObject is IHand hand)
            {
                pinchHands.Add(new PinchHand { Hand = hand });
            }
        }

        Debug.Log($"VolumePinchRotation: {pinchHands.Count} hand(s) can pinch.", this);

        guideProperties = new MaterialPropertyBlock();
        for (int i = 0; i < 3; i++)
        {
            ringMeshes[i] = new Mesh { name = $"Rotation Ring {AxisName(i)}" };
            rings[i] = CreateGuideVisual($"Rotation Guide {AxisName(i)}", true, GuideSegments, ringMeshes[i]);
        }

        arcMesh = new Mesh { name = "Rotation Preview Arc" };
        for (int i = 0; i < 3; i++)
        {
            arcs[i] = CreateGuideVisual($"Rotation Preview {AxisName(i)}", false, ArcSegments + 1, arcMesh);
        }

        if (gripMarkerSize > 0f)
        {
            gripMarker = CreateGripMarker();
        }

        HideGuides();
        lastPinchMode = pinchMode;
    }

    private void OnDestroy()
    {
        foreach (Mesh mesh in ringMeshes)
        {
            Destroy(mesh);
        }

        Destroy(arcMesh);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (toggleModeKey != Key.None && keyboard != null && keyboard[toggleModeKey].wasPressedThisFrame)
        {
            pinchMode = !pinchMode;
            Debug.Log($"VolumePinchRotation: pinch mode {(pinchMode ? "on" : "off (gimbal)")}", this);
        }

        if (pinchMode != lastPinchMode)
        {
            lastPinchMode = pinchMode;
            if (!pinchMode)
            {
                EndPinch();
                HidePreview();

                // The solve hides the gimbal; don't bring it back over a solved puzzle.
                if (gimbalRoot != null)
                {
                    gimbalRoot.SetActive(!IsSolved());
                }
            }
        }

        if (!pinchMode)
        {
            return;
        }

        if (state == State.Idle)
        {
            TryBeginPinch();
        }
        else
        {
            UpdatePinch();
        }

        if (state == State.Idle)
        {
            UpdatePreview();
        }
        else
        {
            HidePreview();
        }
    }

    private void LateUpdate()
    {
        // ConstellationSetup.HandleReset turns the gimbal back on, so keep it off.
        // Wait for placement: NearFieldSkyProxy measures the rings' active renderers.
        if (pinchMode && gimbalRoot != null && gimbalRoot.activeSelf &&
            (nearFieldSkyProxy == null || nearFieldSkyProxy.IsPlaced))
        {
            gimbalRoot.SetActive(false);
        }

        ApplyGuides();
    }

    private void TryBeginPinch()
    {
        bool solved = IsSolved();

        foreach (PinchHand pinchHand in pinchHands)
        {
            IHand hand = pinchHand.Hand;
            if (!hand.IsTrackedDataValid)
            {
                pinchHand.Held = false;
                continue;
            }

            float strength = hand.GetFingerPinchStrength(HandFinger.Index);
            bool wasHeld = pinchHand.Held;
            pinchHand.Held = wasHeld ? strength > pinchOffStrength : strength >= pinchOnStrength;

            // Only a pinch that starts inside counts, not one carried in from outside.
            if (wasHeld || !pinchHand.Held || solved || !hand.IsHighConfidence)
            {
                continue;
            }

            if (!TryGetPinchPoint(pinchHand, out Vector3 point))
            {
                continue;
            }

            Vector3 center = skyConstellationPivot.position;
            if (Vector3.Distance(point, center) > volume.Radius * entryRadiusFactor)
            {
                continue;
            }

            BeginPinch(pinchHand, point, center);
            return;
        }
    }

    private void BeginPinch(PinchHand pinchHand, Vector3 point, Vector3 center)
    {
        if (!frameCaptured)
        {
            CaptureWorkspaceFrame(center);
        }

        activeHand = pinchHand;
        untrackedTime = 0f;
        pinchStart = point;
        lockedAxis = -1;
        state = State.Pending;

        // Commit the pair the preview was showing, so what you saw is what you get.
        int dropped = pinchHand == previewHand && previewDropped >= 0
            ? previewDropped
            : ClassifyDroppedAxis(point - center, -1);
        offeredAxes[0] = (dropped + 1) % 3;
        offeredAxes[1] = (dropped + 2) % 3;

        for (int i = 0; i < 3; i++)
        {
            ringAlpha[i] = i == dropped ? Hidden : offeredAlpha;
        }

        Debug.Log(
            $"VolumePinchRotation: pinch in {RegionName(dropped)} region, offering " +
            $"{AxisName(offeredAxes[0])} and {AxisName(offeredAxes[1])}",
            this
        );
    }

    private void UpdatePinch()
    {
        IHand hand = activeHand.Hand;

        if (IsSolved())
        {
            EndPinch();
            return;
        }

        // Freeze through brief tracking loss instead of dropping the pinch.
        if (!hand.IsTrackedDataValid || !hand.IsHighConfidence)
        {
            untrackedTime += Time.deltaTime;
            if (untrackedTime > trackingGrace)
            {
                EndPinch();
            }

            return;
        }

        untrackedTime = 0f;

        if (hand.GetFingerPinchStrength(HandFinger.Index) <= pinchOffStrength)
        {
            EndPinch();
            return;
        }

        if (!TryGetPinchPoint(activeHand, out Vector3 point))
        {
            return;
        }

        Vector3 center = skyConstellationPivot.position;

        if (state == State.Pending)
        {
            if (Vector3.Distance(point, pinchStart) >= selectDeadZone)
            {
                LockAxis(point, center);
            }

            return;
        }

        RotateWithHand(point, center);
    }

    private void LockAxis(Vector3 point, Vector3 center)
    {
        // Pick the axis the hand has turned the constellation about most so far.
        float bestTurn = -1f;
        foreach (int axisIndex in offeredAxes)
        {
            float turn = Mathf.Abs(LeveredAngle(pinchStart - center, point - center, WorkspaceAxis(axisIndex)));
            if (turn > bestTurn)
            {
                bestTurn = turn;
                lockedAxis = axisIndex;
            }
        }

        // Measure from here on, so the dead-zone movement doesn't jump the constellation.
        Vector3 planar = Vector3.ProjectOnPlane(point - center, WorkspaceAxis(lockedAxis));
        previousDirection = planar.sqrMagnitude > 0.000001f ? planar.normalized : Vector3.zero;
        state = State.Locked;

        // Only the chosen guide stays.
        foreach (int axisIndex in offeredAxes)
        {
            ringAlpha[axisIndex] = axisIndex == lockedAxis ? selectedAlpha : Hidden;
        }

        if (gripMarker != null)
        {
            gripMarker.enabled = true;
        }

        UpdateGripMarker(center);

        Debug.Log($"VolumePinchRotation: locked {AxisName(lockedAxis)}", this);
    }

    private void RotateWithHand(Vector3 point, Vector3 center)
    {
        Vector3 axis = WorkspaceAxis(lockedAxis);
        Vector3 planar = Vector3.ProjectOnPlane(point - center, axis);

        // On the axis there's no angle to follow; wait for the hand to leave it.
        if (planar.sqrMagnitude < 0.000001f)
        {
            return;
        }

        Vector3 direction = planar.normalized;
        if (previousDirection != Vector3.zero)
        {
            float angle = Vector3.SignedAngle(previousDirection, direction, axis) * LeverScale(planar.magnitude) * gain;
            RotateSky(axis, angle);
        }

        previousDirection = direction;
        UpdateGripMarker(center);
    }

    // Turns the sky by angle about a world axis: the sky is mirrored as
    // S * E^-1 * mini, so the mini turns about E * S^-1 * axis
    // (same remap as GimbalHandleTransformer.TargetAxisRemap).
    private void RotateSky(Vector3 worldAxis, float angle)
    {
        Quaternion skyFromEarth = skyRotationReference.rotation * Quaternion.Inverse(miniEarthReference.rotation);
        miniConstellationPivot.Rotate(Quaternion.Inverse(skyFromEarth) * worldAxis, angle, Space.World);
    }

    private void EndPinch()
    {
        if (activeHand != null)
        {
            // Keep it held so the same pinch can't restart once it's back in range.
            activeHand.Held = true;
        }

        activeHand = null;
        lockedAxis = -1;
        state = State.Idle;
        HideGuides();
    }

    // Short arcs of the pair a pinch would offer right now, for the hand
    // nearest the center that's inside the volume and not already pinching.
    private void UpdatePreview()
    {
        if (!showPreview || IsSolved())
        {
            HidePreview();
            return;
        }

        Vector3 center = skyConstellationPivot.position;
        float entryRadius = volume.Radius * entryRadiusFactor;

        PinchHand nearest = null;
        Vector3 nearestPoint = default;
        float nearestDistance = float.MaxValue;
        foreach (PinchHand pinchHand in pinchHands)
        {
            IHand hand = pinchHand.Hand;
            if (pinchHand.Held || !hand.IsTrackedDataValid || !hand.IsHighConfidence ||
                !TryGetPinchPoint(pinchHand, out Vector3 point))
            {
                continue;
            }

            float distance = Vector3.Distance(point, center);
            if (distance <= entryRadius && distance < nearestDistance)
            {
                nearest = pinchHand;
                nearestPoint = point;
                nearestDistance = distance;
            }
        }

        if (nearest == null)
        {
            HidePreview();
            return;
        }

        if (!frameCaptured)
        {
            CaptureWorkspaceFrame(center);
        }

        if (nearest != previewHand)
        {
            previewHand = nearest;
            previewDropped = -1;
        }

        Vector3 offset = nearestPoint - center;
        previewDropped = ClassifyDroppedAxis(offset, previewDropped);

        float alpha = previewAlpha;
        if (offset.magnitude < volume.Radius * centerFraction)
        {
            alpha *= centerPreviewScale;
        }

        Vector3 toViewer = -WorkspaceAxis(2);
        for (int i = 0; i < 3; i++)
        {
            if (i == previewDropped)
            {
                arcAlpha[i] = Hidden;
                continue;
            }

            // Centered on the hand's angle around this axis; near the axis, face the viewer.
            Vector3 axis = WorkspaceAxis(i);
            Vector3 planar = Vector3.ProjectOnPlane(offset, axis);
            if (planar.sqrMagnitude < 0.0001f)
            {
                planar = Vector3.ProjectOnPlane(toViewer, axis);
            }

            if (planar.sqrMagnitude < 0.000001f)
            {
                planar = WorkspaceAxis((i + 1) % 3);
            }

            arcDirection[i] = planar.normalized;
            arcAlpha[i] = alpha;
        }
    }

    private void HidePreview()
    {
        previewHand = null;
        previewDropped = -1;
        for (int i = 0; i < 3; i++)
        {
            arcAlpha[i] = Hidden;
        }
    }

    private void CaptureWorkspaceFrame(Vector3 center)
    {
        Vector3 view = center - viewer.transform.position;
        if (!tiltFrameWithView)
        {
            view = Vector3.ProjectOnPlane(view, Vector3.up);
        }

        workspaceFrame = view.sqrMagnitude > 0.000001f
            ? Quaternion.LookRotation(view.normalized, Vector3.up)
            : Quaternion.identity;
        frameCaptured = true;
        meshKey = null;
    }

    // The axis the hand sits closest to: its circle would have no radius there.
    // Holds the current answer until another axis wins by the hysteresis margin.
    private int ClassifyDroppedAxis(Vector3 offset, int current)
    {
        if (offset.magnitude < volume.Radius * centerFraction)
        {
            return 2;
        }

        Vector3 local = Quaternion.Inverse(workspaceFrame) * offset;
        float x = Mathf.Abs(local.x);
        float y = Mathf.Abs(local.y);
        float z = Mathf.Abs(local.z) * frontWeight;

        int best = z >= x && z >= y ? 2 : x >= y ? 0 : 1;
        if (current < 0 || best == current)
        {
            return best;
        }

        float bestScore = best == 0 ? x : best == 1 ? y : z;
        float currentScore = current == 0 ? x : current == 1 ? y : z;
        return bestScore > currentScore * (1f + regionHysteresis) ? best : current;
    }

    private Vector3 WorkspaceAxis(int index)
    {
        switch (index)
        {
            case 0:
                return workspaceFrame * Vector3.right;
            case 1:
                return workspaceFrame * Vector3.up;
            default:
                return workspaceFrame * Vector3.forward;
        }
    }

    // Angle about axis from one offset to another, scaled down near the axis.
    private float LeveredAngle(Vector3 from, Vector3 to, Vector3 axis)
    {
        Vector3 planarFrom = Vector3.ProjectOnPlane(from, axis);
        Vector3 planarTo = Vector3.ProjectOnPlane(to, axis);
        if (planarFrom.sqrMagnitude < 0.000001f || planarTo.sqrMagnitude < 0.000001f)
        {
            return 0f;
        }

        return Vector3.SignedAngle(planarFrom, planarTo, axis) * LeverScale(planarTo.magnitude);
    }

    private float LeverScale(float lever)
    {
        return Mathf.Min(1f, lever / Mathf.Max(0.0001f, volume.Radius * minLeverFraction));
    }

    private static bool TryGetPinchPoint(PinchHand pinchHand, out Vector3 point)
    {
        if (pinchHand.Hand.GetJointPose(HandJointId.HandThumbTip, out Pose thumb) &&
            pinchHand.Hand.GetJointPose(HandJointId.HandIndexTip, out Pose index))
        {
            point = (thumb.position + index.position) * 0.5f;
            return true;
        }

        point = default;
        return false;
    }

    private bool IsSolved()
    {
        return solveController != null && solveController.IsSolved;
    }

    // Draws ringAlpha / arcAlpha in the current guide style. Runs every frame
    // so the style and tube settings can be switched live from the Inspector.
    private void ApplyGuides()
    {
        bool anyVisible = false;
        for (int i = 0; i < 3; i++)
        {
            anyVisible |= ringAlpha[i] >= 0f || arcAlpha[i] >= 0f;
        }

        if (anyVisible && frameCaptured)
        {
            bool tube = guideStyle == GuideStyle.Tube && tubeMaterial != null;
            if (tube)
            {
                EnsureTubeMeshes();
            }

            Vector3 center = skyConstellationPivot.position;
            float radius = volume.Radius * guideRadiusFraction;

            for (int i = 0; i < 3; i++)
            {
                ApplyGuide(rings[i], ringAlpha[i], tube, center, RingRotation(i), i, radius, true);

                // Local X = the hand's direction, so the arc is centered on the hand's angle.
                Vector3 axis = WorkspaceAxis(i);
                Quaternion arcRotation = arcAlpha[i] >= 0f
                    ? Quaternion.LookRotation(axis, Vector3.Cross(axis, arcDirection[i]))
                    : Quaternion.identity;
                ApplyGuide(arcs[i], arcAlpha[i], tube, center, arcRotation, i, radius, false);
            }

            return;
        }

        for (int i = 0; i < 3; i++)
        {
            SetVisible(rings[i], false, false);
            SetVisible(arcs[i], false, false);
        }
    }

    private void ApplyGuide(GuideVisual guide, float alpha, bool tube, Vector3 center, Quaternion rotation, int axisIndex, float radius, bool ring)
    {
        if (alpha < 0f)
        {
            SetVisible(guide, false, false);
            return;
        }

        SetVisible(guide, !tube, tube);

        if (tube)
        {
            guide.Tube.transform.SetPositionAndRotation(center, rotation);
            guide.TubeFilter.sharedMesh = ring ? ringMeshes[axisIndex] : arcMesh;
            SetGuideAlpha(guide.Tube, alpha);
            return;
        }

        // Local circle in the XY plane, centered on local +X for arcs.
        float sweep = ring ? 360f : previewArcDegrees;
        int count = ring ? GuideSegments : ArcSegments + 1;
        Vector3[] points = ring ? ringPoints : arcPoints;
        for (int k = 0; k < count; k++)
        {
            float t = ring ? (float)k / GuideSegments : (float)k / ArcSegments;
            float angle = (t - (ring ? 0f : 0.5f)) * sweep * Mathf.Deg2Rad;
            points[k] = center + rotation * new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
        }

        guide.Line.widthMultiplier = guideWidth;
        guide.Line.SetPositions(points);
        SetGuideAlpha(guide.Line, alpha);
    }

    private static void SetVisible(GuideVisual guide, bool line, bool tube)
    {
        if (guide.Line.enabled != line)
        {
            guide.Line.enabled = line;
        }

        if (guide.Tube.enabled != tube)
        {
            guide.Tube.enabled = tube;
        }
    }

    // Local Z = the axis; local X fixed per axis, so the baked back fade lines up.
    private Quaternion RingRotation(int axisIndex)
    {
        return Quaternion.LookRotation(WorkspaceAxis(axisIndex), WorkspaceAxis((axisIndex + 2) % 3));
    }

    private void EnsureTubeMeshes()
    {
        float radius = volume.Radius * guideRadiusFraction;
        string key = $"{radius:F4}|{guideWidth:F4}|{tubeSides}|{backFade:F2}|{previewArcDegrees:F1}";
        if (key == meshKey)
        {
            return;
        }

        meshKey = key;
        float tubeRadius = guideWidth * 0.5f;
        Vector3 toViewer = -WorkspaceAxis(2);

        for (int i = 0; i < 3; i++)
        {
            Vector3 localToViewer = Quaternion.Inverse(RingRotation(i)) * toViewer;
            BuildTubeArc(ringMeshes[i], radius, 360f, GuideSegments, tubeSides, tubeRadius, (radial, t) =>
            {
                // Full brightness toward the viewer, fading to backFade on the far side.
                float facing = Vector3.Dot(radial, localToViewer);
                return Mathf.Lerp(backFade, 1f, Mathf.SmoothStep(0f, 1f, (facing + 0.3f) / 0.4f));
            });
        }

        BuildTubeArc(arcMesh, radius, previewArcDegrees, ArcSegments, tubeSides, tubeRadius, (radial, t) => Mathf.Sin(Mathf.PI * t));
    }

    // A tube following an arc of the circle of the given radius in the local XY
    // plane, centered on local +X. Vertex alpha comes from alphaAt(radial, t).
    private static void BuildTubeArc(Mesh mesh, float radius, float sweepDegrees, int segments, int sides, float tubeRadius, System.Func<Vector3, float, float> alphaAt)
    {
        int rings = segments + 1;
        var vertices = new Vector3[rings * sides];
        var normals = new Vector3[vertices.Length];
        var colors = new Color[vertices.Length];
        var triangles = new int[segments * sides * 6];

        for (int i = 0; i < rings; i++)
        {
            float t = (float)i / segments;
            float angle = (t - 0.5f) * sweepDegrees * Mathf.Deg2Rad;
            var radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            float alpha = Mathf.Clamp01(alphaAt(radial, t));

            for (int j = 0; j < sides; j++)
            {
                float around = 2f * Mathf.PI * j / sides;
                Vector3 normal = radial * Mathf.Cos(around) + Vector3.forward * Mathf.Sin(around);
                int v = i * sides + j;
                vertices[v] = radial * radius + normal * tubeRadius;
                normals[v] = normal;
                colors[v] = new Color(1f, 1f, 1f, alpha);
            }
        }

        int tri = 0;
        for (int i = 0; i < segments; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                int a = i * sides + j;
                int b = i * sides + (j + 1) % sides;
                int c = a + sides;
                int d = b + sides;
                triangles[tri++] = a;
                triangles[tri++] = c;
                triangles[tri++] = b;
                triangles[tri++] = b;
                triangles[tri++] = c;
                triangles[tri++] = d;
            }
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
    }

    private GuideVisual CreateGuideVisual(string name, bool loop, int positionCount, Mesh mesh)
    {
        var guideObject = new GameObject(name);
        guideObject.transform.SetParent(transform, false);

        LineRenderer line = guideObject.AddComponent<LineRenderer>();
        line.sharedMaterial = guideMaterial;
        line.useWorldSpace = true;
        line.loop = loop;
        line.positionCount = positionCount;
        line.widthMultiplier = guideWidth;
        line.numCapVertices = 0;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;

        // The tube lives on a child so it can be posed independently of the world-space line.
        var tubeObject = new GameObject("Tube");
        tubeObject.transform.SetParent(guideObject.transform, false);
        MeshFilter filter = tubeObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer tube = tubeObject.AddComponent<MeshRenderer>();
        tube.sharedMaterial = tubeMaterial;
        tube.shadowCastingMode = ShadowCastingMode.Off;
        tube.receiveShadows = false;
        tube.enabled = false;

        return new GuideVisual { Line = line, Tube = tube, TubeFilter = filter };
    }

    private Renderer CreateGripMarker()
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "Rotation Grip Marker";

        // No collider, so it can't trip the star or hand triggers.
        Destroy(marker.GetComponent<Collider>());

        marker.transform.SetParent(transform, false);
        marker.transform.localScale = Vector3.one * gripMarkerSize;

        Renderer markerRenderer = marker.GetComponent<Renderer>();
        markerRenderer.sharedMaterial = guideMaterial;
        markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
        SetGuideAlpha(markerRenderer, selectedAlpha);
        return markerRenderer;
    }

    private void UpdateGripMarker(Vector3 center)
    {
        if (gripMarker == null || previousDirection == Vector3.zero)
        {
            return;
        }

        gripMarker.transform.position = center + previousDirection * (volume.Radius * guideRadiusFraction);
    }

    private void SetGuideAlpha(Renderer guideRenderer, float alpha)
    {
        Color color = guideColor;
        color.a = alpha;
        guideRenderer.GetPropertyBlock(guideProperties);
        guideProperties.SetColor(BaseColorId, color);
        guideRenderer.SetPropertyBlock(guideProperties);
    }

    private void HideGuides()
    {
        for (int i = 0; i < 3; i++)
        {
            ringAlpha[i] = Hidden;
        }

        if (gripMarker != null)
        {
            gripMarker.enabled = false;
        }
    }

    private static string AxisName(int index)
    {
        return index == 0 ? "X" : index == 1 ? "Y" : "Z";
    }

    private static string RegionName(int droppedAxis)
    {
        return droppedAxis == 0 ? "side" : droppedAxis == 1 ? "top/bottom" : "front/center";
    }
}
