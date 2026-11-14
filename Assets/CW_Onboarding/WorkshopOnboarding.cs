using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The workshop noticeboard shown at launch:
///   1. Waits for the head (and the sky volume) to be placed, then floats the
///      board in front of the player, all rules readable at once.
///   2. Arms the trace-to-reveal signature (SignatureReveal) and loops the ghost
///      hand demo until the player starts.
///   3. On completion: flourish, welcome message, a short hold, fade out, then
///      GameFlow continues to the title (or the constellation boxes).
/// GameFlow holds its Onboarding stage (title, boxes and puzzle hidden and not
/// interactable) while this runs.
///
/// Context menu: Replay Onboarding, Debug: Auto-Sign, Skip Onboarding.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorkshopOnboarding : MonoBehaviour
{
    public enum Then
    {
        ConstellationBoxes,
        Title
    }

    public enum Phase
    {
        Idle,
        WaitingToShow,
        Signing,
        Welcome,
        FadingOut,
        Done
    }

    [Header("Flow")]
    [Tooltip("Show the noticeboard every time the app launches.")]
    [SerializeField]
    private bool runOnLaunch = true;

    [Tooltip("Where the player goes after signing.")]
    [SerializeField]
    private Then thenOpen = Then.Title;

    [SerializeField]
    private GameFlow gameFlow;

    [Header("Placement")]
    [Tooltip("Player camera (CenterEyeAnchor).")]
    [SerializeField]
    private Camera viewer;

    [Tooltip("Optional: wait until the sky volume has been placed (it waits for head tracking and desk alignment).")]
    [SerializeField]
    private NearFieldSkyProxy skyProxy;

    [Tooltip("Optional: keep the board in front of this volume so they never intersect.")]
    [SerializeField]
    private CircularWindow volume;

    [Tooltip("Horizontal distance from the eyes to the board (m). Close enough to reach the signature.")]
    [SerializeField]
    [Min(0.2f)]
    private float distance = 0.55f;

    [Tooltip("How far below eye height the board's center sits (m).")]
    [SerializeField]
    private float heightBelowEyes = 0.12f;

    [Tooltip("Sideways offset (m, + = player's right).")]
    [SerializeField]
    private float sideOffset = 0f;

    [Tooltip("Extra tilt (degrees) on top of facing the eyes; + turns the face further up.")]
    [SerializeField]
    private float extraTilt = 0f;

    [Tooltip("Uniform board scale (1 = 0.47 x 0.52 m parchment).")]
    [SerializeField]
    [Min(0.3f)]
    private float boardScale = 1f;

    [Tooltip("Gap kept between the board and the volume's near surface (m).")]
    [SerializeField]
    [Min(0f)]
    private float volumeClearance = 0.05f;

    [Tooltip("Never closer than this, even to clear the volume (m).")]
    [SerializeField]
    [Min(0.2f)]
    private float minDistance = 0.4f;

    [Tooltip("Eye height above the rig origin used if head tracking hasn't started (m); the board moves to the real head once tracking arrives.")]
    [SerializeField]
    [Min(0.5f)]
    private float fallbackEyeHeight = 1.2f;

    [Tooltip("Before the player starts tracing, re-place the board if the head has moved this far from where it was placed (m), e.g. tracking arrived late or they stood up.")]
    [SerializeField]
    [Min(0.05f)]
    private float replaceIfHeadMoved = 0.3f;

    [Tooltip("Hidden while the board is up and restored afterwards (renderers under these roots), e.g. the sky globe.")]
    [SerializeField]
    private Transform[] hideWhileShowing = new Transform[0];

    [Tooltip("Show anyway after this long without placement (s).")]
    [SerializeField]
    [Min(0f)]
    private float maxWaitForPlacement = 4f;

    [Header("Board parts")]
    [Tooltip("Visual root of the board (hidden before and after).")]
    [SerializeField]
    private GameObject board;

    [SerializeField]
    private SignatureReveal signature;

    [SerializeField]
    private GhostHandDemo ghost;

    [SerializeField]
    private StarTrail trail;

    [SerializeField]
    private MetaDisplayName displayName;

    [Header("Agreement line")]
    [SerializeField]
    private TMP_Text agreementLead;

    [SerializeField]
    private TMP_Text agreementName;

    [SerializeField]
    private TMP_Text agreementTail;

    [Tooltip("Ink underline under the name (scaled to its width).")]
    [SerializeField]
    private Transform agreementUnderline;

    [Tooltip("Widest the agreement line may be (m).")]
    [SerializeField]
    [Min(0.05f)]
    private float agreementMaxWidth = 0.41f;

    [Tooltip("Space around the name (m).")]
    [SerializeField]
    [Min(0f)]
    private float agreementGap = 0.007f;

    [Header("Welcome")]
    [SerializeField]
    private TMP_Text welcomeText;

    [Tooltip("Hidden once signing completes (\"Trace here to enter\").")]
    [SerializeField]
    private GameObject traceHint;

    [Tooltip("{0} = the player's name.")]
    [SerializeField]
    private string welcomeFormat = "Welcome to the workshop, {0}!";

    [Tooltip("Widest the welcome line may be (m); longer names shrink it to fit beside the maker's mark.")]
    [SerializeField]
    [Min(0.05f)]
    private float welcomeMaxWidth = 0.3f;

    [Tooltip("Used instead when the account name wasn't available (empty = use the format with the fallback name).")]
    [SerializeField]
    private string genericWelcome = "";

    [Header("Timing (s)")]
    [SerializeField]
    [Min(0f)]
    private float fadeInDuration = 0.8f;

    [Tooltip("Wait up to this long for the account name before showing the board.")]
    [SerializeField]
    [Min(0f)]
    private float nameWaitBeforeShow = 1.5f;

    [SerializeField]
    [Min(0f)]
    private float welcomeDelay = 0.5f;

    [SerializeField]
    [Min(0f)]
    private float welcomeFadeIn = 0.6f;

    [Tooltip("How long the welcome stays before the board fades.")]
    [SerializeField]
    [Min(0f)]
    private float welcomeHold = 2.6f;

    [SerializeField]
    [Min(0.05f)]
    private float fadeOutDuration = 1.1f;

    [Tooltip("The board drifts back this far while fading out (m).")]
    [SerializeField]
    [Min(0f)]
    private float fadeOutDrift = 0.04f;

    [SerializeField]
    private bool logEvents = true;

    /// <summary>Global shader value the board's materials read: 0 = visible, 1 = gone.</summary>
    public const string FadeGlobal = "_CW_BoardFadeOut";

    private static readonly int FadeGlobalId = Shader.PropertyToID(FadeGlobal);

    public Phase CurrentPhase { get; private set; } = Phase.Idle;

    /// <summary>True while the board is up (GameFlow's Onboarding stage).</summary>
    public bool IsRunning => CurrentPhase != Phase.Idle && CurrentPhase != Phase.Done;

    /// <summary>Read by GameFlow in its Awake (serialized state only).</summary>
    public bool WillRunOnLaunch => runOnLaunch && enabled && gameObject.activeInHierarchy;

    public event Action Finished;

    private readonly List<TMP_Text> texts = new List<TMP_Text>();
    private readonly List<float> textAlpha = new List<float>();
    private Coroutine flow;
    private string playerName;
    private bool usedFallback;
    private bool signed;
    private bool placedTracked;
    private Vector3 placedEye;
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private readonly List<Renderer> rendererScratch = new List<Renderer>();

    private void Awake()
    {
        if (board != null)
        {
            board.GetComponentsInChildren(true, texts);
            foreach (TMP_Text t in texts)
            {
                textAlpha.Add(t.color.a);
            }

            board.SetActive(false);
        }

        SetBoardFade(0f);
    }

    private void OnEnable()
    {
        if (signature != null)
        {
            signature.Completed += OnSigned;
        }

        if (displayName != null)
        {
            displayName.Resolved += OnNameResolved;
        }
    }

    private void OnDisable()
    {
        if (signature != null)
        {
            signature.Completed -= OnSigned;
        }

        if (displayName != null)
        {
            displayName.Resolved -= OnNameResolved;
        }

        // Never leave the global fade set (it persists in the Editor after Play).
        Shader.SetGlobalFloat(FadeGlobalId, 0f);
        RestoreHidden();
    }

    private void Update()
    {
        // Late head tracking (the first frames report the rig origin on the
        // floor) or a big move before tracing: bring the board back in front.
        if (CurrentPhase != Phase.Signing || signature == null ||
            signature.CurrentState != SignatureReveal.State.Ready || signature.Progress > 0f)
        {
            return;
        }

        bool tracked = IsHeadTracked();
        Camera cam = viewer != null ? viewer : Camera.main;
        if (cam == null || !tracked)
        {
            return;
        }

        if (!placedTracked || Vector3.Distance(cam.transform.position, placedEye) > replaceIfHeadMoved)
        {
            Place();
            Log("head moved before signing; board re-placed.");
        }
    }

    private void Start()
    {
        if (runOnLaunch)
        {
            flow = StartCoroutine(Run());
        }
    }

    // ---- Controls ----

    [ContextMenu("Replay Onboarding")]
    public void Replay()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("WorkshopOnboarding: replay works in Play mode.", this);
            return;
        }

        StopFlow();
        if (signature != null)
        {
            signature.ResetSignature();
        }

        if (ghost != null)
        {
            ghost.Stop(true);
        }

        if (trail != null)
        {
            trail.Clear();
        }

        if (gameFlow != null)
        {
            gameFlow.EnterOnboarding();
        }

        flow = StartCoroutine(Run());
    }

    [ContextMenu("Debug: Auto-Sign")]
    public void DebugAutoSign()
    {
        if (signature != null)
        {
            signature.DebugAutoSign();
        }
    }

    [ContextMenu("Skip Onboarding")]
    public void Skip()
    {
        if (!IsRunning)
        {
            return;
        }

        StopFlow();
        Finish();
    }

    // ---- Flow ----

    private IEnumerator Run()
    {
        CurrentPhase = Phase.WaitingToShow;
        signed = false;
        if (board != null)
        {
            board.SetActive(false);
        }

        HideOthers();

        // Placement: after head tracking and the sky volume (desk alignment).
        float giveUpAt = Time.realtimeSinceStartup + maxWaitForPlacement;
        while ((skyProxy != null && !skyProxy.IsPlaced || !IsHeadTracked()) && Time.realtimeSinceStartup < giveUpAt)
        {
            yield return null;
        }

        // A moment for the account name, never more than nameWaitBeforeShow.
        float nameWait = 0f;
        while (displayName != null && !displayName.IsDone && nameWait < nameWaitBeforeShow)
        {
            nameWait += Time.unscaledDeltaTime;
            yield return null;
        }

        ApplyName(CurrentName());
        Place();
        ResetVisuals();
        if (board != null)
        {
            board.SetActive(true);
        }

        if (signature != null)
        {
            signature.SetName(playerName);
            signature.Arm();
        }

        CurrentPhase = Phase.Signing;
        Log($"noticeboard shown for \"{playerName}\"{(usedFallback ? " (fallback name)" : "")}.");

        if (ghost != null)
        {
            ghost.Play();
        }

#if UNITY_EDITOR
        // Editor self-test without hands: SessionState "CW_Onboarding_AutoSign" = seconds (0 = off).
        float autoSign = UnityEditor.SessionState.GetFloat(EditorAutoSignKey, 0f);
        if (autoSign > 0f)
        {
            StartCoroutine(EditorAutoSign(autoSign));
        }
#endif

        yield return Fade(1f, 0f, fadeInDuration, 0f);

        while (!signed)
        {
            yield return null;
        }

        // Signed: flourish (from SignatureReveal's trail), welcome, hold, fade.
        CurrentPhase = Phase.Welcome;
        Log($"signed; welcome (frame {Time.frameCount}, t {Time.time:0.0}).");
        if (ghost != null)
        {
            ghost.Stop(false);
        }

        yield return FadeText(traceHint, 0.35f);
        yield return new WaitForSeconds(welcomeDelay);
        if (welcomeText != null)
        {
            welcomeText.text = WelcomeLine();
            FitWelcome();
            welcomeText.gameObject.SetActive(true);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, welcomeFadeIn);
                SetTextAlpha(welcomeText, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
        }

        yield return new WaitForSeconds(welcomeHold);

        CurrentPhase = Phase.FadingOut;
        yield return Fade(0f, 1f, fadeOutDuration, fadeOutDrift);
        flow = null;
        Finish();
    }

