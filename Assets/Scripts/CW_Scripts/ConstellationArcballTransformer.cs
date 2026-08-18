using UnityEngine;
using Oculus.Interaction;

public class ConstellationArcballTransformer : MonoBehaviour, ITransformer
{
    [SerializeField]
    private Transform rotationPivot;

    [SerializeField]
    private ArcballVisualController arcballVisual;

    [Header("Tutorial")]
    [SerializeField]
    private GameObject tutorialHandVisual;

    private bool tutorialDismissed;

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

        // Show visual cue for rotation (semi-transparent sphere with radius defined by grab point)
        arcballVisual?.Show(previousFromCenter.magnitude);

        // Player has successfully begun the arcball interaction.
        DismissTutorial();
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

        // Hide visual cue for rotation (semi-transparent sphere with radius defined by grab point)
        arcballVisual?.Hide();
    }

    public void RandomizeRotation()
    {
        if (grabbable == null)
        {
            Debug.LogWarning(
                "Cannot randomize rotation: grabbable has not been initialized."
            );
            return;
        }

        grabbable.Transform.rotation = Random.rotationUniform;

        hasPreviousGrabPoint = false;
    }

    private void DismissTutorial()
    {
        if (tutorialDismissed)
        {
            return;
        }

        tutorialDismissed = true;

        if (tutorialHandVisual != null)
        {
            tutorialHandVisual.SetActive(false);
        }
    }
}