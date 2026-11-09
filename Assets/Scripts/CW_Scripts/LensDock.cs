using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Lets the Star Chart dock onto the Earth View Disc as a lens. Release it
/// close enough and it snaps flat on the disc, lined up exactly: both share the
/// disc layout, so the chart's stars sit over the Earth view's solved star
/// positions. While held near the disc it leans toward it (snap cue); while
/// docked and held it resists, following only part of the hand until pulled
/// past the breakaway distance.
///
/// Runs in LateUpdate, after ISDK's grab transformer has moved the chart to
/// the hand's pose for the frame, and overrides that pose. The transformer
/// works from the grab-start pose, so the override never accumulates.
/// </summary>
[DisallowMultipleComponent]
public sealed class LensDock : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The chart's Grabbable (tells whether it's held).")]
    [SerializeField]
    private Grabbable grabbable;

    [Tooltip("What the chart docks onto: the Earth View Disc root (same layout as the chart).")]
    [SerializeField]
    private Transform dockTarget;

    [Tooltip("Fades the chart into its lens look while docked.")]
    [SerializeField]
    private StarChartLayout layout;

    [Header("Snapping")]
    [Tooltip("Release within this distance of the docked pose to snap on.")]
    [SerializeField]
    [Min(0f)]
    private float snapDistance = 0.05f;

    [Tooltip("Release with the chart tilted less than this from the disc to snap on.")]
    [SerializeField]
    [Range(0f, 90f)]
    private float snapMaxTilt = 40f;

    [Tooltip("Height of the docked chart above the disc (avoids overlapping surfaces).")]
    [SerializeField]
    [Min(0f)]
    private float dockLift = 0.0015f;

    [Tooltip("While held within snap range, how far the chart leans toward the docked pose (0 = no cue).")]
    [SerializeField]
    [Range(0f, 1f)]
    private float magnetPull = 0.3f;

    [Header("Pulling Off")]
    [Tooltip("Hand distance from the docked pose that pops the chart off.")]
    [SerializeField]
    [Min(0.001f)]
    private float breakawayDistance = 0.04f;

    [Tooltip("While docked and held, the share of the hand's movement the chart follows before breaking away.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float holdResistance = 0.15f;

    [Header("Motion")]
    [Tooltip("Time to ease into or out of the dock, and to fade the lens look.")]
    [SerializeField]
    [Min(0.01f)]
    private float snapDuration = 0.15f;

    [SerializeField]
    private bool startDocked = false;

    [SerializeField]
    private UnityEvent<bool> whenDockedChanged = new UnityEvent<bool>();

    private bool docked;
    private bool wasHeld;
    private bool settling;
    private Pose shownPose;
    private Pose lastHandPose;
    private Pose lastWrittenPose;
    private bool wroteLastFrame;

    public bool IsDocked => docked;

    public UnityEvent<bool> WhenDockedChanged => whenDockedChanged;

    private bool IsHeld => grabbable != null && grabbable.SelectingPointsCount > 0;

    private void Start()
    {
        shownPose = new Pose(transform.position, transform.rotation);
        if (startDocked && dockTarget != null)
        {
            SetDocked(true);
            shownPose = DockPose();
            Write(shownPose);
            if (layout != null)
            {
                layout.LensBlend = 1f;
            }
        }
    }

    /// <summary>Dock or undock from code (eases into place).</summary>
    public void SetDocked(bool value)
    {
        if (docked == value)
        {
            return;
        }

        docked = value;
        settling = true;
        whenDockedChanged.Invoke(docked);
    }

    private void LateUpdate()
    {
        if (dockTarget == null)
        {
            return;
        }

        bool held = IsHeld;
        Pose current = new Pose(transform.position, transform.rotation);

        // The hand-driven pose this frame. If the grab transformer didn't run
        // (no move event), the transform still holds what we wrote: reuse the
        // last hand pose instead of compounding our own offset.
        Pose hand = held && wroteLastFrame && SamePose(current, lastWrittenPose) ? lastHandPose : current;
        if (held)
        {
            lastHandPose = hand;
        }

        if (!wroteLastFrame || (held && !wasHeld))
        {
            // We weren't driving the chart (or a grab just started): start from where it is.
            shownPose = current;
        }

        Pose dock = DockPose();
        Pose target;
        bool write;

        if (held)
        {
            if (docked)
            {
                Vector3 offset = hand.position - dock.position;
                if (offset.magnitude > breakawayDistance)
                {
                    SetDocked(false);
                    target = hand;
                }
                else
                {
                    target = new Pose(dock.position + offset * holdResistance, dock.rotation);
                }
            }
            else if (InSnapRange(hand, dock))
            {
                target = Lerp(hand, dock, magnetPull);
            }
            else
            {
                target = hand;
            }

            write = true;
        }
        else
        {
            if (wasHeld && !docked && InSnapRange(hand, dock))
            {
                SetDocked(true);
            }

            target = docked ? dock : shownPose;
            write = docked || settling;
        }

        wasHeld = held;

        float follow = 1f - Mathf.Exp(-Time.deltaTime * 4f / snapDuration);
        shownPose = Lerp(shownPose, target, settling || docked ? follow : 1f);
        if (settling && SamePose(shownPose, target, 0.0002f, 0.2f))
        {
            shownPose = target;
            settling = false;
        }

        if (write)
        {
            Write(shownPose);
        }
        else
        {
            wroteLastFrame = false;
        }

        if (layout != null)
        {
            layout.LensBlend = Mathf.MoveTowards(layout.LensBlend, docked ? 1f : 0f, Time.deltaTime / snapDuration);
        }
    }

    private Pose DockPose()
    {
        return new Pose(dockTarget.position + dockTarget.up * dockLift, dockTarget.rotation);
    }

    private bool InSnapRange(Pose pose, Pose dock)
    {
        return Vector3.Distance(pose.position, dock.position) <= snapDistance &&
               Vector3.Angle(pose.rotation * Vector3.up, dock.rotation * Vector3.up) <= snapMaxTilt;
    }

    private void Write(Pose pose)
    {
        transform.SetPositionAndRotation(pose.position, pose.rotation);
        lastWrittenPose = new Pose(transform.position, transform.rotation);
        wroteLastFrame = true;
    }

    private static Pose Lerp(Pose a, Pose b, float t)
    {
        return new Pose(Vector3.Lerp(a.position, b.position, t), Quaternion.Slerp(a.rotation, b.rotation, t));
    }

    private static bool SamePose(Pose a, Pose b, float distance = 1e-6f, float degrees = 0.01f)
    {
        return (a.position - b.position).sqrMagnitude <= distance * distance &&
               Quaternion.Angle(a.rotation, b.rotation) <= degrees;
    }

    private void OnDrawGizmosSelected()
    {
        if (dockTarget == null)
        {
            return;
        }

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireSphere(dockTarget.position + dockTarget.up * dockLift, snapDistance);
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(dockTarget.position + dockTarget.up * dockLift, breakawayDistance);
    }
}
