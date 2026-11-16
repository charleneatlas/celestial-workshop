using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Solve reward, part 3 (prototype): sign the constellation.
///
/// Once ConstellationReveal holds the pattern facing the player, a straight
/// signing line appears under (or over) it, frozen in that presentation
/// frame. A placeholder pen (thin cylinder) follows the player's tracked
/// hand; drawing its tip left to right along the line wipes the
/// constellation's name into view, continuously across the glyphs.
///
/// Rules: the stroke must start near the left end; leaving the area or
/// losing tracking pauses it; it resumes only near the revealed edge (never
/// ahead of it); moving back keeps progress. Past the completion threshold the
/// rest of the name appears, the line and pen go away, and onSigned fires
/// once. Reset (ConstellationReveal.Cleared) removes everything.
///
/// The pen is never parented to the hand rig: it is posed from the hand's
/// joint data each frame, so nothing is left behind on the hands.
/// </summary>
[DefaultExecutionOrder(210)]
public sealed class ConstellationSigning : MonoBehaviour
{
    public enum Placement
    {
        Below,
        Above
    }

    public enum PenAnchor
    {
        [Tooltip("Midway between the thumb tip and the index tip.")]
        PinchPoint,
        Wrist
    }

    public enum State
    {
        Off,
        Ready,
        Signing,
        Paused,
        Complete
    }

    [Header("Signing")]
    [SerializeField]
    private bool signingEnabled = true;

    [SerializeField]
    private ConstellationReveal reveal;

    [Tooltip("Source of the current constellation's name.")]
    [SerializeField]
    private ConstellationSetup setup;

    [Tooltip("Hands that can sign (the rig's per-hand HandRef).")]
    [SerializeField]
    [Interface(typeof(IHand))]
    private List<Object> hands = new List<Object>();

    [Tooltip("Optional: sign this text instead of the current constellation's name.")]
    [SerializeField]
    private string nameOverride = "";

    [Header("Placement")]
    [Tooltip("Signature block below or above the presented constellation.")]
    [SerializeField]
    private Placement placement = Placement.Below;

    [Tooltip("Clear space between the pattern's star bounds and the signature block (m).")]
    [SerializeField]
    private float gap = 0.03f;

    [Tooltip("Extra offset of the signature block in the presentation frame: x = player's right, y = up, z = toward the player (m).")]
    [SerializeField]
    private Vector3 placementOffset = Vector3.zero;

    [Tooltip("Optional flat disc display (normal = its local +Y, like the Earth View screens). With Placement Below, the signature block is lowered when needed so its top stays Gap below the display's lowest edge. Empty = place from the constellation only.")]
    [SerializeField]
    private Renderer keepBelowDisplay;

    [Tooltip("Length of the signing line (m). The same for every name; the name is fitted inside it.")]
    [SerializeField]
    [Min(0.02f)]
    private float signingWidth = 0.22f;

    [Header("Name Text")]
    [SerializeField]
    private TMP_FontAsset font;

    [SerializeField]
    private Color textColor = new Color(1f, 0.92f, 0.75f, 1f);

    [Tooltip("Text size: height of the capital letters (m). Long names shrink to fit the width; never stretched or wrapped.")]
    [SerializeField]
    [Min(0.002f)]
    private float maxCapHeight = 0.03f;

    [Tooltip("Fraction of the signing width the name may use.")]
    [SerializeField]
    [Range(0.3f, 1f)]
    private float textWidthFill = 0.9f;

    [Tooltip("Baseline height above the signing line (m).")]
    [SerializeField]
    private float textLift = 0.006f;

    [Tooltip("Softness of the reveal edge (m).")]
    [SerializeField]
    [Min(0f)]
    private float wipeSoftness = 0.006f;

    [Header("Signing Line")]
    [SerializeField]
    private Color lineColor = new Color(0.85f, 0.9f, 1f, 0.6f);

    [Tooltip("Line width (m).")]
    [SerializeField]
    [Min(0.0002f)]
    private float lineThickness = 0.0015f;

    [Tooltip("CelestialWorkshop/GuideLine (referenced so it ships in builds).")]
    [SerializeField]
    private Shader lineShader;

    [Tooltip("Optional: the TMP material the name is drawn with (e.g. the noticeboard's Signature_Gold, a gold glow). Empty = the font's own material.")]
    [SerializeField]
    private Material nameMaterial;

    [Header("Practice")]
    [Tooltip("Found automatically if empty.")]
    [SerializeField]
    private GameFlow gameFlow;

    [Tooltip("Off: practice puzzles skip signing entirely (no line, pen, grip or name).")]
    [SerializeField]
    private bool signInPractice = false;

    [Header("Guild Certificate")]
    [Tooltip("When set, the name is signed on this certificate at the workstation instead of under the constellation in the sphere. Same pen, gesture and thresholds.")]
    [SerializeField]
    private GuildCertificate certificate;

    [Tooltip("While signing on the certificate, the same name is revealed in step inside the sphere, where it used to be signed (same layout as before).")]
    [SerializeField]
    private bool revealInSphereToo = true;

    [Tooltip("Material of the sphere copy. Empty = the font's own material (the original sphere signature: crisp letters, sparkle from the star trail).")]
    [SerializeField]
    private Material sphereNameMaterial;

    [Header("Pen")]
    [Tooltip("Pen to spawn (tip at its origin, or at a child named \"Tip\"). Empty = a generated cylinder using the placeholder size and color below.")]
    [SerializeField]
    private GameObject penPrefab;

