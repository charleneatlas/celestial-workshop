using System.Collections;
using UnityEngine;

public class SkyStampVFX : MonoBehaviour
{
    [SerializeField] private SpriteRenderer crisp;
    [SerializeField] private SpriteRenderer glow;

    [Header("Timing")]
    [SerializeField] private float slamDuration = 0.16f;
    [SerializeField] private float holdDuration = 0.18f;
    [SerializeField] private float fadeDuration = 0.35f;

    [Header("Shape")]
    [SerializeField] private float startScale = 1.3f;
    [SerializeField] private float glowScale = 1.08f;
    [SerializeField] private float glowMaxAlpha = 0.65f;

    private Vector3 baseScale;
    private Vector3 glowBaseScale;
    private Coroutine playRoutine;

    private void Awake()
    {
        baseScale = transform.localScale;
        glowBaseScale = glow.transform.localScale;
    }

    private void Start()
    {
        SetAlpha(crisp, 0f);
        SetAlpha(glow, 0f);
    }

    [ContextMenu("Play Stamp VFX")]
    public void Play()
    {
        if (playRoutine != null)
            StopCoroutine(playRoutine);

        playRoutine = StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        transform.localScale = baseScale * startScale;

        SetAlpha(crisp, 0f);
        SetAlpha(glow, 0f);

        float t = 0f;

        // Slam in
        while (t < slamDuration)
        {
            t += Time.deltaTime;
            float normalized = Mathf.Clamp01(t / slamDuration);
            float eased = 1f - Mathf.Pow(1f - normalized, 3f);

            transform.localScale =
                Vector3.Lerp(baseScale * startScale, baseScale, eased);

            SetAlpha(crisp, eased);
            SetAlpha(glow, eased * glowMaxAlpha);

            yield return null;
        }

        transform.localScale = baseScale;
        SetAlpha(crisp, 1f);
        SetAlpha(glow, glowMaxAlpha);

        yield return new WaitForSeconds(holdDuration);

        // Fade / glow expansion
        t = 0f;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float normalized = Mathf.Clamp01(t / fadeDuration);

            SetAlpha(crisp, 1f - normalized);
            SetAlpha(glow, glowMaxAlpha * (1f - normalized));

            glow.transform.localScale =
                Vector3.Lerp(
                    glowBaseScale,
                    glowBaseScale * glowScale,
                    normalized);

            yield return null;
        }

        SetAlpha(crisp, 0f);
        SetAlpha(glow, 0f);

        transform.localScale = baseScale;
        glow.transform.localScale = glowBaseScale;

        playRoutine = null;
    }

    private static void SetAlpha(SpriteRenderer renderer, float alpha)
    {
        Color color = renderer.color;
        color.a = alpha;
        renderer.color = color;
    }
}