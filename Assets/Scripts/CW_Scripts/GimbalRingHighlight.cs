using UnityEngine;

public class GimbalRingHighlight : MonoBehaviour
{
    [SerializeField] private Renderer ringRenderer;
    [SerializeField] private float idleAlpha = 0.10f;
    [SerializeField] private float hoverAlpha = 0.40f;
    [SerializeField] private float activeAlpha = 0.8f;

    private Material ringMaterial;

    private void Awake()
    {
        ringMaterial = ringRenderer.material;
        SetAlpha(idleAlpha);
    }

    public void SetActive()
    {
        SetAlpha(activeAlpha);
    }

    public void SetInactive()
    {
        SetAlpha(idleAlpha);
    }

    public void SetHover()
    {
        SetAlpha(hoverAlpha);
    }

    public void SetUnhover()
    {
        SetAlpha(idleAlpha);
    }

    private void SetAlpha(float alpha)
    {
        Color c = ringMaterial.color;
        c.a = alpha;
        ringMaterial.color = c;
    }
}