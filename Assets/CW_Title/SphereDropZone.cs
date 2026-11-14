using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A spherical drop zone (e.g. the puzzle's glass sphere). Radius is in world
/// metres, measured from the center Transform (this object if empty).
///
/// Drop feedback: while anything held reports itself inside (SetHovered, e.g.
/// a title letter or a box sculpture), the Glow Renderer's colour (the
/// globe's rim) eases toward Hover Color, so the player can see that letting
/// go here will be accepted.
/// </summary>
public sealed class SphereDropZone : MonoBehaviour, ITitleDropZone
{
    [SerializeField]
    private Transform center;

    [SerializeField]
    [Min(0.001f)]
    private float radius = 0.2f;

    [Header("Drop feedback")]
    [Tooltip("Renderer that glows while something held is inside (e.g. the globe's Rim Sphere). Optional.")]
    [SerializeField]
    private Renderer glowRenderer;

    [Tooltip("Colour property on the glow renderer's material.")]
    [SerializeField]
    private string glowColorProperty = "_Color";

    [Tooltip("Colour while something held is inside (HDR: above 1 glows brighter).")]
    [SerializeField]
    [ColorUsage(true, true)]
    private Color hoverColor = new Color(0.9f, 1.6f, 2.2f, 0.9f);

    [Tooltip("Seconds to ease in and out.")]
    [SerializeField]
    [Min(0.01f)]
    private float fadeSeconds = 0.15f;

    private readonly HashSet<Object> hovering = new HashSet<Object>();
    private MaterialPropertyBlock block;
    private Color baseColor;
    private bool hasBaseColor;
    private float weight;

    public Transform Center
    {
        get => center != null ? center : transform;
        set => center = value;
    }

    public float Radius
    {
        get => radius;
        set => radius = Mathf.Max(0.001f, value);
    }

    /// <summary>True while at least one held object is inside.</summary>
    public bool IsHovered => hovering.Count > 0;

    public bool Contains(Vector3 worldPoint)
    {
        return (worldPoint - Center.position).sqrMagnitude <= radius * radius;
    }

    /// <summary>Held objects report entering (true) and leaving (false) the zone.</summary>
    public void SetHovered(Object source, bool hovered)
    {
        if (source == null)
        {
            return;
        }

        if (hovered)
        {
            hovering.Add(source);
        }
        else
        {
            hovering.Remove(source);
        }
    }

    private void OnDisable()
    {
        hovering.Clear();
        weight = 0f;
        ApplyGlow();
    }

    private void Update()
    {
        // Drop sources that were destroyed or disabled without reporting.
        if (hovering.Count > 0)
        {
            hovering.RemoveWhere(o => o == null || (o is Behaviour b && !b.isActiveAndEnabled));
        }

        float target = hovering.Count > 0 ? 1f : 0f;
        if (Mathf.Approximately(weight, target))
        {
            return;
        }

        weight = Mathf.MoveTowards(weight, target, Time.deltaTime / fadeSeconds);
        ApplyGlow();
    }

    private void ApplyGlow()
    {
        if (glowRenderer == null)
        {
            return;
        }

        block ??= new MaterialPropertyBlock();
        if (weight <= 0f)
        {
            glowRenderer.SetPropertyBlock(null);
            return;
        }

        if (!hasBaseColor)
        {
            Material material = glowRenderer.sharedMaterial;
            baseColor = material != null && material.HasProperty(glowColorProperty)
                ? material.GetColor(glowColorProperty)
                : Color.white;
            hasBaseColor = true;
        }

        glowRenderer.GetPropertyBlock(block);
        block.SetColor(glowColorProperty, Color.Lerp(baseColor, hoverColor, weight));
        glowRenderer.SetPropertyBlock(block);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireSphere(Center.position, radius);
    }
}
