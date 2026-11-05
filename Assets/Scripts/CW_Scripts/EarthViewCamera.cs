using System.Text;
using UnityEngine;

/// <summary>
/// Stands in for the human observer on Earth. ConstellationGenerator builds
/// the sky constellation by casting a ray from the Sky Observer Reference for
/// each blueprint star, through normalize(x * spread, y * spread, 1), then
/// rotating it by Euler(-elevation, azimuth, 0) in the Sky Coordinate Frame.
/// A pinhole camera at the observer, aimed along that rotated forward axis,
/// with a field of view spanning the artboard, sees the solved constellation
/// exactly as the blueprint draws it.
///
/// NearFieldSkyProxy moves and scales the observer and the sky frame together
/// as one rigid group, so following them keeps the Earth perspective intact.
/// The pose depends only on those two transforms, never on the viewer's head
/// or on where the display window is.
/// </summary>
[RequireComponent(typeof(Camera))]
public sealed class EarthViewCamera : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private ConstellationSetup constellationSetup;

    [SerializeField]
    private ConstellationGenerator constellationGenerator;

    [Tooltip("The Earth observer the sky stars were cast from (SkyObserverReference).")]
    [SerializeField]
    private Transform skyObserverReference;

    [Tooltip("Frame the sky was generated in (Sky Coordinate Frame). Moves with the near-field proxy; the puzzle rotates the pivot under it, never the frame itself.")]
    [SerializeField]
    private Transform skyCoordinateFrame;

    [Tooltip("Sky Constellation Pivot: sets the clip planes and holds the stars checked by Verify.")]
    [SerializeField]
    private Transform skyConstellationPivot;

    [Header("Clip Planes")]
    [Tooltip("Near plane as a fraction of the distance to the constellation's center.")]
    [SerializeField]
    [Range(0.001f, 0.5f)]
    private float nearFraction = 0.05f;

    [Tooltip("Far plane as a multiple of the distance to the constellation's center.")]
    [SerializeField]
    [Min(1.5f)]
    private float farMultiple = 5f;

    private Camera earthCamera;

    private void Awake()
    {
        earthCamera = GetComponent<Camera>();
    }

    private void LateUpdate()
    {
        UpdateView();
    }

    private void UpdateView()
    {
        if (constellationSetup == null ||
            constellationGenerator == null ||
            skyObserverReference == null ||
            skyCoordinateFrame == null)
        {
            return;
        }

        if (earthCamera == null)
        {
            earthCamera = GetComponent<Camera>();
        }

        transform.SetPositionAndRotation(
            skyObserverReference.position,
            skyCoordinateFrame.rotation *
            Quaternion.Euler(
                -constellationSetup.SkyElevationDegrees,
                constellationSetup.SkyAzimuthDegrees,
                0f
            )
        );

        // Blueprint coordinates are normalized by half the artboard's shorter
        // side, so the artboard's half extents are (w, h) / min(w, h).
        Vector2 artboard = constellationGenerator.artboardSize;
        float shortSide = Mathf.Min(artboard.x, artboard.y);
        float tanHalfHeight =
            artboard.y / shortSide *
            constellationGenerator.angularSpread *
            constellationSetup.SkyAngularScale;

        earthCamera.fieldOfView = 2f * Mathf.Atan(tanHalfHeight) * Mathf.Rad2Deg;
        earthCamera.aspect = artboard.x / artboard.y;

        if (skyConstellationPivot != null)
        {
            float distance = Vector3.Distance(transform.position, skyConstellationPivot.position);
            if (distance > 0f)
            {
                earthCamera.nearClipPlane = distance * nearFraction;
                earthCamera.farClipPlane = distance * farMultiple;
            }
        }
    }

    /// <summary>
    /// Projects every sky star through the camera and compares it with where
    /// the blueprint places it. Only meaningful at the solved orientation
    /// (ConstellationSetup > Snap To Canonical Solve).
    /// </summary>
    public string BuildVerificationReport()
    {
        UpdateView();

        if (skyConstellationPivot == null || constellationGenerator == null)
        {
            return "EarthViewCamera: missing references.";
        }

        RenderTexture target = earthCamera.targetTexture;
        Vector2 pixels = target != null
            ? new Vector2(target.width, target.height)
            : new Vector2(1024f, 576f);
        Vector2 artboard = constellationGenerator.artboardSize;

        GeneratedConstellationStar[] skyStars =
            skyConstellationPivot.GetComponentsInChildren<GeneratedConstellationStar>(true);

        var report = new StringBuilder();
        report.AppendLine(
            $"Earth view: fov {earthCamera.fieldOfView:F2} deg, aspect {earthCamera.aspect:F3}, " +
            $"{Vector3.Distance(transform.position, skyConstellationPivot.position):F3} m to pivot, " +
            $"clip {earthCamera.nearClipPlane:F4}-{earthCamera.farClipPlane:F3} m");

        float worstPixels = 0f;
        int checkedStars = 0;

        foreach (ConstellationGenerator.StarData star in constellationGenerator.stars)
        {
            GeneratedConstellationStar match = null;
            foreach (GeneratedConstellationStar candidate in skyStars)
            {
                // Skip the solve-stamp copy, which animates on its own.
                if (candidate.GetComponentInParent<SkyStamp3DVFX>(true) != null)
                {
                    continue;
                }

                if (string.Equals(candidate.StarId, star.starName, System.StringComparison.OrdinalIgnoreCase))
                {
                    match = candidate;
                    break;
                }
            }

            if (match == null)
            {
                report.AppendLine($"  {star.starName}: not found under the sky pivot");
                continue;
            }

            Vector3 viewport = earthCamera.WorldToViewportPoint(match.transform.position);
            var expected = new Vector2(
                star.affinityPosition.x / artboard.x,
                1f - star.affinityPosition.y / artboard.y
            );
            Vector2 errorPixels = Vector2.Scale((Vector2)viewport - expected, pixels);
            worstPixels = Mathf.Max(worstPixels, errorPixels.magnitude);
            checkedStars++;

            report.AppendLine(
                $"  {star.starName}: viewport ({viewport.x:F4}, {viewport.y:F4}), " +
                $"blueprint ({expected.x:F4}, {expected.y:F4}), error {errorPixels.magnitude:F2} px, depth {viewport.z:F3} m");
        }

        report.Insert(0, $"EarthViewCamera: {checkedStars} stars, worst error {worstPixels:F2} px at {pixels.x}x{pixels.y}\n");
        return report.ToString();
    }

    [ContextMenu("Verify Earth View")]
    private void LogVerificationReport()
    {
        Debug.Log(BuildVerificationReport(), this);
    }
}
