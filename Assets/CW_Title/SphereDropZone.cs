using UnityEngine;

/// <summary>
/// A spherical drop zone (e.g. the puzzle's glass sphere). Radius is in world
/// metres, measured from the center Transform (this object if empty).
/// </summary>
public sealed class SphereDropZone : MonoBehaviour, ITitleDropZone
{
    [SerializeField]
    private Transform center;

    [SerializeField]
    [Min(0.001f)]
    private float radius = 0.2f;

    public Transform Center
    {
        get => center != null ? center : transform;
        set => center = value;
    }

    public float Radius
    {
        get => radius;
        set => radius = Mathf.Max(0.001f, value);
    }

    public bool Contains(Vector3 worldPoint)
    {
        return (worldPoint - Center.position).sqrMagnitude <= radius * radius;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireSphere(Center.position, radius);
    }
}
