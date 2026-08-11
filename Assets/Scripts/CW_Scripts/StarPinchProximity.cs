using System;
using UnityEngine;

public class StarPinchProximity : MonoBehaviour
{
    [SerializeField]
    private PinchRotationController pinchController;

    [SerializeField]
    private Transform starRoot;

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("NEAR CHECK!" + other.gameObject.tag);
        Debug.Log(
    $"{other.name} | parent: {other.transform.parent?.name} | " +
    $"grandparent: {other.transform.parent?.parent?.name}"
);

        PinchPointMarker pinchPoint =
                other.GetComponentInParent<PinchPointMarker>();

        if (pinchPoint != null)
        {
            pinchController.SetNearbyStar(starRoot);
            Debug.Log($"Nearby star set: {starRoot.name}");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PinchPointMarker pinchPoint =
        other.GetComponentInParent<PinchPointMarker>();

        if (pinchPoint != null)
        {
            pinchController.ClearNearbyStar(starRoot);
        }
    }
}