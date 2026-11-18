using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Capture-branch only: a stationary 16:9 camera for the Big Dipper cinematic.
///
/// It sits where the Earth View Camera sits (the observer the sky was cast
/// from), so the solved constellation lines up into its familiar shape in
/// frame and comes apart when the puzzle is turned. It draws only what the
/// Earth view draws (sky stars, lines and the projected hand), never the room, passthrough, diorama or UI, and renders
/// into its own render texture, so the headset cameras, input and gameplay
/// are untouched. Unity Recorder picks it up by tag (Targeted Camera).
///
/// The field of view fits the sphere the stars sweep around the puzzle pivot,
/// so no star leaves the frame at any rotation.
/// </summary>
[DefaultExecutionOrder(1100)] // after EarthViewCamera (0) and ProjectedHands (1000)
[RequireComponent(typeof(Camera))]
public sealed class CinematicCaptureCamera : MonoBehaviour
{
    public const string CaptureTag = "CinematicCapture";

    [Header("Source")]
    [Tooltip("Copies this camera's pose (the Earth observer) every frame. It only moves when the workshop is placed, so the shot stays still while solving.")]
    [SerializeField]
    private Camera earthViewCamera;

    [Tooltip("Sky Constellation Pivot: the puzzle turns the stars about it. Used to fit the frame.")]
    [SerializeField]
    private Transform skyConstellationPivot;

    [Tooltip("Off: the camera stays wherever you put it (set the Transform by hand).")]
    [SerializeField]
    private bool followEarthView = true;

    [Header("Framing")]
    [Tooltip("Fit the frame height to every star at any puzzle rotation. Off: use Manual Vertical FOV.")]
    [SerializeField]
    private bool fitAnyRotation = true;

    [Tooltip("Extra view angle (degrees) around the stars' sweep, for glow and breathing room.")]
    [SerializeField]
    [Range(0f, 15f)]
    private float paddingDegrees = 2f;

    [Tooltip("Zoom on top of the fit: 1 = fit, above 1 = tighter (stars can leave the frame when turned), below 1 = wider.")]
    [SerializeField]
    [Range(0.5f, 2f)]
    private float zoom = 1f;

    [Tooltip("Vertical field of view when Fit Any Rotation is off.")]
    [SerializeField]
    [Range(5f, 90f)]
    private float manualVerticalFov = 35f;

    [Tooltip("Pull the camera back along its view axis (metres, world). 0 = exactly the observer's view.")]
    [SerializeField]
    [Min(0f)]
    private float dollyBack = 0f;

    [Tooltip("Roll about the view axis (degrees), for composition.")]
    [SerializeField]
    [Range(-45f, 45f)]
    private float roll = 0f;

    [Tooltip("Shift the frame (fractions of frame size) without turning the camera: x right, y up.")]
    [SerializeField]
    private Vector2 lensShift = Vector2.zero;

    [Header("Look")]
    [Tooltip("Earth view's starfield quad. It is sized for the square Earth view, so it is hidden while this camera renders (plain dark background).")]
    [SerializeField]
    private Renderer earthBackdrop;

    [SerializeField]
    private bool hideEarthBackdrop = true;

    [Tooltip("Background colour behind everything. Empty alpha = copy the Earth view's.")]
    [SerializeField]
    private Color background = new Color(0f, 0f, 0f, 0f);

    [Tooltip("URP renderer index (the Earth view uses 1).")]
    [SerializeField]
    private int rendererIndex = 1;

    private Camera captureCamera;
    private bool backdropHidden;
    private float localStarReach;
    private int localStarReachCount = -1;
    private readonly List<GeneratedConstellationStar> starBuffer = new List<GeneratedConstellationStar>();

