using UnityEngine;

public sealed class StellarForgeTrigger : MonoBehaviour
{
    [SerializeField]
    private Rigidbody constellationRigidbody;

    [SerializeField]
    private ConstellationSolveController solveController;

    private bool hasTriggered;

    private void OnTriggerEnter(Collider other)
    {
        if (hasTriggered)
        {
            return;
        }

        if (other.attachedRigidbody != constellationRigidbody)
        {
            return;
        }

        hasTriggered = true;

        solveController.ResetSolve();
    }

    public void ResetTrigger()
    {
        hasTriggered = false;
    }
}