using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// The one thing that puts the workshop in front of the player (formerly
/// DeskAlign). With Floor Level on OpenXR the app can't recenter itself, so it
/// moves the rig so the headset lands on the rig's authored pose, facing its
/// forward, plus the player's height offset.
///
/// When it aligns:
/// - at startup, once the headset is worn and reporting a real pose (or after
///   a timeout, so startup never blocks);
/// - on a system recenter, a tracking-origin change, or <see cref="Recenter"/>
///   (the Recenter button);
/// - when the headset is put on, only if the startup align happened without it
///   being worn. Taking it off and on mid-session doesn't move anything.
///
/// Over Link the head can report "tracked" with a placeholder pose (the rig
/// origin, facing forward) before the headset is really delivering poses, so
/// each align waits for a real pose.
///
/// Anything placed relative to the player rather than the workshop (e.g. the
/// noticeboard) listens to <see cref="RigMoved"/> and moves by the same change.
/// </summary>
public sealed class WorkshopPlacement : MonoBehaviour
{
    [Tooltip("Rig to align. Found automatically if left empty.")]
    [SerializeField]
    private OVRCameraRig rig;

    [Tooltip("Optional: the seat turns to face this (e.g. the globe), so it's straight ahead after every align instead of off to the side. Empty = the rig's authored forward.")]
    [SerializeField]
    private Transform faceTarget;

    [Header("Startup")]
    [Tooltip("Align anyway after this long (real seconds) without a worn, tracked headset, so startup never blocks (Editor without a headset, launch from a PC).")]
    [SerializeField]
    [Min(0f)]
    private float startupTimeout = 5f;

    [Tooltip("Wait this long (real seconds) after the headset is worn and tracked before aligning, so the pose has settled.")]
    [SerializeField]
    [Min(0f)]
    private float settleTime = 0.3f;

    [Header("Height")]
    [Tooltip("Largest height offset the player can set, up or down (m).")]
    [SerializeField]
    [Min(0f)]
    private float maxHeightOffset = 0.3f;

    [Tooltip("How much one Raise / Lower press changes the height (m).")]
    [SerializeField]
    [Min(0.005f)]
    private float heightStep = 0.05f;

    [Header("Recenter Countdown")]
    [Tooltip("Recenter button: seconds the player has to sit back and look ahead before the workshop settles in front of them.")]
    [SerializeField]
    [Min(0.5f)]
    private float countdownSeconds = 3f;

    [Tooltip("How quickly the workshop glides to stay in front of the player during the countdown (higher = snappier).")]
    [SerializeField]
    [Min(0.5f)]
    private float followSharpness = 5f;

    private const string HeightPrefKey = "CW_HeightOffset";

    private Vector3 homePosition;
    private Quaternion homeRotation;
    private readonly List<XRInputSubsystem> inputSubsystems = new List<XRInputSubsystem>();
    private Coroutine pendingAlign;
    private bool alignedWhileWorn;
    private float heightOffset;

    /// <summary>True once the first alignment has happened (worn, or by timeout).</summary>
    public bool IsAligned { get; private set; }

    /// <summary>Raised after every rig move, with the rig's pose before and after.</summary>
    public event Action<Pose, Pose> RigMoved;

    public float HeightStep => heightStep;

    /// <summary>True during the Recenter countdown (the workshop follows the player's view).</summary>
    public bool IsChoosing { get; private set; }

    /// <summary>Recenter countdown started; the argument is its length in seconds.</summary>
    public event Action<float> ChoosingStarted;

    /// <summary>Recenter countdown finished (the workshop is settled) or was cancelled.</summary>
    public event Action ChoosingEnded;

    /// <summary>Seconds left in the Recenter countdown (0 when not choosing).</summary>
    public float ChoosingSecondsLeft { get; private set; }

    /// <summary>How much higher the workshop sits than authored, relative to the player (m; positive = higher). Saved between sessions.</summary>
    public float HeightOffset
    {
        get => heightOffset;
        set
        {
            float clamped = Mathf.Clamp(value, -maxHeightOffset, maxHeightOffset);
            if (Mathf.Approximately(clamped, heightOffset))
            {
                return;
            }

            heightOffset = clamped;
            SaveHeight();
            if (IsAligned)
            {
                ApplyHeight("height " + (heightOffset >= 0f ? "+" : "") + heightOffset.ToString("0.00") + " m");
            }
        }
    }

    private void Awake()
    {
        if (rig == null)
            rig = FindFirstObjectByType<OVRCameraRig>();

        if (rig != null)
        {
            // The authored rig pose is where the player's head should end up.
            homePosition = rig.transform.position;
            homeRotation = rig.transform.rotation;

            if (faceTarget != null)
            {
                Vector3 toTarget = Vector3.ProjectOnPlane(faceTarget.position - homePosition, Vector3.up);
                if (toTarget.sqrMagnitude > 1e-6f)
                {
                    homeRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                }
            }
        }

        try
        {
            heightOffset = Mathf.Clamp(PlayerPrefs.GetFloat(HeightPrefKey, 0f), -maxHeightOffset, maxHeightOffset);
        }
        catch (Exception)
        {
            heightOffset = 0f;
        }
    }

