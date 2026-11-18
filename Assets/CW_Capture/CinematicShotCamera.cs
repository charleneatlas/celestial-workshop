using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Capture-branch only: a stationary 16:9 close-up pinned to something in the
/// workshop (the diorama, the star chart). It orbits a point in the subject's
/// own frame, so it keeps its framing wherever the workshop is placed. It renders
/// into its own render texture and never to the headset, so input and gameplay
/// are untouched. Unity Recorder picks it up by tag (Targeted Camera).
///
/// Framing: Azimuth 0 looks from the subject's -forward side, 90 from its
/// +right side. Elevation is measured above the subject's horizontal plane.
/// Tweak in Play mode, then use the component's Copy / Paste Component Values
/// to keep the numbers after you exit Play mode.
/// </summary>
[DefaultExecutionOrder(1100)]
[RequireComponent(typeof(Camera))]
public sealed class CinematicShotCamera : MonoBehaviour
{
    public enum BackgroundMode
    {
        SolidColor,
        Skybox
    }

    [Header("Subject")]
    [Tooltip("What the shot is pinned to. The camera follows its position and rotation.")]
    [SerializeField]
    private Transform anchor;

    [Tooltip("Point the camera looks at, in the anchor's local space.")]
    [SerializeField]
    private Vector3 targetOffset = Vector3.zero;

    [Header("Framing")]
    [Tooltip("Degrees around the anchor's up axis. 0 = from the anchor's -forward side, 90 = from its +right side.")]
    [SerializeField]
    [Range(-180f, 180f)]
    private float azimuth = 0f;

    [Tooltip("Degrees above the anchor's horizontal plane (90 = straight down).")]
    [SerializeField]
    [Range(-10f, 90f)]
    private float elevation = 35f;

    [Tooltip("Distance from the target point (metres, world).")]
    [SerializeField]
    [Min(0.01f)]
    private float distance = 0.3f;

    [Tooltip("Vertical field of view.")]
    [SerializeField]
    [Range(5f, 90f)]
    private float verticalFov = 25f;

    [Tooltip("Level with the room: Azimuth 0 = the side the subject faces (where the player stands), + = to the player's left as seen from the subject, i.e. the camera moves to the player's right; Elevation = degrees above horizontal. Use it for tilted subjects like the chart easel.")]
    [SerializeField]
    private bool levelWithWorld = false;

    [Tooltip("Roll about the view axis (degrees).")]
    [SerializeField]
    [Range(-180f, 180f)]
    private float roll = 0f;

    [Tooltip("Shift the frame (fractions of frame size) without turning the camera: x right, y up.")]
    [SerializeField]
    private Vector2 lensShift = Vector2.zero;

    [Header("Look")]
    [Tooltip("Layers this shot draws. Leave the headset-only and Earth-view layers out.")]
    [SerializeField]
    private LayerMask cullingMask = 1;

    [SerializeField]
    private BackgroundMode backgroundMode = BackgroundMode.SolidColor;

    [Tooltip("Background colour (Solid Color mode). Shows wherever the headset would show passthrough.")]
    [SerializeField]
    private Color background = new Color(0.02f, 0.02f, 0.03f, 1f);

    [Tooltip("Hidden only while this shot renders (e.g. Guidance: hint text and ghost hands). The headset still sees them.")]
    [SerializeField]
    private GameObject[] hideWhileRendering = new GameObject[0];

    [Tooltip("URP renderer index. -1 = the pipeline's default renderer.")]
    [SerializeField]
    private int rendererIndex = -1;

    [SerializeField]
    private bool postProcessing = true;

    [Tooltip("Near clip plane (metres). Keep it small for these close-ups.")]
    [SerializeField]
    [Min(0.001f)]
    private float nearClip = 0.01f;

    private Camera shotCamera;
    private readonly List<Renderer> hidden = new List<Renderer>();
    private readonly List<Renderer> rendererBuffer = new List<Renderer>();

    private void Awake()
    {
        shotCamera = GetComponent<Camera>();
        shotCamera.stereoTargetEye = StereoTargetEyeMask.None; // never draws to the headset
        shotCamera.usePhysicalProperties = false;
        if (shotCamera.targetTexture == null)
        {
            Debug.LogWarning("CinematicShotCamera: no Target Texture; assign one so it never draws over the Game view.", this);
        }

        UniversalAdditionalCameraData data = shotCamera.GetUniversalAdditionalCameraData();
        if (rendererIndex >= 0)
        {
            data.SetRenderer(rendererIndex);
        }

        data.renderPostProcessing = postProcessing;
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
        Restore();
    }

    private void LateUpdate()
    {
        if (shotCamera == null)
        {
            shotCamera = GetComponent<Camera>();
        }

        if (anchor != null)
        {
            Vector3 target = anchor.TransformPoint(targetOffset);
            Vector3 fromTarget;
            if (levelWithWorld)
            {
                // Level frame: azimuth 0 = the side the subject's face points to
                // (where the player stands), + to the right; elevation above horizontal.
                Vector3 facing = Vector3.ProjectOnPlane(anchor.up, Vector3.up);
                if (facing.sqrMagnitude < 1e-6f)
                {
                    facing = Vector3.ProjectOnPlane(-anchor.forward, Vector3.up);
                }

                fromTarget =
                    Quaternion.AngleAxis(-azimuth, Vector3.up) *
                    Quaternion.AngleAxis(-elevation, Vector3.Cross(Vector3.up, facing.normalized)) *
                    facing.normalized;
            }
            else
            {
                fromTarget =
                    Quaternion.AngleAxis(-azimuth, anchor.up) *
                    Quaternion.AngleAxis(elevation, anchor.right) *
                    -anchor.forward;
            }
            Vector3 position = target + fromTarget * distance;
            Vector3 up = levelWithWorld ? Vector3.up : anchor.up;
            if (Vector3.Cross(target - position, up).sqrMagnitude < 1e-6f)
            {
                up = anchor.forward; // looking straight along up
            }

            Quaternion rotation = Quaternion.LookRotation(target - position, up) * Quaternion.Euler(0f, 0f, roll);
            transform.SetPositionAndRotation(position, rotation);
        }

        shotCamera.cullingMask = cullingMask;
        shotCamera.clearFlags = backgroundMode == BackgroundMode.Skybox && RenderSettings.skybox != null
            ? CameraClearFlags.Skybox
            : CameraClearFlags.SolidColor;
        shotCamera.backgroundColor = background;
        shotCamera.aspect = 16f / 9f;
        shotCamera.lensShift = lensShift;
        shotCamera.fieldOfView = verticalFov;
        shotCamera.nearClipPlane = nearClip;
        shotCamera.farClipPlane = Mathf.Max(distance * 20f, 5f);
    }

    private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam != shotCamera)
        {
            return;
        }

        foreach (GameObject root in hideWhileRendering)
        {
            if (root == null)
            {
                continue;
            }

            root.GetComponentsInChildren(false, rendererBuffer);
            foreach (Renderer r in rendererBuffer)
            {
                if (r.enabled)
                {
                    r.enabled = false;
                    hidden.Add(r);
                }
            }
        }
    }

    private void OnEndCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam == shotCamera)
        {
            Restore();
        }
    }

    private void Restore()
    {
        foreach (Renderer r in hidden)
        {
            if (r != null)
            {
                r.enabled = true;
            }
        }

        hidden.Clear();
    }
}
