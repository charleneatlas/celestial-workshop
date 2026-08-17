using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction.Surfaces;

public class BlueprintController : MonoBehaviour
{
    [Header("Stars")]
    [SerializeField]
    private List<BlueprintStar> stars = new List<BlueprintStar>();

    [Header("Blueprint Surface")]
    [SerializeField]
    private PlaneSurface blueprintPlane;

    [Header("Touch Settings")]
    [Tooltip("Maximum distance along the blueprint surface for a star to count as touched.")]
    [SerializeField]
    private float activationRadius = 0.025f;

    [Header("Constellation Highlighting")]
    [SerializeField]
    private ConstellationSetup constellationSetup;

    [Header("Debug")]
    [SerializeField]
    private bool showDebugLogs = true;

    private BlueprintStar currentStar;

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
            currentStar.SetHighlighted(true);

            if (constellationSetup != null)
            {
                constellationSetup.SetStarHighlighted(
                    currentStar.StarId,
                    true
                );
            }

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

        foreach (BlueprintStar star in stars)
        {
            if (star == null)
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