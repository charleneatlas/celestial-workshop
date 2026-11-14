using UnityEngine;

/// <summary>
/// Reusable "magical ink" trail: a soft warm glow that follows a moving point,
/// tiny twinkling stars left along the path that drift and fade, and a gentle
/// flourish that runs along a stroke when it completes.
///
/// Drive it with BeginStroke / StrokeTo / EndStroke (e.g. from SignatureReveal),
/// SetHead for a resting marker, and Flourish when a signature completes.
/// Builds its own two particle systems and head quad on first use, so the
/// prefab only needs this component and two materials.
///
/// Meant to be shared later with constellation signing; nothing here knows
/// about the noticeboard.
/// </summary>
[DisallowMultipleComponent]
public sealed class StarTrail : MonoBehaviour
{
    [Header("Materials")]
    [Tooltip("Soft round glow (additive). CelestialWorkshop/StarGlow + a soft dot texture.")]
    [SerializeField]
    private Material glowMaterial;

    [Tooltip("Small four-point star (additive). CelestialWorkshop/StarGlow + a star texture.")]
    [SerializeField]
    private Material sparkleMaterial;

    [Header("Look")]
    [SerializeField]
    private Color glowColor = new Color(1f, 0.78f, 0.42f, 1f);

    [SerializeField]
    private Color sparkleColor = new Color(1f, 0.93f, 0.75f, 1f);

    [Tooltip("Overall brightness multiplier (0 = off).")]
    [SerializeField]
    [Range(0f, 3f)]
    private float intensity = 1f;

    [Tooltip("How long trail particles live (s).")]
    [SerializeField]
    [Min(0.05f)]
    private float lifetime = 0.9f;

    [Tooltip("Glow puffs per metre of stroke.")]
    [SerializeField]
    [Min(0f)]
    private float glowPerMetre = 180f;

    [Tooltip("Sparkles per metre of stroke.")]
    [SerializeField]
    [Min(0f)]
    private float sparklesPerMetre = 140f;

    [Tooltip("Glow puff size (m).")]
    [SerializeField]
    [Min(0.001f)]
    private float glowSize = 0.016f;

    [Tooltip("Sparkle size range (m).")]
    [SerializeField]
    private Vector2 sparkleSize = new Vector2(0.0035f, 0.008f);

    [Tooltip("Random scatter of sparkles around the stroke (m).")]
    [SerializeField]
    [Min(0f)]
    private float scatter = 0.006f;

    [Tooltip("Slow drift speed of sparkles (m/s).")]
    [SerializeField]
    [Min(0f)]
    private float drift = 0.012f;

    [Header("Head")]
    [Tooltip("Glow at the tracing point (m). 0 = no head.")]
    [SerializeField]
    [Min(0f)]
    private float headSize = 0.03f;

    [Tooltip("Head pulse speed while resting (Hz).")]
    [SerializeField]
    [Min(0f)]
    private float headPulse = 0.8f;

    [Header("Flourish")]
    [SerializeField]
    [Min(0)]
    private int flourishSparkles = 70;

    [SerializeField]
    [Min(0.05f)]
    private float flourishLifetime = 1.5f;

    [Tooltip("How far the flourish sparkles rise (m/s).")]
    [SerializeField]
    [Min(0f)]
    private float flourishRise = 0.03f;

    public float Intensity
    {
        get => intensity;
        set => intensity = Mathf.Max(0f, value);
    }

