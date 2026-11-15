using CW.Guidance;
using UnityEngine;

/// <summary>
/// Reports the globe as an exact sphere for guidance (e.g. HintLabel's
/// Keep Clear Of list), independent of which of its renderers happen to be
/// enabled. Uses the CircularWindow's radius, so it follows the volume if it
/// is resized. Put it on a child of the globe and reference that child.
/// </summary>
public sealed class GuidanceSphereBounds : MonoBehaviour, IGuidanceBoundsSource
{
    [Tooltip("The globe. Found in parents if empty.")]
    [SerializeField]
    private CircularWindow volume;

    [Tooltip("Multiplies the radius (e.g. 1.05 for a little air around the glass).")]
    [SerializeField]
    [Range(0.5f, 1.5f)]
    private float radiusScale = 1f;

    public bool TryGetBounds(out Bounds bounds)
    {
        if (volume == null)
        {
            volume = GetComponentInParent<CircularWindow>();
        }

        if (volume == null)
        {
            bounds = default;
            return false;
        }

        float radius = volume.Radius * radiusScale;
        bounds = new Bounds(volume.transform.position, Vector3.one * (2f * radius));
        return true;
    }
}
