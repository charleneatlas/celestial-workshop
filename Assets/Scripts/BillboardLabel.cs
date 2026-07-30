using UnityEngine;

public class BillboardLabel : MonoBehaviour
{
    private Transform cameraTransform;

    private void LateUpdate()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (cameraTransform == null)
        {
            return;
        }

        Vector3 direction =
            transform.position - cameraTransform.position;

        direction.y = 0f;

        if (direction.sqrMagnitude > 0.0001f)
        {
            transform.rotation =
                Quaternion.LookRotation(direction);
        }
    }
}