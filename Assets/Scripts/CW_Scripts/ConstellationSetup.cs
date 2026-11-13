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

    [SerializeField]
    private GameObject handGrabInteractable;

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

    [Header("Sky Constellation Appearance")]
    [SerializeField]
    private ConstellationGenerator.AppearanceSettings tableConstellationAppearance;

    [SerializeField]
    private ConstellationGenerator.AppearanceSettings skyConstellationAppearance;

    [Header("Sky Constellation Highlighting")]
    [SerializeField]
    private SkyConstellationHighlightController skyConstellationHighlightController;

    [Header("Sky Stamp VFX")]
    [SerializeField]
    private Transform skyStampVisualContainer;

    [SerializeField]
    private ConstellationGenerator.AppearanceSettings skyStampAppearance;

    [SerializeField]
    private SkyStamp3DVFX skyStampVFX;

    private GameObject skyStampGeneratedRoot;

    [Header("Runtime")]
    [SerializeField]
    private bool generateOnStart = true;

    [SerializeField]
    private bool mirrorRotation = true;

    [Header("Solve")]
    [SerializeField]
    private ConstellationSolveController solveController;

    [SerializeField]
    private float solveSettleDuration = 0.6f; // seconds

    [SerializeField]
    private StellarForgeTrigger stellarForgeTrigger;

    private bool isSettling;
    private Coroutine settleRoutine;

    private Quaternion canonicalMiniRelativeRotation;

    private Rigidbody miniConstellationRigidbody;

    private Vector3 miniConstellationStartLocalPosition;

    private bool isInitialized;

    // Generation-time poses, so a runtime constellation swap can regenerate in
    // the same frame Start did: the sky pivot relative to the observer (the sky
    // group may have been placed, re-aimed and scaled since), the mini pivot's
    // local rotation, and each sky container's world scale at generation.
    private Quaternion skyPivotGenerationRotation;
    private Quaternion miniPivotGenerationLocalRotation;
    private float skyContainerGenerationScale = 1f;
    private float stampContainerGenerationScale = 1f;

    /// <summary>Raised after LoadConstellation has rebuilt the puzzle around a new star set.</summary>
    public event Action ConstellationLoaded;

    // The tabletop constellation's post-solve grab: offered by the game after
    // the settle, and optionally suppressed (e.g. while the post-puzzle menu is up).
    private bool postSolveGrabAvailable;
    private bool postSolveGrabSuppressed;

    // Sky placement, read by EarthViewCamera to reproduce the generated view.
    public float SkyElevationDegrees => skyElevationDegrees;
    public float SkyAzimuthDegrees => skyAzimuthDegrees;
    public float SkyAngularScale => skyAngularScale;

    /// <summary>Raised when the ease into the canonical solved orientation has finished.</summary>
    public event Action SolveSettled;

    public bool IsSettling => isSettling;

    public ConstellationGenerator Generator => constellationGenerator;

    public Transform SkyConstellationPivot => skyConstellationPivot;
    public Transform SkyVisualContainer => skyVisualContainer;
    public SkyStamp3DVFX SkyStampVFX => skyStampVFX;

    /// <summary>Display name of the current constellation (from the generator).</summary>
    public string ConstellationName =>
        constellationGenerator != null ? constellationGenerator.constellationName : "";

    /// <summary>World rotation of the sky pivot in the canonical solved orientation.</summary>
    public Quaternion CanonicalSkyRotation =>
        skyRotationReference.rotation * canonicalMiniRelativeRotation;

    /// <summary>Whether the sky follows the tabletop constellation's rotation each frame.</summary>
    public bool MirrorRotation
    {
        get => mirrorRotation;
        set => mirrorRotation = value;
    }

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

        RecordGenerationPoses();

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

        // The sky was just generated in its solved orientation, and its pivot
        // (with the solve direction under it) is now centered. Aim the solve
        // direction at the observer before anything rotates the sky, so it
        // always matches the current sky placement settings.
        if (solveController != null)
        {
            solveController.CalibrateSolveDirection();
        }

        // Create constellation copy used for VFX at solve
        GenerateSkyStampConstellation();

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

    private void RecordGenerationPoses()
    {
        skyPivotGenerationRotation =
            Quaternion.Inverse(skyObserverReference.rotation) * skyConstellationPivot.rotation;
        miniPivotGenerationLocalRotation = miniConstellationPivot.localRotation;
        skyContainerGenerationScale = skyVisualContainer.lossyScale.x;
        stampContainerGenerationScale =
            skyStampVisualContainer != null ? skyStampVisualContainer.lossyScale.x : 1f;
    }

    /// <summary>
    /// Replaces the puzzle's constellation at runtime: copies the star set into
    /// the scene's generator (so the Earth view, chart and blueprint read the new
    /// data), regenerates the mini, sky and stamp exactly as Start did, recalibrates
    /// the solve, then resets the puzzle (randomize + intro via PuzzleReset).
    /// </summary>
    public void LoadConstellation(
        string displayName,
        Vector2 artboardSize,
        List<ConstellationGenerator.StarData> stars)
    {
        if (!isInitialized || stars == null || stars.Count == 0)
        {
            Debug.LogWarning("ConstellationSetup: can't load a constellation before setup or with no stars.", this);
            return;
        }

        StopAllCoroutines();
        isSettling = false;

        // Back to the unsolved rest state (mini kinematic at home, mirroring on).
        HandleReset();

        // A fresh list instance: consumers (EarthViewCamera) cache by reference.
        constellationGenerator.constellationName = displayName;
        constellationGenerator.artboardSize = artboardSize;
        constellationGenerator.stars = new List<ConstellationGenerator.StarData>(stars);

        // Old roots go now: Destroy is deferred and the generator finds roots by name.
        DetachGeneratedRoot(miniVisualContainer);
        DetachGeneratedRoot(skyVisualContainer);
        DetachGeneratedRoot(skyStampVisualContainer);

        // Generation frame: the poses Start generated in.
        miniConstellationPivot.localRotation = miniPivotGenerationLocalRotation;
        skyConstellationPivot.rotation = skyObserverReference.rotation * skyPivotGenerationRotation;

        GenerateConstellations(SkyGroupScale(skyVisualContainer, skyContainerGenerationScale));
        CenterRotationPivot(miniVisualContainer, miniConstellationPivot);
        CenterRotationPivot(skyVisualContainer, skyConstellationPivot);

        if (solveController != null)
        {
            solveController.CalibrateSolveDirection();
        }

        GenerateSkyStampConstellation(SkyGroupScale(skyStampVisualContainer, stampContainerGenerationScale));
        miniConstellationStartLocalPosition = miniConstellationPivot.localPosition;

        Quaternion generatedSolvedSkyRotation = skyConstellationPivot.rotation;
        AlignSkyRotationReferenceToView();
        canonicalMiniRelativeRotation =
            Quaternion.Inverse(skyRotationReference.rotation) * generatedSolvedSkyRotation;
        CopyRotationToSky();

        foreach (StarChartLayout chart in FindObjectsByType<StarChartLayout>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            chart.Rebuild();
        }

        Debug.Log($"ConstellationSetup: loaded '{displayName}' ({stars.Count} stars).", this);
        ConstellationLoaded?.Invoke();

        if (solveController != null)
        {
            solveController.ResetSolve();
        }

        CopyRotationToSky();
    }

    // How much the sky group has been scaled since generation (NearFieldSkyProxy
    // shrinks it into the volume). The generator places sky stars at world
    // distances from the observer, so those distances must shrink by this too.
    private static float SkyGroupScale(Transform container, float generationScale)
    {
        return container != null && generationScale > 0f
            ? container.lossyScale.x / generationScale
            : 1f;
    }

    private static void DetachGeneratedRoot(Transform container)
    {
        if (container == null)
        {
            return;
        }

        Transform root = container.Find("Generated Constellation");
        if (root == null)
        {
            return;
        }

        root.gameObject.SetActive(false);
        root.SetParent(null, false);
        Destroy(root.gameObject);
    }

    private void GenerateConstellations()
    {
        GenerateConstellations(1f);
    }

    private void GenerateConstellations(float skyGroupScale)
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
                skyDistanceScale * skyGroupScale,
                new Vector3(
                    -skyElevationDegrees,
                    skyAzimuthDegrees,
                    0f
                ),
                skyAngularScale,
                skyDepthScale,
                false
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

    private void GenerateSkyStampConstellation()
    {
        GenerateSkyStampConstellation(1f);
    }

    private void GenerateSkyStampConstellation(float skyGroupScale)
    {
        skyStampGeneratedRoot =
            constellationGenerator.GenerateConstellation(
                skyStampVisualContainer,
                skyStampAppearance,
                skyObserverReference,
                skyDistanceScale * skyGroupScale,
                new Vector3(
                    -skyElevationDegrees,
                    skyAzimuthDegrees,
                    0f
                ),
                skyAngularScale,
                skyDepthScale,
                false
            );

        if (skyStampVFX != null)
        {
            skyStampVFX.Initialize(skyStampGeneratedRoot);
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

        if (handGrabInteractable == null)
        {
            Debug.LogError(
                "ConstellationSetup is missing the Hand Grab Interactable.",
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

        // The swap replaced the sky star's material; re-apply the current
        // normal / near-solve / solved tint on top of it.
        if (skyConstellationHighlightController != null)
        {
            skyConstellationHighlightController.Refresh();
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

        miniConstellationPivot.rotation = targetRotation;

        CopyRotationToSky();

        // The sky constellation is now placed and should no longer
        // follow the tabletop constellation.
        mirrorRotation = false;

        // Play celebratory VFX
        if (skyStampVFX != null)
        {
            skyStampVFX.Play();
        }

        // Release the tabletop constellation to physics to allow to fall to tabletop.
        miniConstellationRigidbody.useGravity = true;
        miniConstellationRigidbody.isKinematic = false;

        // Turn on the tabletop constellation's HandGrabInteractable to allow it to be grabbed by player
        // (unless something has it suppressed right now).
        postSolveGrabAvailable = true;
        ApplyPostSolveGrab();

        isSettling = false;

        SolveSettled?.Invoke();
    }

    /// <summary>
    /// Suppress (or stop suppressing) the post-solve grab without changing
    /// whether the game currently offers it. Releasing suppression restores
    /// the gameplay state: grabbable only if solved and settled.
    /// </summary>
    public void SetPostSolveGrabSuppressed(bool suppressed)
    {
        postSolveGrabSuppressed = suppressed;
        ApplyPostSolveGrab();
    }

    private void ApplyPostSolveGrab()
    {
        if (handGrabInteractable != null)
        {
            handGrabInteractable.SetActive(postSolveGrabAvailable && !postSolveGrabSuppressed);
        }
    }

    public void HandleSolved()
    {
        if (!isSettling)
        {
            //Debug.Log($"Hiding gimbal: {gimbalRoot.name}", this);
            gimbalRoot.SetActive(false);
            //Debug.Log($"Gimbal activeSelf after hide: {gimbalRoot.activeSelf}", this);

            settleRoutine = StartCoroutine(SettleToCanonicalSolve());
        }
    }

    public void HandleReset()
    {
        // A reset mid-settle (e.g. back to the title right after solving)
        // must not finish the solve later: no stamp, no drop, no reveal.
        if (settleRoutine != null)
        {
            StopCoroutine(settleRoutine);
            settleRoutine = null;
        }

        isSettling = false;

        // Turn the gimbal back on
        gimbalRoot.SetActive(true);

        // Turn back off the ability to grab mini constellation
        postSolveGrabAvailable = false;
        ApplyPostSolveGrab();

        // reset Rigidbody state of mini constellation
        miniConstellationRigidbody.useGravity = false;
        miniConstellationRigidbody.isKinematic = true;

        // restore mini constellation position
        miniConstellationPivot.localPosition = miniConstellationStartLocalPosition;

        // restore rotation mirroring
        mirrorRotation = true;

        // Reset solve VFX
        if (skyStampVFX != null)
        {
            skyStampVFX.ResetVFX();
        }

        // Reset stellar forge
        if (stellarForgeTrigger != null)
        {
            stellarForgeTrigger.ResetTrigger();
        }
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

        settleRoutine = StartCoroutine(SettleToCanonicalSolve());
    }
}