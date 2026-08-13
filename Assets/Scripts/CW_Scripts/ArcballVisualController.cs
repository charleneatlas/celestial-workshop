using UnityEngine;

public class ArcballVisualController : MonoBehaviour
{
    [SerializeField]
    private Transform sphereVisual;

    private void Awake()
    {
        Hide();
    }

    public void Show(float radius)
    {
        if (sphereVisual == null)
        {
            return;
        }

        sphereVisual.gameObject.SetActive(true);

        // Unity's built-in Sphere primitive has a diameter of 1 unit,
        // so scale by diameter, not radius.
        float diameter = radius * 2f;

        sphereVisual.localScale =
            Vector3.one * diameter;
    }

    public void Hide()
    {
        if (sphereVisual == null)
        {
            return;
        }

        sphereVisual.gameObject.SetActive(false);
    }
}