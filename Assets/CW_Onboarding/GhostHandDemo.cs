using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction.HandGrab.Visuals;
using Oculus.Interaction.Input;
using UnityEngine;

/// <summary>
/// A faint ghost hand holding the signing pen slowly traces the signature line
/// to show the motion, then lifts away and fades; it loops with a pause until
/// the player starts tracing, and comes back after a period of inactivity.
///
/// Reuses the ISDK ghost hand (HandGhost + HandPuppet) posed with the same
/// recorded PenGrip the player's hand takes, and a copy of the SigningPen
/// prefab, both drawn with one translucent material. It never reveals the
/// name; an optional StarTrail at low intensity hints at the effect.
/// </summary>
[DisallowMultipleComponent]
public sealed class GhostHandDemo : MonoBehaviour
{
    [SerializeField]
    private SignatureReveal signature;

    [Tooltip("ISDK ghost hand prefab (OpenXRGhost-HandRight / -Left).")]
    [SerializeField]
    private GameObject ghostHandPrefab;

    [SerializeField]
    private Handedness handedness = Handedness.Right;

    [Tooltip("The recorded pen grip (same asset the player's hand uses).")]
    [SerializeField]
    private PenGripPose gripPose;

    [Tooltip("Pen the ghost holds (SigningPen). Empty = the ghost points with its hand only.")]
    [SerializeField]
    private GameObject penPrefab;

    [Tooltip("Translucent material for the ghost hand and pen (CelestialWorkshop/GhostHand).")]
    [SerializeField]
    private Material ghostMaterial;

    [Tooltip("Optional faint trail drawn while the ghost traces (never reveals the name).")]
    [SerializeField]
    private StarTrail demoTrail;

    [Header("Pose")]
    [Tooltip("Direction from the pen tip up its body, in the board frame (x = right, y = up, z = away from the player).")]
    [SerializeField]
    private Vector3 penAxis = new Vector3(0.35f, -0.45f, -0.8f);

    [Tooltip("Which side of the pen the hand sits on, in the board frame (the recorded grip decides how far).")]
    [SerializeField]
    private Vector3 handSide = new Vector3(0.6f, -0.8f, 0f);

    [Tooltip("Hover distance toward the player before and after the stroke (m).")]
    [SerializeField]
    [Min(0f)]
    private float hoverDistance = 0.035f;

    [Tooltip("Small up-and-down wave while tracing, like cursive (m).")]
    [SerializeField]
    [Min(0f)]
    private float waveHeight = 0.004f;

    [Header("Timing")]
    [Tooltip("Delay after the board appears (s).")]
    [SerializeField]
    [Min(0f)]
    private float startDelay = 1.2f;

    [SerializeField]
    [Min(0.1f)]
    private float strokeDuration = 2.6f;

    [SerializeField]
    [Min(0f)]
    private float approachDuration = 0.6f;

    [Tooltip("Pause between loops (s).")]
    [SerializeField]
    [Min(0f)]
    private float pauseDuration = 1.6f;

    [SerializeField]
    [Min(0.01f)]
    private float fadeDuration = 0.35f;

    [Tooltip("Return after the player has been idle this long (s). 0 = never return.")]
    [SerializeField]
    [Min(0f)]
    private float returnAfterIdle = 7f;

