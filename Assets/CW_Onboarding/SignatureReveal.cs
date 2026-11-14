using System;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TextCore;

/// <summary>
/// Trace-to-reveal signature on a fixed surface (the workshop noticeboard).
///
/// Same technique and rules as ConstellationSigning, which this mirrors
/// without touching: a pen (SigningPen prefab, posed from the hand's wrist
/// with the recorded PenGrip) follows the tracked hand; drawing its tip left to
/// right along the line wipes a prewritten cursive name into view through
/// TMP's clip rect.
///
///   - The stroke must start near the left end (no accidental completion).
///   - Leaving the area or losing tracking pauses; resuming is allowed only
///     near (or behind) the revealed edge, by either hand after a short pause.
///   - The reveal can't advance faster than Max Advance Speed (tracking jumps).
///   - Past the completion threshold the rest of the name is revealed
///     smoothly, the pen goes away and Completed fires.
///
/// The line runs along this transform's +X (centered on it), +Y is up, and the
/// player looks along +Z (TMP's reading direction), like the board it sits on.
/// An optional StarTrail follows the revealed edge.
/// </summary>
[DefaultExecutionOrder(210)]
public sealed class SignatureReveal : MonoBehaviour
{
    public enum State
    {
        Off,
        Ready,
        Signing,
        Paused,
        Finishing,
        Complete
    }

    [Header("Hands")]
    [Tooltip("Hands that can sign (the rig's per-hand HandRef).")]
    [SerializeField]
    [Interface(typeof(IHand))]
    private List<UnityEngine.Object> hands = new List<UnityEngine.Object>();

    [Header("Name")]
    [Tooltip("The cursive name (TextMeshPro, child of this transform). Its material is copied at runtime for the clip rect.")]
    [SerializeField]
    private TextMeshPro nameText;

    [Tooltip("Length of the signing line (m). The name is fitted inside it.")]
    [SerializeField]
    [Min(0.02f)]
    private float signingWidth = 0.3f;

    [Tooltip("Capital height of the name (m). Long names shrink to fit the width.")]
    [SerializeField]
    [Min(0.002f)]
    private float maxCapHeight = 0.022f;

    [Tooltip("Fraction of the signing width the name may use.")]
    [SerializeField]
    [Range(0.3f, 1f)]
    private float textWidthFill = 0.88f;

    [Tooltip("Baseline height above the signing line (m).")]
    [SerializeField]
    private float textLift = 0.004f;

    [Tooltip("Softness of the reveal edge (m).")]
    [SerializeField]
    [Min(0f)]
    private float wipeSoftness = 0.008f;

    [Tooltip("Hidden until signing completes (e.g. the dotted guide line).")]
    [SerializeField]
    private GameObject[] hideOnComplete = new GameObject[0];

    [Header("Pen (same assets as ConstellationSigning)")]
    [Tooltip("Pen to spawn (tip at a child named \"Tip\", else its origin). Empty = no pen; the pinch point traces.")]
    [SerializeField]
    private GameObject penPrefab;

    [Tooltip("Recorded pen-holding grip (PenGrip asset).")]
    [SerializeField]
    private PenGripPose gripPose;

    [SerializeField]
    private SyntheticHand leftSyntheticHand;

    [SerializeField]
    private SyntheticHand rightSyntheticHand;

    [SerializeField]
    [Range(0f, 1f)]
    private float gripWeight = 1f;

    [SerializeField]
    [Min(0f)]
    private float gripBlendDuration = 0.15f;

    [Tooltip("The pen appears on a hand within this distance of the line (m).")]
    [SerializeField]
    [Min(0.05f)]
    private float penShowDistance = 0.45f;

    [Header("Interaction (forgiving)")]
    [Tooltip("Slack past either end of the line (m).")]
    [SerializeField]
    [Min(0f)]
    private float alongTolerance = 0.05f;

    [Tooltip("How far above or below the line the tip may be (m).")]
    [SerializeField]
    [Min(0.001f)]
    private float verticalTolerance = 0.06f;

    [Tooltip("How far in front of the board, toward the player, the tip may be (m).")]
    [SerializeField]
    [Min(0.001f)]
    private float depthTowardPlayer = 0.08f;

    [Tooltip("How far behind the board surface the tip may be (m).")]
    [SerializeField]
    [Min(0.001f)]
    private float depthBehind = 0.06f;

