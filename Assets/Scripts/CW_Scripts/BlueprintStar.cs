using UnityEngine;

public class BlueprintStar : MonoBehaviour
{
    [SerializeField] private string starId;
    [SerializeField] private GameObject highlightVisual;

    public string StarId => starId;

    /// <summary>For targets generated at runtime (StarChartLayout).</summary>
    public void SetStarId(string id)
    {
        starId = id;
    }

    private void Awake()
    {
        SetHighlighted(false);
    }
    public void SetHighlighted(bool highlighted)
    {
        if (highlightVisual != null)
        {
            highlightVisual.SetActive(highlighted);
        }
    }

}