#if UNITY_EDITOR
    public const string EditorAutoSignKey = "CW_Onboarding_AutoSign";

    private IEnumerator EditorAutoSign(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        Log($"editor self-test: auto-signing (frame {Time.frameCount}, t {Time.time:0.0}, ghost showing {(ghost != null && ghost.IsShowing)}).");
        DebugAutoSign();
    }
#endif

    private void Finish()
    {
        if (signature != null)
        {
            signature.Disarm();
        }

        if (ghost != null)
        {
            ghost.Stop(true);
        }

        if (trail != null)
        {
            trail.Clear();
        }

        if (board != null)
        {
            board.SetActive(false);
        }

        SetBoardFade(0f);
        RestoreHidden();
        CurrentPhase = Phase.Done;
        Log("onboarding complete.");
        if (gameFlow != null)
        {
            gameFlow.FinishOnboarding(thenOpen == Then.Title);
        }

        Finished?.Invoke();
    }

    private void StopFlow()
    {
        if (flow != null)
        {
            StopCoroutine(flow);
            flow = null;
        }
    }

    private void OnSigned()
    {
        if (CurrentPhase == Phase.Signing)
        {
            signed = true;
        }
    }

    // ---- Name ----

    private string CurrentName()
    {
        if (displayName == null)
        {
            usedFallback = true;
            return "Sky Crafter";
        }

        usedFallback = displayName.CurrentStatus != MetaDisplayName.Status.Resolved;
        return displayName.Name;
    }

    private void OnNameResolved(string name)
    {
        // A late real name replaces the fallback only before any tracing.
        if (CurrentPhase == Phase.Signing && signature != null &&
            signature.CurrentState == SignatureReveal.State.Ready && signature.Progress <= 0f &&
            displayName.CurrentStatus == MetaDisplayName.Status.Resolved && name != playerName)
        {
            usedFallback = false;
            ApplyName(name);
            signature.SetName(name);
            Log("account name arrived late; board updated.");
        }
    }

    private void ApplyName(string name)
    {
        playerName = string.IsNullOrWhiteSpace(name) ? "Sky Crafter" : name.Trim();
        LayoutAgreement();
    }

    private string WelcomeLine()
    {
        if (usedFallback && !string.IsNullOrEmpty(genericWelcome))
        {
            return genericWelcome;
        }

        return string.Format(welcomeFormat, playerName);
    }

    // "I, ____Name____, agree to follow the rules of the Celestial Workshop."
    // Three texts laid out on one centered line; the whole line shrinks if a
    // long name would make it too wide.
    private void LayoutAgreement()
    {
        if (agreementLead == null || agreementName == null || agreementTail == null)
        {
            return;
        }

        agreementName.text = playerName;
        float wLead = Width(agreementLead);
        float wName = Width(agreementName);
        float wTail = Width(agreementTail);
        float total = wLead + agreementGap + wName + agreementGap + wTail;
        float s = total > agreementMaxWidth ? agreementMaxWidth / total : 1f;
        float x = -total * 0.5f;
        Place(agreementLead, ref x, wLead, s);
        x += agreementGap;
        float nameCenter = (x + wName * 0.5f) * s;
        Place(agreementName, ref x, wName, s);
        x += agreementGap;
        Place(agreementTail, ref x, wTail, s);

        if (agreementUnderline != null)
        {
            Vector3 p = agreementUnderline.localPosition;
            agreementUnderline.localPosition = new Vector3(nameCenter, p.y, p.z);
            Vector3 scale = agreementUnderline.localScale;
            agreementUnderline.localScale = new Vector3((wName + agreementGap * 2f) * s, scale.y, scale.z);
        }
    }

    // Texts are authored at scale 1 (the builder sets font sizes in metres).
    private static float Width(TMP_Text text)
    {
        text.transform.localScale = Vector3.one;
        return text.GetPreferredValues(text.text).x;
    }

    private static void Place(TMP_Text text, ref float x, float width, float s)
    {
        RectTransform rt = text.rectTransform;
        rt.sizeDelta = new Vector2(width + 0.002f, rt.sizeDelta.y);
        Vector3 p = rt.localPosition;
        rt.localPosition = new Vector3((x + width * 0.5f) * s, p.y, p.z);
        text.transform.localScale = Vector3.one * s;
        x += width;
    }

    // ---- Placement ----

    [ContextMenu("Place In Front Of Viewer")]
    private void Place()
    {
        Camera cam = viewer != null ? viewer : Camera.main;
        if (cam == null)
        {
            return;
        }

        // Before tracking the eye sits at the rig origin, which is on the floor.
        placedTracked = IsHeadTracked() || !Application.isPlaying;
        Vector3 eye = cam.transform.position;
        if (!placedTracked)
        {
            OVRCameraRig rig = cam.GetComponentInParent<OVRCameraRig>();
            Vector3 origin = rig != null ? rig.trackingSpace.position : eye;
            eye = new Vector3(eye.x, origin.y + fallbackEyeHeight, eye.z);
        }

        placedEye = cam.transform.position;
        Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        float d = distance;
        if (volume != null)
        {
            Vector3 toVolume = volume.transform.position - eye;
            float along = Vector3.Dot(toVolume, forward);
            float lateral = Mathf.Abs(Vector3.Dot(toVolume, right) - sideOffset);
            float halfWidth = 0.27f * boardScale;
            if (along > 0f && lateral < volume.Radius + halfWidth)
            {
                float near = along - volume.Radius - volumeClearance;
                if (near < d)
                {
                    d = Mathf.Max(minDistance, near);
                    Log($"board brought to {d:0.00} m to stay in front of the globe.");
                }
            }
        }

        // Pulled in to clear the globe: shrink by the same ratio, so the board
        // keeps its angular size (text stays as readable, the board stays
        // inside the glasses' design area) and the signature gets closer.
        float k = d / Mathf.Max(0.01f, distance);
        Vector3 position = eye + forward * d + right * (sideOffset * k) + Vector3.down * (heightBelowEyes * k);
        // Face the eyes (the board's +Z points away from the player).
        Quaternion rotation = Quaternion.LookRotation(position - eye, Vector3.up) * Quaternion.Euler(extraTilt, 0f, 0f);
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = Vector3.one * (boardScale * k);
    }

    private static bool IsHeadTracked()
    {
        return OVRManager.tracker != null && OVRManager.tracker.isPositionTracked;
    }

    // ---- Hiding the rest of the workshop ----

    private void HideOthers()
    {
        if (hideWhileShowing == null)
        {
            return;
        }

        foreach (Transform root in hideWhileShowing)
        {
            if (root == null)
            {
                continue;
            }

            root.GetComponentsInChildren(true, rendererScratch);
            foreach (Renderer r in rendererScratch)
            {
                if (r.enabled)
                {
                    r.enabled = false;
                    hiddenRenderers.Add(r);
                }
            }
        }

        rendererScratch.Clear();
        if (hiddenRenderers.Count > 0)
        {
            Log($"hid {hiddenRenderers.Count} renderer(s) until signed.");
        }
    }

    private void RestoreHidden()
    {
        if (hiddenRenderers.Count > 0)
        {
            Log($"restored {hiddenRenderers.Count} hidden renderer(s).");
        }

        foreach (Renderer r in hiddenRenderers)
        {
            if (r != null)
            {
                r.enabled = true;
            }
        }

        hiddenRenderers.Clear();
    }

    // The welcome shares the footer with the maker's mark: shrink a long line to fit.
    private void FitWelcome()
    {
        welcomeText.transform.localScale = Vector3.one;
        float w = welcomeText.GetPreferredValues(welcomeText.text).x;
        if (w > welcomeMaxWidth)
        {
            welcomeText.transform.localScale = Vector3.one * (welcomeMaxWidth / w);
        }
    }

    // ---- Fading ----

    private void ResetVisuals()
    {
        SetBoardFade(0f);
        for (int i = 0; i < texts.Count; i++)
        {
            if (texts[i] != null)
            {
                SetTextAlpha(texts[i], 1f);
            }
        }

        if (traceHint != null)
        {
            traceHint.SetActive(true);
        }

        if (welcomeText != null)
        {
            SetTextAlpha(welcomeText, 0f);
            welcomeText.gameObject.SetActive(false);
        }

        if (board != null)
        {
            board.transform.localPosition = Vector3.zero;
        }
    }

    private IEnumerator Fade(float from, float to, float duration, float drift)
    {
        Vector3 start = board != null ? board.transform.localPosition : Vector3.zero;
        float t = 0f;
        while (t < 1f)
        {
            t = duration > 0f ? Mathf.Min(1f, t + Time.deltaTime / duration) : 1f;
            float e = Mathf.SmoothStep(0f, 1f, t);
            float f = Mathf.Lerp(from, to, e);
            SetBoardFade(f);
            if (board != null)
            {
                // Fade in rises gently into place; fade out drifts away from the player.
                board.transform.localPosition = to < from
                    ? start + Vector3.down * (0.015f * (1f - e))
                    : start + Vector3.forward * (drift * e);
            }

            yield return null;
        }
    }

    private IEnumerator FadeText(GameObject go, float duration)
    {
        if (go == null)
        {
            yield break;
        }

        var list = new List<TMP_Text>(go.GetComponentsInChildren<TMP_Text>(true));
        var renderers = new List<Renderer>(go.GetComponentsInChildren<Renderer>(true));
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            foreach (TMP_Text text in list)
            {
                SetTextAlpha(text, 1f - Mathf.Clamp01(t));
            }

            yield return null;
        }

        go.SetActive(false);
    }

    private void SetBoardFade(float fadeOut)
    {
        Shader.SetGlobalFloat(FadeGlobalId, fadeOut);
        for (int i = 0; i < texts.Count; i++)
        {
            TMP_Text text = texts[i];
            if (text == null || text == welcomeText && CurrentPhase != Phase.FadingOut)
            {
                continue;
            }

            Color c = text.color;
            c.a = textAlpha[i] * (1f - fadeOut);
            text.color = c;
        }
    }

    private static void SetTextAlpha(TMP_Text text, float a)
    {
        Color c = text.color;
        c.a = a;
        text.color = c;
    }

    private void Log(string message)
    {
        if (logEvents)
        {
            Debug.Log("WorkshopOnboarding: " + message, this);
        }
    }
}
