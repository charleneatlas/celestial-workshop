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
    [Range(0.1f, 1f)]
    private float handScale = 0.5f;

    [Header("Look (OculusHand material)")]
    [Tooltip("Body opacity at full visibility (the source material's default is 0.8).")]
    [SerializeField]
    [Range(0f, 1f)]
    private float opacity = 0.55f;

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
                HandVisual source = FindSource(handedness);
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

    private HandVisual FindSource(Handedness handedness)
    {
        foreach (HandVisual visual in handVisuals)
        {
            if (visual == null || !visual.isActiveAndEnabled || !visual.IsVisible || visual.Hand == null)
            {
                continue;
            }

            if (visual.Hand.Handedness == handedness)
            {
                return visual;
            }
        }

        return null;
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

        // Exact similarity for the pinch point; the hand around it is scaled by handScale.
        Vector3 contact = copy.ThumbTip != null && copy.IndexTip != null
            ? (copy.ThumbTip.position + copy.IndexTip.position) * 0.5f
            : srcRoot.position;
        dstRoot.SetPositionAndRotation(
            workstation.MiniToSky(contact) + turn * ((srcRoot.position - contact) * (k * handScale)),
            turn * srcRoot.rotation);
        dstRoot.localScale = srcRoot.lossyScale * (k * handScale);

        for (int i = 1; i < copy.From.Length; i++)
        {
            Transform src = copy.From[i];
            copy.To[i].SetLocalPositionAndRotation(src.localPosition, src.localRotation);
            copy.To[i].localScale = src.localScale;
        }
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
