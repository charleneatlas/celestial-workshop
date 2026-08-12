using UnityEngine;
using Oculus.Interaction;

public class ConstellationArcballTransformer : MonoBehaviour, ITransformer
{
    [SerializeField]
    private Transform rotationPivot;

    private IGrabbable grabbable;

    private Vector3 previousFromCenter;
    private bool hasPreviousGrabPoint;

    public void Initialize(IGrabbable grabbable)
    {
        this.grabbable = grabbable;
    }

    public void BeginTransform()
    {
        hasPreviousGrabPoint = false;

        if (grabbable == null ||
            grabbable.GrabPoints.Count == 0)
        {
            return;
        }

        previousFromCenter =
            grabbable.GrabPoints[0].position -
            rotationPivot.position;

        hasPreviousGrabPoint = true;
    }

    public void UpdateTransform()
    {
        if (grabbable == null ||
            grabbable.GrabPoints.Count == 0)
        {
            return;
        }

        Vector3 currentFromCenter =
            grabbable.GrabPoints[0].position -
            rotationPivot.position;

        if (!hasPreviousGrabPoint)
        {
            previousFromCenter = currentFromCenter;
            hasPreviousGrabPoint = true;
            return;
        }

        if (previousFromCenter.sqrMagnitude < 0.000001f ||
            currentFromCenter.sqrMagnitude < 0.000001f)
        {
            previousFromCenter = currentFromCenter;
            return;
        }

        Quaternion deltaRotation =
            Quaternion.FromToRotation(
                previousFromCenter,
                currentFromCenter
            );

        grabbable.Transform.rotation =
            deltaRotation * grabbable.Transform.rotation;

        previousFromCenter = currentFromCenter;
    }

    public void EndTransform()
    {
        hasPreviousGrabPoint = false;
    }
}