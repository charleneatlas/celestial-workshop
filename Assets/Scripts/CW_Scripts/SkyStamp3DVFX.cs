using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class SkyStamp3DVFX : MonoBehaviour
{
    // The stamp is a copy of the constellation laid exactly over the solved
    // one (additive material: alpha = brightness). Only where it differs from
    // the original is it visible, so it swells, glows in the flash colour,
    // settles back onto the stars and then dissolves into them by brightness.

    [Header("Timing")]
    [Tooltip("Swell up to the impact size and full flash brightness (s).")]
    [SerializeField]
    private float riseDuration = 0.08f;

    [Tooltip("Held at the impact size, fully bright (s).")]
    [SerializeField]
    private float holdDuration = 0.3f;

    [Tooltip("Settle from the impact size back onto the stars (s).")]
    [SerializeField]
    private float impactDuration = 0.35f;

    [Tooltip("Fade the flash colour out, leaving the solved constellation (s).")]
    [SerializeField]
    private float disappearDuration = 0.4f;

    [Header("Flash")]
    [Tooltip("Colour of the stamp copy (additive: alpha = brightness). The solved constellation underneath keeps its own colour, so this is the colour of the flash only.")]
    [SerializeField]
    private Color flashColor = new Color(1f, 0.8f, 0.35f, 1f);

    [Tooltip("Brightness left when the settle ends, before the fade.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float settledBrightness = 0.6f;

    [Header("Impact")]
    [SerializeField]
    private float starImpactScale = 1.8f;

    [SerializeField]
    private float lineImpactThickness = 2.5f;

    private GameObject constellationRoot;
    private Coroutine playRoutine;
    private Renderer[] renderers = new Renderer[0];
    private MaterialPropertyBlock block;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public bool IsPlaying => playRoutine != null;

    private readonly List<ScaleRecord> starSpheres =
        new List<ScaleRecord>();

    private readonly List<ScaleRecord> connections =
        new List<ScaleRecord>();

    private struct ScaleRecord
    {
        public Transform transform;
        public Vector3 originalScale;

        public ScaleRecord(
            Transform transform,
            Vector3 originalScale)
        {
            this.transform = transform;
            this.originalScale = originalScale;
        }
    }

    public void Initialize(GameObject generatedRoot)
    {
        constellationRoot = generatedRoot;

        starSpheres.Clear();
        connections.Clear();

        if (constellationRoot == null)
        {
            return;
        }

        GeneratedConstellationStar[] stars =
            constellationRoot.GetComponentsInChildren<
                GeneratedConstellationStar>(true);

        foreach (GeneratedConstellationStar star in stars)
        {
            Transform sphere = star.transform.Find("Sphere");

            if (sphere != null)
            {
                starSpheres.Add(
                    new ScaleRecord(
                        sphere,
                        sphere.localScale
                    )
                );
            }
        }

        Transform[] allTransforms =
            constellationRoot.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in allTransforms)
        {
            if (child.name == "Connection")
            {
                connections.Add(
                    new ScaleRecord(
                        child,
                        child.localScale
                    )
                );
            }
        }

        renderers = constellationRoot.GetComponentsInChildren<Renderer>(true);
        constellationRoot.SetActive(false);
    }

    [ContextMenu("Play Stamp VFX")]
    public void Play()
    {
        if (constellationRoot == null)
        {
            return;
        }

        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
        }

        playRoutine = StartCoroutine(PlayRoutine());
        Debug.Log($"SkyStamp3DVFX: playing ({starSpheres.Count} stars, {connections.Count} connections).", this);
        Played?.Invoke();
    }

    /// <summary>Raised when the solve stamp starts playing.</summary>
    public event System.Action Played;

    public void ResetVFX()
    {
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }

        RestoreOriginalScales();

        if (constellationRoot != null)
        {
            constellationRoot.SetActive(false);
        }
    }

    private IEnumerator PlayRoutine()
    {
        constellationRoot.SetActive(true);

        // Rise: normal -> impact size, dark -> full flash.
        yield return Phase(riseDuration, t =>
        {
            float k = 1f - Mathf.Pow(1f - t, 3f);
            SetGeometryScale(Mathf.Lerp(1f, starImpactScale, k), Mathf.Lerp(1f, lineImpactThickness, k));
            SetBrightness(k);
        });

        // Hold at the impact size.
        SetGeometryScale(starImpactScale, lineImpactThickness);
        SetBrightness(1f);
        yield return Phase(holdDuration, null);

        // Settle back onto the stars, dimming a little.
        yield return Phase(impactDuration, t =>
        {
            float k = t * t * (3f - 2f * t);
            SetGeometryScale(Mathf.Lerp(starImpactScale, 1f, k), Mathf.Lerp(lineImpactThickness, 1f, k));
            SetBrightness(Mathf.Lerp(1f, settledBrightness, k));
        });

        SetGeometryScale(1f, 1f);

        // Dissolve into the solved constellation.
        yield return Phase(disappearDuration, t => SetBrightness(settledBrightness * (1f - t)));

        RestoreOriginalScales();
        SetBrightness(0f);
        constellationRoot.SetActive(false);

        playRoutine = null;
    }

    private static IEnumerator Phase(float duration, System.Action<float> step)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            step?.Invoke(Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, duration)));
            yield return null;
        }
    }

    private void SetBrightness(float brightness)
    {
        block ??= new MaterialPropertyBlock();
        Color c = flashColor;
        c.a *= Mathf.Clamp01(brightness);
        foreach (Renderer r in renderers)
        {
            if (r == null)
            {
                continue;
            }

            block.Clear();
            block.SetColor(BaseColorId, c);
            block.SetColor(ColorId, c);
            r.SetPropertyBlock(block);
        }
    }

    private void SetGeometryScale(
        float starScale,
        float lineThickness)
    {
        foreach (ScaleRecord star in starSpheres)
        {
            star.transform.localScale =
                star.originalScale * starScale;
        }

        foreach (ScaleRecord line in connections)
        {
            Vector3 scale =
                line.originalScale;

            // Keep connection length unchanged.
            // Only alter cylinder thickness.
            scale.x *= lineThickness;
            scale.z *= lineThickness;

            line.transform.localScale = scale;
        }
    }

    private void RestoreOriginalScales()
    {
        foreach (ScaleRecord star in starSpheres)
        {
            if (star.transform != null)
            {
                star.transform.localScale =
                    star.originalScale;
            }
        }

        foreach (ScaleRecord line in connections)
        {
            if (line.transform != null)
            {
                line.transform.localScale =
                    line.originalScale;
            }
        }
    }
}