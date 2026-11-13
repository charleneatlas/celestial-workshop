using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction.Surfaces;
using TMPro;

public class BlueprintController : MonoBehaviour
{
    [Header("Stars")]
    [SerializeField]
    private List<BlueprintStar> stars = new List<BlueprintStar>();

    [Header("Blueprint Surface")]
    [SerializeField]
    private PlaneSurface blueprintPlane;

    [SerializeField]
    private Transform blueprintVisual;

    [Header("Touch Settings")]
    [Tooltip("Maximum distance along the blueprint surface for a star to count as touched.")]
    [SerializeField]
    private float activationRadius = 0.025f;

    [Header("Constellation Highlighting")]
    [SerializeField]
    private ConstellationSetup constellationSetup;

    [SerializeField]
    private ConstellationGenerator constellationGenerator;

    [SerializeField]
    private TMP_Text distanceLabel;

    [SerializeField]
    private Vector3 distanceLabelOffset =
        new Vector3(0f, -0.025f, 0f);

    [Header("Distance Label Style")]
    [Tooltip("Optional: fades the label between its paper and lens colours as the chart docks.")]
    [SerializeField]
    private StarChartLayout chartLayout;

    [Tooltip("Text colour on the paper.")]
    [SerializeField]
    private Color distanceTextColor = new Color(0.10f, 0.10f, 0.12f, 1f);

    [Tooltip("Halo behind the text on the paper (alpha 0 = no halo).")]
    [SerializeField]
    private Color distanceHaloColor = new Color(0.98f, 0.95f, 0.86f, 0.95f);

    [Tooltip("Text colour while docked as a lens over the Earth view.")]
    [SerializeField]
    private Color distanceLensTextColor = new Color(0.98f, 0.96f, 0.90f, 1f);

    [Tooltip("Halo while docked as a lens.")]
    [SerializeField]
    private Color distanceLensHaloColor = new Color(0.03f, 0.04f, 0.10f, 0.9f);

    [Tooltip("Halo spread around the letters.")]
    [Range(0f, 1f)]
    [SerializeField]
    private float distanceHaloSize = 0.6f;

    [Tooltip("Letter weight (TMP face dilate): above 0 is bolder.")]
    [Range(-0.5f, 0.5f)]
    [SerializeField]
    private float distanceTextWeight = 0.15f;

    [Tooltip("Keeps the label at least this far inside the chart's rim (needs Chart Layout).")]
    [SerializeField]
    private float distanceEdgeMargin = 0.008f;

    private Material distanceMaterial;

    [Header("Tutorial")]
    [SerializeField]
    private GameObject tutorialHandVisual;

    private bool tutorialDismissed;

    [Header("Debug")]
    [SerializeField]
    private bool showDebugLogs = true;

    private BlueprintStar currentStar;

    /// <summary>Raised with the star id each time a finger lands on a chart star.</summary>
    public event System.Action<string> StarTouched;

    private void Start()
    {
        foreach (BlueprintStar star in stars)
        {
            if (star != null)
            {
                star.SetHighlighted(false);

                if (constellationSetup != null)
                {
                    constellationSetup.SetStarHighlighted(
                        star.StarId,
                        false
                    );
                }
                // Hide light year distance label
                HideDistance();
            }
        }
    }

    public void UpdatePokePosition(Vector3 worldPosition)
    {
        BlueprintStar closestStar = FindClosestStar(worldPosition);

        if (closestStar == currentStar)
        {
            return;
        }

        // Turn off the previous highlight.
        if (currentStar != null)
        {
            currentStar.SetHighlighted(false);

            if (constellationSetup != null)
            {
                constellationSetup.SetStarHighlighted(
                    currentStar.StarId,
                    false
                );
            }

            // Hide light year distance label
            HideDistance();

            if (showDebugLogs)
            {
                Debug.Log(
                    $"Blueprint star exited: {currentStar.StarId}"
                );
            }
        }

        currentStar = closestStar;

        // Turn on the new highlight.
        if (currentStar != null)
        {
            // Player has successfully used the blueprint interaction.
            DismissTutorial();

            currentStar.SetHighlighted(true);

            if (constellationSetup != null)
            {
                constellationSetup.SetStarHighlighted(
                    currentStar.StarId,
                    true
                );
            }

            // Show distance of current star
            ShowDistance(currentStar);
            StarTouched?.Invoke(currentStar.StarId);

            if (showDebugLogs)
            {
                Debug.Log(
                    $"Blueprint star touched: {currentStar.StarId}"
                );
            }
        }
    }

    public void EndPoke()
    {
        if (currentStar != null)
        {
            currentStar.SetHighlighted(false);

            if (constellationSetup != null)
            {
                constellationSetup.SetStarHighlighted(
                    currentStar.StarId,
                    false
                );
            }

            // Hide light year distance label
            HideDistance();

            if (showDebugLogs)
            {
                Debug.Log(
                    $"Blueprint poke ended: {currentStar.StarId}"
                );
            }
        }

        currentStar = null;
    }

