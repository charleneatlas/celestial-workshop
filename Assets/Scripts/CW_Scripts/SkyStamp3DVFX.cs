using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class SkyStamp3DVFX : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField]
    private float impactDuration = 0.14f;

    [SerializeField]
    private float holdDuration = 0.12f;

    [SerializeField]
    private float disappearDuration = 0.25f;

    [Header("Impact")]
    [SerializeField]
    private float starImpactScale = 1.8f;

    [SerializeField]
    private float lineImpactThickness = 2.5f;

    private GameObject constellationRoot;
    private Coroutine playRoutine;

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
    }

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

        float elapsed = 0f;

        // Impact: thick/large -> normal.
        while (elapsed < impactDuration)
        {
            elapsed += Time.deltaTime;

            float t =
                Mathf.Clamp01(elapsed / impactDuration);

            // Quick ease-out.
            float eased =
                1f - Mathf.Pow(1f - t, 3f);

            float starScale =
                Mathf.Lerp(
                    starImpactScale,
                    1f,
                    eased
                );

            float lineThickness =
                Mathf.Lerp(
                    lineImpactThickness,
                    1f,
                    eased
                );

            SetGeometryScale(
                starScale,
                lineThickness
            );

            yield return null;
        }

        SetGeometryScale(1f, 1f);

        yield return new WaitForSeconds(holdDuration);

        elapsed = 0f;

        // Collapse away.
        while (elapsed < disappearDuration)
        {
            elapsed += Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / disappearDuration
                );

            float remaining = 1f - t;

            SetGeometryScale(
                remaining,
                remaining
            );

            yield return null;
        }

        RestoreOriginalScales();
        constellationRoot.SetActive(false);

        playRoutine = null;
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