    [Tooltip("To start, the tip must enter within this distance of the left end; to resume, within this distance ahead of the revealed edge (m).")]
    [SerializeField]
    [Min(0.001f)]
    private float startResumeTolerance = 0.05f;

    [Tooltip("Fastest the reveal can advance (m/s), so a tracking jump can't skip ahead.")]
    [SerializeField]
    [Min(0.01f)]
    private float maxAdvanceSpeed = 1.2f;

    [Tooltip("Pause on low-confidence tracking as well as lost tracking.")]
    [SerializeField]
    private bool requireHighConfidence = true;

    [Tooltip("After this long paused, either hand may resume (s).")]
    [SerializeField]
    [Min(0f)]
    private float handReleaseAfter = 1.5f;

    [Header("Completion")]
    [Tooltip("Fraction of the line that completes the signature; the rest is revealed automatically.")]
    [SerializeField]
    [Range(0.3f, 1f)]
    private float completionThreshold = 0.68f;

    [Tooltip("Time to reveal the remainder after the threshold (s).")]
    [SerializeField]
    [Min(0f)]
    private float autoFinishDuration = 0.55f;

    [Header("Effects")]
    [Tooltip("Optional star trail following the revealed edge.")]
    [SerializeField]
    private StarTrail trail;

    [Tooltip("Where on the name the trail runs, as a fraction of the cap height above the baseline.")]
    [SerializeField]
    [Range(0f, 1.5f)]
    private float trailHeight = 0.45f;

    [SerializeField]
    private UnityEvent onCompleted = new UnityEvent();

    [Header("Debug")]
    [SerializeField]
    private bool showInteractionArea;

    [SerializeField]
    private bool logEvents = true;

    public event Action Started;
    public event Action Paused;
    public event Action ThresholdReached;
    public event Action Completed;

    public State CurrentState => state;
    public float Progress => signingWidth > 0f ? progress / signingWidth : 0f;
    public bool IsArmed => state != State.Off;

    /// <summary>World position of the line's left end (for demos).</summary>
    public Vector3 LineLeft => LineLeftWorld();

    /// <summary>World position of the line's right end.</summary>
    public Vector3 LineRight => LineLeftWorld() + transform.right * Width;

    /// <summary>World point of the current revealed edge, at trail height.</summary>
    public Vector3 EdgePoint => EdgeWorld(progress);

    /// <summary>Signing width in world units.</summary>
    public float Width => signingWidth * transform.lossyScale.x;

    /// <summary>Seconds since the player last advanced or touched the line.</summary>
    public float IdleTime { get; private set; }

    private static readonly int ClipRectId = Shader.PropertyToID("_ClipRect");
    private static readonly int MaskSoftnessXId = Shader.PropertyToID("_MaskSoftnessX");
    private static readonly int MaskSoftnessYId = Shader.PropertyToID("_MaskSoftnessY");
    private const float HandSwitchHysteresis = 0.05f;

    private readonly List<IHand> handList = new List<IHand>();
    private State state = State.Off;
    private float progress; // metres along the line (local units)
    private IHand activeHand;
    private IHand penHand;
    private float pausedTime;
    private Coroutine finishRoutine;
    private Material textMaterial;
    private float fittedCapHeight;
    private Transform penRoot;
    private Vector3 tipOffset;

    private sealed class GripState
    {
        public float Weight;
        public bool Locked;
    }

    private readonly GripState leftGrip = new GripState();
    private readonly GripState rightGrip = new GripState();

    private void Awake()
    {
        foreach (UnityEngine.Object o in hands)
        {
            if (o is IHand hand)
            {
                handList.Add(hand);
            }
        }

        if (nameText != null)
        {
            nameText.enabled = false;
        }
    }

    private void OnDisable()
    {
        Disarm();
    }

    private void OnDestroy()
    {
        if (textMaterial != null)
        {
            Destroy(textMaterial);
        }
    }

    // ---- Public controls ----

    /// <summary>Sets the name to sign (fitted to the line). Ignored mid-stroke.</summary>
    public void SetName(string text)
    {
        if (nameText == null || state == State.Signing || state == State.Finishing || state == State.Complete)
        {
            return;
        }

        nameText.text = string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
        FitText();
        ApplyWipe();
    }

