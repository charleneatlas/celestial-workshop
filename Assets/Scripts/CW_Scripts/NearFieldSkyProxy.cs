using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Prototype: shows the sky constellation as a near-field, object-scale volume
/// instead of out in the sky. Once the head is tracked, it places the volume
/// (a sphere-shaped CircularWindow) at its authored position (or in front of the
/// viewer) and moves the sky's frame, observer and rotation references together
/// as one rotated, uniformly scaled group so the constellation sits inside it.
/// Moving them together keeps every relative direction intact, so rotation
/// mirroring and solve keep roughly working. Where the Earth observer ends up
/// around the volume is set under Observer: by default straight below it, or
/// on the line back toward the viewer's head at placement time. The observer's
/// distance from the center follows from the scale, and the Earth view image
/// is the same wherever it sits.
/// Optionally moves the gimbal onto the volume, so its rings turn the
/// constellation inside about the ring's own axis.
/// </summary>
public sealed class NearFieldSkyProxy : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private CircularWindow volume;

    [Tooltip("Viewer camera (CenterEyeAnchor).")]
    [SerializeField]
    private Camera viewer;

    [SerializeField]
    private Transform skyConstellationPivot;

    [SerializeField]
    private Transform skyObserverReference;

    [Tooltip("Moved together as one group: the sky frame plus the references the setup and solve scripts measure against.")]
    [SerializeField]
    private List<Transform> skyRoots = new List<Transform>();

    [Header("Placement")]
    [Tooltip("Off: keep the volume where it was placed in the scene. On: place it relative to the head.")]
    [SerializeField]
    private bool placeInFrontOfHead;

    [Tooltip("Eye height above the rig used for the view direction when the head never tracks.")]
    [SerializeField]
    private float fallbackEyeHeight = 1.2f;

    [Tooltip("Horizontal distance from the eyes to the volume's center.")]
    [SerializeField]
    [Min(0.1f)]
    private float forwardDistance = 0.8f;

    [Tooltip("How far below eye height the volume's center sits.")]
    [SerializeField]
    private float heightBelowEyes = 0.3f;

    [SerializeField]
    [Min(0.05f)]
    private float volumeRadius = 0.22f;

    [Tooltip("Fraction of the volume's radius the constellation may reach, in any rotation.")]
    [SerializeField]
    [Range(0.1f, 1f)]
    private float contentFill = 0.8f;

    public enum SkyAnchor
    {
        [Tooltip("The constellation's pivot sits at the volume's center, scaled to Content Fill. The observer ends up outside the volume.")]
        VolumeCenter,

        [Tooltip("The observer sits inside the volume, Observer Inset above the shell (the diorama's eye height), and the sky is scaled around it until the constellation just fits the far side of the volume. Scaling around the observer keeps the Earth view identical.")]
        Observer
    }

    [Header("Sky Scale")]
    [Tooltip("What the sky is scaled around. Observer: the constellation becomes the sky above the diorama.")]
    [SerializeField]
    private SkyAnchor skyAnchor = SkyAnchor.Observer;

    [Tooltip("Observer anchor: how far inside the shell the observer sits, along its view (m). Default = diorama ground thickness + margin + figure eye height.")]
    [SerializeField]
    [Min(0f)]
    private float observerInset = 0.063f;

    [Tooltip("Observer anchor: fraction of the volume's radius the constellation may reach, in any rotation.")]
    [SerializeField]
    [Range(0.1f, 1f)]
    private float skyFill = 0.95f;

    public enum ObserverPlacement
    {
        [Tooltip("Observer on the line from the volume's center back toward the viewer's head at placement time (the original behaviour).")]
        TowardHead,

        [Tooltip("Observer in a fixed direction from the volume's center, set by Observer Elevation and Observer Heading.")]
        FixedDirection
    }

    [Header("Observer")]
    [Tooltip("Where the Earth observer sits around the volume. The Earth view image doesn't change; only the observer's position and the constellation's orientation in the room do.")]
    [SerializeField]
    private ObserverPlacement observerPlacement = ObserverPlacement.FixedDirection;

    [Tooltip("Fixed Direction: observer's angle above the volume's center, seen from the center. -90 = straight below, 0 = level, 90 = straight above.")]
    [SerializeField]
    [Range(-90f, 90f)]
    private float observerElevation = -90f;

    [Tooltip("Fixed Direction: turns the observer around the vertical axis, in degrees. 0 = on the viewer's side. At -90 or 90 elevation this only turns which way is up in the Earth view (0 = up points toward the viewer).")]
    [SerializeField]
    [Range(-180f, 180f)]
    private float observerHeading;

    [Header("Gimbal On Volume")]
    [SerializeField]
    private bool gimbalOnVolume = true;

    [SerializeField]
    private Transform gimbalRoot;

    [Tooltip("Center of the gimbal rings; lands on the volume's center.")]
    [SerializeField]
    private Transform gimbalCenter;

    [Tooltip("Frame the gimbal rotates the mini constellation in (Mini Earth Reference).")]
    [SerializeField]
    private Transform miniEarthReference;

    [Tooltip("Frame the mini's rotation is mirrored into on the sky (Sky Reference Rotation).")]
    [SerializeField]
    private Transform skyRotationReference;

    [Tooltip("Innermost ring's radius as a multiple of the volume's radius.")]
    [SerializeField]
    [Min(0.1f)]
    private float gimbalRadiusFactor = 1.15f;

    [Tooltip("Deactivated while the gimbal is on the volume.")]
    [SerializeField]
    private List<GameObject> hideWhileGimbalOnVolume = new List<GameObject>();

    [Tooltip("Renderers under these are turned off while the gimbal is on the volume; the transforms stay live for rotation mirroring.")]
    [SerializeField]
    private List<Transform> hideRenderersUnder = new List<Transform>();

    [Header("Timing")]
    [Tooltip("Place anyway after this long without head tracking (Editor without a headset).")]
    [SerializeField]
    [Min(0f)]
    private float trackingTimeout = 3f;

    [Tooltip("Wait after tracking starts, so DeskAlign has moved the rig first.")]
    [SerializeField]
    [Min(0f)]
    private float settleDelay = 0.5f;

    private Transform proxyGroup;
    private Vector3 authoredCenter;
    private Vector3 rigHomePosition;
    private bool gimbalMoved;

    /// <summary>True once the volume and sky have been placed (and the gimbal moved, if enabled).</summary>
    public bool IsPlaced { get; private set; }

    /// <summary>Farthest star from the sky pivot after placement (world metres), so the constellation fits a sphere this big around its pivot in any rotation. 0 before placement.</summary>
    public float ConstellationReach { get; private set; }

    private void Awake()
    {
        authoredCenter = transform.position;

        OVRCameraRig rig = viewer != null ? viewer.GetComponentInParent<OVRCameraRig>() : null;
        rigHomePosition = rig != null ? rig.transform.position : authoredCenter;
    }

    private IEnumerator Start()
    {
        // Real time, so a long first-frame hitch (Editor, Link) doesn't use up the timeout.
        float giveUpAt = Time.realtimeSinceStartup + trackingTimeout;
        while (!IsHeadTracked() && Time.realtimeSinceStartup < giveUpAt)
        {
            yield return null;
        }

        // Also lets ConstellationSetup generate and center the sky first.
        yield return new WaitForSeconds(settleDelay);

        Place();
    }

    private static bool IsHeadTracked()
    {
        return OVRManager.tracker != null && OVRManager.tracker.isPositionTracked;
    }

    [ContextMenu("Place Now")]
    private void Place()
    {
        if (volume == null || viewer == null || skyConstellationPivot == null || skyObserverReference == null)
        {
            Debug.LogWarning("NearFieldSkyProxy: missing references.", this);
            return;
        }

        float contentRadius = MeasureContentRadius();
        if (contentRadius <= 0f)
        {
            Debug.LogWarning("NearFieldSkyProxy: no stars found under the sky pivot.", this);
            return;
        }

        // Before tracking the eye sits at the rig origin, which is on the floor.
        bool tracked = IsHeadTracked();
        Transform eye = viewer.transform;
        Vector3 eyePosition = tracked ? eye.position : rigHomePosition + Vector3.up * fallbackEyeHeight;

        Vector3 center = authoredCenter;
        if (placeInFrontOfHead && tracked)
        {
            Vector3 forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

            center =
                eyePosition +
                forward * forwardDistance +
                Vector3.down * heightBelowEyes;
        }

        // Rotate the sky so its original view direction (observer -> pivot)
        // becomes the new one (observer's new spot -> volume center), then
        // shrink it around the pivot.
        Vector3 eyeView = center - eyePosition;
        ObserverView(eyeView, out Vector3 newView, out Vector3 newViewUp);
        Vector3 originalView = skyConstellationPivot.position - skyObserverReference.position;
        Quaternion rotation =
            Quaternion.LookRotation(newView, newViewUp) *
            Quaternion.Inverse(Quaternion.LookRotation(originalView, Vector3.up));
        float scale = volumeRadius * contentFill / contentRadius;

        // Observer anchor: the observer ends up observerDistance from the center
        // and the pivot (observerToPivot * scale) beyond it along the view, so
        // the constellation's far reach is (observerToPivot + contentRadius) *
        // scale - observerDistance from the center; make that the fill radius.
        float observerToPivot = Vector3.Distance(skyObserverReference.position, skyConstellationPivot.position);
        float observerDistance = Mathf.Max(0f, volumeRadius - observerInset);
        if (skyAnchor == SkyAnchor.Observer)
        {
            scale = (volumeRadius * skyFill + observerDistance) / (observerToPivot + contentRadius);
        }

        if (proxyGroup == null)
        {
            proxyGroup = new GameObject("Near-Field Sky Proxy").transform;
            proxyGroup.SetPositionAndRotation(skyConstellationPivot.position, Quaternion.identity);

            foreach (Transform root in skyRoots)
            {
                if (root != null)
                {
                    root.SetParent(proxyGroup, true);
                }
            }
        }

        proxyGroup.SetPositionAndRotation(center, rotation * proxyGroup.rotation);
        proxyGroup.localScale *= scale;

        if (skyAnchor == SkyAnchor.Observer)
        {
            // Slide the whole group (scale stays about the observer's ray) so the
            // observer lands inside the volume, just above the shell.
            Vector3 observerTarget = center - newView.normalized * observerDistance;
            proxyGroup.position += observerTarget - skyObserverReference.position;
        }

        // The volume itself keeps facing the viewer, whatever the observer does.
        volume.transform.SetPositionAndRotation(center, Quaternion.LookRotation(-eyeView, Vector3.up));
        volume.Radius = volumeRadius;

        Debug.Log(
            $"NearFieldSkyProxy: volume at {center} (r {volumeRadius:F2} m, {eyeView.magnitude:F2} m from eyes, " +
            $"observer {observerPlacement}{(observerPlacement == ObserverPlacement.FixedDirection ? $" elevation {observerElevation:F0} heading {observerHeading:F0}" : "")}, " +
            $"head {(tracked ? "tracked" : "NOT tracked, used fallback eye")}), sky scaled x{scale:G3} around the {skyAnchor}, " +
            $"pivot {Vector3.Distance(skyConstellationPivot.position, center):F3} m from center, reach {contentRadius * scale:F3} m",
            this
        );

        if (gimbalOnVolume)
        {
            MoveGimbalOntoVolume(center);
        }

        ConstellationReach = contentRadius * scale;
        IsPlaced = true;
    }

    // Direction the observer looks (toward the volume's center) and which way
    // is up in its view. Toward Head keeps world up, as before. Fixed Direction
    // tilts that frame about the horizontal: at elevation 0 it matches Toward
    // Head on level ground, and as the observer drops below, the top of its
    // view tips toward the heading side, like leaning back to look up.
    private void ObserverView(Vector3 eyeView, out Vector3 view, out Vector3 viewUp)
    {
        if (observerPlacement == ObserverPlacement.TowardHead)
        {
            view = eyeView;
            viewUp = Vector3.up;
            return;
        }

        Vector3 towardViewer = Vector3.ProjectOnPlane(-eyeView, Vector3.up);
        towardViewer = towardViewer.sqrMagnitude > 1e-6f ? towardViewer.normalized : Vector3.back;
        Vector3 heading = Quaternion.AngleAxis(observerHeading, Vector3.up) * towardViewer;

        float pitch = observerElevation * Mathf.Deg2Rad;
        Vector3 toObserver = Mathf.Cos(pitch) * heading + Mathf.Sin(pitch) * Vector3.up;
        view = -toObserver;
        viewUp = -Mathf.Sin(pitch) * heading + Mathf.Cos(pitch) * Vector3.up;
    }

    // Poses the gimbal around the volume the way it sat around the mini
    // constellation. The sky turns as S * E^-1 * mini, so the handles spin the
    // mini about E * S^-1 * ringAxis to turn the sky about the ring's own axis.
    private void MoveGimbalOntoVolume(Vector3 center)
    {
        if (gimbalMoved)
        {
            return;
        }

        if (gimbalRoot == null || gimbalCenter == null || miniEarthReference == null || skyRotationReference == null)
        {
            Debug.LogWarning("NearFieldSkyProxy: missing gimbal references, leaving the gimbal where it is.", this);
            return;
        }

        gimbalRoot.SetParent(null, true);

        // Measure before rotating, while the rings still line up with the world axes.
        float ringRadius = MeasureRingRadius();
        if (ringRadius > 0f)
        {
            gimbalRoot.localScale *= volumeRadius * gimbalRadiusFactor / ringRadius;
        }

        Quaternion skyFromEarth = skyRotationReference.rotation * Quaternion.Inverse(miniEarthReference.rotation);
        gimbalRoot.rotation = skyFromEarth * gimbalRoot.rotation;
        gimbalRoot.position += center - gimbalCenter.position;

        Quaternion earthFromSky = Quaternion.Inverse(skyFromEarth);
        foreach (GimbalHandleTransformer handle in gimbalRoot.GetComponentsInChildren<GimbalHandleTransformer>(true))
        {
            handle.TargetAxisRemap = earthFromSky;
        }

        foreach (GameObject hidden in hideWhileGimbalOnVolume)
        {
            if (hidden != null)
            {
                hidden.SetActive(false);
            }
        }

        foreach (Transform root in hideRenderersUnder)
        {
            if (root == null)
            {
                continue;
            }

            foreach (Renderer hiddenRenderer in root.GetComponentsInChildren<Renderer>(true))
            {
                hiddenRenderer.enabled = false;
            }
        }

        gimbalMoved = true;
    }

    // Radius of the innermost ring, so every ring stays outside the volume.
    private float MeasureRingRadius()
    {
        float radius = float.MaxValue;
        foreach (Renderer ring in gimbalCenter.GetComponentsInChildren<Renderer>())
        {
            Vector3 extents = ring.bounds.extents;
            radius = Mathf.Min(radius, Mathf.Max(extents.x, extents.y, extents.z));
        }

        return radius == float.MaxValue ? 0f : radius;
    }

    // Largest distance from the pivot to any star's bounds, so the
    // constellation stays inside the volume however it's rotated.
    private float MeasureContentRadius()
    {
        float radius = 0f;
        Vector3 pivot = skyConstellationPivot.position;

        foreach (Renderer starRenderer in skyConstellationPivot.GetComponentsInChildren<Renderer>())
        {
            if (!starRenderer.CompareTag("Star"))
            {
                continue;
            }

            Bounds bounds = starRenderer.bounds;
            radius = Mathf.Max(radius, Vector3.Distance(pivot, bounds.center) + bounds.extents.magnitude);
        }

        return radius;
    }
}