    private ParticleSystem glow;
    private ParticleSystem sparkles;
    private Transform head;
    private MeshRenderer headRenderer;
    private Material headMaterial;
    private bool stroking;
    private Vector3 lastPoint;
    private float glowCarry;
    private float sparkleCarry;
    private bool headVisible;
    private float headStrength;
    private float headTarget;
    private Vector3 headPoint;
    private bool headResting;
    private Camera cachedCamera;

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        EnsureBuilt();
    }

    private void OnDestroy()
    {
        if (headMaterial != null)
        {
            Destroy(headMaterial);
        }
    }

    // ---- API ----

    /// <summary>Starts a stroke at this point (no particles along a jump).</summary>
    public void BeginStroke(Vector3 point)
    {
        EnsureBuilt();
        stroking = true;
        lastPoint = point;
        glowCarry = 0f;
        sparkleCarry = 0f;
        SetHead(true, point, false);
    }

    /// <summary>Continues the stroke to this point, emitting along the way.</summary>
    public void StrokeTo(Vector3 point)
    {
        if (!stroking)
        {
            BeginStroke(point);
            return;
        }

        Vector3 delta = point - lastPoint;
        float distance = delta.magnitude;
        if (distance > 1e-5f && intensity > 0f)
        {
            Vector3 dir = delta / distance;
            glowCarry += distance * glowPerMetre;
            while (glowCarry >= 1f)
            {
                glowCarry -= 1f;
                Vector3 p = point - dir * (glowCarry / Mathf.Max(1f, glowPerMetre));
                EmitGlow(p);
            }

            sparkleCarry += distance * sparklesPerMetre;
            while (sparkleCarry >= 1f)
            {
                sparkleCarry -= 1f;
                Vector3 p = point - dir * (sparkleCarry / Mathf.Max(1f, sparklesPerMetre));
                EmitSparkle(p, lifetime, drift);
            }
        }

        lastPoint = point;
        SetHead(true, point, false);
    }

    /// <summary>Ends the stroke; the head stays where it stopped if keepHead.</summary>
    public void EndStroke(bool keepHead)
    {
        stroking = false;
        SetHead(keepHead, lastPoint, true);
    }

    /// <summary>
    /// Shows a glowing head at a point. Resting heads pulse gently (e.g. to mark
    /// where a paused signature can resume).
    /// </summary>
    public void SetHead(bool visible, Vector3 point, bool resting)
    {
        headVisible = visible;
        headPoint = point;
        headResting = resting;
        headTarget = visible ? 1f : 0f;
    }

    /// <summary>A gentle burst of rising stars along a stroke from a to b.</summary>
    public void Flourish(Vector3 a, Vector3 b)
    {
        EnsureBuilt();
        if (intensity <= 0f)
        {
            return;
        }

        for (int i = 0; i < flourishSparkles; i++)
        {
            float t = (i + Random.value) / Mathf.Max(1, flourishSparkles);
            Vector3 p = Vector3.Lerp(a, b, t);
            EmitSparkle(p, flourishLifetime * Random.Range(0.6f, 1f), drift, flourishRise, 1.4f);
            if (i % 3 == 0)
            {
                EmitGlow(p, 1.6f, flourishLifetime * 0.6f);
            }
        }
    }

    /// <summary>Removes all particles and the head immediately.</summary>
    public void Clear()
    {
        stroking = false;
        headVisible = false;
        headStrength = 0f;
        headTarget = 0f;
        if (glow != null)
        {
            glow.Clear();
        }

        if (sparkles != null)
        {
            sparkles.Clear();
        }

        if (headRenderer != null)
        {
            headRenderer.enabled = false;
        }
    }

    // ---- Emission ----

    private void EmitGlow(Vector3 p, float sizeScale = 1f, float life = -1f)
    {
        var e = new ParticleSystem.EmitParams
        {
            position = p + Random.insideUnitSphere * (scatter * 0.3f),
            velocity = Random.insideUnitSphere * (drift * 0.3f),
            startSize = glowSize * sizeScale * Random.Range(0.75f, 1.2f),
            startLifetime = (life > 0f ? life : lifetime) * Random.Range(0.6f, 0.9f),
            startColor = Scale(glowColor, intensity * 0.55f),
            rotation = Random.Range(0f, 360f)
        };
        glow.Emit(e, 1);
    }

    private void EmitSparkle(Vector3 p, float life, float driftSpeed, float rise = 0f, float sizeScale = 1f)
    {
        Vector3 v = Random.insideUnitSphere * driftSpeed;
        v.y += rise * Random.Range(0.4f, 1f);
        var e = new ParticleSystem.EmitParams
        {
            position = p + Random.insideUnitSphere * scatter,
            velocity = v,
            startSize = Random.Range(sparkleSize.x, sparkleSize.y) * sizeScale,
            startLifetime = life * Random.Range(0.5f, 1f),
            startColor = Scale(sparkleColor, intensity),
            rotation = Random.Range(-20f, 20f)
        };
        sparkles.Emit(e, 1);
    }

    private static Color32 Scale(Color c, float k)
    {
        // Additive: brightness lives in RGB; alpha drives the fade curve.
        return new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), 1f);
    }

    // ---- Head ----

    private void LateUpdate()
    {
        if (head == null)
        {
            return;
        }

        headStrength = Mathf.MoveTowards(headStrength, headTarget, Time.deltaTime * 4f);
        bool show = headStrength > 0.001f && headSize > 0f && intensity > 0f;
        headRenderer.enabled = show;
        if (!show)
        {
            return;
        }

        if (cachedCamera == null || !cachedCamera.isActiveAndEnabled)
        {
            cachedCamera = Camera.main;
        }

        float pulse = headResting ? 0.75f + 0.25f * Mathf.Sin(Time.time * Mathf.PI * 2f * headPulse) : 1f;
        head.position = headPoint;
        if (cachedCamera != null)
        {
            head.rotation = Quaternion.LookRotation(head.position - cachedCamera.transform.position, cachedCamera.transform.up);
        }

        head.localScale = Vector3.one * (headSize * (headResting ? 0.8f : 1f) * pulse);
        headMaterial.SetColor(ColorId, Scale(glowColor, intensity * headStrength * pulse * 0.9f));
    }

    // ---- Construction ----

    private void EnsureBuilt()
    {
        if (glow != null)
        {
            return;
        }

        glow = BuildSystem("Trail Glow", glowMaterial, false);
        sparkles = BuildSystem("Trail Sparkles", sparkleMaterial, true);

        if (glowMaterial != null)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Trail Head";
            Collider c = quad.GetComponent<Collider>();
            if (c != null)
            {
                Destroy(c);
            }

            head = quad.transform;
            head.SetParent(transform, false);
            headRenderer = quad.GetComponent<MeshRenderer>();
            headMaterial = new Material(glowMaterial) { name = "Trail Head (instance)" };
            headRenderer.sharedMaterial = headMaterial;
            headRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            headRenderer.receiveShadows = false;
            headRenderer.sortingOrder = 20;
            headRenderer.enabled = false;
        }
    }

    private ParticleSystem BuildSystem(string name, Material material, bool twinkle)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        // Looping with emission off: the system keeps simulating whatever
        // Emit() adds, however long the session.
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = twinkle ? 800 : 500;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            twinkle
                ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) }
                : new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(0.35f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(gradient);

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = twinkle
            ? new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.15f, 1f), new Keyframe(0.5f, 0.6f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 0f)))
            : new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.2f, 1f), new Keyframe(1f, 1.4f)));

        if (twinkle)
        {
            ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        }

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = twinkle ? 22 : 21;
        renderer.minParticleSize = 0f;
        renderer.maxParticleSize = 1f;

        ps.Play();
        return ps;
    }
}
