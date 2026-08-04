using UnityEngine;

public sealed class ConstellationSetup : MonoBehaviour
{
    [Header("Generator")]
    [SerializeField]
    private ConstellationGenerator constellationGenerator;

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

    private bool isInitialized;

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
        constellationGenerator.GenerateConstellation(
            miniVisualContainer);

        constellationGenerator.GenerateConstellation(
            skyVisualContainer);
    }

    private void CopyRotationToSky()
    {
        // This requires Mini Earth Reference and Sky Coordinate Frame
        // to represent equivalent coordinate axes.
        skyConstellationPivot.localRotation =
            miniConstellationPivot.localRotation;
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

        return isValid;
    }
}