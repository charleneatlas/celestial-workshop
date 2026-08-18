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

    [Serializable]
    public class AppearanceSettings
    {
        public Material starMaterial;
        public Material starHighlightMaterial;
        public Material lineMaterial;

        [Min(0.001f)]
        public float starDiameter = 0.05f;

        [Min(0.001f)]
        public float lineWidth = 0.01f;
    }

    [Header("Affinity Designer")]
    public Vector2 artboardSize = new Vector2(1920f, 1080f);

    [Header("Star Data")]
    public List<StarData> stars = new List<StarData>();

    [Header("Sculpture Settings")]
    [Min(0.01f)]
    public float maximumRadius = 1f;

    [Min(0.01f)]
    public float angularSpread = 0.7f;

    public bool useLogarithmicDistance;

    [Range(0.01f, 0.9f)]
    public float logarithmicMinimumRadius = 0.2f;

    [Header("Preview / Default Appearance")]
    [Tooltip("The appearance of runtime generated constellations is set in ConstellationSetup()")]
    public Material starMaterial;

    public Material lineMaterial;

    [Min(0.001f)]
    public float starDiameter = 0.05f;

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

    [ContextMenu("Rebuild Preview Constellation")]
    public void RebuildConstellationPreview()
    {
        GenerateConstellation(transform);
    }

    public GameObject GenerateConstellation(
        Transform destinationParent,
        AppearanceSettings appearance = null,
        Transform observerReference = null,
        float distanceScale = 1f,
        Vector3 directionEulerOffset = default,
        float angularScale = 1f,
        float depthScale = 1f)
    {
        if (destinationParent == null)
        {
            Debug.LogError(
                "A destination parent is required to generate the constellation.",
                this
            );

            return null;
        }

        Material activeStarMaterial =
            appearance != null ? appearance.starMaterial : starMaterial;

        Material activeStarHighlightMaterial =
            appearance != null
                ? appearance.starHighlightMaterial
                : null;

        Material activeLineMaterial =
            appearance != null ? appearance.lineMaterial : lineMaterial;

        float activeStarDiameter =
            appearance != null ? appearance.starDiameter : starDiameter;

        float activeLineWidth =
            appearance != null ? appearance.lineWidth : lineWidth;

        ClearGeneratedObjects(destinationParent);

        if (stars == null || stars.Count == 0)
        {
            Debug.LogWarning("Add at least one star before rebuilding.");
            return null;
        }

        if (artboardSize.x <= 0f || artboardSize.y <= 0f)
        {
            Debug.LogError(
                "Artboard width and height must be greater than zero."
            );

            return null;
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

                return null;
            }

            maximumDistance =
                Mathf.Max(maximumDistance, star.distanceLightYears);

            minimumDistance =
                Mathf.Min(minimumDistance, star.distanceLightYears);
        }

        GameObject generatedRootObject =
            new GameObject(GeneratedRootName);

        generatedRootObject.transform.SetParent(
            destinationParent,
            false
        );

        generatedRootObject.transform.localPosition = Vector3.zero;
        generatedRootObject.transform.localRotation = Quaternion.identity;
        generatedRootObject.transform.localScale = Vector3.one;

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
                normalizedBlueprintPosition.x * angularSpread * angularScale,
                normalizedBlueprintPosition.y * angularSpread * angularScale,
                1f
            ).normalized;

            direction =
                Quaternion.Euler(directionEulerOffset) *
                direction;

            float radialDistance = CalculateRadialDistance(
                star.distanceLightYears,
                minimumDistance,
                maximumDistance
            );

            Vector3 localPosition;

            if (observerReference == null)
            {
                // Existing behavior.
                // Used for the tabletop constellation and editor preview.
                localPosition = direction * radialDistance;
            }
            else
            {
                // For the distant sky constellation, the rays originate
                // from the fixed observer position in the workshop.

                Vector3 worldDirection =
                    generatedRootObject.transform.TransformDirection(direction);

                float scaledDistance =
                    radialDistance * distanceScale;

                // The farthest star defines the distant "shell."
                float shellDistance =
                    maximumRadius * distanceScale;

                // depthScale = 1:
                //     preserve the original depth.
                //
                // depthScale = 0:
                //     pull every star onto the same distant shell.
                //
                // Values in between preserve some of the relative depth.
                float compressedDistance =
                    Mathf.Lerp(
                        shellDistance,
                        scaledDistance,
                        depthScale
                    );

                Vector3 worldPosition =
                    observerReference.position +
                    worldDirection * compressedDistance;

                // Convert the desired world position back into the
                // generated constellation's local coordinate system.
                localPosition =
                    generatedRootObject.transform.InverseTransformPoint(
                        worldPosition
                    );
            }

            positions[i] = localPosition;

            // Unscaled root representing the star's position.
            GameObject starRoot = new GameObject(star.starName);

            starRoot.transform.SetParent(
                generatedRootObject.transform,
                false
            );

            starRoot.transform.localPosition = localPosition;
            starRoot.transform.localRotation = Quaternion.identity;
            starRoot.transform.localScale = Vector3.one;

            // Visible sphere beneath the unscaled star root.
            GameObject sphere =
                GameObject.CreatePrimitive(PrimitiveType.Sphere);

            sphere.name = "Sphere";
            sphere.tag = "Star";

            sphere.transform.SetParent(
                starRoot.transform,
                false
            );

            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localRotation = Quaternion.identity;
            sphere.transform.localScale =
                Vector3.one * activeStarDiameter;

            Renderer sphereRenderer = sphere.GetComponent<Renderer>();

            if (activeStarMaterial != null)
            {
                sphereRenderer.sharedMaterial = activeStarMaterial;
            }
            else
            {
                Debug.LogWarning(
                    "No star material assigned. Generated stars will use the default material.",
                    this
                );
            }

            GeneratedConstellationStar generatedStar =
                starRoot.AddComponent<GeneratedConstellationStar>();

            generatedStar.Initialize(
                star.starName,
                sphereRenderer,
                activeStarMaterial,
                activeStarHighlightMaterial
            );

            if (showDebugLabels && starLabelPrefab != null)
            {
                GameObject labelObject = Instantiate(
                    starLabelPrefab,
                    starRoot.transform
                );

                labelObject.name = "Label";

                labelObject.transform.localPosition =
                    Vector3.up * labelOffset;

                labelObject.transform.localRotation =
                    Quaternion.identity;

                TMP_Text labelText =
                    labelObject.GetComponentInChildren<TMP_Text>();

                if (labelText != null)
                {
                    labelText.text =
                        $"{star.starName}\n" +
                        $"{star.distanceLightYears:0} ly";
                }
            }
        }

        for (int i = 0; i < positions.Length - 1; i++)
        {
            CreateCylinderBetweenPoints(
                positions[i],
                positions[i + 1],
                generatedRootObject.transform,
                activeLineMaterial,
                activeLineWidth
            );
        }

        return generatedRootObject;
    }

    private void CreateCylinderBetweenPoints(
    Vector3 start,
    Vector3 end,
    Transform parent,
    Material material,
    float width
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
            width,
            length * 0.5f,
            width
        );

        if (material != null)
        {
            cylinder.GetComponent<Renderer>().sharedMaterial = material;
        }
        else
        {
            Debug.LogWarning(
                "No line material assigned. Generated connections will use the default material.",
                this
            );
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

    [ContextMenu("Clear Preview Constellation")]
    public void ClearGeneratedObjectsPreview()
    {
        ClearGeneratedObjects(transform);
    }

    private void ClearGeneratedObjects(Transform destinationParent)
    {
        if (destinationParent == null)
        {
            return;
        }

        Transform existingGeneratedRoot =
            destinationParent.Find(GeneratedRootName);

        if (existingGeneratedRoot == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(existingGeneratedRoot.gameObject);
        }
        else
        {
            DestroyImmediate(existingGeneratedRoot.gameObject);
        }
    }

    public bool TryGetStarData(string starId, out StarData starData)
    {
        foreach (StarData star in stars)
        {
            if (string.Equals(
                star.starName,
                starId,
                StringComparison.OrdinalIgnoreCase))
            {
                starData = star;
                return true;
            }
        }

        starData = null;
        return false;
    }
}