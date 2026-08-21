using UnityEngine;

public class GimbalTutorialController : MonoBehaviour
{
    [SerializeField]
    private GameObject tutorialHandVisual;

    private bool tutorialDismissed;

    public void DismissTutorial()
    {
        if (tutorialDismissed)
            return;

        tutorialDismissed = true;

        if (tutorialHandVisual != null)
        {
            tutorialHandVisual.SetActive(false);
        }
    }
}