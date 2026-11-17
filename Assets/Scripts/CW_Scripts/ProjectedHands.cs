using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Projected hands: while the player turns the workstation mini, a visual-only
/// copy of their hand is posed in the hidden sky's space, so the Earth view
/// camera sees the hand among the stars and the globe shows it with them.
///
/// Mapping: each frame the copy's root takes the real hand visual's pose
/// carried through WorkstationMini's mini -> sky similarity (rotation, uniform
/// scale, translation), and every bone below copies its local pose, so the
/// copy is the real hand at sky scale, holding the sky where the real one
/// holds the mini. No world positions are copied between the two scales.
///
/// Visibility follows the rotation pinch only (VolumePinchRotation.IsPinching),
/// not tracking or proximity: in when a pinch on the mini starts, out with a
/// fade when it ends, hidden when solved (the pinch ends then). The copy lives
/// on the Earth-view-only layer, which the headset cameras don't render, so
/// the player never sees a duplicate hand in the room. The instructional
/// ghost hands are separate and untouched.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class ProjectedHands : MonoBehaviour
{
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int OutlineOpacityId = Shader.PropertyToID("_OutlineOpacity");
    private static readonly int ColorTopId = Shader.PropertyToID("_ColorTop");
    private static readonly int ColorBottomId = Shader.PropertyToID("_ColorBottom");
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

    [Header("References")]
    [SerializeField]
    private VolumePinchRotation pinchRotation;

    [SerializeField]
    private WorkstationMini workstation;

    [Tooltip("The rig's hand visuals, in order of preference (the first active, visible one per hand is copied).")]
    [SerializeField]
    private HandVisual[] handVisuals = new HandVisual[0];

    [Header("Visibility")]
    [Tooltip("Layer only the Earth view camera renders.")]
    [SerializeField]
    private string layerName = "EarthViewOnly";

    [Tooltip("Off: only the pinching hand. On: both hands while either pinches.")]
    [SerializeField]
    private bool showBothHands;

    [SerializeField]
    [Min(0f)]
    private float fadeInSeconds = 0.12f;

    [SerializeField]
    [Min(0f)]
    private float fadeOutSeconds = 0.35f;

    [Tooltip("Size of the projected hand relative to an exact copy, scaled about the pinch point (thumb/index tips), so the fingertips stay on the matching point of the sky. The Earth camera sits much closer to the sky than the eyes are to the mini, so a full-size hand would fill the view.")]
    [SerializeField]
    [Range(0.05f, 1f)]
    private float handScale = 0.6f;

    [Tooltip("Keep the hand the same size in the Earth view wherever the pinch is: the copy is scaled with its distance from the Earth camera (about the pinch point), so it never balloons near the camera. Hand Scale then applies at Reference Distance.")]
    [SerializeField]
    private bool constantApparentSize = true;

    [Tooltip("Distance from the Earth camera (m) at which the copy is exactly Hand Scale.")]
    [SerializeField]
    [Min(0.01f)]
    private float referenceDistance = 0.3f;

    [Tooltip("Limits on the distance correction (x Hand Scale), so extreme distances stay sensible.")]
    [SerializeField]
    private Vector2 apparentScaleLimits = new Vector2(0.2f, 1.5f);

    [Header("Look (OculusHand material)")]
    [Tooltip("Body opacity at full visibility (the source material's default is 0.8).")]
    [SerializeField]
    [Range(0f, 1f)]
    private float opacity = 0.35f;

    [SerializeField]
    [Range(0f, 1f)]
    private float outlineOpacity = 0.4f;

    [Tooltip("Off: the hand's own colours. On: Tint Top / Bottom / Outline below (e.g. a violet silhouette).")]
    [SerializeField]
    private bool useTint;

    [SerializeField]
    private Color tintTop = new Color(0.45f, 0.38f, 0.9f, 1f);

    [SerializeField]
    private Color tintBottom = new Color(0.2f, 0.16f, 0.5f, 1f);

    [SerializeField]
    private Color tintOutline = new Color(0.75f, 0.7f, 1f, 1f);

    [Tooltip("Diagnostics: once a second while a hand is projected, log its mapping (scale, pinch point, where the copy lands, its size and distance from the Earth camera).")]
    [SerializeField]
    private bool logDiagnostics;

    [Tooltip("Optional: replaces the copied hand materials.")]
    [SerializeField]
    private Material overrideMaterial;

    private sealed class HandCopy
    {
        public HandVisual Source;
        public GameObject Root;
        public Transform[] From;
        public Transform[] To;
        public SkinnedMeshRenderer[] Renderers;
        public Transform ThumbTip;
        public Transform IndexTip;
        public float Alpha;
    }

    private readonly HandCopy[] copies = new HandCopy[2];
    private MaterialPropertyBlock block;
    private int layer = -1;

    /// <summary>True while any projected hand is drawn (for checks).</summary>
    public bool AnyVisible => (copies[0] != null && copies[0].Alpha > 0f) || (copies[1] != null && copies[1].Alpha > 0f);

    private void Awake()
    {
        layer = LayerMask.NameToLayer(layerName);
        if (layer < 0)
        {
            Debug.LogWarning($"ProjectedHands: no '{layerName}' layer; projected hands are disabled.", this);
            enabled = false;
        }

        block = new MaterialPropertyBlock();
    }

    private void OnDisable()
    {
        for (int i = 0; i < copies.Length; i++)
        {
            if (copies[i] != null)
            {
                copies[i].Alpha = 0f;
                SetVisible(copies[i], false);
            }
        }
    }

    private void OnDestroy()
    {
        foreach (HandCopy copy in copies)
        {
            if (copy != null && copy.Root != null)
            {
                Destroy(copy.Root);
            }
        }
    }

    private void LateUpdate()
    {
        bool pinching = pinchRotation != null && pinchRotation.IsPinching &&
                        workstation != null && workstation.IsReady;
        IHand active = pinching ? pinchRotation.ActiveHand : null;

        for (int side = 0; side < 2; side++)
        {
            Handedness handedness = side == 0 ? Handedness.Left : Handedness.Right;
            bool want = pinching && active != null && (showBothHands || active.Handedness == handedness);

            HandCopy copy = copies[side];
            if (want)
            {
                HandVisual source = FindSource(handedness, active);
                if (source == null)
                {
                    want = false;
                }
                else if (copy == null || copy.Source != source)
                {
                    copy = Rebuild(side, source);
                }
            }

            if (copy == null)
            {
                continue;
            }

            float step = want
                ? (fadeInSeconds > 0f ? Time.deltaTime / fadeInSeconds : 1f)
                : (fadeOutSeconds > 0f ? Time.deltaTime / fadeOutSeconds : 1f);
            copy.Alpha = Mathf.MoveTowards(copy.Alpha, want ? 1f : 0f, step);

            // A hand that stops being tracked mid-fade just stays where it was.
            bool sourceLive = copy.Source != null && copy.Source.isActiveAndEnabled && copy.Source.IsVisible;
            if (copy.Alpha <= 0f)
            {
                SetVisible(copy, false);
                continue;
            }

            if (sourceLive)
            {
                Sync(copy);
            }

            SetVisible(copy, true);
            ApplyLook(copy);
        }
    }

    // The visual of the hand doing the pinch. The rig also has the distance-grab
    // reticles' hand visuals, placed and sized differently;
    // which comes first in the list differs between Link and the device, so
    // match the pinching hand itself and only fall back to a non-synthetic one.
    private HandVisual FindSource(Handedness handedness, IHand pinching)
    {
        HandVisual fallback = null;
        foreach (HandVisual visual in handVisuals)
        {
            if (visual == null || !visual.isActiveAndEnabled || !visual.IsVisible || visual.Hand == null ||
                visual.Hand.Handedness != handedness)
            {
                continue;
            }

            if (pinching != null && ReferenceEquals(visual.Hand, pinching))
            {
                return visual;
            }

            // The distance-grab reticle hands live under "...HandReticle"; the
            // player's own visuals (driven through a SyntheticHand for grab
            // poses, so the hand type can't tell them apart) don't.
            if (!IsReticle(visual.transform) && fallback == null)
            {
                fallback = visual;
            }
        }

        return fallback;
    }

    private static bool IsReticle(Transform t)
    {
        for (; t != null; t = t.parent)
        {
            if (t.name.Contains("Reticle"))
            {
                return true;
            }
        }

        return false;
    }

    // Visual-only copy of the hand visual's hierarchy: transforms plus the
    // skinned mesh, bones remapped to the copy. No scripts, colliders or events.
    private HandCopy Rebuild(int side, HandVisual source)
    {
        if (copies[side] != null && copies[side].Root != null)
        {
            Destroy(copies[side].Root);
        }

        Transform[] from = source.GetComponentsInChildren<Transform>(true);
        var to = new Transform[from.Length];
        var map = new System.Collections.Generic.Dictionary<Transform, Transform>(from.Length);

        for (int i = 0; i < from.Length; i++)
        {
            var node = new GameObject(i == 0 ? $"Projected Hand ({source.Hand.Handedness})" : from[i].name);
            node.layer = layer;
            to[i] = node.transform;
            map[from[i]] = to[i];
        }

        for (int i = 1; i < from.Length; i++)
        {
            Transform parent = from[i].parent != null && map.TryGetValue(from[i].parent, out Transform mapped) ? mapped : to[0];
            to[i].SetParent(parent, false);

            // Mirror what's switched on: each visual carries two hand rigs (one
            // per tracking path) and only the one being driven is active. The
            // idle one sits at the rig origin; copied and turned on, it lands
            // near the Earth camera and shows as a huge hand on the device.
            to[i].gameObject.SetActive(from[i].gameObject.activeSelf);
        }

        var renderers = new System.Collections.Generic.List<SkinnedMeshRenderer>();
        for (int i = 0; i < from.Length; i++)
        {
            SkinnedMeshRenderer src = from[i].GetComponent<SkinnedMeshRenderer>();
            if (src == null)
            {
                continue;
            }

            SkinnedMeshRenderer dst = to[i].gameObject.AddComponent<SkinnedMeshRenderer>();
            dst.sharedMesh = src.sharedMesh;
            dst.sharedMaterials = overrideMaterial != null ? new[] { overrideMaterial } : src.sharedMaterials;
            Transform[] bones = src.bones;
            var copiedBones = new Transform[bones.Length];
            for (int b = 0; b < bones.Length; b++)
            {
                copiedBones[b] = bones[b] != null && map.TryGetValue(bones[b], out Transform bone) ? bone : null;
            }

            dst.bones = copiedBones;
            dst.rootBone = src.rootBone != null && map.TryGetValue(src.rootBone, out Transform rootBone) ? rootBone : to[i];
            dst.updateWhenOffscreen = true;
            dst.shadowCastingMode = ShadowCastingMode.Off;
            dst.receiveShadows = false;
            dst.enabled = false;
            renderers.Add(dst);
        }

        var copy = new HandCopy
        {
            Source = source,
            Root = to[0].gameObject,
            From = from,
            To = to,
            Renderers = renderers.ToArray(),
            ThumbTip = Joint(source, HandJointId.HandThumbTip),
            IndexTip = Joint(source, HandJointId.HandIndexTip),
            Alpha = copies[side] != null ? copies[side].Alpha : 0f
        };

        copies[side] = copy;
        Debug.Log($"ProjectedHands: projecting {source.name} ({source.Hand.Handedness}), {renderers.Count} renderer(s).", this);
        return copy;
    }

    private static Transform Joint(HandVisual visual, HandJointId id)
    {
        var joints = visual.Joints;
        return joints != null && (int)id < joints.Count ? joints[(int)id] : null;
    }

    private void Sync(HandCopy copy)
    {
        Transform srcRoot = copy.From[0];
        Transform dstRoot = copy.To[0];
        float k = workstation.MiniToSkyScale;
        Quaternion turn = workstation.MiniToSkyRotation;

        // Exact similarity for the pinch point; the hand around it is scaled by
        // handScale (and, with Constant Apparent Size, by its distance from the
        // Earth camera, so it looks the same size wherever the pinch is).
        Vector3 contact = copy.ThumbTip != null && copy.IndexTip != null
            ? (copy.ThumbTip.position + copy.IndexTip.position) * 0.5f
            : srcRoot.position;
        Vector3 skyContact = workstation.MiniToSky(contact);
        float scale = handScale;
        if (constantApparentSize)
        {
            Camera earth = earthViewCamera != null ? earthViewCamera : (earthViewCamera = FindEarthCamera());
            if (earth != null)
            {
                float distance = Vector3.Distance(earth.transform.position, skyContact);
                scale *= Mathf.Clamp(distance / Mathf.Max(0.01f, referenceDistance), apparentScaleLimits.x, apparentScaleLimits.y);
            }
        }

        dstRoot.SetPositionAndRotation(
            skyContact + turn * ((srcRoot.position - contact) * (k * scale)),
            turn * srcRoot.rotation);
        dstRoot.localScale = srcRoot.lossyScale * (k * scale);
        LogDiagnostics(copy, contact, k);

        for (int i = 1; i < copy.From.Length; i++)
        {
            Transform src = copy.From[i];
            Transform dst = copy.To[i];
            bool on = src.gameObject.activeSelf;
            if (dst.gameObject.activeSelf != on)
            {
                dst.gameObject.SetActive(on);   // the visual switched rigs (tracking path changed)
            }

            dst.SetLocalPositionAndRotation(src.localPosition, src.localRotation);
            dst.localScale = src.localScale;
        }
    }

    private float nextDiagnosticAt;

    private void LogDiagnostics(HandCopy copy, Vector3 contact, float k)
    {
        if (!logDiagnostics || Time.unscaledTime < nextDiagnosticAt)
        {
            return;
        }

        nextDiagnosticAt = Time.unscaledTime + 1f;
        Transform srcRoot = copy.From[0];
        Transform dstRoot = copy.To[0];
        Transform root = copy.Source.Root;
        Bounds b = default;
        bool any = false;
        foreach (SkinnedMeshRenderer r in copy.Renderers)
        {
            if (r != null && r.gameObject.activeInHierarchy)
            {
                if (!any) { b = r.bounds; any = true; } else { b.Encapsulate(r.bounds); }
            }
        }

        Camera earth = earthViewCamera != null ? earthViewCamera : (earthViewCamera = FindEarthCamera());
        float camDistance = earth != null ? Vector3.Distance(earth.transform.position, b.center) : -1f;
        Vector3 skyContact = workstation.MiniToSky(contact);
        Debug.Log($"ProjectedHands DIAG {copy.Source.name}: k {k:F4}, handScale {handScale}, hand.Scale {copy.Source.Hand.Scale:F3}, " +
                  $"srcRoot '{srcRoot.name}' lossy {srcRoot.lossyScale.x:F3} at {srcRoot.position:F3}, visRoot '{(root != null ? root.name : "-")}' lossy {(root != null ? root.lossyScale.x : 0f):F3} at {(root != null ? root.position : Vector3.zero):F3}, " +
                  $"contact {contact:F3} (thumb {(copy.ThumbTip != null ? copy.ThumbTip.name : "-")}), mini centre {workstation.Center:F3} (contact {Vector3.Distance(contact, workstation.Center) * 100f:F1} cm away), " +
                  $"-> sky {skyContact:F3}; constant size {constantApparentSize}; copy root {dstRoot.position:F3} lossy {dstRoot.lossyScale.x:F4}, copy mesh size {(any ? b.size.magnitude : 0f):F3} m at {(any ? b.center : Vector3.zero):F3}, " +
                  $"{camDistance:F3} m from the Earth camera, active meshes {copy.Renderers.Count(r => r != null && r.gameObject.activeInHierarchy)}", this);
    }

    private Camera earthViewCamera;

    private static Camera FindEarthCamera()
    {
        EarthViewCamera evc = FindFirstObjectByType<EarthViewCamera>();
        return evc != null ? evc.GetComponent<Camera>() : null;
    }

    private void SetVisible(HandCopy copy, bool visible)
    {
        foreach (SkinnedMeshRenderer renderer in copy.Renderers)
        {
            if (renderer != null && renderer.enabled != visible)
            {
                renderer.enabled = visible;
            }
        }
    }

    private void ApplyLook(HandCopy copy)
    {
        foreach (SkinnedMeshRenderer renderer in copy.Renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            renderer.GetPropertyBlock(block);
            block.SetFloat(OpacityId, opacity * copy.Alpha);
            block.SetFloat(OutlineOpacityId, outlineOpacity * copy.Alpha);
            if (useTint)
            {
                block.SetColor(ColorTopId, tintTop);
                block.SetColor(ColorBottomId, tintBottom);
                block.SetColor(OutlineColorId, tintOutline);
            }

            renderer.SetPropertyBlock(block);
        }
    }

#if UNITY_EDITOR
    /// <summary>Editor checks: build and pose a projected hand from a source visual, as if pinching.</summary>
    public GameObject DebugProject(HandVisual source)
    {
        int side = source.Hand != null && source.Hand.Handedness == Handedness.Left ? 0 : 1;
        HandCopy copy = Rebuild(side, source);
        copy.Alpha = 1f;
        Sync(copy);
        SetVisible(copy, true);
        ApplyLook(copy);
        return copy.Root;
    }
#endif
}
