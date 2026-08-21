using UnityEngine;

public class SkyConstellationHighlightController : MonoBehaviour
{
    [SerializeField] private Color normalColor;
    [SerializeField] private Color nearSolveColor;
    [SerializeField] private Color solvedColor;

    private Renderer[] renderers;

    public void SetConstellation(GameObject constellationRoot)
    {
        renderers =
            constellationRoot.GetComponentsInChildren<Renderer>(true);

        SetNormal();
    }

    public void SetNormal()
    {
        SetColor(normalColor);
        Debug.Log("CONSTELLATION NO LONGER NEAR SOLVED!");
    }


    public void SetNearSolve()
    {
        SetColor(nearSolveColor);
        Debug.Log("CONSTELLATION NEAR SOLVED!");
    }

    public void SetSolved()
    {
        SetColor(solvedColor);
    }

    private void SetColor(Color color)
    {
        if (renderers == null)
            return;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            foreach (Material material in renderer.materials)
            {
                material.color = color;
                Debug.Log("Material actually getting set");
            }
        }
    }
}