using System.Collections.Generic;
using CW.Guidance;
using UnityEngine;

/// <summary>
/// First-time demonstration for the Star Chart: while the guidance "chart"
/// step is up, a translucent ghost hand (the old blueprint's pointing hand, in
/// the rotate demo's GhostHand style) reaches toward the chart, gently taps
/// the star nearest the player with its index fingertip, eases back, fades,
/// pauses and repeats. It stops for good when the step completes (the first
/// chart star touch) and steps aside while the player's finger is near.
///
/// The hand is a static pointing pose (baked into its bones); only its root
/// moves, once per frame while shown. Nothing runs while the step is closed.
/// </summary>
public sealed class ChartGhostHand : MonoBehaviour
{
    [SerializeField]
    private GuidanceDirector director;

    [Tooltip("The Star Chart (its active targets are the stars it can tap).")]
    [SerializeField]
    private StarChartLayout chart;

    [Tooltip("Pointing hand prefab (built from the old blueprint's tutorial hand by Tools > Celestial Workshop > Hook Up Chart Ghost Hand).")]
    [SerializeField]
    private GameObject pointingHandPrefab;

    [Tooltip("GhostHand material (the rotate demo's look); a runtime copy is faded through _Fade.")]
    [SerializeField]
    private Material ghostMaterial;

    [Tooltip("Player camera (CenterEyeAnchor). Defaults to the main camera.")]
    [SerializeField]
    private Transform viewer;

    [Tooltip("Guidance step that shows the demo.")]
    [SerializeField]
    private string stepId = "chart";

    [Header("Pose")]
    [Tooltip("Angle of the pointing finger above the paper (degrees). Low keeps the forearm along the chart, so the open end of the ghost wrist never faces the player.")]
    [SerializeField]
    [Range(5f, 80f)]
    private float fingerElevation = 30f;

    [Tooltip("Where the forearm comes from, as seen on the chart: 0 = straight up from below, positive = from the player's left (degrees).")]
    [SerializeField]
    [Range(-90f, 90f)]
    private float approachFromLeft = 35f;

    [Header("Tap (metres above the paper)")]
    [SerializeField]
    [Min(0f)]
    private float startHeight = 0.06f;

    [SerializeField]
    [Min(0f)]
    private float hoverHeight = 0.015f;

    [Tooltip("Closest the fingertip gets: just at the paper, never through it.")]
    [SerializeField]
    [Min(0f)]
    private float contactHeight = 0.002f;

    [Header("Timing (seconds)")]
    [SerializeField]
    [Min(0f)]
    private float startDelay = 1.2f;

    [SerializeField]
    [Min(0.05f)]
    private float approachDuration = 0.6f;

    [SerializeField]
    [Min(0.05f)]
    private float tapDownDuration = 0.35f;

    [SerializeField]
    [Min(0f)]
    private float tapHoldDuration = 0.2f;

    [SerializeField]
    [Min(0.05f)]
    private float tapUpDuration = 0.35f;

    [SerializeField]
    [Min(0.05f)]
    private float leaveDuration = 0.5f;

    [SerializeField]
    [Min(0f)]
    private float pauseDuration = 1.8f;

    [Header("Look")]
    [SerializeField]
    [Range(0f, 1f)]
    private float opacity = 0.5f;

    [Tooltip("Hide while one of the player's index fingertips is this close to the chart's centre (m), and wait Return After before showing again. 0 = never.")]
    [SerializeField]
    [Min(0f)]
    private float stepAsideDistance = 0.15f;

    [SerializeField]
    [Min(0f)]
    private float returnAfter = 4f;

    private static readonly int FadeId = Shader.PropertyToID("_Fade");

    private GameObject hand;
    private Material materialInstance;
    private readonly List<Renderer> renderers = new List<Renderer>();
    private Transform indexTip;
    private Quaternion handFrameInRoot = Quaternion.identity;   // pointing finger / back of hand, relative to the root

    private bool stepOpen;
    private bool done;
    private float cycleStart;
    private float resumeAt;
    private float alpha = -1f;
    private BlueprintStar target;
    private Transform[] playerTips;

    private void Awake()
    {
        if (director == null)
        {
            director = FindFirstObjectByType<GuidanceDirector>(FindObjectsInactive.Include);
        }

        if (chart == null)
        {
            chart = FindFirstObjectByType<StarChartLayout>(FindObjectsInactive.Include);
        }
    }

    private void OnEnable()
    {
        if (director != null)
        {
            director.StepShown += OnStepShown;
            director.StepCompleted += OnStepCompleted;
        }
    }