    [Tooltip("Peak opacity of the ghost.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float opacity = 0.55f;

    private static readonly int FadeId = Shader.PropertyToID("_Fade");

    private HandGhost ghost;
    private Transform penRoot;
    private Vector3 tipOffset;
    private Material materialInstance;
    private Coroutine loop;
    private float alpha;
    private bool running;

    public bool IsShowing => alpha > 0.001f;

    /// <summary>Starts looping (after the start delay) until the player signs.</summary>
    public void Play()
    {
        Stop(true);
        running = true;
        loop = StartCoroutine(Loop());
    }

    /// <summary>Stops and hides the ghost.</summary>
    public void Stop(bool immediate)
    {
        running = false;
        if (loop != null)
        {
            StopCoroutine(loop);
            loop = null;
        }

        if (immediate)
        {
            SetAlpha(0f);
            SetVisible(false);
        }
        else if (isActiveAndEnabled && alpha > 0f)
        {
            loop = StartCoroutine(FadeOutOnly());
        }

        if (demoTrail != null)
        {
            demoTrail.EndStroke(false);
        }
    }

    private void OnDisable()
    {
        Stop(true);
    }

    private void OnDestroy()
    {
        // The ghost hand and pen live unparented (real-world size even when
        // the board is scaled), so they are removed here.
        if (ghost != null)
        {
            Destroy(ghost.transform.root.gameObject);
        }

        if (penRoot != null)
        {
            Destroy(penRoot.gameObject);
        }

        if (materialInstance != null)
        {
            Destroy(materialInstance);
        }
    }

    // ---- Loop ----

    private bool PlayerActive()
    {
        if (signature == null)
        {
            return false;
        }

        SignatureReveal.State s = signature.CurrentState;
        if (s == SignatureReveal.State.Signing || s == SignatureReveal.State.Finishing || s == SignatureReveal.State.Complete || s == SignatureReveal.State.Off)
        {
            return true;
        }

        // Ready or paused: the player is "active" until idle long enough.
        return signature.Progress > 0f && (returnAfterIdle <= 0f || signature.IdleTime < returnAfterIdle);
    }

    private IEnumerator Loop()
    {
        EnsureBuilt();
        yield return new WaitForSeconds(startDelay);

        while (running)
        {
            // Wait while the player is (or recently was) tracing.
            while (PlayerActive())
            {
                if (signature != null && signature.CurrentState == SignatureReveal.State.Complete)
                {
                    running = false;
                    loop = null;
                    yield break;
                }

                yield return null;
            }

            yield return Demonstrate();
            if (!running)
            {
                break;
            }

            float until = Time.time + pauseDuration;
            while (Time.time < until && !PlayerActive())
            {
                yield return null;
            }
        }

        loop = null;
    }

    private IEnumerator Demonstrate()
    {
        if (signature == null)
        {
            yield break;
        }

        // Start at the revealed edge, so a returning demo shows where to resume.
        float from = Mathf.Clamp01(signature.Progress);
        float to = 1f;
        Transform frame = signature.transform;
        Vector3 toward = -frame.forward;
        SetVisible(true);

        // Approach: fade in hovering near the start, then touch down.
        float t = 0f;
        while (t < 1f)
        {
            if (PlayerActive()) { yield return Abort(); yield break; }
            t += Time.deltaTime / Mathf.Max(0.01f, approachDuration);
            float e = Smooth(Mathf.Clamp01(t));
            SetAlpha(Mathf.Clamp01(t * approachDuration / fadeDuration) * opacity);
            PoseAt(PointOn(from, 0f) + toward * (hoverDistance * (1f - e)));
            yield return null;
        }

        if (demoTrail != null)
        {
            demoTrail.BeginStroke(PointOn(from, 0f));
        }

        // Trace.
        float span = Mathf.Max(0.05f, to - from);
        float duration = strokeDuration * span;
        t = 0f;
        while (t < 1f)
        {
            if (PlayerActive()) { yield return Abort(); yield break; }
            t += Time.deltaTime / duration;
            float u = Mathf.Lerp(from, to, EaseInOut(Mathf.Clamp01(t)));
            Vector3 p = PointOn(u, Mathf.Sin(u * Mathf.PI * 9f) * waveHeight);
            PoseAt(p);
            if (demoTrail != null)
            {
                demoTrail.StrokeTo(p);
            }

            yield return null;
        }

        if (demoTrail != null)
        {
            demoTrail.EndStroke(false);
        }

        // Lift away and fade.
        t = 0f;
        Vector3 end = PointOn(to, 0f);
        while (t < 1f)
        {
            t += Time.deltaTime / fadeDuration;
            float e = Smooth(Mathf.Clamp01(t));
            SetAlpha((1f - e) * opacity);
            PoseAt(end + toward * (hoverDistance * e) + frame.right * (0.02f * e));
            yield return null;
        }

        SetAlpha(0f);
        SetVisible(false);
    }

    private IEnumerator Abort()
    {
        if (demoTrail != null)
        {
            demoTrail.EndStroke(false);
        }

        yield return FadeOutOnly();
    }

    private IEnumerator FadeOutOnly()
    {
        float start = alpha;
        float t = 0f;
        while (t < 1f && start > 0f)
        {
            t += Time.deltaTime / fadeDuration;
            SetAlpha(start * (1f - Mathf.Clamp01(t)));
            yield return null;
        }

        SetAlpha(0f);
        SetVisible(false);
    }

    private static float Smooth(float x) => x * x * (3f - 2f * x);

    private static float EaseInOut(float x) => x < 0.5f ? 2f * x * x : 1f - Mathf.Pow(-2f * x + 2f, 2f) * 0.5f;

    // Point on the signature line at fraction u, lifted along the board's up.
    private Vector3 PointOn(float u, float lift)
    {
        Vector3 a = signature.LineLeft;
        Vector3 b = signature.LineRight;
        Transform frame = signature.transform;
        return Vector3.Lerp(a, b, u) + frame.up * (0.008f + lift);
    }

    // ---- Posing ----

    /// <summary>Poses the ghost so the pen tip sits at this point (edit-mode previews use it too).</summary>
    public void PoseAt(Vector3 tip)
    {
        EnsureBuilt();
        Transform frame = signature != null ? signature.transform : transform;
        // Pen body (+Y) along penAxis; the recorded wrist sits on the pen's -Z side,
        // so -Z is turned toward handSide.
        Vector3 axis = frame.TransformDirection(penAxis.sqrMagnitude > 1e-6f ? penAxis.normalized : Vector3.back);
        Vector3 toHand = Vector3.ProjectOnPlane(frame.TransformDirection(handSide), axis);
        if (toHand.sqrMagnitude < 1e-6f)
        {
            toHand = Vector3.ProjectOnPlane(-frame.up, axis);
        }

        Quaternion penRotation = Quaternion.LookRotation(-toHand.normalized, axis);
        Vector3 penPosition = tip - penRotation * tipOffset;
        if (penRoot != null)
        {
            penRoot.SetPositionAndRotation(penPosition, penRotation);
        }

        if (ghost != null && gripPose != null && gripPose.For(handedness).valid)
        {
            PenGripPose.Side side = gripPose.For(handedness);
            var wrist = new Pose(penPosition + penRotation * side.wristPosition, penRotation * side.wristRotation);
            ghost.SetPose(side.handPose, wrist);
        }
        else if (ghost != null)
        {
            ghost.transform.SetPositionAndRotation(tip, penRotation);
        }
    }

    private void EnsureBuilt()
    {
        if (ghost != null || ghostHandPrefab == null)
        {
            return;
        }

        if (ghostMaterial != null && materialInstance == null)
        {
            materialInstance = new Material(ghostMaterial) { name = ghostMaterial.name + " (instance)" };
        }

        GameObject hand = Instantiate(ghostHandPrefab);
        hand.name = "Ghost Hand";
        ghost = hand.GetComponentInChildren<HandGhost>(true);
        ApplyMaterial(hand);

        if (penPrefab != null)
        {
            GameObject pen = Instantiate(penPrefab);
            pen.name = "Ghost Pen";
            foreach (Collider c in pen.GetComponentsInChildren<Collider>(true))
            {
                Destroy(c);
            }

            foreach (Rigidbody rb in pen.GetComponentsInChildren<Rigidbody>(true))
            {
                Destroy(rb);
            }

            ApplyMaterial(pen);
            penRoot = pen.transform;
            Transform tip = null;
            foreach (Transform child in penRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child != penRoot && child.name == "Tip")
                {
                    tip = child;
                }
            }

            tipOffset = tip != null ? Quaternion.Inverse(penRoot.rotation) * (tip.position - penRoot.position) : Vector3.zero;
        }

        SetAlpha(0f);
        SetVisible(false);
    }

    private readonly List<Renderer> renderers = new List<Renderer>();

    private void ApplyMaterial(GameObject root)
    {
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            renderers.Add(r);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingOrder = 15;
            if (materialInstance != null)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    mats[i] = materialInstance;
                }

                r.sharedMaterials = mats;
            }
        }
    }

    private void SetAlpha(float a)
    {
        alpha = a;
        if (materialInstance != null)
        {
            materialInstance.SetFloat(FadeId, a);
        }
    }

    private void SetVisible(bool visible)
    {
        foreach (Renderer r in renderers)
        {
            if (r != null)
            {
                r.enabled = visible;
            }
        }
    }
}
