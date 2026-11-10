using UnityEngine;

public class GeneratedConstellationStar : MonoBehaviour
{
    [SerializeField]
    private string starId;

    private Renderer starRenderer;
    private Material normalMaterial;
    private Material highlightMaterial;

    public string StarId => starId;

    /// <summary>True while marked (showing the highlight material).</summary>
    public bool IsHighlighted { get; private set; }

    public Renderer StarRenderer => starRenderer;

    public void Initialize(
        string id,
        Renderer renderer,
        Material normal,
        Material highlight)
    {
        starId = id;
        starRenderer = renderer;
        normalMaterial = normal;
        highlightMaterial = highlight;

        SetHighlighted(false);
    }

    public void SetHighlighted(bool highlighted)
    {
        if (starRenderer == null)
        {
            return;
        }

        IsHighlighted = highlighted && highlightMaterial != null;

        if (highlighted && highlightMaterial != null)
        {
            starRenderer.sharedMaterial = highlightMaterial;
        }
        else if (normalMaterial != null)
        {
            starRenderer.sharedMaterial = normalMaterial;
        }
    }
}