    private void OnDisable()
    {
        if (director != null)
        {
            director.StepShown -= OnStepShown;
            director.StepCompleted -= OnStepCompleted;
        }

        SetAlpha(0f);
    }

    private void OnDestroy()
    {
        if (hand != null)
        {
            Destroy(hand);
        }

        if (materialInstance != null)
        {
            Destroy(materialInstance);
        }
    }

    private void OnStepShown(string id)
    {
        if (id != stepId || done)
        {
            return;
        }

        stepOpen = true;
        resumeAt = Time.time + startDelay;
        cycleStart = resumeAt;
        target = null;
    }

    private void OnStepCompleted(string id)
    {
        if (id == stepId)
        {
            done = true;
            stepOpen = false;
        }
    }

    private void Update()
    {
        bool want = stepOpen && !done && director != null && director.IsActive(stepId) &&
                    chart != null && chart.isActiveAndEnabled;
        if (!want)
        {
            SetAlpha(0f);
            return;
        }

        EnsureBuilt();
        if (hand == null || Time.time < resumeAt)
        {
            SetAlpha(0f);
            return;
        }

        if (PlayerFingerNear())
        {
            // The player is reaching for the chart: get out of the way.
            resumeAt = Time.time + returnAfter;
            cycleStart = resumeAt;
            SetAlpha(0f);
            return;
        }

        float period = approachDuration + tapDownDuration + tapHoldDuration + tapUpDuration + leaveDuration + pauseDuration;
        float t = (Time.time - cycleStart) % period;
        if (target == null || !target.gameObject.activeInHierarchy || t < Time.deltaTime)
        {
            target = PickTarget();
        }

        if (target == null)
        {
            SetAlpha(0f);
            return;
        }

        // Height of the fingertip above the paper, and the fade, through the cycle.
        float height;
        float fade;
        float t1 = approachDuration, t2 = t1 + tapDownDuration, t3 = t2 + tapHoldDuration, t4 = t3 + tapUpDuration, t5 = t4 + leaveDuration;
        if (t < t1)
        {
            float k = Smooth(t / t1);
            height = Mathf.Lerp(startHeight, hoverHeight, k);
            fade = k;
        }
        else if (t < t2)
        {
            height = Mathf.Lerp(hoverHeight, contactHeight, Smooth((t - t1) / tapDownDuration));
            fade = 1f;
        }
        else if (t < t3)
        {
            height = contactHeight;
            fade = 1f;
        }
        else if (t < t4)
        {
            height = Mathf.Lerp(contactHeight, hoverHeight, Smooth((t - t3) / tapUpDuration));
            fade = 1f;
        }
        else if (t < t5)
        {
            float k = Smooth((t - t4) / leaveDuration);
            height = Mathf.Lerp(hoverHeight, startHeight, k);
            fade = 1f - k;
        }
        else
        {
            height = startHeight;
            fade = 0f;
        }

        PoseAt(target.transform.position, height);
        SetAlpha(fade * opacity);
    }

    // The chart's normal on the player's side.
    private Vector3 ChartNormal(Transform eye)
    {
        Vector3 normal = chart.transform.up;
        Vector3 toEye = eye != null ? eye.position - chart.transform.position : normal;
        return Vector3.Dot(normal, toEye) < 0f ? -normal : normal;
    }

    private void PoseAt(Vector3 star, float height)
    {
        Transform eye = Viewer();
        Vector3 normal = ChartNormal(eye);

        // Directions on the paper as the player sees it: down and left.
        Vector3 down = Vector3.ProjectOnPlane(Vector3.down, normal);
        if (down.sqrMagnitude < 1e-6f)
        {
            down = Vector3.ProjectOnPlane(-chart.transform.forward, normal);
        }

        down.Normalize();
        Vector3 viewerRight = eye != null ? eye.right : Vector3.right;
        Vector3 left = Vector3.ProjectOnPlane(-viewerRight, normal);
        left = left.sqrMagnitude > 1e-6f ? left.normalized : Vector3.Cross(normal, down).normalized;

        // The forearm comes up from the lower left, low over the paper; the
        // finger points back along it, onto the star.
        float az = approachFromLeft * Mathf.Deg2Rad;
        float el = fingerElevation * Mathf.Deg2Rad;
        Vector3 along = (down * Mathf.Cos(az) + left * Mathf.Sin(az)).normalized;
        Vector3 towardWrist = (along * Mathf.Cos(el) + normal * Mathf.Sin(el)).normalized;
        Vector3 pointing = -towardWrist;

        // Back of the hand toward the player.
        Vector3 back = Vector3.ProjectOnPlane(normal, pointing);
        if (back.sqrMagnitude < 1e-6f)
        {
            back = -down;
        }

        Quaternion rootRotation = Quaternion.LookRotation(pointing, back.normalized) * Quaternion.Inverse(handFrameInRoot);
        Transform root = hand.transform;
        root.rotation = rootRotation;

        // Fingertip on the point above the star.
        Vector3 tipTarget = star + normal * height;
        Vector3 tip = indexTip != null ? indexTip.position : root.position;
        root.position += tipTarget - tip;
    }

