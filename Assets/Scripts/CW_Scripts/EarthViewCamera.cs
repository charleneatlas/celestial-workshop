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

    public enum ViewShape
    {
        Artboard,
        Circle
    }

    [Header("Framing")]
    [Tooltip("Artboard: frame exactly the blueprint's rectangle. Circle: a square view centered on the artboard, for a round display; the circle's radius is Circle Radius (artboard pixels).")]
    [SerializeField]
    private ViewShape viewShape = ViewShape.Artboard;

    [Tooltip("Circle framing: size the circle to the current constellation (farthest star from the artboard's center x Fit Margin), so no star is ever cut off. The zoom then differs per constellation; the display's size doesn't.")]
    [SerializeField]
    private bool autoFitCircle = true;

    [Tooltip("Auto-fit: circle radius as a multiple of the farthest star's distance from the artboard's center.")]
    [SerializeField]
    [Range(1f, 2f)]
    private float fitMargin = 1.15f;

    [Tooltip("Circle framing without auto-fit: radius of the visible circle in artboard pixels, around the artboard's center. Must reach the farthest star.")]
    [SerializeField]
    [Min(1f)]
    private float circleRadius = 880f;

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
        // side; the view spans ViewExtent artboard pixels around its center.
        Vector2 artboard = constellationGenerator.artboardSize;
        Vector2 view = ViewExtent(artboard);
        float shortSide = Mathf.Min(artboard.x, artboard.y);
        float tanHalfHeight =
            view.y / shortSide *
            constellationGenerator.angularSpread *
            constellationSetup.SkyAngularScale;

        earthCamera.fieldOfView = 2f * Mathf.Atan(tanHalfHeight) * Mathf.Rad2Deg;
        earthCamera.aspect = view.x / view.y;

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

    // Width and height of the rendered view in artboard pixels, centered on the artboard.
    private Vector2 ViewExtent(Vector2 artboard) =>
        viewShape == ViewShape.Circle ? Vector2.one * (2f * CircleRadius(artboard)) : artboard;

    /// <summary>Radius of the circle framing, in artboard pixels.</summary>
    public float CircleRadius(Vector2 artboard)
    {
        if (!autoFitCircle || constellationGenerator == null || constellationGenerator.stars == null)
        {
            return circleRadius;
        }

        Vector2 center = artboard * 0.5f;
        float farthest = 0f;
        foreach (ConstellationGenerator.StarData star in constellationGenerator.stars)
        {
            farthest = Mathf.Max(farthest, Vector2.Distance(star.affinityPosition, center));
        }

        return farthest > 0f ? farthest * fitMargin : circleRadius;
    }

    /// <summary>
    /// Where an artboard position lands on a round chart framed like the Earth
    /// view's circle: (-1..1, -1..1) across the circle, x right, y up (toward
    /// the image top). Disc screens map x to local +X and y to local +Z, so
    /// anything laid out with this lines up with the Earth view disc.
    /// </summary>
    public static Vector2 ArtboardToDisc(Vector2 artboardPosition, Vector2 artboard, float circleRadiusPixels)
    {
        return new Vector2(
            (artboardPosition.x - artboard.x * 0.5f) / circleRadiusPixels,
            (artboard.y * 0.5f - artboardPosition.y) / circleRadiusPixels
        );
    }

    /// <summary>ArtboardToDisc using this camera's circle framing.</summary>
    public Vector2 ArtboardToDisc(Vector2 artboardPosition)
    {
        Vector2 artboard = constellationGenerator.artboardSize;
        return ArtboardToDisc(artboardPosition, artboard, CircleRadius(artboard));
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
        Vector2 view = ViewExtent(artboard);

        GeneratedConstellationStar[] skyStars =
            skyConstellationPivot.GetComponentsInChildren<GeneratedConstellationStar>(true);

        var report = new StringBuilder();
        report.AppendLine(
            $"Earth view: {viewShape}{(viewShape == ViewShape.Circle ? $" (radius {CircleRadius(artboard):F0} px{(autoFitCircle ? ", auto-fit" : "")})" : "")}, " +
            $"fov {earthCamera.fieldOfView:F2} deg, aspect {earthCamera.aspect:F3}, " +
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
                0.5f + (star.affinityPosition.x - artboard.x * 0.5f) / view.x,
                0.5f + (artboard.y * 0.5f - star.affinityPosition.y) / view.y
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