    /// <summary>Shows the (hidden) name and starts accepting strokes.</summary>
    public void Arm()
    {
        if (nameText == null)
        {
            Debug.LogWarning("SignatureReveal: no name text assigned.", this);
            return;
        }

        StopFinish();
        progress = 0f;
        activeHand = null;
        pausedTime = 0f;
        IdleTime = 0f;
        EnsureMaterial();
        nameText.enabled = true;
        FitText();
        SetHidden(false);
        EnsurePen();
        state = State.Ready;
        ApplyWipe();
        Log($"ready to sign \"{nameText.text}\" ({handList.Count} hand(s)).");
    }

    /// <summary>Stops signing, removes the pen and releases the hands (keeps the revealed text).</summary>
    public void Disarm()
    {
        StopFinish();
        if (penRoot != null)
        {
            Destroy(penRoot.gameObject);
            penRoot = null;
        }

        penHand = null;
        activeHand = null;
        ReleaseGripNow(leftSyntheticHand, leftGrip);
        ReleaseGripNow(rightSyntheticHand, rightGrip);
        if (trail != null)
        {
            trail.EndStroke(false);
        }

        if (state != State.Complete)
        {
            state = State.Off;
        }
    }

    /// <summary>Back to unsigned and hidden (for replays).</summary>
    public void ResetSignature()
    {
        Disarm();
        state = State.Off;
        progress = 0f;
        SetHidden(false);
        if (trail != null)
        {
            trail.Clear();
        }

        if (nameText != null)
        {
            nameText.enabled = false;
        }
    }

    [ContextMenu("Debug: Auto-Sign")]
    public void DebugAutoSign()
    {
        if (!Application.isPlaying || state == State.Off || state == State.Complete || state == State.Finishing)
        {
            Debug.LogWarning("SignatureReveal: auto-sign needs an armed signature in Play mode.", this);
            return;
        }

        StopFinish();
        finishRoutine = StartCoroutine(AutoSign());
    }

    private IEnumerator AutoSign()
    {
        state = State.Signing;
        activeHand = null;
        if (trail != null)
        {
            trail.BeginStroke(EdgeWorld(progress));
        }

        Started?.Invoke();
        while (progress < completionThreshold * signingWidth)
        {
            progress = Mathf.Min(signingWidth, progress + signingWidth * 0.55f * Time.deltaTime);
            FeedTrail();
            ApplyWipe();
            yield return null;
        }

        finishRoutine = null;
        BeginFinish();
    }

    // ---- Per frame ----

    private void LateUpdate()
    {
        if (state == State.Off)
        {
            return;
        }

        if (state == State.Ready || state == State.Signing || state == State.Paused)
        {
            if (finishRoutine == null)
            {
                UpdateInteraction();
            }
        }
        else
        {
            PosePen(null, default);
        }

        ApplyWipe();
        UpdateGrip(leftSyntheticHand, leftGrip, Handedness.Left);
        UpdateGrip(rightSyntheticHand, rightGrip, Handedness.Right);
    }

    private void UpdateInteraction()
    {
        float dt = Time.deltaTime;
        IdleTime += dt;

        if (state == State.Signing)
        {
            bool tracked = TryGetPenPose(activeHand, out Pose pen);
            PosePen(tracked ? activeHand : null, pen);
            if (tracked && InsideArea(TipOf(pen), out float along))
            {
                float before = progress;
                float target = Mathf.Clamp(along, 0f, signingWidth);
                progress = Mathf.Max(progress, Mathf.Min(target, progress + maxAdvanceSpeed * dt));
                IdleTime = 0f;
                if (progress > before + 1e-5f)
                {
                    FeedTrail();
                }

                if (progress >= completionThreshold * signingWidth)
                {
                    BeginFinish();
                }
            }
            else
            {
                state = State.Paused;
                pausedTime = 0f;
                if (trail != null)
                {
                    trail.EndStroke(true);
                }

                Paused?.Invoke();
                Log($"paused at {Progress:P0} ({(tracked ? "left the area" : "tracking lost")}).");
            }

            return;
        }

        // Ready or Paused: who may (re)start, and do they?
        if (state == State.Paused)
        {
            pausedTime += dt;
        }

        bool anyHand = activeHand == null || (state == State.Paused && pausedTime >= handReleaseAfter);
        IHand closest = null;
        Pose closestPen = default;
        float closestDistance = float.MaxValue;
        float penHandDistance = float.MaxValue;
        Pose penHandPen = default;
        Vector3 resumePoint = LineLeftWorld() + transform.right * (progress * transform.lossyScale.x);

        foreach (IHand hand in handList)
        {
            if (!anyHand && hand != activeHand)
            {
                continue;
            }

            if (!TryGetPenPose(hand, out Pose pen))
            {
                continue;
            }

            Vector3 tip = TipOf(pen);
            if (InsideArea(tip, out float along) && along <= progress + startResumeTolerance && along >= -alongTolerance)
            {
                bool resumed = state == State.Paused;
                activeHand = hand;
                state = State.Signing;
                IdleTime = 0f;
                PosePen(hand, pen);
                if (trail != null)
                {
                    trail.BeginStroke(EdgeWorld(progress));
                }

                if (!resumed)
                {
                    Started?.Invoke();
                }

                Log($"{(resumed ? "resumed" : "started")} with the {hand.Handedness} hand at {Progress:P0}.");
                return;
            }

            float distance = Vector3.Distance(tip, resumePoint);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = hand;
                closestPen = pen;
            }

            if (hand == penHand)
            {
                penHandDistance = distance;
                penHandPen = pen;
            }
        }

