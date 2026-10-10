using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Prototype: shows the sky constellation as a near-field, object-scale volume
/// instead of out in the sky. Once the head is tracked, it places the volume
/// (a sphere-shaped CircularWindow) in front of the viewer and moves the sky's
/// frame, observer and rotation references together as one rotated, uniformly
/// scaled group so the constellation sits inside the volume.
/// Moving them together keeps every relative direction intact, so rotation
/// mirroring and solve keep roughly working; the solve direction now points
/// back toward the viewer's head position at placement time.
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

    private IEnumerator Start()
    {
        float waited = 0f;
        while (!IsHeadTracked() && waited < trackingTimeout)
        {
            waited += Time.deltaTime;
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

        Transform eye = viewer.transform;
        Vector3 forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

        Vector3 center =
            eye.position +
            forward * forwardDistance +
            Vector3.down * heightBelowEyes;

        // Rotate the sky so its original view direction (observer -> pivot)
        // becomes the new one (eye -> volume center), then shrink it around the pivot.
        Vector3 originalView = skyConstellationPivot.position - skyObserverReference.position;
        Vector3 newView = center - eye.position;
        Quaternion rotation =
            Quaternion.LookRotation(newView, Vector3.up) *
            Quaternion.Inverse(Quaternion.LookRotation(originalView, Vector3.up));
        float scale = volumeRadius * contentFill / contentRadius;

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

        volume.transform.SetPositionAndRotation(center, Quaternion.LookRotation(-newView, Vector3.up));
        volume.Radius = volumeRadius;

        Debug.Log(
            $"NearFieldSkyProxy: volume at {center} (r {volumeRadius:F2} m, {newView.magnitude:F2} m from eyes), " +
            $"sky scaled x{scale:G3}",
            this
        );
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