    private void Awake()
    {
        captureCamera = GetComponent<Camera>();
        captureCamera.stereoTargetEye = StereoTargetEyeMask.None; // never draws to the headset
        captureCamera.usePhysicalProperties = false;
        if (captureCamera.targetTexture == null)
        {
            Debug.LogWarning("CinematicCaptureCamera: no Target Texture; assign CinematicCapture_1080p so it never draws over the Game view.", this);
        }

        UniversalAdditionalCameraData data = captureCamera.GetUniversalAdditionalCameraData();
        data.SetRenderer(rendererIndex);
        data.renderPostProcessing = false;
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        RenderPipelineManager.endCameraRendering += OnEndCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        RenderPipelineManager.endCameraRendering -= OnEndCamera;
        RestoreBackdrop();
    }

    private void LateUpdate()
    {
        if (captureCamera == null)
        {
            captureCamera = GetComponent<Camera>();
        }

        if (earthViewCamera == null)
        {
            return;
        }

        captureCamera.cullingMask = earthViewCamera.cullingMask;
        captureCamera.clearFlags = CameraClearFlags.SolidColor;
        captureCamera.backgroundColor = background.a > 0f ? background : earthViewCamera.backgroundColor;

        if (followEarthView)
        {
            Transform source = earthViewCamera.transform;
            Quaternion rotation = source.rotation * Quaternion.Euler(0f, 0f, roll);
            transform.SetPositionAndRotation(source.position - source.forward * dollyBack, rotation);
        }

        captureCamera.aspect = 16f / 9f;
        captureCamera.lensShift = lensShift;
        captureCamera.fieldOfView = Mathf.Clamp(FitVerticalFov() / zoom, 1f, 170f);

        float distance = skyConstellationPivot != null
            ? Vector3.Distance(transform.position, skyConstellationPivot.position)
            : 0f;
        captureCamera.nearClipPlane = Mathf.Max(0.001f, earthViewCamera.nearClipPlane);
        captureCamera.farClipPlane = Mathf.Max(earthViewCamera.farClipPlane + dollyBack, distance * 2f);
    }

    // Vertical field of view (degrees) that holds the stars' sweep sphere.
    private float FitVerticalFov()
    {
        if (!fitAnyRotation || skyConstellationPivot == null)
        {
            return manualVerticalFov;
        }

        RefreshLocalStarReach();
        Vector3 toPivot = skyConstellationPivot.position - transform.position;
        float distance = toPivot.magnitude;
        float reach = localStarReach * skyConstellationPivot.lossyScale.x;
        if (reach <= 0f || distance <= reach)
        {
            return manualVerticalFov;
        }

        float halfAngle =
            Vector3.Angle(transform.forward, toPivot) +
            Mathf.Asin(reach / distance) * Mathf.Rad2Deg +
            paddingDegrees;
        return Mathf.Min(2f * halfAngle, 170f);
    }

    private void RefreshLocalStarReach()
    {
        starBuffer.Clear();
        skyConstellationPivot.GetComponentsInChildren(true, starBuffer);
        if (starBuffer.Count == localStarReachCount)
        {
            return;
        }

        localStarReachCount = starBuffer.Count;
        localStarReach = 0f;
        foreach (GeneratedConstellationStar star in starBuffer)
        {
            // The solve-stamp copy animates on its own.
            if (star.GetComponentInParent<SkyStamp3DVFX>(true) != null)
            {
                continue;
            }

            localStarReach = Mathf.Max(localStarReach, skyConstellationPivot.InverseTransformPoint(star.transform.position).magnitude);
        }
    }

    // Hide the Earth view's square backdrop only while this camera renders.
    private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam != captureCamera || !hideEarthBackdrop || earthBackdrop == null || !earthBackdrop.enabled)
        {
            return;
        }

        earthBackdrop.enabled = false;
        backdropHidden = true;
    }

    private void OnEndCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam == captureCamera)
        {
            RestoreBackdrop();
        }
    }

    private void RestoreBackdrop()
    {
        if (backdropHidden && earthBackdrop != null)
        {
            earthBackdrop.enabled = true;
        }

        backdropHidden = false;
    }
}
