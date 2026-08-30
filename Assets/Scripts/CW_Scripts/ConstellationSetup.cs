using System;
using System.Collections;
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

    [SerializeField]
    private GameObject gimbalRoot;

    [Header("Sky Constellation")]
    [SerializeField]
    private Transform skyConstellationPivot;

    [SerializeField]
    private Transform skyVisualContainer;

    [SerializeField]
    private Transform skyObserverReference;

    [SerializeField]
    [Min(1f)]
    private float skyDistanceScale = 30f;

    [SerializeField]
    [Range(0f, 1f)]
    private float skyDepthScale = 0.1f;

    [SerializeField]
    [Range(-90f, 90f)]
    private float skyElevationDegrees = 30f;

    [SerializeField]
    [Range(-180f, 180f)]
    private float skyAzimuthDegrees = 0f;

    [SerializeField]
    [Range(0.1f, 2f)]
    private float skyAngularScale = 0.6f;

    [Header("Sky Constellation Highlighting")]
    [SerializeField]
    private SkyConstellationHighlightController skyConstellationHighlightController;

    [Header("Runtime")]
    [SerializeField]
    private bool generateOnStart = true;

    [SerializeField]
    private bool mirrorRotation = true;

    [SerializeField]
    private ConstellationGenerator.AppearanceSettings tableConstellationAppearance;

    [SerializeField]
    private ConstellationGenerator.AppearanceSettings skyConstellationAppearance;

    [Header("Solve")]
    [SerializeField]
    private ConstellationSolveController solveController;

    [SerializeField]
    private float solveSettleDuration = 0.6f; // seconds

    private bool isSettling;

    private Quaternion canonicalMiniRelativeRotation;

    private Rigidbody miniConstellationRigidbody;

    private Vector3 miniConstellationStartLocalPosition;

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

        // Save the tabletop constellation's position for reset.
        miniConstellationStartLocalPosition = miniConstellationPivot.localPosition;

        // Preserve the sky constellation's generated solved orientation.
        Quaternion generatedSolvedSkyRotation =
            skyConstellationPivot.rotation;

        // Take into account the perspective from which observer is viewing the sky constellation. 
        // Align sky rotation axes to view space so gimbal input maps intuitively in the sky.
        AlignSkyRotationReferenceToView();

        // Determine the relative rotation that reproduces the original
        // generated solved orientation in the new sky view-space frame.
        canonicalMiniRelativeRotation =
            Quaternion.Inverse(skyRotationReference.rotation) *
            generatedSolvedSkyRotation;

        // Make the remote constellation immediately match
        // the randomized tabletop constellation.
        CopyRotationToSky();

        isInitialized = true;

        // Start the puzzle in a randomized, unsolved orientation.
        StartCoroutine(RandomizeAfterInitialization());
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
                tableConstellationAppearance
            );

        GameObject skyGeneratedRoot =
            constellationGenerator.GenerateConstellation(
                skyVisualContainer,
                skyConstellationAppearance,
                skyObserverReference,
                skyDistanceScale,
                new Vector3(
                    -skyElevationDegrees,
                    skyAzimuthDegrees,
                    0f
                ),
                skyAngularScale,
                skyDepthScale
            );

        BuildStarLookup(
            miniGeneratedRoot,
            skyGeneratedRoot
        );

        // Give the highlight controller ONLY the generated sky constellation.
        if (skyConstellationHighlightController != null)
        {
            skyConstellationHighlightController.SetConstellation(
                skyGeneratedRoot
            );
        }
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

    private void AlignSkyRotationReferenceToView()
    {
        Vector3 viewDirection =
            (skyConstellationPivot.position - skyObserverReference.position)
            .normalized;

        Vector3 viewUp =
            Vector3.ProjectOnPlane(Vector3.up, viewDirection)
            .normalized;

        skyRotationReference.rotation =
            Quaternion.LookRotation(viewDirection, viewUp);
    }

    private bool ValidateReferences()
    {
        bool isValid = true;

        if (constellationGenerator == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing its ConstellationGenerator.",
                this
            );

            isValid = false;
        }

        if (miniConstellationPivot == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Mini Constellation Pivot.",
                this
            );

            isValid = false;
        }
        else
        {
            miniConstellationRigidbody =
                miniConstellationPivot.GetComponent<Rigidbody>();

            if (miniConstellationRigidbody == null)
            {
                Debug.LogError(
                    "Mini Constellation Pivot is missing its Rigidbody.",
                    this
                );

                isValid = false;
            }
        }

        if (miniVisualContainer == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Mini Visual Container.",
                this
            );

            isValid = false;
        }

        if (skyConstellationPivot == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Sky Constellation Pivot.",
                this
            );

            isValid = false;
        }

        if (skyVisualContainer == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Sky Visual Container.",
                this
            );

            isValid = false;
        }

        if (miniEarthReference == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Mini Earth Reference.",
                this
            );

            isValid = false;
        }

        if (skyRotationReference == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Sky Rotation Reference.",
                this
            );

            isValid = false;
        }

        if (skyObserverReference == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Sky Observer Reference.",
                this
            );

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
                matchingStars =
                    new List<GeneratedConstellationStar>();

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

    private IEnumerator RandomizeAfterInitialization()
    {
        // Wait until all Start() methods have had a chance to initialize.
        yield return null;

        if (solveController != null)
        {
            solveController.ResetSolve();
        }

        // Immediately make the sky match the newly randomized table rotation.
        CopyRotationToSky();
    }

    private IEnumerator SettleToCanonicalSolve()
    {
        isSettling = true;

        Quaternion startRotation =
            miniConstellationPivot.rotation;

        Quaternion targetRotation =
            miniEarthReference.rotation *
            canonicalMiniRelativeRotation;

        float elapsed = 0f;

        while (elapsed < solveSettleDuration)
        {
            elapsed += Time.deltaTime;

            float t =
                Mathf.Clamp01(elapsed / solveSettleDuration);

            // Smooth ease-in/ease-out.
            t = t * t * (3f - 2f * t);

            miniConstellationPivot.rotation =
                Quaternion.Slerp(
                    startRotation,
                    targetRotation,
                    t
                );

            yield return null;
        }

        miniConstellationPivot.rotation =
            targetRotation;

        CopyRotationToSky();

        // The sky constellation is now placed and should no longer
        // follow the tabletop constellation.
        mirrorRotation = false;

        // Release the tabletop constellation to physics.
        miniConstellationRigidbody.useGravity = true;
        miniConstellationRigidbody.isKinematic = false;

        isSettling = false;
    }

    public void HandleSolved()
    {
        if (!isSettling)
        {
            //Debug.Log($"Hiding gimbal: {gimbalRoot.name}", this);
            gimbalRoot.SetActive(false);
            //Debug.Log($"Gimbal activeSelf after hide: {gimbalRoot.activeSelf}", this);

            StartCoroutine(SettleToCanonicalSolve());
        }
    }

    public void HandleReset()
    {
        gimbalRoot.SetActive(true);

        // reset Rigidbody state of mini constellation
        miniConstellationRigidbody.useGravity = false;
        miniConstellationRigidbody.isKinematic = true;

        // restore mini constellation position
        miniConstellationPivot.localPosition =
            miniConstellationStartLocalPosition;

        // restore rotation mirroring
        mirrorRotation = true;

        // Later: reset solve VFX
    }

    [ContextMenu("Snap To Canonical Solve")]
    private void SnapToCanonicalSolve()
    {
        miniConstellationPivot.rotation =
            miniEarthReference.rotation *
            canonicalMiniRelativeRotation;

        CopyRotationToSky();
    }

    [ContextMenu("Test Canonical Settle")]
    private void TestCanonicalSettle()
    {
        if (!Application.isPlaying || isSettling)
        {
            return;
        }

        StartCoroutine(SettleToCanonicalSolve());
    }
}