    [Header("Pen Grip (recorded pose)")]
    [Tooltip("Recorded pen-holding grip. When set, the pen sits in the hand where it was recorded and the visible fingers take the grip. Empty = the hand-frame offsets below.")]
    [SerializeField]
    private PenGripPose gripPose;

    [Tooltip("The SyntheticHand the left hand visual reads (the last in its chain).")]
    [SerializeField]
    private SyntheticHand leftSyntheticHand;

    [Tooltip("The SyntheticHand the right hand visual reads (the last in its chain).")]
    [SerializeField]
    private SyntheticHand rightSyntheticHand;

    [Tooltip("How strongly the visible fingers take the grip (1 = fully).")]
    [SerializeField]
    [Range(0f, 1f)]
    private float gripWeight = 1f;

    [Tooltip("Blend in/out time for the grip (s).")]
    [SerializeField]
    [Min(0f)]
    private float gripBlendDuration = 0.15f;

    [Tooltip("Fine-tune of the recorded pen placement, in the pen's own space (m).")]
    [SerializeField]
    private Vector3 gripNudgePosition = Vector3.zero;

    [Tooltip("Fine-tune of the recorded pen placement, in the pen's own space (degrees).")]
    [SerializeField]
    private Vector3 gripNudgeRotation = Vector3.zero;

#if UNITY_EDITOR
    [Tooltip("Editor only: the scene pen the grip was recorded on (its HandGrabPoses are read by \"Capture Grip From Pen Pose Reference\").")]
    [SerializeField]
    private Transform penPoseReference;
#endif

    [Header("Pen (placeholder, no prefab)")]
    [SerializeField]
    [Min(0.01f)]
    private float penLength = 0.14f;

    [SerializeField]
    [Min(0.001f)]
    private float penThickness = 0.008f;

    [SerializeField]
    private Color penColor = new Color(0.75f, 0.6f, 0.35f, 1f);

    [Tooltip("Lit shader for the pen (URP Lit). Empty = looked up by name.")]
    [SerializeField]
    private Shader penShader;

    [Header("Pen Offsets (no recorded grip)")]
    [Tooltip("Where the pen tip is measured from on the hand.")]
    [SerializeField]
    private PenAnchor anchor = PenAnchor.PinchPoint;

    [Tooltip("Tip position from the anchor, in the hand frame (m): x = toward the thumb side, y = out of the back of the hand, z = toward the fingertips. Mirrored automatically for the left hand.")]
    [SerializeField]
    private Vector3 penPositionOffset = new Vector3(0f, -0.005f, 0.012f);

    [Tooltip("Pen orientation in the same hand frame (degrees). At zero the pen body points out of the back of the hand; the default leans it back toward the wrist. Mirrored automatically for the left hand.")]
    [SerializeField]
    private Vector3 penRotationOffset = new Vector3(-53f, 0f, 0f);

    [Header("Interaction")]
    [Tooltip("Slack past either end of the line (m).")]
    [SerializeField]
    [Min(0f)]
    private float alongTolerance = 0.04f;

    [Tooltip("How far above or below the line the tip may be (m).")]
    [SerializeField]
    [Min(0.001f)]
    private float verticalTolerance = 0.06f;

    [Tooltip("How far in front of the line, toward the player, the tip may be (m).")]
    [SerializeField]
    [Min(0.001f)]
    private float depthTowardPlayer = 0.08f;

    [Tooltip("How far behind the line, away from the player, the tip may be (m).")]
    [SerializeField]
    [Min(0.001f)]
    private float depthBehind = 0.05f;

    [Tooltip("To start, the tip must enter within this distance of the line's left end; to resume, within this distance ahead of the revealed edge (m).")]
    [SerializeField]
    [Min(0.001f)]
    private float startResumeTolerance = 0.04f;

    [Tooltip("Fastest the reveal can advance (m/s), so a tracking jump can't skip ahead.")]
    [SerializeField]
    [Min(0.01f)]
    private float maxAdvanceSpeed = 1.5f;

    [Tooltip("Pause on low-confidence tracking as well as lost tracking.")]
    [SerializeField]
    private bool requireHighConfidence = true;

    [Tooltip("After this long paused, either hand may resume (s). 0 = the hand that started keeps the gesture.")]
    [SerializeField]
    [Min(0f)]
    private float handReleaseAfter = 2f;

    [Header("Completion")]
    [Tooltip("Fraction of the line that completes the signature.")]
    [SerializeField]
    [Range(0.5f, 1f)]
    private float completionThreshold = 0.9f;

    [Tooltip("Fired once per attempt when the signature completes (for the circular window / menu transition).")]
    [SerializeField]
    private UnityEvent onSigned = new UnityEvent();

    [Header("Debug")]
    [Tooltip("Outline the interaction area in the headset.")]
    [SerializeField]
    private bool showInteractionArea = false;

    [SerializeField]
    private bool logEvents = true;

    /// <summary>Fired once per attempt when the signature completes.</summary>
    public event System.Action Signed;

    public State CurrentState => state;
    /// <summary>True while this attempt is signed on the Guild Certificate.</summary>
    public bool IsOnCertificate => onCertificate;

    // Line length and name size for this attempt (the certificate's, or the sphere layout's).
    private float Width => onCertificate ? certificateWidth : signingWidth;
    private float CapHeight => onCertificate ? certificateCapHeight : maxCapHeight;

    public float Progress => Width > 0f ? progress / Width : 0f;
    public bool IsComplete => state == State.Complete;

    private const float HandSwitchHysteresis = 0.05f;
    private const int CertificateTextSortingOrder = 5;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int DashFractionId = Shader.PropertyToID("_DashFraction");
    private static readonly int ClipRectId = Shader.PropertyToID("_ClipRect");
    private static readonly int MaskSoftnessXId = Shader.PropertyToID("_MaskSoftnessX");
    private static readonly int MaskSoftnessYId = Shader.PropertyToID("_MaskSoftnessY");

