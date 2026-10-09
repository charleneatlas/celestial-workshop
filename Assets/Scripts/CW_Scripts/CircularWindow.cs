using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// World-space circular window. A stencil disc (Mask) marks where masked
/// content may draw; everything under Masked Roots is kept on the
/// WindowMasked layer, which the URP renderers only draw where the stencil
/// is set. A soft rim quad covers the hard stencil edge.
/// Surround switches what's outside the window: the VR skybox, or
/// passthrough (a punch-out clears the sky outside the window to transparent
/// over the passthrough underlay).
/// </summary>
[ExecuteAlways]
public sealed class CircularWindow : MonoBehaviour
{
    public const string MaskedLayerName = "WindowMasked";

    public enum Surround
    {
        VRSky,
        Passthrough
    }

    [Header("Shape")]
    [SerializeField]
    [Min(0.01f)]
    private float radius = 5f;

    [SerializeField]
    private Transform maskQuad;

    [SerializeField]
    private Renderer rimRenderer;

    [Tooltip("Rim quad size relative to the mask disc. The glow can spread this far past the edge.")]
    [SerializeField]
    [Range(1.01f, 2f)]
    private float rimOversize = 1.2f;

    [Header("Masked Content")]
    [Tooltip("These roots and all their children (including ones spawned at runtime) only render inside the window.")]
    [SerializeField]
    private List<Transform> maskedRoots = new List<Transform>();

    [Header("Surround")]
    [SerializeField]
    private Surround surround = Surround.VRSky;

    [Tooltip("Viewer camera (CenterEyeAnchor). Used for fitting and to size the passthrough punch-out.")]
    [SerializeField]
    private Camera targetCamera;

    [Tooltip("Camera-centered sphere that clears the sky outside the window to transparent in passthrough mode.")]
    [SerializeField]
    private GameObject punchOut;

    [Tooltip("Passthrough underlay enabled in passthrough mode.")]
    [SerializeField]
    private OVRPassthroughLayer passthroughLayer;

    [Tooltip("Play mode key that flips between VR sky and passthrough (Editor / Link testing).")]
    [SerializeField]
    private Key toggleSurroundKey = Key.P;

    // Punch-out sphere radius as a fraction of the camera's far clip plane:
    // behind all content, in front of the far-plane skybox.
    private const float PunchOutFarClipFraction = 0.9f;

    private static readonly int RimRadiusId = Shader.PropertyToID("_Radius");

    private int maskedLayer = -1;
    private MaterialPropertyBlock rimProperties;
    private bool surroundDirty;

    public float Radius
    {
        get => radius;
        set
        {
            radius = Mathf.Max(0.01f, value);
            ApplyShape();
        }
    }

    public IReadOnlyList<Transform> MaskedRoots => maskedRoots;

    public Surround CurrentSurround => surround;

    private void OnEnable()
    {
        maskedLayer = LayerMask.NameToLayer(MaskedLayerName);
        if (maskedLayer < 0)
        {
            Debug.LogWarning(
                $"CircularWindow: layer '{MaskedLayerName}' not found. " +
                "Run Celestial Workshop > Circular Window > Setup.",
                this
            );
        }

        ApplyShape();
        ApplySurround();
    }

    private void OnValidate()
    {
        ApplyShape();

        // SetActive isn't allowed during OnValidate; apply on the next Update.
        surroundDirty = true;
    }

