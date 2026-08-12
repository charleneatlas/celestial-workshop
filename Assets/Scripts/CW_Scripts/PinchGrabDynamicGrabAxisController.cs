using UnityEngine;

public class PinchGrabDynamicAxisController : MonoBehaviour
{
    [SerializeField]
    private Transform miniConstellationPivot;

    [SerializeField]
    private Transform dynamicRotationPivot;

    [SerializeField]
    private Transform fingertip;

    [Header("Temporary Test")]
    [SerializeField]
    private Transform testStar;

    private bool isPinchGrabActive = false;
    private Vector3 previousFingerPosition;

    private void Update()
    {
        if (!isPinchGrabActive || testStar == null)
        {
            return;
        }

        Vector3 currentFingerPosition = fingertip.position;

        Vector3 fingerMovement =
            currentFingerPosition - previousFingerPosition;

        previousFingerPosition = currentFingerPosition;

        if (fingerMovement.sqrMagnitude < 0.000001f)
        {
            return;
        }

        // Vector3 fromCenter =
        //     testStar.position - miniConstellationPivot.position;

        // Vector3 rotationAxis =
        //     Vector3.Cross(
        //         fromCenter,
        //         fingerMovement
        //     );

        // if (rotationAxis.sqrMagnitude < 0.000001f)
        // {
        //     return;
        // }

        // rotationAxis.Normalize();

        // // OneGrabRotateTransformer is configured to use
        // // DynamicRotationPivot's local Up axis.
        // dynamicRotationPivot.up = rotationAxis;
    }

    public void BeginGrab()
    {
        isPinchGrabActive = true;
        previousFingerPosition = fingertip.position;

        Vector3 fromCenter =
            testStar.position - miniConstellationPivot.position;

        Vector3 fingerDirection =
            fingertip.forward;

        Vector3 rotationAxis =
            Vector3.Cross(fromCenter, fingerDirection);

        if (rotationAxis.sqrMagnitude > 0.000001f)
        {
            dynamicRotationPivot.up = rotationAxis.normalized;
        }
    }

    public void EndGrab()
    {
        isPinchGrabActive = false;
    }
}