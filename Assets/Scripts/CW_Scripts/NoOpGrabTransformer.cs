using UnityEngine;
using Oculus.Interaction;

public class NoOpGrabTransformer : MonoBehaviour, ITransformer
{
    public void Initialize(IGrabbable grabbable)
    {
        // Intentionally nothing.
    }

    public void BeginTransform()
    {
        // Intentionally nothing.
    }

    public void UpdateTransform()
    {
        // Intentionally nothing.
    }

    public void EndTransform()
    {
        // Intentionally nothing.
    }
}