using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class ConstellationGenerator : MonoBehaviour
{
    [Serializable]
    public class StarData
    {
        public string starName;

        // Raw center position copied from Affinity Designer.
        public Vector2 affinityPosition;

        public float distanceLightYears;
    }

    [Header("Affinity Designer")]
    public Vector2 artboardSize = new Vector2(1200f, 800f);

    [Header("Star Data")]
    public List<StarData> stars = new List<StarData>();

    [Header("Sculpture Settings")]
    [Min(0.01f)]
    public float maximumRadius = 1f;

    [Min(0.01f)]
    public float angularSpread = 0.7f;

    [Min(0.001f)]
    public float starDiameter = 0.05f;

    public bool useLogarithmicDistance;

    [Range(0.01f, 0.9f)]
    public float logarithmicMinimumRadius = 0.2f;

    [Header("Optional Appearance")]
    public Material starMaterial;
    public Material lineMaterial;

    [Min(0.001f)]
    public float lineWidth = 0.01f;

    [Header("Debug Labels")]
    public bool showDebugLabels = true;
    public GameObject starLabelPrefab;
    public float labelOffset = 0.08f;

    [Header("Earth View Debug")]
    public bool showEarthViewMarker = true;
    public GameObject earthViewMarkerPrefab;

    private const string GeneratedRootName = "Generated Constellation";

    [ContextMenu("Rebuild Constellation")]
    public void RebuildConstellation()
    {
        ClearGeneratedObjects();

        if (stars == null || stars.Count == 0)
        {
            Debug.LogWarning("Add at least one star before rebuilding.");
            return;
        }

        if (artboardSize.x <= 0f || artboardSize.y <= 0f)
        {
            Debug.LogError("Artboard width and height must be greater than zero.");
            return;
        }

        float maximumDistance = 0f;
        float minimumDistance = float.MaxValue;

        foreach (StarData star in stars)
        {
            if (star.distanceLightYears <= 0f)
            {
                Debug.LogError(
                    $"Distance for {star.starName} must be greater than zero."
                );
                return;
            }

            maximumDistance =
                Mathf.Max(maximumDistance, star.distanceLightYears);

            minimumDistance =
                Mathf.Min(minimumDistance, star.distanceLightYears);
        }

        GameObject generatedRootObject =
            new GameObject(GeneratedRootName);

        generatedRootObject.transform.SetParent(transform, false);

        if (showEarthViewMarker && earthViewMarkerPrefab != null)
        {
            GameObject earthMarker = Instantiate(
                earthViewMarkerPrefab,
                generatedRootObject.transform
            );

            earthMarker.name = "Earth View Marker";
            earthMarker.transform.localPosition = Vector3.zero;
            earthMarker.transform.localRotation = Quaternion.identity;
            earthMarker.transform.localScale = Vector3.one;
        }

        Vector3[] positions = new Vector3[stars.Count];

        for (int i = 0; i < stars.Count; i++)
        {
            StarData star = stars[i];

            Vector2 normalizedBlueprintPosition =
                ConvertAffinityPosition(star.affinityPosition);

            Vector3 direction = new Vector3(
                normalizedBlueprintPosition.x * angularSpread,
                normalizedBlueprintPosition.y * angularSpread,
                1f
            ).normalized;

            float radialDistance = CalculateRadialDistance(
                star.distanceLightYears,
                minimumDistance,
                maximumDistance
            );

            Vector3 localPosition = direction * radialDistance;
            positions[i] = localPosition;

            // Create an unscaled root for this star.
            GameObject starRoot = new GameObject(star.starName);

            starRoot.transform.SetParent(
                generatedRootObject.transform,
                false
            );

            starRoot.transform.localPosition = localPosition;

            // Create the visible sphere beneath it.
            GameObject sphere =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);

            sphere.name = "Sphere";

            sphere.transform.SetParent(
                starRoot.transform,
                false
            );

            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localRotation = Quaternion.identity;
            sphere.transform.localScale =
                Vector3.one * starDiameter;

            if (starMaterial != null)
            {
                sphere.GetComponent<Renderer>().sharedMaterial =
                    starMaterial;
            }

            // Create the label as a sibling of the sphere.
            if (showDebugLabels && starLabelPrefab != null)
            {
                GameObject labelObject = Instantiate(
                    starLabelPrefab,
                    starRoot.transform
                );

                labelObject.name = "Label";
                labelObject.transform.localPosition =
                    Vector3.up * labelOffset;

                TMP_Text labelText =
                    labelObject.GetComponentInChildren<TMP_Text>();

                if (labelText != null)
                {
                    labelText.text =
                        $"{star.starName}\n{star.distanceLightYears:0} ly";
                }
            }
        }

        for (int i = 0; i < positions.Length - 1; i++)
        {
            CreateCylinderBetweenPoints(
                positions[i],
                positions[i + 1],
                generatedRootObject.transform
            );
        }
    }

    private void CreateCylinderBetweenPoints(
    Vector3 start,
    Vector3 end,
    Transform parent
)
    {
        Vector3 direction = end - start;
        float length = direction.magnitude;

        if (length <= Mathf.Epsilon)
        {
            return;
        }

        GameObject cylinder =
            GameObject.CreatePrimitive(PrimitiveType.Cylinder);

        cylinder.name = "Connection";
        cylinder.transform.SetParent(parent, false);

        // Place it halfway between the two stars.
        cylinder.transform.localPosition = (start + end) * 0.5f;

        // Unity cylinders extend along their local Y axis.
        cylinder.transform.localRotation =
            Quaternion.FromToRotation(Vector3.up, direction.normalized);

        // A Unity primitive cylinder has a default height of 2 units,
        // so the Y scale is half the desired connection length.
        cylinder.transform.localScale = new Vector3(
            lineWidth,
            length * 0.5f,
            lineWidth
        );

        if (lineMaterial != null)
        {
            cylinder.GetComponent<Renderer>().sharedMaterial =
                lineMaterial;
        }

        // Prevent the connection rods from interfering with hand grabbing.
        Collider cylinderCollider = cylinder.GetComponent<Collider>();

        if (cylinderCollider != null)
        {
            if (Application.isPlaying)
            {
                Destroy(cylinderCollider);
            }
            else
            {
                DestroyImmediate(cylinderCollider);
            }
        }
    }

    private Vector2 ConvertAffinityPosition(Vector2 affinityPosition)
    {
        float commonScale =
            Mathf.Min(artboardSize.x, artboardSize.y) * 0.5f;

        float centeredX =
            affinityPosition.x - artboardSize.x * 0.5f;

        // Affinity Y increases downward. Unity Y increases upward.
        float centeredY =
            artboardSize.y * 0.5f - affinityPosition.y;

        return new Vector2(
            centeredX / commonScale,
            centeredY / commonScale
        );
    }

    private float CalculateRadialDistance(
        float distance,
        float minimumDistance,
        float maximumDistance
    )
    {
        if (!useLogarithmicDistance)
        {
            // Uniform scaling preserves the true distance ratios.
            return distance / maximumDistance * maximumRadius;
        }

        float minimumLog = Mathf.Log10(minimumDistance);
        float maximumLog = Mathf.Log10(maximumDistance);
        float distanceLog = Mathf.Log10(distance);

        float normalizedDistance = Mathf.InverseLerp(
            minimumLog,
            maximumLog,
            distanceLog
        );

        return Mathf.Lerp(
            maximumRadius * logarithmicMinimumRadius,
            maximumRadius,
            normalizedDistance
        );
    }

    [ContextMenu("Clear Constellation")]
    public void ClearGeneratedObjects()
    {
        Transform existing = transform.Find(GeneratedRootName);

        if (existing == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(existing.gameObject);
        }
        else
        {
            DestroyImmediate(existing.gameObject);
        }
    }
}