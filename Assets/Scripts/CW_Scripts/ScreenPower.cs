using System.Collections;
using UnityEngine;

/// <summary>
/// Turns the Earth View Disc's screen off and on with an old CRT look. The
/// disc itself stays on the table; only the picture changes.
///
/// Power off: the Earth view camera stops, so the screen holds its last
/// frame. That picture brightens as it squashes into a horizontal line, the
/// line shrinks to a dot, and the dot fades to dark glass.
/// Power on: the camera restarts, a dot appears, stretches into a line, opens
/// into the live picture, and settles from overexposed to normal.
///
/// The line runs along the picture's own horizontal (the screen's UV frame),
/// so it turns with the disc, like a real tube. Needs the screen to use the
/// CelestialWorkshop/CRTScreen shader; values are set per renderer with a
/// MaterialPropertyBlock, so the material asset is never changed.
/// </summary>
[DisallowMultipleComponent]
public sealed class ScreenPower : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The screen renderer (Earth View Disc/Screen), using the CRTScreen shader.")]
    [SerializeField]
    private Renderer screen;

    [Tooltip("The camera feeding the screen. Stopped while the screen is off (the picture freezes on its last frame, and the render is saved); restarted on power on. Empty = never touched.")]
    [SerializeField]
    private Camera feedCamera;

    [Header("Timing")]
    [SerializeField]
    [Min(0.05f)]
    private float powerOffDuration = 0.6f;

    [SerializeField]
    [Min(0.05f)]
    private float powerOnDuration = 0.8f;

    [Header("Look")]
    [Tooltip("Color of the collapsing line, the dot and the power-on overexposure. White for a classic tube, warm amber for a lamp-like glow.")]
    [SerializeField]
    private Color glowColor = new Color(1f, 0.8f, 0.5f, 1f);

    [Tooltip("The screen when off: dark glass.")]
    [SerializeField]
    private Color offColor = new Color(0.035f, 0.045f, 0.06f, 1f);

    [Tooltip("Half thickness of the line and the dot, as a fraction of the screen's width.")]
    [SerializeField]
    [Range(0.001f, 0.05f)]
    private float lineThickness = 0.006f;

    [Tooltip("How far the glow bleeds past the line and the dot, as a fraction of the screen's width.")]
    [SerializeField]
    [Range(0.001f, 0.2f)]
    private float haloSize = 0.03f;

    [Tooltip("Brightness flicker while powering on (and briefly while powering off).")]
    [SerializeField]
    private bool flicker = true;

    [Tooltip("Scanlines during the transitions. They squash with the picture. The settled picture never shows them.")]
    [SerializeField]
    private bool scanlines = true;

    [SerializeField]
    [Min(10f)]
    private float scanlineCount = 160f;

    [Header("Sound (optional)")]
    [SerializeField]
    private AudioClip powerOffClip;

    [SerializeField]
    private AudioClip powerOnClip;

    [Tooltip("Plays the clips. Empty = one is added to this object (3D sound) the first time a clip plays.")]
    [SerializeField]
    private AudioSource audioSource;

    /// <summary>True while the screen is on, or powering on.</summary>
    public bool IsOn { get; private set; } = true;

    public bool IsTransitioning => routine != null;

    private static readonly int HeightId = Shader.PropertyToID("_Height");
    private static readonly int WidthId = Shader.PropertyToID("_Width");
    private static readonly int GlowId = Shader.PropertyToID("_Glow");
    private static readonly int EnergyId = Shader.PropertyToID("_Energy");
    private static readonly int BrightnessId = Shader.PropertyToID("_Brightness");
    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    private static readonly int OffColorId = Shader.PropertyToID("_OffColor");
    private static readonly int LineThicknessId = Shader.PropertyToID("_LineThickness");
    private static readonly int HaloSizeId = Shader.PropertyToID("_HaloSize");
    private static readonly int ScanlinesId = Shader.PropertyToID("_Scanlines");
    private static readonly int ScanlineCountId = Shader.PropertyToID("_ScanlineCount");

    private MaterialPropertyBlock block;
    private Coroutine routine;

    // Current effect state.
    private float height = 1f;
    private float width = 1f;
    private float glow;
    private float energy = 1f;
    private float brightness = 1f;
    private float scanlineStrength;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        SetOnState();
        Apply();
    }

    private void OnDisable()
    {
        // Never leave the screen stuck mid-transition.
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
            if (IsOn)
            {
                SetOnState();
            }
            else
            {
                SetOffState();
            }

            Apply();
        }
    }

    /// <summary>Plays the CRT power off. Does nothing if already off (or powering off).</summary>
    public void PowerOff()
    {
        if (!IsOn)
        {
            return;
        }

        if (!isActiveAndEnabled)
        {
            SetPowerImmediate(false);
            return;
        }

        IsOn = false;
        Restart(PowerOffRoutine());
    }

    /// <summary>Plays the CRT power on. Does nothing if already on (or powering on).</summary>
    public void PowerOn()
    {
        if (IsOn)
        {
            return;
        }

        if (!isActiveAndEnabled)
        {
            // Can't animate (e.g. during teardown): just be on.
            SetPowerImmediate(true);
            return;
        }

        IsOn = true;
        Restart(PowerOnRoutine());
    }

    /// <summary>Switches straight to on or off with no effect.</summary>
    public void SetPowerImmediate(bool on)
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }

        IsOn = on;
        if (on)
        {
            SetOnState();
        }
        else
        {
            SetOffState();
        }

        SetFeed(on);
        Apply();
    }

    [ContextMenu("Preview Power Off")]
    private void PreviewPowerOff()
    {
        if (Application.isPlaying)
        {
            PowerOff();
        }
        else
        {
            Debug.LogWarning("ScreenPower: preview works in Play mode.", this);
        }
    }

    [ContextMenu("Preview Power On")]
    private void PreviewPowerOn()
    {
        if (Application.isPlaying)
        {
            PowerOn();
        }
        else
        {
            Debug.LogWarning("ScreenPower: preview works in Play mode.", this);
        }
    }

    private void Restart(IEnumerator next)
    {
        if (routine != null)
        {
            StopCoroutine(routine);
        }

        routine = StartCoroutine(next);
    }

    // Timeline as fractions of powerOffDuration:
    //   0.00-0.40  picture squashes to a line, washing toward the glow color
    //   0.40-0.65  line shrinks to a dot
    //   0.65-1.00  dot fades out
    private IEnumerator PowerOffRoutine()
    {
        SetFeed(false); // freeze the last frame
        Play(powerOffClip);

        float startHeight = height;
        float startWidth = width;
        float startGlow = glow;
        float elapsed = 0f;
        while (elapsed < powerOffDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / powerOffDuration);

            float collapse = Phase(t, 0f, 0.4f);
            float shrink = Phase(t, 0.4f, 0.65f);
            float fade = Phase(t, 0.65f, 1f);

            // Accelerating collapse, like the deflection dying.
            height = Mathf.Lerp(startHeight, 0f, collapse * collapse);
            glow = Mathf.Lerp(startGlow, 1f, Mathf.Sqrt(collapse));
            width = Mathf.Lerp(startWidth, 0f, shrink * shrink);
            energy = 1f - fade * fade * (3f - 2f * fade);
            brightness = 1f + 0.35f * collapse * (1f - fade) + Flicker(t < 0.4f ? 0.12f : 0f);
            scanlineStrength = scanlines ? 1f - shrink : 0f;
            Apply();
            yield return null;
        }

        SetOffState();
        Apply();
        routine = null;
    }

    // Timeline as fractions of powerOnDuration:
    //   0.00-0.15  dot appears
    //   0.15-0.35  dot stretches into a line
    //   0.35-0.65  line opens into the picture (overexposed)
    //   0.65-1.00  exposure settles to normal
    private IEnumerator PowerOnRoutine()
    {
        SetFeed(true); // live picture from the first frame it shows
        Play(powerOnClip);

        float startEnergy = energy;
        float elapsed = 0f;
        while (elapsed < powerOnDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / powerOnDuration);

            float appear = Phase(t, 0f, 0.15f);
            float stretch = Phase(t, 0.15f, 0.35f);
            float open = Phase(t, 0.35f, 0.65f);
            float settle = Phase(t, 0.65f, 1f);

            energy = Mathf.Lerp(startEnergy, 1f, appear);
            width = EaseOut(stretch);
            height = EaseOut(open);
            glow = Mathf.Lerp(1f, 0.35f, open) * (1f - settle * settle * (3f - 2f * settle));
            brightness = 1f + 0.25f * (1f - settle) + Flicker(0.2f * (1f - settle));
            scanlineStrength = scanlines ? 1f - settle : 0f;
            Apply();
            yield return null;
        }

        SetOnState();
        Apply();
        routine = null;
    }

    private void SetOnState()
    {
        height = 1f;
        width = 1f;
        glow = 0f;
        energy = 1f;
        brightness = 1f;
        scanlineStrength = 0f;
    }

    private void SetOffState()
    {
        height = 0f;
        width = 0f;
        glow = 1f;
        energy = 0f;
        brightness = 1f;
        scanlineStrength = 0f;
    }

    private void Apply()
    {
        if (screen == null)
        {
            return;
        }

        if (block == null)
        {
            block = new MaterialPropertyBlock();
        }

        screen.GetPropertyBlock(block);
        block.SetFloat(HeightId, height);
        block.SetFloat(WidthId, width);
        block.SetFloat(GlowId, glow);
        block.SetFloat(EnergyId, energy);
        block.SetFloat(BrightnessId, brightness);
        block.SetColor(GlowColorId, glowColor);
        block.SetColor(OffColorId, offColor);
        block.SetFloat(LineThicknessId, lineThickness);
        block.SetFloat(HaloSizeId, haloSize);
        block.SetFloat(ScanlinesId, scanlineStrength);
        block.SetFloat(ScanlineCountId, scanlineCount);
        screen.SetPropertyBlock(block);
    }

    private void SetFeed(bool on)
    {
        if (feedCamera != null)
        {
            feedCamera.enabled = on;
        }
    }

    private void Play(AudioClip clip)
    {
        if (clip == null)
        {
            return;
        }

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
        }

        audioSource.PlayOneShot(clip);
    }

    private float Flicker(float amount)
    {
        if (!flicker || amount <= 0f)
        {
            return 0f;
        }

        // Changes about 30 times a second, independent of frame rate.
        float n = Mathf.PerlinNoise(Mathf.Floor(Time.time * 30f) * 0.37f, 0.5f);
        return (n - 0.5f) * 2f * amount;
    }

    private static float Phase(float t, float start, float end)
    {
        return Mathf.Clamp01((t - start) / (end - start));
    }

    private static float EaseOut(float t)
    {
        return 1f - (1f - t) * (1f - t);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Keep the look settings live in the Inspector during Play.
        if (Application.isPlaying && block != null)
        {
            Apply();
        }
    }
#endif
}