    private readonly List<IHand> handList = new List<IHand>();

    private State state = State.Off;
    private bool firedThisAttempt;
    private float progress;
    private IHand activeHand;
    private IHand penHand;
    private float pausedTime;
    private Coroutine autoSign;

    // Presentation frame, frozen when the signing area is built.
    private Vector3 frameCenter;
    private Vector3 frameRight;
    private Vector3 frameUp;
    private Vector3 frameToward;
    private Vector2 frameHalf;
    private bool onCertificate;
    private float certificateWidth;
    private float certificateCapHeight;

    // The sphere copy of the name while signing on the certificate (no input of its own).
    private TextMeshPro skyText;
    private Material skyMaterial;
    private bool hasSkyFrame;
    private Vector3 skyCenter;
    private Vector3 skyRight;
    private Vector3 skyUp;
    private Vector3 skyToward;
    private Vector2 skyHalf;
    private Vector3 skyLineLeft;
    private float skyCapHeight;
    private float skyRevealFraction;
    private Vector3 lineLeft;
    private float fittedCapHeight;
    private bool layoutDirty;

    private GameObject signingRoot;
    private LineRenderer line;
    private LineRenderer areaFront;
    private LineRenderer areaBack;
    private TextMeshPro nameText;
    private Transform penRoot;
    private Transform penTip;
    private Transform penBody;
    private Material lineMaterial;
    private Material areaMaterial;
    private Material textMaterial;
    private Material penMaterial;

    // Tip offset from the pen root, in the root's rotation frame (world scale).
    private Vector3 tipOffset;

    private sealed class GripState
    {
        public float Weight;
        public bool Locked;
    }

    private readonly GripState leftGrip = new GripState();
    private readonly GripState rightGrip = new GripState();

    /// <summary>The pen tip (null while no pen exists).</summary>
    public Transform PenTip => penTip;

    /// <summary>
    /// A point on the signing line at lineFraction of its length (0 = left end),
    /// raised to capFraction of the name's cap height above the text baseline.
    /// False while there is no signing line. Read-only; used by SigningStarTrail.
    /// </summary>
    /// <summary>Line fraction of the sphere copy's revealed edge (follows Progress, letter for letter).</summary>
    public float SkyProgress => state == State.Complete ? 1f : skyRevealFraction;

    /// <summary>
    /// Like TryGetLinePoint, on the sphere copy's line (false while there is
    /// no sphere copy: not on the certificate, or it is off).
    /// </summary>
    public bool TryGetSkyLinePoint(float lineFraction, float capFraction, out Vector3 point)
    {
        if (signingRoot == null || skyText == null)
        {
            point = default;
            return false;
        }

        point = skyLineLeft + skyRight * (signingWidth * lineFraction) + skyUp * (textLift + skyCapHeight * capFraction);
        return true;
    }

    public bool TryGetLinePoint(float lineFraction, float capFraction, out Vector3 point)
    {
        if (signingRoot == null)
        {
            point = default;
            return false;
        }

        point = lineLeft + frameRight * (Width * lineFraction) + frameUp * (textLift + fittedCapHeight * capFraction);
        return true;
    }

    private void Awake()
    {
        if (gameFlow == null)
        {
            gameFlow = FindFirstObjectByType<GameFlow>();
        }

        foreach (Object handObject in hands)
        {
            if (handObject is IHand hand)
            {
                handList.Add(hand);
            }
        }
    }

    private void OnEnable()
    {
        if (reveal != null)
        {
            reveal.Presented += OnPresented;
            reveal.Cleared += ClearSigning;
        }
    }

    private void OnDisable()
    {
        if (reveal != null)
        {
            reveal.Presented -= OnPresented;
            reveal.Cleared -= ClearSigning;
        }

        ClearSigning();
    }

    private void OnDestroy()
    {
        foreach (Material material in new[] { lineMaterial, areaMaterial, textMaterial, skyMaterial, penMaterial })
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }

    private void OnValidate()
    {
        layoutDirty = true;
    }

    private void OnPresented()
    {
        if (signingEnabled && !SkipsSigning)
        {
            BeginAttempt();
        }
    }

    /// <summary>True when the current puzzle has no signing (a practice letter).</summary>
    public bool SkipsSigning => !signInPractice && gameFlow != null && gameFlow.IsPractice;

    // ---- Public controls ----

    /// <summary>Removes the signing visuals and progress, and re-arms completion.</summary>
    public void ClearSigning()
    {
        if (autoSign != null)
        {
            StopCoroutine(autoSign);
            autoSign = null;
        }

        if (nameText != null && signingRoot != null && !nameText.transform.IsChildOf(signingRoot.transform))
        {
            Destroy(nameText.gameObject);
        }

        if (signingRoot != null)
        {
            Destroy(signingRoot);
        }

        onCertificate = false;
        skyText = null;
        hasSkyFrame = false;
        skyRevealFraction = 0f;

        signingRoot = null;
        line = null;
        areaFront = null;
        areaBack = null;
        nameText = null;
        penRoot = null;
        penTip = null;
        penBody = null;

        state = State.Off;
        progress = 0f;
        activeHand = null;
        penHand = null;
        pausedTime = 0f;
        firedThisAttempt = false;
        ReleaseGripNow(leftSyntheticHand, leftGrip);
        ReleaseGripNow(rightSyntheticHand, rightGrip);
    }

