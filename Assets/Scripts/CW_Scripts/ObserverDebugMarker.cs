using UnityEngine;

/// <summary>
/// Temporary debug aid: a small sphere at the Earth-view observer, the exact
/// point the Earth View Camera renders from. Follows the camera each frame
/// (after EarthViewCamera has placed it), so it always matches the feed.
/// Placement can instead put it where the ray from the viewpoint through the
/// spherical volume's center enters or exits the volume's surface.
///
/// Stays on the Default layer: the room view draws it, and the Earth View
/// Camera (WindowMasked only) never does. No colliders or interaction.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(100)]
public sealed class ObserverDebugMarker : MonoBehaviour
{
    public enum Placement
    {
        Viewpoint,
        VolumeEntry,
        VolumeExit
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [Tooltip("Transform whose position is the Earth-view viewpoint (the Earth View Camera).")]
    [SerializeField]
    private Transform viewpoint;

    [Tooltip("Viewpoint: at the observer. VolumeEntry / VolumeExit: where the ray from the observer through the volume's center crosses its surface (near side / far side).")]
    [SerializeField]
    private Placement placement = Placement.Viewpoint;

    [Tooltip("Spherical volume, for the VolumeEntry / VolumeExit placements.")]
    [SerializeField]
    private CircularWindow volume;

    [Tooltip("Also rotate the marker: up points from the observer toward the volume's center, forward is the Earth view's up direction (so children like the diorama turn with Observer Heading). Off: position only.")]
    [SerializeField]
    private bool orientToObserverRay;

    [Tooltip("Sphere renderer shown at the marker position.")]
    [SerializeField]
    private Renderer markerRenderer;

    [SerializeField]
    private bool showMarker = true;

    [Tooltip("Marker diameter in metres.")]
    [SerializeField]
    [Min(0.001f)]
    private float diameter = 0.02f;

    [SerializeField]
    private Color color = new Color(1f, 0.5f, 0.05f, 1f);

    private MaterialPropertyBlock properties;

    private void OnValidate()
    {
        ApplyAppearance();
    }

    private void OnEnable()
    {
        ApplyAppearance();
    }

    private void OnDisable()
    {
        if (markerRenderer != null)
        {
            markerRenderer.enabled = false;
        }
    }

    private void LateUpdate()
    {
        if (viewpoint != null)
        {
            Vector3 target = TargetPosition(viewpoint.position);
            if (transform.position != target)
            {
                transform.position = target;
            }

            if (orientToObserverRay && TryObserverRayRotation(viewpoint, out Quaternion rotation)
                && Quaternion.Angle(transform.rotation, rotation) > 1e-3f)
            {
                transform.rotation = rotation;
            }
        }

        ApplyAppearance();
    }

    private bool TryObserverRayRotation(Transform observer, out Quaternion rotation)
    {
        rotation = Quaternion.identity;
        if (volume == null)
        {
            return false;
        }

        Vector3 up = volume.transform.position - observer.position;
        if (up.sqrMagnitude < 1e-10f)
        {
            return false;
        }

        up.Normalize();
        Vector3 forward = Vector3.ProjectOnPlane(observer.up, up);
        if (forward.sqrMagnitude < 1e-8f)
        {
            forward = Vector3.ProjectOnPlane(observer.forward, up);
            if (forward.sqrMagnitude < 1e-8f)
            {
                return false;
            }
        }

        rotation = Quaternion.LookRotation(forward.normalized, up);
        return true;
    }

    private Vector3 TargetPosition(Vector3 observer)
    {
        if (placement == Placement.Viewpoint || volume == null)
        {
            return observer;
        }

        Vector3 center = volume.transform.position;
        Vector3 direction = center - observer;
        if (direction.sqrMagnitude < 1e-10f)
        {
            return center;
        }

        direction.Normalize();
        float side = placement == Placement.VolumeEntry ? -1f : 1f;
        return center + direction * (side * volume.Radius);
    }

    private void ApplyAppearance()
    {
        if (markerRenderer == null)
        {
            return;
        }

        markerRenderer.enabled = showMarker && isActiveAndEnabled;

        Transform sphere = markerRenderer.transform;
        Vector3 scale = Vector3.one * diameter;
        if (sphere.localScale != scale)
        {
            sphere.localScale = scale;
        }

        properties ??= new MaterialPropertyBlock();
        markerRenderer.GetPropertyBlock(properties);
        properties.SetColor(BaseColorId, color);
        markerRenderer.SetPropertyBlock(properties);
    }
}
