using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Prototype: pinch anywhere inside the near-field volume to rotate the
/// constellation about one fixed workspace axis.
/// Where the pinch lands picks which two axes are offered (the axis the pinch
/// sits on is dropped, since its circle would have no radius there); the first
/// deliberate hand movement locks one of them for the rest of the pinch.
/// Rotation follows the hand's angle around the locked axis, so the grabbed
/// spot stays under the fingers without the hand having to reach the guide.
/// Like the gimbal, it turns the hidden mini pivot about the remapped axis and
/// ConstellationSetup mirrors that to the sky, so solve, settle and reset are
/// unchanged. Pinch Mode off hands control back to the gimbal (A/B toggle).
/// </summary>
public sealed class VolumePinchRotation : MonoBehaviour
{
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

    [Tooltip("Viewer camera (CenterEyeAnchor). Fixes the workspace axes on the first pinch.")]
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

    [Tooltip("A pinch starts a rotation within this multiple of the volume's radius.")]
    [SerializeField]
    [Min(0.1f)]
    private float entryRadiusFactor = 1.1f;

    [Tooltip("Hold the pinch through tracking loss this short.")]
    [SerializeField]
    [Min(0f)]
    private float trackingGrace = 0.2f;

    [Header("Axis Offer")]
    [Tooltip("Inside this fraction of the radius the pinch direction is noise: offer the front pair (X and Y).")]
    [SerializeField]
    [Range(0f, 1f)]
    private float centerFraction = 0.25f;

    [Tooltip("Weight on the front/back component when classifying. Below 1 makes the sides and top easier to reach.")]
    [SerializeField]
    [Range(0.1f, 1f)]
    private float frontWeight = 1f;

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
    [Tooltip("Translucent unlit material; tinted with _BaseColor.")]
    [SerializeField]
    private Material guideMaterial;

    [SerializeField]
    [Range(0.1f, 1f)]
    private float guideRadiusFraction = 0.85f;

    [SerializeField]
    [Min(0.0005f)]
    private float guideWidth = 0.002f;

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

    private const int GuideSegments = 64;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private sealed class PinchHand
    {
        public IHand Hand;
        public bool Held;
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

    private readonly LineRenderer[] guides = new LineRenderer[3];
    private readonly Vector3[] guidePoints = new Vector3[GuideSegments];
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
        for (int i = 0; i < guides.Length; i++)
        {
            guides[i] = CreateGuide(i);
        }

        if (gripMarkerSize > 0f)
        {
            gripMarker = CreateGripMarker();
        }

        HideGuides();
        lastPinchMode = pinchMode;
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

        int dropped = ClassifyDroppedAxis(point - center);
        offeredAxes[0] = (dropped + 1) % 3;
        offeredAxes[1] = (dropped + 2) % 3;

        for (int i = 0; i < guides.Length; i++)
        {
            bool offered = i != dropped;
            guides[i].enabled = offered;
            if (offered)
            {
                LayoutGuide(i, center);
                SetGuideAlpha(guides[i], offeredAlpha);
            }
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
            guides[axisIndex].enabled = axisIndex == lockedAxis;
        }

        SetGuideAlpha(guides[lockedAxis], selectedAlpha);

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
    }

    // The axis the pinch sits closest to: its circle would have no radius there.
    private int ClassifyDroppedAxis(Vector3 offset)
    {
        if (offset.magnitude < volume.Radius * centerFraction)
        {
            return 2;
        }

        Vector3 local = Quaternion.Inverse(workspaceFrame) * offset;
        float x = Mathf.Abs(local.x);
        float y = Mathf.Abs(local.y);
        float z = Mathf.Abs(local.z) * frontWeight;

        if (z >= x && z >= y)
        {
            return 2;
        }

        return x >= y ? 0 : 1;
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

    private LineRenderer CreateGuide(int axisIndex)
    {
        var guideObject = new GameObject($"Rotation Guide {AxisName(axisIndex)}");
        guideObject.transform.SetParent(transform, false);

        LineRenderer line = guideObject.AddComponent<LineRenderer>();
        line.sharedMaterial = guideMaterial;
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = GuideSegments;
        line.widthMultiplier = guideWidth;
        line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
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
        markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        SetGuideAlpha(markerRenderer, selectedAlpha);
        return markerRenderer;
    }

    private void LayoutGuide(int axisIndex, Vector3 center)
    {
        Vector3 axis = WorkspaceAxis(axisIndex);
        Vector3 start = WorkspaceAxis((axisIndex + 1) % 3) * (volume.Radius * guideRadiusFraction);

        for (int i = 0; i < GuideSegments; i++)
        {
            guidePoints[i] = center + Quaternion.AngleAxis(360f * i / GuideSegments, axis) * start;
        }

        guides[axisIndex].SetPositions(guidePoints);
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
        foreach (LineRenderer guide in guides)
        {
            if (guide != null)
            {
                guide.enabled = false;
            }
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