    private BlueprintStar FindClosestStar(Vector3 worldPosition)
    {
        if (blueprintPlane == null)
        {
            Debug.LogWarning(
                "BlueprintController needs a PlaneSurface reference."
            );

            return null;
        }

        // Project the finger position onto the actual paper plane.
        Plane plane = blueprintPlane.GetPlane();
        Vector3 surfacePoint =
            plane.ClosestPointOnPlane(worldPosition);

        BlueprintStar closestStar = null;
        float closestDistance = Mathf.Infinity;

        // The chart's active targets follow the loaded constellation
        // (generated for letters); the authored list is the fallback.
        IReadOnlyList<BlueprintStar> candidates =
            chartLayout != null ? chartLayout.ActiveTargets : stars;
        foreach (BlueprintStar star in candidates)
        {
            if (star == null || !star.gameObject.activeInHierarchy)
            {
                continue;
            }

            // Also project the star target onto the paper plane.
            Vector3 starSurfacePoint =
                plane.ClosestPointOnPlane(star.transform.position);

            float distance = Vector3.Distance(
                surfacePoint,
                starSurfacePoint
            );

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestStar = star;
            }
        }

        if (closestDistance <= activationRadius)
        {
            return closestStar;
        }

        return null;
    }

    private void ShowDistance(BlueprintStar star)
    {
        if (star == null ||
            distanceLabel == null ||
            constellationGenerator == null)
        {
            return;
        }

        if (constellationGenerator.TryGetStarData(
            star.StarId,
            out ConstellationGenerator.StarData starData))
        {
            distanceLabel.text =
                $"{starData.distanceLightYears:0}\nlight years";

            distanceLabel.transform.position =
                star.transform.position +
                blueprintVisual.TransformDirection(distanceLabelOffset);

            StyleDistanceLabel();
            distanceLabel.gameObject.SetActive(true);
            KeepDistanceLabelOnChart();
        }
    }

    // Slides the label sideways (and, if needed, toward the centre) so the
    // whole text block stays inside the round chart.
    private void KeepDistanceLabelOnChart()
    {
        if (chartLayout == null || distanceLabel == null)
        {
            return;
        }

        Transform chart = chartLayout.transform;
        float toChart = distanceLabel.transform.lossyScale.x / Mathf.Max(1e-6f, chart.lossyScale.x);
        Vector2 size = distanceLabel.GetPreferredValues(distanceLabel.text);
        float halfWidth = size.x * 0.5f * toChart;
        float halfHeight = size.y * 0.5f * toChart;
        float radius = Mathf.Max(0f, chartLayout.ChartRadius - distanceEdgeMargin);

        Vector3 local = chart.InverseTransformPoint(distanceLabel.transform.position);
        float maxZ = Mathf.Max(0f, radius - halfHeight);
        local.z = Mathf.Clamp(local.z, -maxZ, maxZ);

        float farZ = Mathf.Abs(local.z) + halfHeight;
        float halfChord = Mathf.Sqrt(Mathf.Max(0f, radius * radius - farZ * farZ));
        float maxX = Mathf.Max(0f, halfChord - halfWidth);
        local.x = Mathf.Clamp(local.x, -maxX, maxX);

        distanceLabel.transform.position = chart.TransformPoint(local);
    }

    private void LateUpdate()
    {
        // Keep the colours right while the chart docks or undocks mid-poke.
        if (distanceLabel != null && distanceLabel.gameObject.activeSelf && chartLayout != null)
        {
            StyleDistanceLabel();
        }
    }

    // Dark text with a light halo on the paper; light text with a dark halo
    // when docked as a lens. Uses its own copy of the font material.
    private void StyleDistanceLabel()
    {
        if (distanceLabel == null)
        {
            return;
        }

        if (distanceMaterial == null)
        {
            distanceMaterial = new Material(distanceLabel.fontSharedMaterial)
            {
                name = distanceLabel.fontSharedMaterial.name + " (distance label)",
                hideFlags = HideFlags.DontSave
            };
            distanceMaterial.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            distanceMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            distanceMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
            distanceLabel.fontSharedMaterial = distanceMaterial;
        }

        float lens = chartLayout != null ? chartLayout.LensBlend : 0f;
        distanceMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, distanceTextWeight);
        distanceMaterial.SetFloat(ShaderUtilities.ID_UnderlayDilate, distanceHaloSize);
        distanceMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, Mathf.Lerp(0.3f, 0.8f, distanceHaloSize));
        distanceMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, Color.Lerp(distanceHaloColor, distanceLensHaloColor, lens));
        ShaderUtilities.GetShaderPropertyIDs();
        ShaderUtilities.UpdateShaderRatios(distanceMaterial);
        distanceLabel.color = Color.Lerp(distanceTextColor, distanceLensTextColor, lens);
    }

    private void OnDestroy()
    {
        if (distanceMaterial != null)
        {
            Destroy(distanceMaterial);
        }
    }

    private void HideDistance()
    {
        if (distanceLabel != null)
        {
            distanceLabel.gameObject.SetActive(false);
        }
    }

    private void DismissTutorial()
    {
        if (tutorialDismissed)
        {
            return;
        }

        tutorialDismissed = true;

        if (tutorialHandVisual != null)
        {
            tutorialHandVisual.SetActive(false);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;

        foreach (BlueprintStar star in stars)
        {
            if (star != null)
            {
                Gizmos.DrawWireSphere(
                    star.transform.position,
                    activationRadius
                );
            }
        }
    }
}