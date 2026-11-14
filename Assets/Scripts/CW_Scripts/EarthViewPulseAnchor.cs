using CW.Guidance;
using UnityEngine;

/// <summary>
/// Guidance target for "the constellation in the globe". The player never sees
/// the 3D sky stars (the headset camera skips the WindowMasked layer); what is
/// in the globe is the Earth view image on the large display. So the bounds this
/// reports are the live sky stars mapped onto that display
/// (ConstellationIntro.DisplayPoint), recomputed every frame so a pulse ring
/// stays on the picture while the constellation turns.
/// </summary>
public sealed class EarthViewPulseAnchor : MonoBehaviour, IGuidanceBoundsSource
{
    [Tooltip("Owns the display mapping (DisplayPoint).")]
    [SerializeField]
    private ConstellationIntro intro;

    [Tooltip("The Earth view camera that renders the display image.")]
    [SerializeField]
    private Camera earthCamera;

    [Tooltip("Sky Constellation Pivot: its active stars are mapped.")]
    [SerializeField]
    private Transform skyPivot;

    [Tooltip("Scales the reported size (the pulse adds its own padding). Lower = tighter ring around the picture.")]
    [SerializeField]
    [Range(0.3f, 1.5f)]
    private float sizeScale = 0.8f;

    [Tooltip("Ignore stars behind the Earth view camera.")]
    [SerializeField]
    private bool skipBehindCamera = true;

    public bool TryGetBounds(out Bounds bounds)
    {
        bounds = default;
        if (intro == null || earthCamera == null || skyPivot == null)
        {
            return false;
        }

        points.Clear();
        foreach (MeshRenderer star in skyPivot.GetComponentsInChildren<MeshRenderer>())
        {
            if (!star.enabled || star.name == "Connection")
            {
                continue;
            }

            Vector3 world = star.bounds.center;
            if (skipBehindCamera && earthCamera.WorldToViewportPoint(world).z <= 0f)
            {
                continue;
            }

            points.Add(intro.DisplayPoint(earthCamera, world));
        }

        if (points.Count == 0)
        {
            return false;
        }

        // Centre of the points' box; then a cube whose extents' length is the
        // farthest star's distance, so a ring sized from extents.magnitude
        // circles the picture (the display is tilted, so the axis-aligned box
        // of the points alone would also count its depth and oversize the ring).
        Bounds box = new Bounds(points[0], Vector3.zero);
        for (int i = 1; i < points.Count; i++)
        {
            box.Encapsulate(points[i]);
        }

        float farthest = 0f;
        foreach (Vector3 point in points)
        {
            farthest = Mathf.Max(farthest, Vector3.Distance(point, box.center));
        }

        bounds = new Bounds(box.center, Vector3.one * (2f * farthest * sizeScale / Mathf.Sqrt(3f)));
        return true;
    }

    private readonly System.Collections.Generic.List<Vector3> points = new System.Collections.Generic.List<Vector3>();
}
