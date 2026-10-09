using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World-space circular window. A stencil disc (Mask) marks where masked
/// content may draw; everything under Masked Roots is kept on the
/// WindowMasked layer, which the URP renderers only draw where the stencil
/// is set. A soft rim quad covers the hard stencil edge.
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

    [Tooltip("Camera whose background changes with the surround mode.")]
    [SerializeField]
    private Camera targetCamera;

    [Tooltip("Optional sky drawn only inside the window (on the WindowMasked layer) for passthrough mode.")]
    [SerializeField]
    private GameObject windowSky;

    [Tooltip("Optional passthrough component (e.g. OVRPassthroughLayer) enabled in passthrough mode.")]
    [SerializeField]
    private Behaviour passthroughLayer;

    private static readonly int RimRadiusId = Shader.PropertyToID("_Radius");

    private int maskedLayer = -1;
    private MaterialPropertyBlock rimProperties;
    private CameraClearFlags vrSkyClearFlags = CameraClearFlags.Skybox;
    private Color vrSkyBackground;
    private bool cameraStateCaptured;

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

        if (isActiveAndEnabled)
        {
            ApplySurround();
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
        bool passthrough = surround == Surround.Passthrough;

        if (windowSky != null)
        {
            windowSky.SetActive(passthrough);
        }

        if (passthroughLayer != null)
        {
            passthroughLayer.enabled = passthrough;
        }

        if (targetCamera == null)
        {
            return;
        }

        if (!cameraStateCaptured && !passthrough)
        {
            vrSkyClearFlags = targetCamera.clearFlags;
            vrSkyBackground = targetCamera.backgroundColor;
            cameraStateCaptured = true;
        }

        if (passthrough)
        {
            targetCamera.clearFlags = CameraClearFlags.SolidColor;
            targetCamera.backgroundColor = Color.clear;
        }
        else
        {
            // Without a captured state (e.g. scene saved in passthrough mode), fall back to the skybox.
            targetCamera.clearFlags = cameraStateCaptured ? vrSkyClearFlags : CameraClearFlags.Skybox;

            if (cameraStateCaptured)
            {
                targetCamera.backgroundColor = vrSkyBackground;
            }
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
