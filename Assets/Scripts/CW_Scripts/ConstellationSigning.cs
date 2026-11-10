using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
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

    [Header("Pen (placeholder)")]
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
    public float Progress => signingWidth > 0f ? progress / signingWidth : 0f;
    public bool IsComplete => state == State.Complete;

    private const float HandSwitchHysteresis = 0.05f;
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

    /// <summary>The pen tip (null while no pen exists).</summary>
    public Transform PenTip => penTip;

    private void Awake()
    {
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
        foreach (Material material in new[] { lineMaterial, areaMaterial, textMaterial, penMaterial })
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
        if (signingEnabled)
        {
            BeginAttempt();
        }
    }

    // ---- Public controls ----

    /// <summary>Removes the signing visuals and progress, and re-arms completion.</summary>
    public void ClearSigning()
    {
        if (autoSign != null)
        {
            StopCoroutine(autoSign);
            autoSign = null;
        }

        if (signingRoot != null)
        {
            Destroy(signingRoot);
        }

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
        if (reveal == null ||
            !reveal.TryGetPresentation(out frameCenter, out frameRight, out frameUp, out frameToward, out frameHalf))
        {
            Debug.LogWarning("ConstellationSigning: no held presentation to sign under.", this);
            return;
        }

        string text = SignatureText();
        if (string.IsNullOrEmpty(text) || font == null)
        {
            Debug.LogWarning("ConstellationSigning: no name or no font; signing skipped.", this);
            return;
        }

        BuildVisuals(text);
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
            progress = Mathf.Lerp(start, signingWidth, Mathf.Clamp01(elapsed / duration));
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
            Layout();
        }

        ApplyLook();

        if (state != State.Complete && autoSign == null)
        {
            UpdateInteraction();
        }

        ApplyWipe();
    }

    private void UpdateInteraction()
    {
        float dt = Time.deltaTime;

        if (state == State.Signing)
        {
            bool tracked = TryGetTipPose(activeHand, out Pose tip);
            PoseSigningPen(tracked ? activeHand : null, tip);
            if (tracked && InsideArea(tip.position, out float along))
            {
                float target = Mathf.Clamp(along, 0f, signingWidth);
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

            if (!TryGetTipPose(hand, out Pose tip))
            {
                continue;
            }

            if (InsideArea(tip.position, out float along) && along <= progress + startResumeTolerance)
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

            float distance = Vector3.Distance(tip.position, resumePoint);
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
        if (state == State.Complete || progress < completionThreshold * signingWidth)
        {
            return false;
        }

        progress = signingWidth;
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
        return along >= -alongTolerance && along <= signingWidth + alongTolerance &&
               Mathf.Abs(height) <= verticalTolerance &&
               depth <= depthTowardPlayer && depth >= -depthBehind;
    }

    // ---- Hand and pen ----

    private bool HandUsable(IHand hand)
    {
        return hand != null && hand.IsTrackedDataValid && (!requireHighConfidence || hand.IsHighConfidence);
    }

    /// <summary>World pose of the pen tip on this hand (tip position, pen rotation).</summary>
    private bool TryGetTipPose(IHand hand, out Pose tip)
    {
        tip = default;
        if (!HandUsable(hand) || !hand.GetRootPose(out Pose wrist))
        {
            return false;
        }

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

        // Name text, with its own copy of the font material (clip rect on).
        var textObject = new GameObject("Signed Name") { layer = 0 };
        textObject.transform.SetParent(signingRoot.transform, false);
        nameText = textObject.AddComponent<TextMeshPro>();
        nameText.font = font;
        if (textMaterial == null || textMaterial.shader != font.material.shader)
        {
            if (textMaterial != null)
            {
                Destroy(textMaterial);
            }

            textMaterial = new Material(font.material) { name = font.name + " (signing)" };
        }

        textMaterial.CopyPropertiesFromMaterial(font.material);
        textMaterial.EnableKeyword("UNITY_UI_CLIP_RECT");
        nameText.fontSharedMaterial = textMaterial;
        nameText.extraPadding = true;
        nameText.text = text;
        nameText.fontSize = 10f;
        nameText.alignment = TextAlignmentOptions.Baseline;
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.overflowMode = TextOverflowModes.Overflow;
        if (textObject.TryGetComponent(out MeshRenderer textRenderer))
        {
            textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            textRenderer.receiveShadows = false;
        }

        // Placeholder pen: tip at the root's origin, body along +Y.
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

        Layout();
        ApplyLook();
        ApplyWipe();
    }

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

        // Fit: capital height maxCapHeight, shrunk to the usable width.
        nameText.transform.localScale = Vector3.one;
        Vector2 h = nameText.GetPreferredValues("H");
        float capInText = h.y * (font.faceInfo.capLine / Mathf.Max(1e-4f, font.faceInfo.lineHeight));
        float textWidth = nameText.GetPreferredValues(nameText.text).x;
        float scale = capInText > 0f ? maxCapHeight / capInText : 0.001f;
        float usable = signingWidth * textWidthFill;
        if (textWidth * scale > usable && textWidth > 0f)
        {
            scale = usable / textWidth;
        }

        fittedCapHeight = capInText * scale;
        nameText.rectTransform.sizeDelta = new Vector2(Mathf.Max(textWidth * 1.2f, 1f), Mathf.Max(h.y * 2f, 1f));
        nameText.transform.localScale = Vector3.one * scale;

        // Line height in the frame, measured from the pattern's center.
        float lineHeight = placement == Placement.Below
            ? -(frameHalf.y + gap + textLift + fittedCapHeight)
            : frameHalf.y + gap;
        Vector3 lineCenter = frameCenter +
                             frameUp * (lineHeight + placementOffset.y) +
                             frameRight * placementOffset.x +
                             frameToward * placementOffset.z;
        lineLeft = lineCenter - frameRight * (signingWidth * 0.5f);

        if (line != null)
        {
            line.SetPosition(0, lineLeft);
            line.SetPosition(1, lineLeft + frameRight * signingWidth);
        }

        // Text faces the player, reading along the line (local +X = right).
        nameText.transform.SetPositionAndRotation(lineCenter + frameUp * textLift, Quaternion.LookRotation(-frameToward, frameUp));

        if (areaFront != null)
        {
            Vector3 a = lineLeft - frameRight * alongTolerance;
            Vector3 b = lineLeft + frameRight * (signingWidth + alongTolerance);
            Vector3 v = frameUp * verticalTolerance;
            Vector3 front = frameToward * depthTowardPlayer;
            Vector3 back = -frameToward * depthBehind;
            areaFront.SetPositions(new[] { a - v + front, a + v + front, b + v + front, b - v + front });
            areaBack.SetPositions(new[] { a - v + back, a + v + back, b + v + back, b - v + back });
        }
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
            nameText.color = textColor;
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

    // Clip the name at the revealed edge, in the text's own local space.
    private void ApplyWipe()
    {
        if (nameText == null || textMaterial == null)
        {
            return;
        }

        Transform t = nameText.transform;
        float scale = Mathf.Max(1e-6f, t.lossyScale.x);
        const float Far = 100000f;
        float edge;
        if (state == State.Complete)
        {
            edge = Far;
        }
        else
        {
            edge = t.InverseTransformPoint(lineLeft + frameRight * progress).x;
        }

        textMaterial.SetVector(ClipRectId, new Vector4(-Far, -Far, edge, Far));
        textMaterial.SetFloat(MaskSoftnessXId, wipeSoftness / scale);
        textMaterial.SetFloat(MaskSoftnessYId, 0f);
    }

    private void OnDrawGizmosSelected()
    {
        if (state == State.Off)
        {
            return;
        }

        Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.8f);
        Vector3 center = lineLeft + frameRight * (signingWidth * 0.5f) + frameToward * ((depthTowardPlayer - depthBehind) * 0.5f);
        Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.LookRotation(-frameToward, frameUp), Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(signingWidth + 2f * alongTolerance, 2f * verticalTolerance, depthTowardPlayer + depthBehind));
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.9f);
        Gizmos.DrawWireSphere(lineLeft + frameRight * progress, startResumeTolerance);
    }
}