    [ContextMenu("Retry Signing")]
    public void RetrySigning()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("ConstellationSigning: retry works in Play mode.", this);
            return;
        }

        ClearSigning();
        if (reveal != null && reveal.CurrentPhase == ConstellationReveal.Phase.Holding)
        {
            BeginAttempt();
        }
        else
        {
            Debug.Log("ConstellationSigning: the reveal isn't holding yet; use ConstellationReveal > Preview Reveal first.", this);
        }
    }

    [ContextMenu("Clear Signing")]
    private void ClearSigningFromMenu()
    {
        ClearSigning();
    }

    [ContextMenu("Debug: Auto-Sign")]
    private void AutoSignFromMenu()
    {
        if (!Application.isPlaying || state == State.Off || state == State.Complete)
        {
            Debug.LogWarning("ConstellationSigning: auto-sign needs a signing line in Play mode (Retry Signing or Preview Reveal first).", this);
            return;
        }

        if (autoSign != null)
        {
            StopCoroutine(autoSign);
        }

        autoSign = StartCoroutine(AutoSign(1.5f));
    }

    // ---- Attempt ----

    private void BeginAttempt()
    {
        ClearSigning();
        if (certificate != null && certificate.ShouldUse)
        {
            certificate.Present();
            onCertificate = certificate.TryGetSigningFrame(out frameCenter, out frameRight, out frameUp, out frameToward,
                out certificateWidth, out certificateCapHeight);
            frameHalf = Vector2.zero;
            if (!onCertificate)
            {
                certificate.HideNow();
            }
            else if (revealInSphereToo && reveal != null)
            {
                hasSkyFrame = reveal.TryGetPresentation(out skyCenter, out skyRight, out skyUp, out skyToward, out skyHalf);
            }
        }

        if (!onCertificate && (reveal == null ||
            !reveal.TryGetPresentation(out frameCenter, out frameRight, out frameUp, out frameToward, out frameHalf)))
        {
            Debug.LogWarning("ConstellationSigning: no held presentation to sign under.", this);
            return;
        }

        string text = SignatureText();
        if (string.IsNullOrEmpty(text) || font == null)
        {
            if (onCertificate)
            {
                certificate.HideNow();
            }

            Debug.LogWarning("ConstellationSigning: no name or no font; signing skipped.", this);
            return;
        }

        BuildVisuals(text);
        if (onCertificate)
        {
            AttachToCertificate();
        }

        state = State.Ready;
        if (logEvents)
        {
            Debug.Log($"ConstellationSigning: ready to sign \"{text}\" ({handList.Count} hand(s)).", this);
        }
    }

    private string SignatureText()
    {
        if (!string.IsNullOrWhiteSpace(nameOverride))
        {
            return nameOverride.Trim();
        }

        return setup != null ? setup.ConstellationName : "";
    }

    private IEnumerator AutoSign(float duration)
    {
        state = State.Signing;
        activeHand = null;
        float start = progress;
        float elapsed = 0f;
        while (state == State.Signing)
        {
            elapsed += Time.deltaTime;
            progress = Mathf.Lerp(start, Width, Mathf.Clamp01(elapsed / duration));
            if (CheckCompletion())
            {
                break;
            }

            yield return null;
        }

        autoSign = null;
    }

    // ---- Per frame ----

    private void LateUpdate()
    {
        if (state == State.Off)
        {
            return;
        }

        if (!signingEnabled)
        {
            ClearSigning();
            return;
        }

        if (layoutDirty)
        {
            layoutDirty = false;
            // On the certificate the name is parented to it (laid out once, at build).
            if (!onCertificate)
            {
                Layout();
            }
        }

        ApplyLook();

        if (state != State.Complete && autoSign == null)
        {
            UpdateInteraction();
        }

        ApplyWipe();
        UpdateGrip(leftSyntheticHand, leftGrip, Handedness.Left);
        UpdateGrip(rightSyntheticHand, rightGrip, Handedness.Right);
    }

    private void UpdateInteraction()
    {
        float dt = Time.deltaTime;

        if (state == State.Signing)
        {
            bool tracked = TryGetPenPose(activeHand, out Pose tip);
            PoseSigningPen(tracked ? activeHand : null, tip);
            if (tracked && InsideArea(TipOf(tip), out float along))
            {
                float target = Mathf.Clamp(along, 0f, Width);
                progress = Mathf.Max(progress, Mathf.Min(target, progress + maxAdvanceSpeed * dt));
                CheckCompletion();
            }
            else
            {
                state = State.Paused;
                pausedTime = 0f;
                if (logEvents)
                {
                    Debug.Log($"ConstellationSigning: paused at {Progress:P0} ({(tracked ? "left the area" : "tracking lost")}).", this);
                }
            }

            return;
        }

        // Ready or Paused: find who may (re)start and whether they do.
        if (state == State.Paused)
        {
            pausedTime += dt;
        }

        bool anyHand = activeHand == null || (state == State.Paused && handReleaseAfter > 0f && pausedTime >= handReleaseAfter);
        IHand closest = null;
        Pose closestTip = default;
        float closestDistance = float.MaxValue;
        float penHandDistance = float.MaxValue;
        Pose penHandTip = default;
        Vector3 resumePoint = lineLeft + frameRight * progress;

        foreach (IHand hand in handList)
        {
            if (!anyHand && hand != activeHand)
            {
                continue;
            }

            if (!TryGetPenPose(hand, out Pose tip))
            {
                continue;
            }

            if (InsideArea(TipOf(tip), out float along) && along <= progress + startResumeTolerance)
            {
                bool resumed = state == State.Paused;
                activeHand = hand;
                state = State.Signing;
                PoseSigningPen(hand, tip);
                if (logEvents)
                {
                    Debug.Log($"ConstellationSigning: {(resumed ? "resumed" : "started")} with the {hand.Handedness} hand at {Progress:P0}.", this);
                }

                return;
            }

            float distance = Vector3.Distance(TipOf(tip), resumePoint);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = hand;
                closestTip = tip;
            }

            if (hand == penHand)
            {
                penHandDistance = distance;
                penHandTip = tip;
            }
        }

        // Pen stays on its current hand unless the other is clearly closer.
        if (penHand != null && penHandDistance < float.MaxValue && penHandDistance <= closestDistance + HandSwitchHysteresis)
        {
            PoseSigningPen(penHand, penHandTip);
        }
        else
        {
            PoseSigningPen(closest, closestTip);
        }
    }

    private bool CheckCompletion()
    {
        if (state == State.Complete || progress < completionThreshold * Width)
        {
            return false;
        }

        progress = Width;
        state = State.Complete;
        activeHand = null;
        if (line != null)
        {
            line.gameObject.SetActive(false);
        }

        if (penRoot != null)
        {
            penRoot.gameObject.SetActive(false);
        }

        SetAreaVisible(false);
        ApplyWipe();

        if (!firedThisAttempt)
        {
            firedThisAttempt = true;
            if (logEvents)
            {
                Debug.Log("ConstellationSigning: signed.", this);
            }

            Signed?.Invoke();
            onSigned?.Invoke();
        }

        return true;
    }

    // Line coordinates: along from the left end (m), height above the line,
    // depth toward the player. True when the tip is inside the forgiving box.
    private bool InsideArea(Vector3 point, out float along)
    {
        Vector3 offset = point - lineLeft;
        along = Vector3.Dot(offset, frameRight);
        float height = Vector3.Dot(offset, frameUp);
        float depth = Vector3.Dot(offset, frameToward);
        return along >= -alongTolerance && along <= Width + alongTolerance &&
               Mathf.Abs(height) <= verticalTolerance &&
               depth <= depthTowardPlayer && depth >= -depthBehind;
    }

    // ---- Hand and pen ----

    private bool HandUsable(IHand hand)
    {
        return hand != null && hand.IsTrackedDataValid && (!requireHighConfidence || hand.IsHighConfidence);
    }

    private Vector3 TipOf(Pose pen)
    {
        return pen.position + pen.rotation * tipOffset;
    }

    private bool HasGrip(Handedness handedness)
    {
        return gripPose != null && gripPose.For(handedness).valid;
    }

    /// <summary>
    /// World pose of the pen root on this hand. With a recorded grip it comes
    /// from the wrist alone (never the fingers, which the grip itself poses).
    /// </summary>
    private bool TryGetPenPose(IHand hand, out Pose pen)
    {
        pen = default;
        if (!HandUsable(hand) || !hand.GetRootPose(out Pose wrist))
        {
            return false;
        }

        if (HasGrip(hand.Handedness))
        {
            pen = gripPose.For(hand.Handedness).PenFromWrist(wrist);
            pen.position += pen.rotation * gripNudgePosition;
            pen.rotation *= Quaternion.Euler(gripNudgeRotation);
            return true;
        }

        return TryGetOffsetPenPose(hand, wrist, out pen);
    }

    // Fallback without a recorded grip: tip offset from the pinch point or
    // wrist, in a hand frame shared (mirrored) by both hands.
    private bool TryGetOffsetPenPose(IHand hand, Pose wrist, out Pose tip)
    {
        tip = default;
        Vector3 origin = wrist.position;
        if (anchor == PenAnchor.PinchPoint)
        {
            if (!hand.GetJointPose(HandJointId.HandThumbTip, out Pose thumb) ||
                !hand.GetJointPose(HandJointId.HandIndexTip, out Pose index))
            {
                return false;
            }

            origin = (thumb.position + index.position) * 0.5f;
        }

        // Hand frame shared by both hands (mirror images of each other).
        HandMirroring.HandSpace space = hand.Handedness == Handedness.Left
            ? HandMirroring.LeftHandSpace
            : HandMirroring.RightHandSpace;
        Vector3 thumbSide = wrist.rotation * space.thumbSide;
        Vector3 dorsal = wrist.rotation * space.dorsal;
        Vector3 distal = wrist.rotation * space.distal;

        Vector3 ToWorld(Vector3 v) => thumbSide * v.x + dorsal * v.y + distal * v.z;

        Quaternion r = Quaternion.Euler(penRotationOffset);
        Vector3 body = ToWorld(r * Vector3.up);
        Vector3 facing = ToWorld(r * Vector3.forward);

        tip = new Pose(origin + ToWorld(penPositionOffset), Quaternion.LookRotation(facing, body));
        return true;
    }

    // The pen's body runs along its local +Y from the tip at its origin.
    private void PoseSigningPen(IHand hand, Pose tip)
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
            penRoot.SetPositionAndRotation(tip.position, tip.rotation);
        }
    }

    // ---- Visuals ----

    private void BuildVisuals(string text)
    {
        signingRoot = new GameObject("Constellation Signing") { layer = 0 };

        if (lineShader == null)
        {
            lineShader = Shader.Find("CelestialWorkshop/GuideLine");
        }

        if (lineShader != null)
        {
            if (lineMaterial == null)
            {
                lineMaterial = new Material(lineShader) { name = "Signing Line" };
                areaMaterial = new Material(lineShader) { name = "Signing Area" };
            }

            line = NewLine("Signing Line", lineMaterial, 2, false);
            areaFront = NewLine("Area Front", areaMaterial, 4, true);
            areaBack = NewLine("Area Back", areaMaterial, 4, true);
        }

        // Name text, with its own copy of the name material (clip rect on).
        nameText = NewNameText("Signed Name", text, ref textMaterial, " (signing)", nameMaterial);

        // The sphere copy: same name and look, revealed in step, no input.
        if (onCertificate && hasSkyFrame)
        {
            skyText = NewNameText("Signed Name (Sphere)", text, ref skyMaterial, " (signing, sphere)", sphereNameMaterial);
        }

        if (penPrefab != null)
        {
            BuildPrefabPen();
        }
        else
        {
            BuildPlaceholderPen();
        }

        Layout();
        ApplyLook();
        ApplyWipe();
    }

    private TextMeshPro NewNameText(string objectName, string text, ref Material material, string suffix, Material look)
    {
        Material source = look != null ? look : font.material;
        var textObject = new GameObject(objectName) { layer = 0 };
        textObject.transform.SetParent(signingRoot.transform, false);
        TextMeshPro tmp = textObject.AddComponent<TextMeshPro>();
        tmp.font = font;
        if (material == null || material.shader != source.shader)
        {
            if (material != null)
            {
                Destroy(material);
            }

            material = new Material(source) { name = source.name + suffix };
        }

        material.CopyPropertiesFromMaterial(source);
        material.shaderKeywords = source.shaderKeywords;
        material.EnableKeyword("UNITY_UI_CLIP_RECT");
        tmp.fontSharedMaterial = material;
        tmp.extraPadding = true;
        tmp.text = text;
        tmp.fontSize = 10f;
        tmp.alignment = TextAlignmentOptions.Baseline;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        if (textObject.TryGetComponent(out MeshRenderer textRenderer))
        {
            textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
        }

        return tmp;
    }

    // The certificate prints its own line; the name is carried by the
    // certificate (so it leaves with it) and drawn over the parchment.
    private void AttachToCertificate()
    {
        if (line != null)
        {
            line.gameObject.SetActive(false);
        }

        if (nameText != null)
        {
            if (nameText.TryGetComponent(out MeshRenderer textRenderer))
            {
                textRenderer.sortingOrder = CertificateTextSortingOrder;
            }

            nameText.transform.SetParent(certificate.NameLine, true);
        }

        certificate.BeginAppear();
    }

    private void BuildPrefabPen()
    {
        GameObject pen = Instantiate(penPrefab, signingRoot.transform, false);
        pen.name = penPrefab.name;
        penRoot = pen.transform;
        penBody = null;

        // Visual only: no physics or colliders near the hands.
        foreach (Collider c in pen.GetComponentsInChildren<Collider>(true))
        {
            Destroy(c);
        }

        foreach (Rigidbody rb in pen.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.detectCollisions = false;
        }

        Transform tip = FindChild(penRoot, "Tip");
        penTip = tip != null ? tip : penRoot;
        tipOffset = Quaternion.Inverse(penRoot.rotation) * (penTip.position - penRoot.position);
        penRoot.gameObject.SetActive(false);
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

    private void BuildPlaceholderPen()
    {
        // Placeholder pen: tip at the root's origin, body along +Y.
        tipOffset = Vector3.zero;
        penRoot = new GameObject("Placeholder Pen") { layer = 0 }.transform;
        penRoot.SetParent(signingRoot.transform, false);
        penTip = new GameObject("Tip") { layer = 0 }.transform;
        penTip.SetParent(penRoot, false);
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.name = "Body";
        body.layer = 0;
        DestroyImmediate(body.GetComponent<Collider>());
        penBody = body.transform;
        penBody.SetParent(penRoot, false);
        if (penShader == null)
        {
            penShader = Shader.Find("Universal Render Pipeline/Lit");
        }

        if (penMaterial == null && penShader != null)
        {
            penMaterial = new Material(penShader) { name = "Placeholder Pen" };
        }

        var penRenderer = body.GetComponent<MeshRenderer>();
        if (penMaterial != null)
        {
            penRenderer.sharedMaterial = penMaterial;
        }

        penRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        penRoot.gameObject.SetActive(false);
    }

    // ---- Recorded grip on the visible hand ----

    // Blends the hand holding the pen into the recorded grip and every other
    // hand back out. Fingers are locked only while the weight is above zero.
    private void UpdateGrip(SyntheticHand synthetic, GripState grip, Handedness handedness)
    {
        if (synthetic == null)
        {
            return;
        }

        bool holding = HasGrip(handedness) &&
                       penHand != null && penHand.Handedness == handedness &&
                       penRoot != null && penRoot.gameObject.activeInHierarchy;
        float target = holding ? gripWeight : 0f;
        float step = gripBlendDuration > 0f ? Time.deltaTime / gripBlendDuration : 1f;
        grip.Weight = Mathf.MoveTowards(grip.Weight, target, step);

        if (grip.Weight > 0f && HasGrip(handedness))
        {
            // Re-asserted every frame: other ISDK visuals on the same hand
            // may free the fingers (a no-op when already locked).
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

#if UNITY_EDITOR
    // Reads the HandGrabPoses recorded on the reference pen (one per hand;
    // the left can be ISDK's mirrored copy) into the Pen Grip Pose asset.
    [ContextMenu("Capture Grip From Pen Pose Reference")]
    private void CaptureGrip()
    {
        if (penPoseReference == null)
        {
            Debug.LogWarning("ConstellationSigning: assign Pen Pose Reference first.", this);
            return;
        }

        if (gripPose == null)
        {
            const string folder = "Assets/CW_Art/HandPoses";
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
            {
                UnityEditor.AssetDatabase.CreateFolder("Assets/CW_Art", "HandPoses");
            }

            gripPose = ScriptableObject.CreateInstance<PenGripPose>();
            UnityEditor.AssetDatabase.CreateAsset(gripPose, folder + "/PenGrip.asset");
        }

        var report = new System.Text.StringBuilder();
        bool foundLeft = false;
        bool foundRight = false;
        foreach (HandGrabPose pose in penPoseReference.GetComponentsInChildren<HandGrabPose>(true))
        {
            if (pose.HandPose == null)
            {
                continue;
            }

            Handedness handedness = pose.HandPose.Handedness;
            if ((handedness == Handedness.Left && foundLeft) || (handedness == Handedness.Right && foundRight))
            {
                report.AppendLine($"  skipped extra {handedness} pose on {pose.name} (keep one per hand)");
                continue;
            }

            Transform relativeTo = pose.RelativeTo != null ? pose.RelativeTo : penPoseReference;
            Pose wristInPen = pose.RelativeTo != null
                ? pose.RelativePose
                : new Pose(relativeTo.InverseTransformPoint(pose.transform.position), Quaternion.Inverse(relativeTo.rotation) * pose.transform.rotation);

            PenGripPose.Side side = gripPose.For(handedness);
            side.handPose = new HandPose(pose.HandPose);
            side.wristPosition = wristInPen.position;
            side.wristRotation = wristInPen.rotation;
            side.valid = true;
            if (handedness == Handedness.Left)
            {
                foundLeft = true;
            }
            else
            {
                foundRight = true;
            }

            report.AppendLine($"  {handedness} from {pose.name}: wrist {wristInPen.position.ToString("F4")} in pen space");
        }

        UnityEditor.EditorUtility.SetDirty(gripPose);
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log($"ConstellationSigning: captured grip into {UnityEditor.AssetDatabase.GetAssetPath(gripPose)} " +
                  $"(right {(foundRight ? "yes" : "NO")}, left {(foundLeft ? "yes" : "NO")}).\n{report}", this);
    }
#endif

    private LineRenderer NewLine(string name, Material material, int count, bool loop)
    {
        var lineObject = new GameObject(name) { layer = 0 };
        lineObject.transform.SetParent(signingRoot.transform, false);
        LineRenderer lr = lineObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.sharedMaterial = material;
        lr.alignment = LineAlignment.View;
        lr.numCapVertices = 2;
        lr.positionCount = count;
        lr.loop = loop;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.startColor = Color.white;
        lr.endColor = Color.white;
        return lr;
    }

    // Fits the name and places the line, text and area in the frozen frame.
    private void Layout()
    {
        if (signingRoot == null || nameText == null)
        {
            return;
        }

        fittedCapHeight = FitName(nameText, Width, CapHeight);
        Vector3 lineCenter = onCertificate
            ? frameCenter
            : SphereLineCenter(frameCenter, frameRight, frameUp, frameToward, frameHalf, fittedCapHeight);
        lineLeft = lineCenter - frameRight * (Width * 0.5f);

        if (line != null)
        {
            line.SetPosition(0, lineLeft);
            line.SetPosition(1, lineLeft + frameRight * Width);
        }

        // Text faces the player, reading along the line (local +X = right).
        nameText.transform.SetPositionAndRotation(lineCenter + frameUp * textLift, Quaternion.LookRotation(-frameToward, frameUp));

        if (skyText != null)
        {
            // Exactly where the sphere signature used to be.
            skyCapHeight = FitName(skyText, signingWidth, maxCapHeight);
            Vector3 skyLineCenter = SphereLineCenter(skyCenter, skyRight, skyUp, skyToward, skyHalf, skyCapHeight);
            skyLineLeft = skyLineCenter - skyRight * (signingWidth * 0.5f);
            skyText.transform.SetPositionAndRotation(skyLineCenter + skyUp * textLift, Quaternion.LookRotation(-skyToward, skyUp));
        }

        if (areaFront != null)
        {
            Vector3 a = lineLeft - frameRight * alongTolerance;
            Vector3 b = lineLeft + frameRight * (Width + alongTolerance);
            Vector3 v = frameUp * verticalTolerance;
            Vector3 front = frameToward * depthTowardPlayer;
            Vector3 back = -frameToward * depthBehind;
            areaFront.SetPositions(new[] { a - v + front, a + v + front, b + v + front, b - v + front });
            areaBack.SetPositions(new[] { a - v + back, a + v + back, b + v + back, b - v + back });
        }
    }

    // Capital height cap, shrunk to the usable width; returns the fitted cap
    // height (m). The text must sit under the (unscaled) signing root.
    private float FitName(TextMeshPro text, float width, float cap)
    {
        text.transform.localScale = Vector3.one;
        Vector2 h = text.GetPreferredValues("H");
        float capInText = h.y * (font.faceInfo.capLine / Mathf.Max(1e-4f, font.faceInfo.lineHeight));
        float textWidth = text.GetPreferredValues(text.text).x;
        float scale = capInText > 0f ? cap / capInText : 0.001f;
        float usable = width * textWidthFill;
        if (textWidth * scale > usable && textWidth > 0f)
        {
            scale = usable / textWidth;
        }

        text.rectTransform.sizeDelta = new Vector2(Mathf.Max(textWidth * 1.2f, 1f), Mathf.Max(h.y * 2f, 1f));
        text.transform.localScale = Vector3.one * scale;
        return capInText * scale;
    }

    // The sphere layout: the line under (or over) the presented pattern, kept
    // below the Earth view display, plus the placement offset.
    private Vector3 SphereLineCenter(Vector3 center, Vector3 right, Vector3 up, Vector3 toward, Vector2 half, float cap)
    {
        float lineHeight = placement == Placement.Below
            ? -(half.y + gap + textLift + cap)
            : half.y + gap;

        if (placement == Placement.Below && keepBelowDisplay != null)
        {
            // Lowest point of the disc along up, relative to center.
            Transform display = keepBelowDisplay.transform;
            Bounds local = keepBelowDisplay.localBounds;
            float radius = Mathf.Max(local.extents.x * display.lossyScale.x, local.extents.z * display.lossyScale.z);
            float tilt = Vector3.Dot(display.up, up);
            float displayBottom = Vector3.Dot(display.position - center, up)
                                  - radius * Mathf.Sqrt(Mathf.Max(0f, 1f - tilt * tilt));
            float blockTop = lineHeight + placementOffset.y + textLift + cap;
            float limit = displayBottom - gap;
            if (blockTop > limit)
            {
                lineHeight -= blockTop - limit;
            }
        }

        return center + up * (lineHeight + placementOffset.y) + right * placementOffset.x + toward * placementOffset.z;
    }

    // Live Inspector look settings.
    private void ApplyLook()
    {
        if (lineMaterial != null)
        {
            lineMaterial.SetColor(BaseColorId, lineColor);
            lineMaterial.SetFloat(DashFractionId, 1f);
            areaMaterial.SetColor(BaseColorId, new Color(lineColor.r, lineColor.g, lineColor.b, lineColor.a * 0.5f));
            areaMaterial.SetFloat(DashFractionId, 1f);
        }

        if (line != null)
        {
            line.widthMultiplier = lineThickness;
        }

        SetAreaVisible(showInteractionArea && state != State.Complete);

        if (nameText != null)
        {
            // Same face colour as the noticeboard signature, on the certificate and in the sphere.
            nameText.color = textColor;
        }

        if (skyText != null)
        {
            skyText.color = textColor;
        }

        if (penBody != null)
        {
            penBody.localPosition = new Vector3(0f, penLength * 0.5f, 0f);
            penBody.localScale = new Vector3(penThickness, penLength * 0.5f, penThickness);
        }

        if (penMaterial != null)
        {
            penMaterial.SetColor(BaseColorId, penColor);
        }
    }

    private void SetAreaVisible(bool visible)
    {
        if (areaFront != null)
        {
            areaFront.widthMultiplier = lineThickness * 0.6f;
            areaBack.widthMultiplier = lineThickness * 0.6f;
            areaFront.enabled = visible;
            areaBack.enabled = visible;
        }
    }

    // Clip the name at the revealed edge, in the text's own local space; the
    // sphere copy is clipped at the same fraction of its own letters.
    private void ApplyWipe()
    {
        if (nameText == null || textMaterial == null)
        {
            return;
        }

        bool complete = state == State.Complete;
        float edgeLocal = nameText.transform.InverseTransformPoint(lineLeft + frameRight * progress).x;
        float fraction = Clip(nameText, textMaterial, complete, false, edgeLocal, out _);

        if (skyText != null && skyMaterial != null)
        {
            Clip(skyText, skyMaterial, complete, true, fraction, out float skyEdgeLocal);
            Vector3 skyEdge = skyText.transform.TransformPoint(new Vector3(skyEdgeLocal, 0f, 0f));
            skyRevealFraction = Mathf.Clamp01(Vector3.Dot(skyEdge - skyLineLeft, skyRight) / Mathf.Max(1e-4f, signingWidth));
        }
    }

    // Sets the clip rect. byFraction: value is a fraction of the letters'
    // width; otherwise a local x. Returns the revealed fraction of the letters.
    private float Clip(TextMeshPro text, Material material, bool complete, bool byFraction, float value, out float edge)
    {
        Transform t = text.transform;
        float scale = Mathf.Max(1e-6f, t.lossyScale.x);
        float softness = wipeSoftness / scale;

        // Keep every clip value close to the text: TMP's mobile shader carries
        // the clip maths in 16-bit halves on Quest (max 65504), so "infinitely
        // far" edges saturate there and the clip silently stops working.
        Bounds bounds = text.textBounds;
        if (bounds.size.x <= 0f)
        {
            text.ForceMeshUpdate();
            bounds = text.textBounds;
        }

        float pad = Mathf.Max(bounds.size.y, 1f) + softness;
        float left = bounds.min.x - pad;
        float right = bounds.max.x + pad;
        edge = complete
            ? right
            : Mathf.Clamp(byFraction ? Mathf.LerpUnclamped(bounds.min.x, bounds.max.x, value) : value, left, right);

        material.SetVector(ClipRectId, new Vector4(left, bounds.min.y - pad, edge, bounds.max.y + pad));
        material.SetFloat(MaskSoftnessXId, softness);
        material.SetFloat(MaskSoftnessYId, 0f);
        return bounds.size.x > 0f ? Mathf.Clamp01((edge - bounds.min.x) / bounds.size.x) : 0f;
    }

    private void OnDrawGizmosSelected()
    {
        if (state == State.Off)
        {
            return;
        }

        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.8f);
        Vector3 center = lineLeft + frameRight * (Width * 0.5f) + frameToward * ((depthTowardPlayer - depthBehind) * 0.5f);
        Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.LookRotation(-frameToward, frameUp), Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(Width + 2f * alongTolerance, 2f * verticalTolerance, depthTowardPlayer + depthBehind));
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.9f);
        Gizmos.DrawWireSphere(lineLeft + frameRight * progress, startResumeTolerance);
    }
}
