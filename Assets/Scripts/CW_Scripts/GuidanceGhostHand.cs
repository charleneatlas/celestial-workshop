using System.Collections.Generic;
using CW.Guidance;
using Oculus.Interaction.HandGrab.Visuals;
using Oculus.Interaction.Input;
using UnityEngine;

/// <summary>
/// First-time demonstration for rotating the constellation: while the guidance
/// "rotate" step is up, a translucent ghost hand pinches beside the small
/// constellation at the workstation and sweeps sideways, left to right, along
/// the turn guide ring (tilted toward the player with the mini's control frame);
/// without a workstation it sweeps low in the globe, below the constellation
/// picture. Then it fades and repeats.
///
/// It gets out of the way at once when the player pinches in the globe, comes
/// back after a quiet spell if the step is still open, and stops for good when
/// the step completes (the first axis lock). Reuses the noticeboard's ghost:
/// the ISDK ghost hand prefab, the GhostHand material and the recorded pen grip
/// (thumb against index reads as a pinch). The noticeboard is not touched.
/// </summary>
public sealed class GuidanceGhostHand : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private GuidanceDirector director;

    [Tooltip("Pinching in the globe hides the ghost.")]
    [SerializeField]
    private VolumePinchRotation pinchRotation;

    [Tooltip("The globe (the demo runs while it is active; without a workstation the demo happens in it).")]
    [SerializeField]
    private CircularWindow volume;

    [Tooltip("The workstation mini the player turns: the demo pinches and sweeps around it. Found automatically if empty.")]
    [SerializeField]
    private WorkstationMini workstation;

    [Tooltip("At the workstation, the sweep circle's radius as a multiple of the mini's reach. 1.1 = the turn guide ring (VolumePinchRotation's constellation guide fraction).")]
    [SerializeField]
    [Range(0.5f, 3f)]
    private float workstationRadiusFactor = 1.1f;

    [Tooltip("At the workstation, the sweep's offset along the ring's axis, as a fraction of its radius. 0 = on the guide ring.")]
    [SerializeField]
    [Range(-1f, 1f)]
    private float workstationHeightFraction = 0f;

    [Tooltip("Player camera (CenterEyeAnchor). Defaults to the main camera.")]
    [SerializeField]
    private Transform viewer;

    [Tooltip("ISDK ghost hand prefab (OpenXRGhost-HandRight / -Left).")]
    [SerializeField]
    private GameObject ghostHandPrefab;

    [SerializeField]
    private Handedness handedness = Handedness.Right;

    [Tooltip("Recorded pen grip: its thumb-to-index shape is used as the pinch.")]
    [SerializeField]
    private PenGripPose gripPose;

    [Tooltip("Translucent ghost material (CelestialWorkshop/GhostHand, uses _Fade).")]
    [SerializeField]
    private Material ghostMaterial;

    [Tooltip("Guidance step that shows the demo.")]
    [SerializeField]
    private string stepId = "rotate";

    [Header("Path (fractions of the globe radius, viewer-relative)")]
    [Tooltip("Height of the sweep below the centre. Negative = below, under the constellation picture.")]
    [SerializeField]
    [Range(-0.95f, 0.95f)]
    private float heightFraction = -0.25f;

    [Tooltip("Distance of the pinch from the globe's vertical axis.")]
    [SerializeField]
    [Range(0.1f, 0.95f)]
    private float leverFraction = 0.55f;

    [Tooltip("Sweep around the vertical axis, centred on the side facing the player (degrees).")]
    [SerializeField]
    [Range(10f, 150f)]
    private float sweepDegrees = 70f;

    [Tooltip("Where the fingers point, in the viewer frame (x right, y up, z away from the player). Default: forward into the globe, a little down and inward, as when reaching in to pinch.")]
    [SerializeField]
    private Vector3 fingerDirection = new Vector3(-0.25f, -0.3f, 1f);

    [Tooltip("Where the back of the hand faces, in the viewer frame. Default: up and a little to the outside, palm toward the globe's centre.")]
    [SerializeField]
    private Vector3 backOfHandDirection = new Vector3(0.45f, 0.85f, -0.15f);

    [Header("Timing (seconds)")]
    [SerializeField]
    [Min(0f)]
    private float startDelay = 1.5f;

    [SerializeField]
    [Min(0.05f)]
    private float approachDuration = 0.5f;

    [SerializeField]
    [Min(0.1f)]
    private float sweepDuration = 1.6f;

    [SerializeField]
    [Min(0.05f)]
    private float releaseDuration = 0.4f;

    [SerializeField]
    [Min(0f)]
    private float pauseDuration = 2f;

    [Tooltip("After the player pinches, wait this long with no pinch before demonstrating again.")]
    [SerializeField]
    [Min(0f)]
    private float returnAfterIdle = 6f;

    [Header("Look")]
    [SerializeField]
    [Range(0f, 1f)]
    private float opacity = 0.5f;

    [Tooltip("How far the hand drifts in from outside the globe before pinching (m).")]
    [SerializeField]
    [Min(0f)]
    private float approachDistance = 0.06f;

    private static readonly int FadeId = Shader.PropertyToID("_Fade");

    private HandGhost ghost;
    private Material materialInstance;
    private readonly List<Renderer> renderers = new List<Renderer>();
    private Transform indexTip;
    private Transform thumbTip;
    private Transform wristBone;
    private Transform middleBase;
    private Transform indexBase;
    private Transform littleBase;

    // The grip's own hand frame (fingers forward, back of hand up) relative to
    // the root rotation passed to SetPose; measured once from the bones.
    private Quaternion handFrameInRoot = Quaternion.identity;
    private bool calibrated;

    private bool stepOpen;
    private bool done;
    private float cycleStart;
    private float resumeAt;
    private float alpha;
    private Quaternion frame = Quaternion.identity;

    private void Awake()
    {
        if (workstation == null)
        {
            workstation = FindFirstObjectByType<WorkstationMini>();
        }
    }

    // Where the demo happens: around the workstation mini when there is one.
    private bool UseWorkstation => workstation != null && workstation.isActiveAndEnabled && workstation.IsReady;

    private Vector3 DemoCenter => UseWorkstation ? workstation.Center : volume.transform.position;

    private float DemoRadius => UseWorkstation ? workstation.Reach * workstationRadiusFactor : volume.Radius;

    private void OnEnable()
    {
        if (director != null)
        {
            director.StepShown += OnStepShown;
            director.StepCompleted += OnStepCompleted;
        }

        if (pinchRotation != null)
        {
            pinchRotation.PinchStarted += OnPlayerPinched;
        }
    }

    private void OnDisable()
    {
        if (director != null)
        {
            director.StepShown -= OnStepShown;
            director.StepCompleted -= OnStepCompleted;
        }

        if (pinchRotation != null)
        {
            pinchRotation.PinchStarted -= OnPlayerPinched;
        }

        SetAlpha(0f);
    }

    private void OnDestroy()
    {
        if (ghost != null)
        {
            Destroy(ghost.gameObject);
        }

        if (materialInstance != null)
        {
            Destroy(materialInstance);
        }
    }

    private void OnStepShown(string id)
    {
        if (id != stepId || done)
        {
            return;
        }

        stepOpen = true;
        resumeAt = Time.time + startDelay;
        cycleStart = resumeAt;
    }

    private void OnStepCompleted(string id)
    {
        if (id == stepId)
        {
            done = true;
            stepOpen = false;
        }
    }

    private void OnPlayerPinched()
    {
        // The player is trying it: step aside right away.
        resumeAt = Time.time + returnAfterIdle;
        cycleStart = resumeAt;
    }

    private void Update()
    {
        bool want = stepOpen && !done && director != null && director.IsActive(stepId) &&
                    volume != null && volume.gameObject.activeInHierarchy &&
                    (pinchRotation == null || !pinchRotation.IsPinching) &&
                    Time.time >= resumeAt;

        if (!want)
        {
            // Quick fade, then hidden.
            SetAlpha(Mathf.MoveTowards(alpha, 0f, Time.deltaTime / 0.15f));
            return;
        }

        EnsureBuilt();
        if (ghost == null)
        {
            return;
        }

        float period = approachDuration + sweepDuration + releaseDuration + pauseDuration;
        float t = (Time.time - cycleStart) % period;

        // Each cycle starts from the current view, so a re-seat doesn't leave it off to one side.
        if (t < Time.deltaTime || frame == Quaternion.identity)
        {
            CaptureFrame();
        }

        float radius = DemoRadius;
        float half = 0.5f * sweepDegrees;
        Vector3 inward = Vector3.zero;
        float azimuth;
        float fade;

        if (t < approachDuration)
        {
            float k = Smooth(t / approachDuration);
            azimuth = -half;
            inward = Arc(-half, radius).normalized * (approachDistance * (1f - k));
            fade = k;
        }
        else if (t < approachDuration + sweepDuration)
        {
            float k = Smooth((t - approachDuration) / sweepDuration);
            azimuth = Mathf.Lerp(-half, half, k);   // left to right
            fade = 1f;
        }
        else if (t < approachDuration + sweepDuration + releaseDuration)
        {
            float k = Smooth((t - approachDuration - sweepDuration) / releaseDuration);
            azimuth = half;
            inward = (frame * Vector3.up) * (0.03f * k);
            fade = 1f - k;
        }
        else
        {
            azimuth = half;
            fade = 0f;
        }

        Vector3 pinchPoint = DemoCenter + Arc(azimuth, radius) + inward;
        PoseAt(pinchPoint);
        SetAlpha(fade * opacity);
    }

    // Point on the sweep circle; azimuth 0 faces the player, positive to the player's right.
    private Vector3 Arc(float azimuthDegrees, float radius)
    {
        float a = azimuthDegrees * Mathf.Deg2Rad;
        Vector3 toward = frame * Vector3.back;
        Vector3 right = frame * Vector3.right;
        float height = UseWorkstation ? workstationHeightFraction : heightFraction;
        float lever = UseWorkstation ? 1f : leverFraction;
        return frame * Vector3.up * (height * radius) +
               (right * Mathf.Sin(a) + toward * Mathf.Cos(a)) * (lever * radius);
    }

    private void CaptureFrame()
    {
        Transform eye = viewer != null ? viewer : (Camera.main != null ? Camera.main.transform : null);
        if (eye == null)
        {
            return;
        }

        // At the workstation the sideways turn is about the mini's control-frame up
        // (tilted toward the player), the axis of the turn guide ring.
        Vector3 up = Vector3.up;
        if (UseWorkstation)
        {
            up = workstation.ControlFrame * Vector3.up;
            if (Vector3.Dot(up, Vector3.up) < 0f)
            {
                up = -up;
            }
        }

        Vector3 look = Vector3.ProjectOnPlane(DemoCenter - eye.position, up);
        if (look.sqrMagnitude > 1e-6f)
        {
            frame = Quaternion.LookRotation(look.normalized, up);
        }
    }

    // Poses the ghost so the middle of its thumb and index tips sits on point,
    // with the fingers and the back of the hand facing the configured directions.
    private void PoseAt(Vector3 point)
    {
        PenGripPose.Side side = gripPose != null ? gripPose.For(handedness) : null;
        if (side == null || !side.valid)
        {
            ghost.transform.position = point;
            return;
        }

        if (!calibrated)
        {
            Calibrate(side);
        }

        Vector3 fingers = frame * (fingerDirection.sqrMagnitude > 1e-6f ? fingerDirection.normalized : Vector3.forward);
        Vector3 back = frame * (backOfHandDirection.sqrMagnitude > 1e-6f ? backOfHandDirection.normalized : Vector3.up);
        Quaternion desired = Quaternion.LookRotation(fingers, back);
        var wrist = new Pose(point, desired * Quaternion.Inverse(handFrameInRoot));
        ghost.SetPose(side.handPose, wrist);

        // Shift so the fingertips meet on the point.
        if (indexTip != null && thumbTip != null)
        {
            Vector3 pinch = 0.5f * (indexTip.position + thumbTip.position);
            wrist.position += point - pinch;
            ghost.SetPose(side.handPose, wrist);
        }
    }

    // Measures the grip's hand frame from its bones with an identity root, so
    // any desired finger / back-of-hand direction can be turned into a root rotation.
    private void Calibrate(PenGripPose.Side side)
    {
        calibrated = true;
        if (wristBone == null || middleBase == null || indexBase == null || littleBase == null)
        {
            Debug.LogWarning("GuidanceGhostHand: hand bones not found; using the root orientation as is.", this);
            return;
        }

        ghost.SetPose(side.handPose, new Pose(Vector3.zero, Quaternion.identity));
        Vector3 fingers = middleBase.position - wristBone.position;
        Vector3 across = indexBase.position - littleBase.position; // toward the thumb side
        Vector3 back = handedness == Handedness.Right ? Vector3.Cross(across, fingers) : Vector3.Cross(fingers, across);
        if (fingers.sqrMagnitude > 1e-8f && back.sqrMagnitude > 1e-8f)
        {
            handFrameInRoot = Quaternion.LookRotation(fingers.normalized, back.normalized);
        }
    }

    private void EnsureBuilt()
    {
        if (ghost != null || ghostHandPrefab == null)
        {
            return;
        }

        if (ghostMaterial != null && materialInstance == null)
        {
            materialInstance = new Material(ghostMaterial) { name = ghostMaterial.name + " (rotate demo)" };
        }

        GameObject hand = Instantiate(ghostHandPrefab);
        hand.name = "Rotate Demo Ghost Hand";
        ghost = hand.GetComponentInChildren<HandGhost>(true);

        foreach (Collider c in hand.GetComponentsInChildren<Collider>(true))
        {
            Destroy(c);
        }

        foreach (Transform bone in hand.GetComponentsInChildren<Transform>(true))
        {
            string n = bone.name.ToLowerInvariant();
            if (indexTip == null && n.Contains("index") && n.Contains("tip"))
            {
                indexTip = bone;
            }
            else if (thumbTip == null && n.Contains("thumb") && n.Contains("tip"))
            {
                thumbTip = bone;
            }
            else if (wristBone == null && n.Contains("wrist"))
            {
                wristBone = bone;
            }
            else if (middleBase == null && n.Contains("middle") && n.Contains("proximal"))
            {
                middleBase = bone;
            }
            else if (indexBase == null && n.Contains("index") && n.Contains("proximal"))
            {
                indexBase = bone;
            }
            else if (littleBase == null && n.Contains("little") && n.Contains("proximal"))
            {
                littleBase = bone;
            }
        }

        if (indexTip == null || thumbTip == null)
        {
            Debug.LogWarning("GuidanceGhostHand: fingertip bones not found; the pinch point is approximate.", this);
        }

        foreach (Renderer r in hand.GetComponentsInChildren<Renderer>(true))
        {
            renderers.Add(r);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (materialInstance != null)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    mats[i] = materialInstance;
                }

                r.sharedMaterials = mats;
            }
        }

        alpha = -1f;
        SetAlpha(0f);
    }

    private void SetAlpha(float a)
    {
        if (Mathf.Approximately(a, alpha))
        {
            return;
        }

        alpha = a;
        if (materialInstance != null)
        {
            materialInstance.SetFloat(FadeId, a);
        }

        bool visible = a > 0.001f;
        foreach (Renderer r in renderers)
        {
            if (r != null && r.enabled != visible)
            {
                r.enabled = visible;
            }
        }
    }

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }
}