        // The pen shows on a hand near the board; it stays on its current
        // hand unless the other is clearly closer.
        float show = penShowDistance;
        if (penHand != null && penHandDistance <= show && penHandDistance <= closestDistance + HandSwitchHysteresis)
        {
            PosePen(penHand, penHandPen);
        }
        else if (closest != null && closestDistance <= show)
        {
            PosePen(closest, closestPen);
        }
        else
        {
            PosePen(null, default);
        }

        if (trail != null && state == State.Paused && progress > 0f)
        {
            trail.SetHead(true, EdgeWorld(progress), true);
        }
    }

    private void FeedTrail()
    {
        if (trail != null)
        {
            trail.StrokeTo(EdgeWorld(progress));
        }
    }

    private void BeginFinish()
    {
        if (state == State.Finishing || state == State.Complete)
        {
            return;
        }

        state = State.Finishing;
        activeHand = null;
        PosePen(null, default);
        ThresholdReached?.Invoke();
        Log($"threshold reached at {Progress:P0}; finishing.");
        StopFinish();
        finishRoutine = StartCoroutine(FinishReveal());
    }

    private IEnumerator FinishReveal()
    {
        float start = progress;
        float t = 0f;
        while (t < 1f && autoFinishDuration > 0f)
        {
            t = Mathf.Min(1f, t + Time.deltaTime / autoFinishDuration);
            float eased = 1f - (1f - t) * (1f - t);
            progress = Mathf.Lerp(start, signingWidth, eased);
            FeedTrail();
            ApplyWipe();
            yield return null;
        }

        progress = signingWidth;
        finishRoutine = null;
        state = State.Complete;
        SetHidden(true);
        if (trail != null)
        {
            trail.EndStroke(false);
            trail.Flourish(EdgeWorld(0f), EdgeWorld(signingWidth));
        }

        if (penRoot != null)
        {
            Destroy(penRoot.gameObject);
            penRoot = null;
        }

        penHand = null;
        ApplyWipe();
        Log("signed.");
        Completed?.Invoke();
        onCompleted?.Invoke();
    }

    private void StopFinish()
    {
        if (finishRoutine != null)
        {
            StopCoroutine(finishRoutine);
            finishRoutine = null;
        }
    }

    // ---- Geometry (local line units = this transform's local space) ----

    private Vector3 LineLeftWorld()
    {
        return transform.TransformPoint(new Vector3(-signingWidth * 0.5f, 0f, 0f));
    }

    private Vector3 EdgeWorld(float along)
    {
        float y = textLift + fittedCapHeight * trailHeight;
        return transform.TransformPoint(new Vector3(-signingWidth * 0.5f + along, y, 0f));
    }

    // Tip in line coordinates: along from the left end, height, depth toward the player.
    private bool InsideArea(Vector3 point, out float along)
    {
        Vector3 local = transform.InverseTransformPoint(point);
        // Tolerances are in metres; the transform may be scaled.
        Vector3 s = transform.lossyScale;
        along = local.x + signingWidth * 0.5f;
        float height = local.y * s.y;
        float toward = -local.z * s.z;
        float alongM = along * s.x;
        float widthM = signingWidth * s.x;
        along = alongM / Mathf.Max(1e-6f, s.x);
        return alongM >= -alongTolerance && alongM <= widthM + alongTolerance &&
               Mathf.Abs(height) <= verticalTolerance &&
               toward <= depthTowardPlayer && toward >= -depthBehind;
    }

    // ---- Pen ----

    private bool HandUsable(IHand hand)
    {
        return hand != null && hand.IsTrackedDataValid && (!requireHighConfidence || hand.IsHighConfidence);
    }

    private bool HasGrip(Handedness handedness)
    {
        return gripPose != null && gripPose.For(handedness).valid;
    }

    private Vector3 TipOf(Pose pen)
    {
        return pen.position + pen.rotation * tipOffset;
    }

    private bool TryGetPenPose(IHand hand, out Pose pen)
    {
        pen = default;
        if (!HandUsable(hand) || !hand.GetRootPose(out Pose wrist))
        {
            return false;
        }

        if (penPrefab != null && HasGrip(hand.Handedness))
        {
            pen = gripPose.For(hand.Handedness).PenFromWrist(wrist);
            return true;
        }

        // No pen or no grip: trace with the pinch point (index/thumb midpoint).
        if (!hand.GetJointPose(HandJointId.HandIndexTip, out Pose index))
        {
            return false;
        }

        Vector3 origin = index.position;
        if (hand.GetJointPose(HandJointId.HandThumbTip, out Pose thumb))
        {
            origin = Vector3.Lerp(index.position, thumb.position, 0.3f);
        }

        pen = new Pose(origin, wrist.rotation);
        return true;
    }

    private void EnsurePen()
    {
        if (penPrefab == null)
        {
            tipOffset = Vector3.zero;
            return;
        }

        if (penRoot != null)
        {
            return;
        }

        GameObject pen = Instantiate(penPrefab);
        pen.name = penPrefab.name + " (signature)";
        foreach (Collider c in pen.GetComponentsInChildren<Collider>(true))
        {
            Destroy(c);
        }

        foreach (Rigidbody rb in pen.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }

        penRoot = pen.transform;
        Transform tip = FindChild(penRoot, "Tip");
        tipOffset = tip != null ? Quaternion.Inverse(penRoot.rotation) * (tip.position - penRoot.position) : Vector3.zero;
        pen.SetActive(false);
    }

    private static Transform FindChild(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child != parent && child.name == name)
            {
                return child;
            }
        }

        return null;
    }

    private void PosePen(IHand hand, Pose pen)
    {
        penHand = hand;
        if (penRoot == null)
        {
            return;
        }

        bool show = hand != null;
        if (penRoot.gameObject.activeSelf != show)
        {
            penRoot.gameObject.SetActive(show);
        }

        if (show)
        {
            penRoot.SetPositionAndRotation(pen.position, pen.rotation);
        }
    }

    // Blends the pen hand's visible fingers into the recorded grip (as
    // ConstellationSigning does); every other hand blends back out.
    private void UpdateGrip(SyntheticHand synthetic, GripState grip, Handedness handedness)
    {
        if (synthetic == null)
        {
            return;
        }

        bool holding = HasGrip(handedness) && penHand != null && penHand.Handedness == handedness &&
                       penRoot != null && penRoot.gameObject.activeInHierarchy;
        float target = holding ? gripWeight : 0f;
        float step = gripBlendDuration > 0f ? Time.deltaTime / gripBlendDuration : 1f;
        grip.Weight = Mathf.MoveTowards(grip.Weight, target, step);

        if (grip.Weight > 0f && HasGrip(handedness))
        {
            for (int i = 0; i < Constants.NUM_FINGERS; i++)
            {
                synthetic.SetFingerFreedom((HandFinger)i, JointFreedom.Locked, true);
            }

            grip.Locked = true;
            synthetic.OverrideAllJoints(gripPose.For(handedness).handPose.JointRotations, grip.Weight);
        }
        else if (grip.Locked)
        {
            synthetic.FreeAllJoints();
            grip.Locked = false;
        }
    }

    private static void ReleaseGripNow(SyntheticHand synthetic, GripState grip)
    {
        grip.Weight = 0f;
        if (grip.Locked && synthetic != null)
        {
            synthetic.FreeAllJoints();
        }

        grip.Locked = false;
    }

    // ---- Text ----

    private void EnsureMaterial()
    {
        if (nameText == null)
        {
            return;
        }

        Material source = nameText.fontSharedMaterial;
        if (textMaterial == null && source != null)
        {
            textMaterial = new Material(source) { name = source.name + " (signature reveal)" };
            textMaterial.EnableKeyword("UNITY_UI_CLIP_RECT");
            nameText.fontSharedMaterial = textMaterial;
        }

        nameText.extraPadding = true;
    }

    private void FitText()
    {
        if (nameText == null || nameText.font == null)
        {
            return;
        }

        Transform t = nameText.transform;
        t.localScale = Vector3.one;
        Vector2 h = nameText.GetPreferredValues("H");
        FaceInfo face = nameText.font.faceInfo;
        float capInText = h.y * (face.capLine / Mathf.Max(1e-4f, face.lineHeight));
        float textWidth = nameText.GetPreferredValues(nameText.text).x;
        float scale = capInText > 0f ? maxCapHeight / capInText : 0.001f;
        float usable = signingWidth * textWidthFill;
        if (textWidth * scale > usable && textWidth > 0f)
        {
            scale = usable / textWidth;
        }

        fittedCapHeight = capInText * scale;
        nameText.alignment = TextAlignmentOptions.Baseline;
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.overflowMode = TextOverflowModes.Overflow;
        nameText.rectTransform.sizeDelta = new Vector2(Mathf.Max(textWidth * 1.2f, 1f), Mathf.Max(h.y * 2f, 1f));
        t.localScale = Vector3.one * scale;
        t.localPosition = new Vector3(0f, textLift, 0f);
        t.localRotation = Quaternion.identity;
        nameText.ForceMeshUpdate();
    }

    // Clip the name at the revealed edge, in the text's local space. Clip
    // values stay near the text: TMP's mobile shader uses 16-bit halves on
    // Quest, so "infinitely far" edges would saturate (see ConstellationSigning).
    private void ApplyWipe()
    {
        if (nameText == null || textMaterial == null || !nameText.enabled)
        {
            return;
        }

        Transform t = nameText.transform;
        float scale = Mathf.Max(1e-6f, t.lossyScale.x);
        float softness = wipeSoftness / scale;
        Bounds bounds = nameText.textBounds;
        if (bounds.size.x <= 0f)
        {
            nameText.ForceMeshUpdate();
            bounds = nameText.textBounds;
        }

        float pad = Mathf.Max(bounds.size.y, 1f) + softness;
        float left = bounds.min.x - pad;
        float right = bounds.max.x + pad;
        float edge = state == State.Complete
            ? right
            : Mathf.Clamp(t.InverseTransformPoint(transform.TransformPoint(new Vector3(-signingWidth * 0.5f + progress, 0f, 0f))).x, left, right);
        if (state != State.Complete && progress <= 0f)
        {
            edge = left;
        }

        textMaterial.SetVector(ClipRectId, new Vector4(left, bounds.min.y - pad, edge, bounds.max.y + pad));
        textMaterial.SetFloat(MaskSoftnessXId, softness);
        textMaterial.SetFloat(MaskSoftnessYId, 0f);
    }

    private void SetHidden(bool complete)
    {
        if (hideOnComplete == null)
        {
            return;
        }

        foreach (GameObject go in hideOnComplete)
        {
            if (go != null)
            {
                go.SetActive(!complete);
            }
        }
    }

    private void Log(string message)
    {
        if (logEvents)
        {
            Debug.Log("SignatureReveal: " + message, this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.8f);
        Vector3 s = transform.lossyScale;
        Vector3 size = new Vector3(
            (signingWidth * s.x + 2f * alongTolerance) / Mathf.Max(1e-6f, s.x),
            2f * verticalTolerance / Mathf.Max(1e-6f, s.y),
            (depthTowardPlayer + depthBehind) / Mathf.Max(1e-6f, s.z));
        Vector3 center = new Vector3(0f, 0f, (depthBehind - depthTowardPlayer) * 0.5f / Mathf.Max(1e-6f, s.z));
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(center, size);
        Gizmos.matrix = Matrix4x4.identity;
        if (showInteractionArea && Application.isPlaying)
        {
            Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.9f);
            Gizmos.DrawWireSphere(EdgePoint, startResumeTolerance);
        }
    }
}
