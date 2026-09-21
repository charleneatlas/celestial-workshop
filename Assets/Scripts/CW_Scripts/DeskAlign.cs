using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Puts the player at the desk on startup and after every system recenter.
/// With Floor Level on OpenXR the app can't trigger a recenter itself, so instead
/// we move the rig so the headset lands on the rig's authored pose, facing its forward.
/// </summary>
public sealed class DeskAlign : MonoBehaviour
{
    [Tooltip("Rig to align. Found automatically if left empty.")]
    [SerializeField]
    private OVRCameraRig rig;

    private Vector3 homePosition;
    private Quaternion homeRotation;
    private readonly List<XRInputSubsystem> inputSubsystems = new List<XRInputSubsystem>();
    private Coroutine pendingAlign;

    private IEnumerator Start()
    {
        if (rig == null)
            rig = FindFirstObjectByType<OVRCameraRig>();

        if (rig == null)
        {
            Debug.LogWarning("DeskAlign: no OVRCameraRig found.");
            yield break;
        }

        // The authored rig pose is where the player's head should end up.
        homePosition = rig.transform.position;
        homeRotation = rig.transform.rotation;

        SubsystemManager.GetSubsystems(inputSubsystems);
        foreach (var subsystem in inputSubsystems)
            subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;

        if (OVRManager.display != null)
            OVRManager.display.RecenteredPose += RequestAlign;

        while (!OVRManager.tracker.isPositionTracked)
            yield return null;

        RequestAlign();
    }

    private void OnDestroy()
    {
        foreach (var subsystem in inputSubsystems)
            subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;

        if (OVRManager.display != null)
            OVRManager.display.RecenteredPose -= RequestAlign;
    }

    private void OnTrackingOriginUpdated(XRInputSubsystem subsystem) => RequestAlign();

    private void RequestAlign()
    {
        if (pendingAlign != null)
            StopCoroutine(pendingAlign);

        pendingAlign = StartCoroutine(AlignNextFrame());
    }

    private IEnumerator AlignNextFrame()
    {
        // Let the rig pick up the new head pose before reading it.
        yield return null;
        yield return null;

        Align();
        pendingAlign = null;
    }

    private void Align()
    {
        Transform rigTransform = rig.transform;
        Transform head = rig.centerEyeAnchor;

        // Start from the authored pose so repeated calls give the same result.
        rigTransform.SetPositionAndRotation(homePosition, homeRotation);

        Vector3 headForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (headForward.sqrMagnitude < 0.0001f)
            headForward = Vector3.ProjectOnPlane(head.up, Vector3.up); // looking straight up/down

        Vector3 homeForward = homeRotation * Vector3.forward;
        float yawDelta = Vector3.SignedAngle(headForward, homeForward, Vector3.up);
        rigTransform.RotateAround(head.position, Vector3.up, yawDelta);

        // Only slide horizontally; Floor Level already gives the real floor height.
        Vector3 offset = homePosition - head.position;
        offset.y = 0f;
        rigTransform.position += offset;

        Debug.Log($"DeskAlign: aligned (yaw {yawDelta:F1}°, moved {offset.magnitude:F2} m)");
    }
}
