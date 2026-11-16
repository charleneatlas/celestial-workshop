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

    /// <summary>
    /// Per-star value (0..1) the SkyStarTwinkle shader uses to decide whether
    /// this star twinkles and with what phase. Set once when the constellation
    /// is built; ignored by materials without _TwinkleSeed (the wooden model).
    /// </summary>
    public float TwinkleSeed { get; private set; } = 1f;

    public static readonly int TwinkleSeedId = Shader.PropertyToID("_TwinkleSeed");

    private static MaterialPropertyBlock seedBlock;

    public void SetTwinkleSeed(float seed)
    {
        TwinkleSeed = seed;
        if (starRenderer == null || IsHighlighted || starRenderer.sharedMaterial == null ||
            !starRenderer.sharedMaterial.HasProperty(TwinkleSeedId))
        {
            return;
        }

        seedBlock ??= new MaterialPropertyBlock();
        starRenderer.GetPropertyBlock(seedBlock);
        seedBlock.SetFloat(TwinkleSeedId, seed);
        starRenderer.SetPropertyBlock(seedBlock);
    }

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