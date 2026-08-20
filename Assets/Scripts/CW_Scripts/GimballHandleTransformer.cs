using UnityEngine;
using Oculus.Interaction;

public class GimbalHandleTransformer : MonoBehaviour, ITransformer
{
    public enum Axis
    {
        Right,
        Up,
        Forward
    }

    [Header("References")]
    [SerializeField] private Transform _pivotTransform;
    [SerializeField] private Transform _rotationTarget;
    [SerializeField] private Transform _handleTransform;

    [Header("Rotation")]
    [SerializeField] private Axis _rotationAxis = Axis.Up;
    [SerializeField] private bool _rotateHandleOrientation = true;

    private IGrabbable _grabbable;

    private Vector3 _previousDirection;
    private float _handleRadius;

    public void Initialize(IGrabbable grabbable)
    {
        _grabbable = grabbable;
    }

    public void BeginTransform()
    {
        if (_grabbable == null ||
            _grabbable.GrabPoints.Count == 0 ||
            _pivotTransform == null ||
            _rotationTarget == null ||
            _handleTransform == null)
        {
            return;
        }

        Vector3 axis = GetWorldAxis();

        // Establish the handle's ring radius from where the handle
        // currently sits relative to the pivot.
        Vector3 handleOffset =
            _handleTransform.position - _pivotTransform.position;

        Vector3 planarHandleOffset =
            Vector3.ProjectOnPlane(handleOffset, axis);

        _handleRadius = planarHandleOffset.magnitude;

        // Establish the starting hand direction around the ring.
        Vector3 grabOffset =
            _grabbable.GrabPoints[0].position - _pivotTransform.position;

        Vector3 planarGrabOffset =
            Vector3.ProjectOnPlane(grabOffset, axis);

        if (planarGrabOffset.sqrMagnitude > 0.000001f)
        {
            _previousDirection = planarGrabOffset.normalized;
        }
        else if (planarHandleOffset.sqrMagnitude > 0.000001f)
        {
            _previousDirection = planarHandleOffset.normalized;
        }
    }

    public void UpdateTransform()
    {
        if (_grabbable == null ||
            _grabbable.GrabPoints.Count == 0 ||
            _pivotTransform == null ||
            _rotationTarget == null ||
            _handleTransform == null)
        {
            return;
        }

        Vector3 axis = GetWorldAxis();

        // Where is the hand relative to the pivot?
        Vector3 grabOffset =
            _grabbable.GrabPoints[0].position - _pivotTransform.position;

        // Remove movement along the rotation axis.
        Vector3 planarGrabOffset =
            Vector3.ProjectOnPlane(grabOffset, axis);

        // If the hand gets extremely close to the axis,
        // there isn't a useful angular direction.
        if (planarGrabOffset.sqrMagnitude < 0.000001f)
        {
            return;
        }

        Vector3 currentDirection = planarGrabOffset.normalized;

        // How far did the hand travel around the circle this frame?
        float angleDelta = Vector3.SignedAngle(
            _previousDirection,
            currentDirection,
            axis
        );

        // Rotate the constellation/object.
        _rotationTarget.RotateAround(
            _pivotTransform.position,
            axis,
            angleDelta
        );

        // Move the handle onto the same circular path.
        _handleTransform.position =
            _pivotTransform.position +
            currentDirection * _handleRadius;

        // Make the handle itself rotate as though attached to the ring.
        if (_rotateHandleOrientation)
        {
            _handleTransform.rotation =
                Quaternion.AngleAxis(angleDelta, axis) *
                _handleTransform.rotation;
        }

        _previousDirection = currentDirection;
    }

    public void EndTransform()
    {
        // Nothing special needed yet.
    }

    private Vector3 GetWorldAxis()
    {
        switch (_rotationAxis)
        {
            case Axis.Right:
                return _pivotTransform.right;

            case Axis.Up:
                return _pivotTransform.up;

            case Axis.Forward:
                return _pivotTransform.forward;

            default:
                return _pivotTransform.up;
        }
    }
}