using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ConstellationSetup : MonoBehaviour
{
    [Header("Generator")]
    [SerializeField]
    private ConstellationGenerator constellationGenerator;

    [Header("Rotation References")]
    [SerializeField]
    private Transform miniEarthReference;

    [SerializeField]
    private Transform skyRotationReference;

    [Header("Mini Constellation")]
    [SerializeField]
    private Transform miniConstellationPivot;

    [SerializeField]
    private Transform miniVisualContainer;

    [Header("Sky Constellation")]
    [SerializeField]
    private Transform skyConstellationPivot;

    [SerializeField]
    private Transform skyVisualContainer;

    [Header("Runtime")]
    [SerializeField]
    private bool generateOnStart = true;

    [SerializeField]
    private bool mirrorRotation = true;

    [SerializeField]
    private ConstellationGenerator.AppearanceSettings tableConstellationAppearance;

    [SerializeField]
    private ConstellationGenerator.AppearanceSettings skyConstellationAppearance;

    private bool isInitialized;

    private readonly Dictionary<string, List<GeneratedConstellationStar>>
    generatedStarsById =
        new Dictionary<string, List<GeneratedConstellationStar>>(
            StringComparer.OrdinalIgnoreCase
        );

    private void Start()
    {
        if (!ValidateReferences())
        {
            enabled = false;
            return;
        }

        if (generateOnStart)
        {
            GenerateConstellations();
        }

        // Make sure constellations pivoting around a sensible center
        CenterRotationPivot(
            miniVisualContainer,
            miniConstellationPivot
        );

        CenterRotationPivot(
            skyVisualContainer,
            skyConstellationPivot
        );

        // Make sure they match immediately, before the first visible update.
        CopyRotationToSky();

        isInitialized = true;
    }

    private void LateUpdate()
    {
        if (!isInitialized || !mirrorRotation)
        {
            return;
        }

        CopyRotationToSky();
    }

    private void GenerateConstellations()
    {
        GameObject miniGeneratedRoot =
        constellationGenerator.GenerateConstellation(
            miniVisualContainer,
            tableConstellationAppearance);

        GameObject skyGeneratedRoot =
        constellationGenerator.GenerateConstellation(
            skyVisualContainer,
            skyConstellationAppearance);

        BuildStarLookup(
            miniGeneratedRoot,
            skyGeneratedRoot
        );
    }

    private void CopyRotationToSky()
    {
        // Mini Earth Reference and Sky Rotation Reference should represent
        // equivalent coordinate axes. Sky Coordinate Frame only controls placement.
        Quaternion miniRelativeRotation =
        Quaternion.Inverse(miniEarthReference.rotation) *
        miniConstellationPivot.rotation;

        skyConstellationPivot.rotation =
            skyRotationReference.rotation *
            miniRelativeRotation;
    }

    private bool ValidateReferences()
    {
        bool isValid = true;

        if (constellationGenerator == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing its ConstellationGenerator.",
                this);

            isValid = false;
        }

        if (miniConstellationPivot == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Mini Constellation Pivot.",
                this);

            isValid = false;
        }

        if (miniVisualContainer == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Mini Visual Container.",
                this);

            isValid = false;
        }

        if (skyConstellationPivot == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Sky Constellation Pivot.",
                this);

            isValid = false;
        }

        if (skyVisualContainer == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Sky Visual Container.",
                this);

            isValid = false;
        }

        if (miniEarthReference == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Mini Earth Reference.",
                this);

            isValid = false;
        }

        if (skyRotationReference == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Sky Rotation Reference.",
                this);

            isValid = false;
        }

        return isValid;
    }

    private void CenterRotationPivot(
    Transform constellationRoot,
    Transform rotationPivot)
    {
        if (constellationRoot == null || rotationPivot == null)
        {
            Debug.LogWarning(
                "Missing constellation root or rotation pivot."
            );
            return;
        }

        Transform[] children =
            constellationRoot.GetComponentsInChildren<Transform>();

        Vector3 totalPosition = Vector3.zero;
        int starCount = 0;

        foreach (Transform child in children)
        {
            if (child == constellationRoot)
                continue;

            if (!child.CompareTag("Star"))
                continue;

            totalPosition += child.position;
            starCount++;
        }

        if (starCount == 0)
        {
            Debug.LogWarning(
                $"No stars found under {constellationRoot.name}."
            );
            return;
        }

        Vector3 center = totalPosition / starCount;

        // Preserve the constellation's world-space transform.
        // Moving its parent pivot would otherwise move the constellation too.
        Vector3 originalRootPosition = constellationRoot.position;
        Quaternion originalRootRotation = constellationRoot.rotation;

        // Move the actual pivot to the center of the stars.
        rotationPivot.position = center;

        // Put the constellation back where it was in world space.
        // This changes its local offset relative to the newly centered pivot.
        constellationRoot.SetPositionAndRotation(
            originalRootPosition,
            originalRootRotation
        );

        Debug.Log(
            $"{rotationPivot.name}: centered using " +
            $"{starCount} stars under {constellationRoot.name}. " +
            $"World center = {center}"
        );
    }

    private void BuildStarLookup(
    GameObject miniGeneratedRoot,
    GameObject skyGeneratedRoot)
    {
        generatedStarsById.Clear();

        RegisterGeneratedStars(miniGeneratedRoot);
        RegisterGeneratedStars(skyGeneratedRoot);
    }

    private void RegisterGeneratedStars(GameObject generatedRoot)
    {
        if (generatedRoot == null)
        {
            return;
        }

        GeneratedConstellationStar[] generatedStars =
            generatedRoot.GetComponentsInChildren<GeneratedConstellationStar>();

        foreach (GeneratedConstellationStar star in generatedStars)
        {
            if (star == null || string.IsNullOrEmpty(star.StarId))
            {
                continue;
            }

            if (!generatedStarsById.TryGetValue(
                    star.StarId,
                    out List<GeneratedConstellationStar> matchingStars))
            {
                matchingStars = new List<GeneratedConstellationStar>();

                generatedStarsById.Add(
                    star.StarId,
                    matchingStars
                );
            }

            matchingStars.Add(star);
        }
    }

    public void SetStarHighlighted(
        string starId,
        bool highlighted)
    {
        if (string.IsNullOrEmpty(starId))
        {
            return;
        }

        if (!generatedStarsById.TryGetValue(
                starId,
                out List<GeneratedConstellationStar> matchingStars))
        {
            Debug.LogWarning(
                $"No generated constellation star found with ID '{starId}'.",
                this
            );

            return;
        }

        foreach (GeneratedConstellationStar star in matchingStars)
        {
            star.SetHighlighted(highlighted);
        }
    }
}