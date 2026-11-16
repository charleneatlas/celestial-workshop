using System.Collections;
using UnityEngine;

/// <summary>
/// The Celestial Workshop Guild certificate: a small parchment on a wooden
/// writing board that takes the place of the solved miniature at the
/// workstation. ConstellationSigning signs the constellation's name on its
/// central line (the existing pen, gesture and reveal, just relocated here),
/// then the guild's wax seal is pressed on, the certificate is held briefly
/// and it goes away. Built by Tools > Celestial Workshop > Build Guild Certificate.
///
/// Frame: the board is built in quad space (+X right, +Y up, its face toward
/// -Z), so this object's -Z faces the player. Placed when presented, from the
/// workstation's position and the player's eyes, never every frame.
///
/// Lifecycle: Present (ConstellationSigning, when its attempt begins) ->
/// Signed: seal -> hold -> exit. A puzzle reset or leaving the puzzle hides
/// it at once.
/// </summary>
[DisallowMultipleComponent]
public sealed class GuildCertificate : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The certificate appears at this workstation. Found automatically if empty.")]
    [SerializeField]
    private WorkstationMini workstation;

    [Tooltip("Viewer camera (CenterEyeAnchor): the board faces it. Found automatically (main camera) if empty.")]
    [SerializeField]
    private Camera viewer;

    [Tooltip("Found automatically if empty.")]
    [SerializeField]
    private ConstellationSigning signing;

    [Tooltip("Found automatically if empty.")]
    [SerializeField]
    private GameFlow gameFlow;

    [Tooltip("Found automatically if empty.")]
    [SerializeField]
    private ConstellationSolveController solveController;

    [Header("Parts (set by the builder)")]
    [Tooltip("Scaled in and out; hidden while the certificate is away.")]
    [SerializeField]
    private Transform visual;

    [Tooltip("The name line: origin at the line's center on the baseline, +X along the line, facing -Z.")]
    [SerializeField]
    private Transform nameLine;

    [Tooltip("The wax seal (pops in after signing).")]
    [SerializeField]
    private Transform seal;

    [Header("Use")]
    [Tooltip("Practice letters are signed on the certificate too. Off = they sign in the sphere as before.")]
    [SerializeField]
    private bool useForPractice = true;

    [Header("Placement (tune in the headset)")]
    [Tooltip("Offset from the workstation's center (its frame: x right, y up, z away from the player), metres.")]
    [SerializeField]
    private Vector3 offset = new Vector3(0f, 0.01f, 0f);

    [Tooltip("Face the eyes (a lectern tilted toward them). Off = upright.")]
    [SerializeField]
    private bool faceEyes = true;

    [Tooltip("Extra tilt (degrees): positive leans the top of the board away, like a desk.")]
    [SerializeField]
    [Range(-30f, 45f)]
    private float extraTilt = 8f;

    [Tooltip("Uniform scale of the whole certificate.")]
    [SerializeField]
    [Range(0.5f, 1.6f)]
    private float scale = 1f;

    [Tooltip("Eye height above the rig when the head isn't tracked (Editor without a headset).")]
    [SerializeField]
    private float fallbackEyeHeight = 1.2f;

    [Header("Signing")]
    [Tooltip("Length of the signing line (m, at scale 1).")]
    [SerializeField]
    [Min(0.03f)]
    private float lineWidth = 0.15f;

    [Tooltip("Height of the name's capitals (m, at scale 1). Long names shrink to fit.")]
    [SerializeField]
    [Min(0.003f)]
    private float nameCapHeight = 0.013f;

    [Tooltip("Ink of the signed name.")]
    [SerializeField]
    private Color inkColor = new Color(0.20f, 0.11f, 0.33f, 1f);

    [Header("Timing (s)")]
    [SerializeField]
    [Min(0.01f)]
    private float appearDuration = 0.45f;

    [Tooltip("After the signature completes, before the seal is pressed.")]
    [SerializeField]
    [Min(0f)]
    private float sealDelay = 0.35f;

    [SerializeField]
    [Min(0.01f)]
    private float sealDuration = 0.4f;

    [Tooltip("The finished, sealed certificate stays this long.")]
    [SerializeField]
    [Min(0f)]
    private float holdAfterSeal = 2.5f;

    [SerializeField]
    [Min(0.01f)]
    private float exitDuration = 0.5f;

    [SerializeField]
    private bool logEvents = true;

    /// <summary>True from Present until it has gone (or was hidden).</summary>
    public bool IsShown { get; private set; }

    /// <summary>Signed name ink.</summary>
    public Color InkColor => inkColor;

    /// <summary>The name line (signing visuals are parented here so they leave with the certificate).</summary>
    public Transform NameLine => nameLine;

    private Coroutine routine;
    private Vector3 sealScale = Vector3.one;
    private bool subscribed;

    private void Awake()
    {
        if (workstation == null)
        {
            workstation = FindFirstObjectByType<WorkstationMini>();
        }

        if (signing == null)
        {
            signing = FindFirstObjectByType<ConstellationSigning>();
        }

        if (gameFlow == null)
        {
            gameFlow = FindFirstObjectByType<GameFlow>();
        }

        if (solveController == null)
        {
            solveController = FindFirstObjectByType<ConstellationSolveController>();
        }

        if (seal != null)
        {
            sealScale = seal.localScale;
        }

        HideNow();
    }

    private void OnEnable()
    {
        if (subscribed)
        {
            return;
        }

        if (signing != null)
        {
            signing.Signed += HandleSigned;
        }

        if (solveController != null)
        {
            solveController.PuzzleReset += HideNow;
        }

        if (gameFlow != null)
        {
            gameFlow.StageChanged += HandleStage;
        }

        subscribed = true;
    }

    private void OnDisable()
    {
        if (signing != null)
        {
            signing.Signed -= HandleSigned;
        }

        if (solveController != null)
        {
            solveController.PuzzleReset -= HideNow;
        }

        if (gameFlow != null)
        {
            gameFlow.StageChanged -= HandleStage;
        }

        subscribed = false;
        HideNow();
    }

    /// <summary>Whether the current puzzle signs here (always, unless it's practice and practice is off).</summary>
    public bool ShouldUse =>
        isActiveAndEnabled && visual != null && nameLine != null &&
        (useForPractice || gameFlow == null || !gameFlow.IsPractice);

    /// <summary>
    /// Places the certificate at the workstation, full size, so the signing
    /// frame (TryGetSigningFrame) is valid and the name can be attached. Call
    /// BeginAppear afterwards to bring it in.
    /// </summary>
    public void Present()
    {
        StopRoutine();
        Place();
        visual.gameObject.SetActive(true);
        visual.localScale = Vector3.one;
        if (seal != null)
        {
            seal.localScale = Vector3.zero;
            seal.gameObject.SetActive(false);
        }

        IsShown = true;
        Log("presented.");
    }

    /// <summary>Grows the presented certificate in (with the name attached to it).</summary>
    public void BeginAppear()
    {
        if (!IsShown)
        {
            return;
        }

        StopRoutine();
        visual.localScale = Vector3.zero;
        routine = StartCoroutine(ScaleVisual(0f, 1f, appearDuration, true, null));
    }

    /// <summary>
    /// The signing frame: the line's center on the baseline, its direction,
    /// up on the paper and toward the player; line width and name cap height
    /// in world metres.
    /// </summary>
    public bool TryGetSigningFrame(out Vector3 center, out Vector3 right, out Vector3 up, out Vector3 toward,
        out float width, out float capHeight)
    {
        if (nameLine == null)
        {
            center = right = up = toward = default;
            width = capHeight = 0f;
            return false;
        }

        float s = transform.lossyScale.x;
        center = nameLine.position;
        right = nameLine.right;
        up = nameLine.up;
        toward = -nameLine.forward;
        width = lineWidth * s;
        capHeight = nameCapHeight * s;
        return true;
    }

    [ContextMenu("Hide Now")]
    public void HideNow()
    {
        StopRoutine();
        IsShown = false;
        if (visual != null)
        {
            visual.gameObject.SetActive(false);
        }
    }

    [ContextMenu("Preview: Present")]
    private void PreviewPresent()
    {
        if (Application.isPlaying)
        {
            Present();
            BeginAppear();
        }
    }

    [ContextMenu("Preview: Seal")]
    private void PreviewSeal()
    {
        if (Application.isPlaying)
        {
            HandleSigned();
        }
    }

    private void HandleStage(GameFlow.Stage stage)
    {
        if (stage != GameFlow.Stage.Puzzle)
        {
            HideNow();
        }
    }

    private void HandleSigned()
    {
        if (!IsShown)
        {
            return;
        }

        StopRoutine();
        routine = StartCoroutine(SealAndExit());
    }

    private IEnumerator SealAndExit()
    {
        visual.localScale = Vector3.one;
        yield return new WaitForSeconds(sealDelay);

        if (seal != null)
        {
            seal.gameObject.SetActive(true);
            float t = 0f;
            while (t < sealDuration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / sealDuration);
                // Pressed on: starts a little large and settles with a small overshoot.
                float k = u < 0.6f
                    ? Mathf.Lerp(1.6f, 0.92f, EaseIn(u / 0.6f))
                    : Mathf.Lerp(0.92f, 1f, EaseOut((u - 0.6f) / 0.4f));
                seal.localScale = sealScale * k;
                yield return null;
            }

            seal.localScale = sealScale;
            Log("sealed.");
        }

        yield return new WaitForSeconds(holdAfterSeal);
        yield return ScaleVisual(1f, 0f, exitDuration, false, null);
        visual.gameObject.SetActive(false);
        IsShown = false;
        routine = null;
        Log("gone.");
    }

    private IEnumerator ScaleVisual(float from, float to, float duration, bool overshoot, System.Action done)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            float k = overshoot ? EaseOutBack(u) : u * u * (3f - 2f * u);
            visual.localScale = Vector3.one * Mathf.LerpUnclamped(from, to, k);
            yield return null;
        }

        visual.localScale = Vector3.one * to;
        done?.Invoke();
    }

    private void Place()
    {
        Transform station = workstation != null ? workstation.transform : transform.parent;
        Vector3 basePosition = station != null ? station.position : transform.position;
        Quaternion baseRotation = station != null ? station.rotation : transform.rotation;
        Vector3 position = basePosition + baseRotation * offset;

        Vector3 eye = EyePosition(position);
        Vector3 away = position - eye;
        Vector3 flat = Vector3.ProjectOnPlane(away, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f)
        {
            flat = baseRotation * Vector3.forward;
        }

        Vector3 look = faceEyes && away.sqrMagnitude > 1e-6f ? away.normalized : flat.normalized;
        Quaternion rotation = Quaternion.LookRotation(look, Vector3.up) * Quaternion.Euler(extraTilt, 0f, 0f);
        transform.SetPositionAndRotation(position, rotation);

        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = Vector3.one * (scale / Mathf.Max(1e-4f, parentScale.x));
    }

    private Vector3 EyePosition(Vector3 fallbackFrom)
    {
        Camera cam = viewer != null ? viewer : Camera.main;
        if (cam == null)
        {
            return fallbackFrom - Vector3.forward;
        }

        bool tracked = OVRManager.tracker != null && OVRManager.tracker.isPositionTracked;
        OVRCameraRig rig = cam.GetComponentInParent<OVRCameraRig>();
        return tracked || rig == null
            ? cam.transform.position
            : rig.transform.position + Vector3.up * fallbackEyeHeight;
    }

    private void StopRoutine()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
    }

    private static float EaseIn(float u) => u * u;

    private static float EaseOut(float u) => 1f - (1f - u) * (1f - u);

    private static float EaseOutBack(float u)
    {
        const float c1 = 1.4f;
        const float c3 = c1 + 1f;
        float v = u - 1f;
        return 1f + c3 * v * v * v + c1 * v * v;
    }

    private void Log(string message)
    {
        if (logEvents)
        {
            Debug.Log("GuildCertificate: " + message, this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (nameLine == null)
        {
            return;
        }

        float s = transform.lossyScale.x;
        Gizmos.color = new Color(0.6f, 0.4f, 1f, 0.8f);
        Gizmos.DrawLine(nameLine.position - nameLine.right * lineWidth * 0.5f * s, nameLine.position + nameLine.right * lineWidth * 0.5f * s);
    }
}
