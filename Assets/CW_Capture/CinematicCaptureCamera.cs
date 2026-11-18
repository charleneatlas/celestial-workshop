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
/// Earth view draws (sky stars, lines and the projected hand, plus an optional
/// starfield background), never the room, passthrough, diorama or UI, and renders
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

    public enum BackgroundMode
    {
        SolidColor,
        Skybox,
        EarthBackdrop
    }

    [Header("Look")]
    [Tooltip("What fills the frame behind the constellation: a plain colour, the scene's starfield skybox, or the Earth view's starfield backdrop texture stretched to 16:9.")]
    [SerializeField]
    private BackgroundMode backgroundMode = BackgroundMode.SolidColor;

    [Tooltip("Background colour (Solid Color mode). Empty alpha = copy the Earth view's.")]
    [SerializeField]
    private Color background = new Color(0f, 0f, 0f, 0f);

    [Tooltip("Skybox mode: material to show. Empty = the scene's skybox (Lighting settings). This camera draws a private copy, so gameplay is untouched.")]
    [SerializeField]
    private Material skyboxMaterial;

    [Tooltip("Skybox mode: brightness multiplier on the material's exposure. Lower keeps the constellation dominant.")]
    [SerializeField]
    [Range(0f, 3f)]
    private float skyboxBrightness = 1f;

    [Tooltip("Skybox mode: turn the sky about the vertical axis (degrees) to choose which stars sit behind the constellation.")]
    [SerializeField]
    [Range(0f, 360f)]
    private float skyboxRotation = 0f;

    [Tooltip("Earth View Backdrop quad (child of the Earth View Camera, EarthViewOnly layer).")]
    [SerializeField]
    private Renderer earthBackdrop;

    [Tooltip("Earth Backdrop mode: degrees of sky the texture spans (the Earth view uses 90).")]
    [SerializeField]
    [Min(1f)]
    private float backdropTextureSpan = 90f;

    [Tooltip("Earth Backdrop mode: tint (the Earth view uses 0.6 grey).")]
    [SerializeField]
    private Color backdropTint = new Color(0.6f, 0.6f, 0.6f, 1f);

    [Tooltip("URP renderer index (the Earth view uses 1).")]
    [SerializeField]
    private int rendererIndex = 1;

    private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
    private static readonly int RotationId = Shader.PropertyToID("_Rotation");

    private Camera captureCamera;
    private Skybox skyboxComponent;
    private Material skyboxInstance;
    private Material skyboxInstanceSource;
    private float skyboxBaseExposure = 1f;
    private MaterialPropertyBlock backdropBlock;
    private MaterialPropertyBlock savedBackdropBlock;
    private bool backdropSwapped;
    private bool backdropWasEnabled;
    private Vector3 backdropPosition;
    private Quaternion backdropRotation;
    private Vector3 backdropScale;
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

    private void OnDestroy()
    {
        if (skyboxInstance != null)
        {
            Destroy(skyboxInstance);
        }
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
        captureCamera.backgroundColor = background.a > 0f ? background : earthViewCamera.backgroundColor;
        captureCamera.clearFlags = backgroundMode == BackgroundMode.Skybox && UpdateSkybox()
            ? CameraClearFlags.Skybox
            : CameraClearFlags.SolidColor;

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

    // Skybox mode: a private copy of the sky material, so brightness and
    // rotation never touch the scene's own skybox.
    private bool UpdateSkybox()
    {
        Material source = skyboxMaterial != null ? skyboxMaterial : RenderSettings.skybox;
        if (source == null)
        {
            return false;
        }

        if (skyboxInstance == null || skyboxInstanceSource != source)
        {
            if (skyboxInstance != null)
            {
                Destroy(skyboxInstance);
            }

            skyboxInstance = new Material(source) { name = source.name + " (Cinematic Capture)" };
            skyboxInstanceSource = source;
            skyboxBaseExposure = source.HasProperty(ExposureId) ? source.GetFloat(ExposureId) : 1f;
        }

        if (skyboxInstance.HasProperty(ExposureId))
        {
            skyboxInstance.SetFloat(ExposureId, skyboxBaseExposure * skyboxBrightness);
        }

        if (skyboxInstance.HasProperty(RotationId))
        {
            skyboxInstance.SetFloat(RotationId, skyboxRotation);
        }

        if (skyboxComponent == null && !TryGetComponent(out skyboxComponent))
        {
            skyboxComponent = gameObject.AddComponent<Skybox>();
            skyboxComponent.hideFlags = HideFlags.DontSave;
        }

        skyboxComponent.material = skyboxInstance;
        return true;
    }

    // While this camera renders, the Earth backdrop is hidden or, in Earth
    // Backdrop mode, shown and refitted to this camera's 16:9 frame. The
    // Earth view gets it back untouched afterwards.
    private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam != captureCamera || earthBackdrop == null)
        {
            return;
        }

        bool show = backgroundMode == BackgroundMode.EarthBackdrop;
        if (!show && !earthBackdrop.enabled)
        {
            return;
        }

        Transform quad = earthBackdrop.transform;
        backdropSwapped = true;
        backdropWasEnabled = earthBackdrop.enabled;
        backdropPosition = quad.position;
        backdropRotation = quad.rotation;
        backdropScale = quad.localScale;
        savedBackdropBlock ??= new MaterialPropertyBlock();
        earthBackdrop.GetPropertyBlock(savedBackdropBlock);

        earthBackdrop.enabled = show;
        if (show)
        {
            FitBackdropToCapture(quad);
        }
    }

    private void FitBackdropToCapture(Transform quad)
    {
        float distance = skyConstellationPivot != null
            ? Vector3.Distance(transform.position, skyConstellationPivot.position)
            : captureCamera.farClipPlane * 0.2f;
        float depth = Mathf.Lerp(distance, captureCamera.farClipPlane, 0.85f);
        float height = 2f * depth * Mathf.Tan(captureCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
        Vector3 parentScale = quad.parent != null ? quad.parent.lossyScale : Vector3.one;
        Vector2 shift = captureCamera.lensShift;

        quad.SetPositionAndRotation(
            transform.position +
            transform.forward * depth +
            transform.right * (shift.x * height * captureCamera.aspect) +
            transform.up * (shift.y * height),
            transform.rotation);
        quad.localScale = new Vector3(
            height * captureCamera.aspect / Mathf.Max(parentScale.x, 1e-5f),
            height / Mathf.Max(parentScale.y, 1e-5f),
            1f);

        float tileY = Mathf.Clamp(captureCamera.fieldOfView / backdropTextureSpan, 0.01f, 1f);
        float tileX = Mathf.Clamp(tileY * captureCamera.aspect, 0.01f, 1f);
        backdropBlock ??= new MaterialPropertyBlock();
        earthBackdrop.GetPropertyBlock(backdropBlock);
        backdropBlock.SetVector(BaseMapStId, new Vector4(tileX, tileY, 0.5f - tileX * 0.5f, 0.5f - tileY * 0.5f));
        backdropBlock.SetColor(BaseColorId, backdropTint);
        earthBackdrop.SetPropertyBlock(backdropBlock);
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
        if (backdropSwapped && earthBackdrop != null)
        {
            Transform quad = earthBackdrop.transform;
            quad.SetPositionAndRotation(backdropPosition, backdropRotation);
            quad.localScale = backdropScale;
            earthBackdrop.SetPropertyBlock(savedBackdropBlock);
            earthBackdrop.enabled = backdropWasEnabled;
        }

        backdropSwapped = false;
    }
}
