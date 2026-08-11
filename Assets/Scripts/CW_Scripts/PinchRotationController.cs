using UnityEngine;

public class PinchRotationController : MonoBehaviour
{
    [SerializeField]
    private Transform miniConstellationPivot;

    [SerializeField]
    private OVRHand hand;

    [SerializeField]
    private Transform fingertip;

    [SerializeField]
    private float pinchThreshold = 0.5f;

    [SerializeField]
    private float rotationSensitivity = 300f;

    private Transform nearbyStar;
    private Transform activeStar;

    private bool isPinching;
    private Vector3 previousFingerPosition;

    private void Start()
    {
        previousFingerPosition = fingertip.position;
        isPinching = false;
    }

    private void Update()
    {
        float pinchValue =
            hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);

        // Start pinch.
        if (!isPinching &&
            pinchValue > pinchThreshold &&
            nearbyStar != null)
        {
            BeginPinch();
        }

        // End pinch.
        if (isPinching &&
            pinchValue <= pinchThreshold)
        {
            EndPinch();
        }

        if (!isPinching || activeStar == null)
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

    private void BeginPinch()
    {
        isPinching = true;

        // Lock onto whichever star was nearby
        // when the pinch began.
        activeStar = nearbyStar;

        // Prevent a jump on the first frame.
        previousFingerPosition = fingertip.position;
    }

    private void EndPinch()
    {
        isPinching = false;
        activeStar = null;
    }

    public void SetNearbyStar(Transform star)
    {
        nearbyStar = star;
    }

    public void ClearNearbyStar(Transform star)
    {
        if (nearbyStar == star)
        {
            nearbyStar = null;
        }
    }
}