    // The active chart star nearest the player's eye (the near side of the chart).
    private BlueprintStar PickTarget()
    {
        Transform eye = Viewer();
        BlueprintStar best = null;
        float bestDistance = float.MaxValue;
        foreach (BlueprintStar star in chart.ActiveTargets)
        {
            if (star == null || !star.gameObject.activeInHierarchy)
            {
                continue;
            }

            float d = eye != null ? Vector3.Distance(eye.position, star.transform.position) : 0f;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = star;
            }
        }

        return best;
    }

    private bool PlayerFingerNear()
    {
        if (stepAsideDistance <= 0f)
        {
            return false;
        }

        if (playerTips == null)
        {
            // The player's hand rigs live under the camera rig (the ghost and
            // the projected hands don't).
            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            var tips = new List<Transform>();
            if (rig != null)
            {
                foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "XRHand_IndexTip" || t.name == "Hand_IndexTip")
                    {
                        tips.Add(t);
                    }
                }
            }

            if (tips.Count == 0)
            {
                return false;   // hands not built yet; look again next frame
            }

            playerTips = tips.ToArray();
        }

        Vector3 c = chart.transform.position;
        foreach (Transform tip in playerTips)
        {
            if (tip != null && tip.gameObject.activeInHierarchy && Vector3.Distance(tip.position, c) < stepAsideDistance)
            {
                return true;
            }
        }

        return false;
    }

    private Transform Viewer() => viewer != null ? viewer : (Camera.main != null ? Camera.main.transform : null);

    private void EnsureBuilt()
    {
        if (hand != null || pointingHandPrefab == null)
        {
            return;
        }

        hand = Instantiate(pointingHandPrefab);
        hand.name = "Chart Demo Ghost Hand";
        foreach (Collider c in hand.GetComponentsInChildren<Collider>(true))
        {
            Destroy(c);
        }

        if (ghostMaterial != null)
        {
            materialInstance = new Material(ghostMaterial) { name = ghostMaterial.name + " (chart demo)" };
        }

        foreach (Renderer r in hand.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.gameObject.activeInHierarchy)
            {
                continue;   // the rig carries an unused second hand mesh
            }

            if (materialInstance != null)
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    mats[i] = materialInstance;
                }

                r.sharedMaterials = mats;
            }

            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            renderers.Add(r);
        }

        Transform wrist = null, indexBase = null, middleBase = null, littleBase = null;
        foreach (Transform t in hand.GetComponentsInChildren<Transform>(true))
        {
            switch (t.name)
            {
                case "XRHand_IndexTip": indexTip = t; break;
                case "XRHand_Wrist": wrist = t; break;
                case "XRHand_IndexProximal": indexBase = t; break;
                case "XRHand_MiddleProximal": middleBase = t; break;
                case "XRHand_LittleProximal": littleBase = t; break;
            }
        }

        // The pointing finger and the back of the hand, measured from the bones
        // with the root at identity (a left hand: back = fingers x across).
        hand.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        if (indexTip != null && indexBase != null && wrist != null && middleBase != null && littleBase != null)
        {
            Vector3 pointing = indexTip.position - indexBase.position;
            Vector3 across = indexBase.position - littleBase.position;
            Vector3 palmForward = middleBase.position - wrist.position;
            Vector3 back = Vector3.Cross(palmForward, across);
            if (pointing.sqrMagnitude > 1e-8f && back.sqrMagnitude > 1e-8f)
            {
                handFrameInRoot = Quaternion.LookRotation(pointing.normalized, Vector3.ProjectOnPlane(back, pointing).normalized);
            }
        }
        else
        {
            Debug.LogWarning("ChartGhostHand: hand bones not found; the pose may be off.", this);
        }

        alpha = -1f;
        SetAlpha(0f);
    }

    private void SetAlpha(float a)
    {
        if (Mathf.Approximately(a, alpha))
        {
            return;
        }

        alpha = a;
        if (materialInstance != null)
        {
            materialInstance.SetFloat(FadeId, a);
        }

        bool visible = a > 0.001f;
        foreach (Renderer r in renderers)
        {
            if (r != null && r.enabled != visible)
            {
                r.enabled = visible;
            }
        }
    }

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }
}
