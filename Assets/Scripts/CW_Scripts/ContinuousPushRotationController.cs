using UnityEngine;

public class ContinuousPushRotationController : MonoBehaviour
{
    [SerializeField]
    private Transform miniConstellationPivot;

    [SerializeField]
    private Transform fingertip;

    [SerializeField]
    private float rotationSensitivity = 300f;

    private Transform activeStar;
    private bool isPushing;

    private Vector3 previousFingerPosition;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        previousFingerPosition = fingertip.position;
    }

    // Update is called once per frame
    private void Update()
    {
        if (!isPushing || activeStar == null)
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

    public void BeginPush(Transform pokedStar)
    {
        activeStar = pokedStar;
        isPushing = true;

        previousFingerPosition = fingertip.position;
    }

    public void EndPush()
    {
        isPushing = false;
        activeStar = null;
    }
}