    private void Start()
    {
        if (rig == null)
        {
            Debug.LogWarning("WorkshopPlacement: no OVRCameraRig found.");
            return;
        }

        SubsystemManager.GetSubsystems(inputSubsystems);
        foreach (var subsystem in inputSubsystems)
            subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;

        if (OVRManager.display != null)
            OVRManager.display.RecenteredPose += OnSystemRecenter;

        OVRManager.HMDMounted += OnHeadsetMounted;

        pendingAlign = StartCoroutine(StartupAlign());
    }

    private void OnDestroy()
    {
        foreach (var subsystem in inputSubsystems)
            subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;

        if (OVRManager.display != null)
            OVRManager.display.RecenteredPose -= OnSystemRecenter;

        OVRManager.HMDMounted -= OnHeadsetMounted;
    }

    // ---- Public controls ----

    /// <summary>
    /// Recenter button: a short countdown in which the workshop glides to stay
    /// in front of the player's view while they sit back and look ahead; then
    /// it settles there. (Aligning on the press itself would use the head
    /// leaning in to the button, so the workshop would end up too far away.)
    /// </summary>
    [ContextMenu("Recenter")]
    public void Recenter()
    {
        if (rig == null)
            return;

        if (pendingAlign != null)
            StopCoroutine(pendingAlign);

        pendingAlign = StartCoroutine(ChooseAndSettle());
    }

    /// <summary>Immediate recenter from the current head pose (no countdown).</summary>
    [ContextMenu("Recenter Now")]
    public void RecenterNow() => RequestAlign("recenter now");

    [ContextMenu("Raise Workshop")]
    public void Raise() => HeightOffset += heightStep;

    [ContextMenu("Lower Workshop")]
    public void Lower() => HeightOffset -= heightStep;

    [ContextMenu("Reset Height")]
    public void ResetHeight() => HeightOffset = 0f;

    // ---- Triggers ----

    private void OnTrackingOriginUpdated(XRInputSubsystem subsystem) => RequestAlign("tracking origin changed");

    private void OnSystemRecenter() => RequestAlign("system recenter");

    private void OnHeadsetMounted()
    {
        // Only fix a startup align that didn't have the player in the headset;
        // otherwise putting it back on mid-session would snap the scene.
        if (IsAligned && !alignedWhileWorn)
            RequestAlign("headset put on");
    }

    private void RequestAlign(string reason)
    {
        if (rig == null)
            return;

        if (pendingAlign != null)
            StopCoroutine(pendingAlign);

        EndChoosing();

        pendingAlign = StartCoroutine(AlignWhenReady(reason));
    }

    private IEnumerator StartupAlign()
    {
        // Real time, so a long first-frame hitch (Editor, Link) doesn't use up the timeout.
        float giveUpAt = Time.realtimeSinceStartup + startupTimeout;
        float readySince = -1f;
        while (Time.realtimeSinceStartup < giveUpAt)
        {
            if (HasRealHeadPose() && IsWorn())
            {
                if (readySince < 0f)
                    readySince = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - readySince >= settleTime)
                    break;
            }
            else
            {
                readySince = -1f;
            }

            yield return null;
        }

