using UnityEngine;

public class PinchGrabRotationController : MonoBehaviour
{
    [SerializeField]
    private Transform miniConstellationPivot;

    [SerializeField]
    private Transform fingertip;

    [SerializeField]
    private float rotationSensitivity = 300f;

    private Transform activeStar;

    private bool isPinchGrabActive = false;
    private Vector3 previousFingerPosition;


    private void Update()
    {
        if (!isPinchGrabActive || activeStar == null)
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

        Vector3 fromCenter =
            activeStar.position - miniConstellationPivot.position;

        Vector3 rotationAxis =
            Vector3.Cross(
                fromCenter,
                fingerMovement
            ).normalized;

        float rotationAmount =
            fingerMovement.magnitude * rotationSensitivity;

        miniConstellationPivot.Rotate(
            rotationAxis,
            rotationAmount,
            Space.World
        );
    }

    public void BeginGrab(Transform star)
    {
        activeStar = star;
        isPinchGrabActive = true;
        previousFingerPosition = fingertip.position;
    }

    public void EndGrab()
    {
        isPinchGrabActive = false;
        activeStar = null;
    }
}