    private void Update()
    {
        if (surroundDirty)
        {
            surroundDirty = false;
            ApplySurround();
        }

        if (!Application.isPlaying)
        {
            return;
        }

        // OVRManager may start after this component, so keep it in sync
        // rather than setting it once. Only at runtime, so the scene's saved
        // setting stays off.
        bool passthrough = surround == Surround.Passthrough;
        if (OVRManager.instance != null && OVRManager.instance.isInsightPassthroughEnabled != passthrough)
        {
            OVRManager.instance.isInsightPassthroughEnabled = passthrough;
        }

        if (toggleSurroundKey == Key.None)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[toggleSurroundKey].wasPressedThisFrame)
        {
            SetSurround(surround == Surround.VRSky ? Surround.Passthrough : Surround.VRSky);
            Debug.Log($"CircularWindow: surround -> {surround}", this);
        }
    }

    private void LateUpdate()
    {
        // Constellations are generated at runtime under the masked roots,
        // so keep re-applying the layer instead of setting it once.
        if (maskedLayer < 0)
        {
            return;
        }

        foreach (Transform root in maskedRoots)
        {
            if (root != null)
            {
                SetLayerRecursively(root, maskedLayer);
            }
        }
    }

    public void ApplyShape()
    {
        float diameter = radius * 2f;

        if (maskQuad != null)
        {
            maskQuad.localScale = new Vector3(diameter, diameter, 1f);
        }

        if (rimRenderer != null)
        {
            float rimSize = diameter * rimOversize;
            rimRenderer.transform.localScale = new Vector3(rimSize, rimSize, 1f);

            rimProperties ??= new MaterialPropertyBlock();
            rimRenderer.GetPropertyBlock(rimProperties);
            rimProperties.SetFloat(RimRadiusId, 0.5f / rimOversize);
            rimRenderer.SetPropertyBlock(rimProperties);
        }
    }

    /// <summary>
    /// Aims the window at the middle of the masked content's angular extent as
    /// seen from the viewer, places it just in front of the nearest content,
    /// and sizes it so every masked renderer falls inside the circle.
    /// Works in angles because the content mixes very different depths
    /// (the sky constellation is hundreds of meters out, the tiny land a few).
    /// The sky constellation only exists at runtime, so fit in Play mode.
    /// </summary>
    public bool FitToMaskedContent(Vector3 viewerPosition, float padding = 1.1f, float nearOffset = 0.5f)
    {
        var corners = new List<Vector3>();
        float nearestDistance = float.MaxValue;
        var renderers = new List<Renderer>();

        foreach (Transform root in maskedRoots)
        {
            if (root == null)
            {
                continue;
            }

            root.GetComponentsInChildren(renderers);
            foreach (Renderer contentRenderer in renderers)
            {
                if (!contentRenderer.enabled)
                {
                    continue;
                }

                Bounds bounds = contentRenderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    corners.Add(bounds.center + Vector3.Scale(
                        bounds.extents,
                        new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f)
                    ));
                }

                nearestDistance = Mathf.Min(
                    nearestDistance,
                    Vector3.Distance(viewerPosition, bounds.ClosestPoint(viewerPosition))
                );
            }
        }

        if (corners.Count == 0)
        {
            return false;
        }

        // Middle of the yaw/pitch extent of every corner.
        float minYaw = float.MaxValue, maxYaw = float.MinValue;
        float minPitch = float.MaxValue, maxPitch = float.MinValue;
        foreach (Vector3 corner in corners)
        {
            Vector3 direction = corner - viewerPosition;
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            minYaw = Mathf.Min(minYaw, yaw);
            maxYaw = Mathf.Max(maxYaw, yaw);
            minPitch = Mathf.Min(minPitch, pitch);
            maxPitch = Mathf.Max(maxPitch, pitch);
        }

        Vector3 axis = Quaternion.Euler(-(minPitch + maxPitch) * 0.5f, (minYaw + maxYaw) * 0.5f, 0f) * Vector3.forward;

        float maxAngle = 0f;
        foreach (Vector3 corner in corners)
        {
            maxAngle = Mathf.Max(maxAngle, Vector3.Angle(axis, corner - viewerPosition));
        }

        float windowDistance = Mathf.Max(0.1f, nearestDistance - nearOffset);
        float clampedAngle = Mathf.Min(maxAngle * padding, 80f);

        transform.SetPositionAndRotation(
            viewerPosition + axis * windowDistance,
            Quaternion.LookRotation(axis, Vector3.up)
        );
        Radius = windowDistance * Mathf.Tan(clampedAngle * Mathf.Deg2Rad);
        return true;
    }

    [ContextMenu("Fit To Masked Content (from Main Camera)")]
    private void FitFromMainCamera()
    {
        Camera viewer = targetCamera != null ? targetCamera : Camera.main;
        if (viewer == null || !FitToMaskedContent(viewer.transform.position))
        {
            Debug.LogWarning("CircularWindow: nothing to fit (no camera or no masked renderers).", this);
            return;
        }

        Debug.Log(
            $"CircularWindow fitted: position {transform.position}, " +
            $"rotation {transform.eulerAngles}, radius {radius:F2}",
            this
        );
    }

    public void SetSurround(Surround newSurround)
    {
        surround = newSurround;
        ApplySurround();
    }

    private void ApplySurround()
    {
        // The camera keeps its skybox in both modes. In passthrough mode the
        // punch-out clears the sky outside the window to transparent, so the
        // sky inside the window is exactly the VR one.
        bool passthrough = surround == Surround.Passthrough;

        if (punchOut != null)
        {
            if (targetCamera != null)
            {
                float diameter = targetCamera.farClipPlane * PunchOutFarClipFraction * 2f;
                punchOut.transform.localScale = Vector3.one * diameter;
            }

            if (punchOut.activeSelf != passthrough)
            {
                punchOut.SetActive(passthrough);
            }
        }

        if (passthroughLayer != null)
        {
            passthroughLayer.enabled = passthrough;
        }
    }

    private static void SetLayerRecursively(Transform target, int layer)
    {
        if (target.gameObject.layer != layer)
        {
            target.gameObject.layer = layer;
        }

        for (int i = 0; i < target.childCount; i++)
        {
            SetLayerRecursively(target.GetChild(i), layer);
        }
    }
}