        bool worn = HasRealHeadPose() && IsWorn();
        Align(worn ? "startup, headset worn" : $"startup timeout ({startupTimeout:0.#} s): tracked {IsTracked()}, real pose {HasRealHeadPose()}, worn {IsWorn()}");
        alignedWhileWorn = worn;
        pendingAlign = null;
    }

    private IEnumerator AlignWhenReady(string reason)
    {
        // Let the rig pick up the new head pose before reading it.
        yield return null;
        yield return null;

        while (!HasRealHeadPose())
            yield return null;

        Align(reason);
        if (IsWorn())
            alignedWhileWorn = true;
        pendingAlign = null;
    }

    private IEnumerator ChooseAndSettle()
    {
        IsChoosing = true;
        ChoosingSecondsLeft = countdownSeconds;
        ChoosingStarted?.Invoke(countdownSeconds);
        Debug.Log($"WorkshopPlacement: recenter countdown ({countdownSeconds:0.#} s)");

        Transform rigTransform = rig.transform;
        float endAt = Time.realtimeSinceStartup + countdownSeconds;
        while (Time.realtimeSinceStartup < endAt)
        {
            ChoosingSecondsLeft = endAt - Time.realtimeSinceStartup;
            if (HasRealHeadPose())
            {
                // Glide toward the pose that would put the workshop in front of
                // the head right now.
                AlignedPose(out Vector3 targetPosition, out Quaternion targetRotation, out _, out _);
                float k = 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
                Pose before = new Pose(rigTransform.position, rigTransform.rotation);
                rigTransform.SetPositionAndRotation(
                    Vector3.Lerp(before.position, targetPosition, k),
                    Quaternion.Slerp(before.rotation, targetRotation, k));
                RigMoved?.Invoke(before, new Pose(rigTransform.position, rigTransform.rotation));
            }

            yield return null;
        }

        while (!HasRealHeadPose())
            yield return null;

        Align("recenter button");
        if (IsWorn())
            alignedWhileWorn = true;
        pendingAlign = null;
        EndChoosing();
    }

    private void EndChoosing()
    {
        if (!IsChoosing)
            return;

        IsChoosing = false;
        ChoosingSecondsLeft = 0f;
        ChoosingEnded?.Invoke();
    }

    // ---- State ----

    private static bool IsTracked() => OVRManager.tracker != null && OVRManager.tracker.isPositionTracked;

    // Presence sensor; reports true where there isn't one (Editor, some Link setups).
    private static bool IsWorn() => OVRManager.instance == null || OVRManager.instance.isUserPresent;

    // A placeholder pose sits exactly on the rig origin's vertical axis, facing
    // straight ahead. A worn headset is never exactly there.
    private bool HasRealHeadPose()
    {
        if (!IsTracked())
            return false;

        Transform head = rig.centerEyeAnchor;
        Vector3 local = head.localPosition;
        return Mathf.Abs(local.x) + Mathf.Abs(local.z) > 1e-4f
            || Quaternion.Angle(head.localRotation, Quaternion.identity) > 0.01f;
    }

    // ---- Moves ----

    private void Align(string reason)
    {
        Transform rigTransform = rig.transform;
        Pose before = new Pose(rigTransform.position, rigTransform.rotation);
        AlignedPose(out Vector3 position, out Quaternion rotation, out float yawDelta, out float slide);
        rigTransform.SetPositionAndRotation(position, rotation);

        IsAligned = true;
        Pose after = new Pose(rigTransform.position, rigTransform.rotation);
        Debug.Log($"WorkshopPlacement: aligned ({reason}; t {Time.realtimeSinceStartup:0.0} s, yaw {yawDelta:F1}°, moved {slide:F2} m, height {heightOffset:+0.00;-0.00;0.00} m)");
        RigMoved?.Invoke(before, after);
    }

    // The rig pose that puts the head on the authored home pose, facing its
    // forward (yaw only), plus the height offset. Computed from the head's pose
    // relative to the rig, so it doesn't move anything.
    private void AlignedPose(out Vector3 position, out Quaternion rotation, out float yawDelta, out float slide)
    {
        Transform rigTransform = rig.transform;
        Transform head = rig.centerEyeAnchor;
        Vector3 headLocal = rigTransform.InverseTransformPoint(head.position);
        Vector3 headForwardLocal = rigTransform.InverseTransformDirection(head.forward);
        Vector3 headUpLocal = rigTransform.InverseTransformDirection(head.up);

        // Head yaw in the rig's frame, as if the rig sat at home.
        Vector3 headForward = Vector3.ProjectOnPlane(homeRotation * headForwardLocal, Vector3.up);
        if (headForward.sqrMagnitude < 0.0001f)
            headForward = Vector3.ProjectOnPlane(homeRotation * headUpLocal, Vector3.up); // looking straight up/down

        Vector3 homeForward = homeRotation * Vector3.forward;
        yawDelta = Vector3.SignedAngle(headForward, homeForward, Vector3.up);
        rotation = Quaternion.AngleAxis(yawDelta, Vector3.up) * homeRotation;

        // Slide horizontally; Floor Level gives the real floor height. The
        // height offset moves the rig the other way, so the workshop rises or
        // falls relative to the player.
        Vector3 headWorld = homePosition + rotation * headLocal;
        Vector3 offset = homePosition - headWorld;
        offset.y = 0f;
        slide = offset.magnitude;
        position = homePosition + offset - Vector3.up * heightOffset;
    }

    // Height change only: move the rig vertically, keep everything else.
    private void ApplyHeight(string reason)
    {
        Transform rigTransform = rig.transform;
        Pose before = new Pose(rigTransform.position, rigTransform.rotation);
        Vector3 p = rigTransform.position;
        p.y = homePosition.y - heightOffset;
        rigTransform.position = p;
        Pose after = new Pose(rigTransform.position, rigTransform.rotation);
        Debug.Log($"WorkshopPlacement: {reason}");
        RigMoved?.Invoke(before, after);
    }

    private void SaveHeight()
    {
        try
        {
            PlayerPrefs.SetFloat(HeightPrefKey, heightOffset);
            PlayerPrefs.Save();
        }
        catch (Exception)
        {
            // Not saved this time; the offset still applies for the session.
        }
    }
}
