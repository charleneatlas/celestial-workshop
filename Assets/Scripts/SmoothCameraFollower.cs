using UnityEngine;

public class SmoothCameraFollower : MonoBehaviour
{
    public enum UpdateMode { Update, LateUpdate, FixedUpdate }

    [Header("Target")]
    [Tooltip("Usually the HMD camera transform")]
    public Transform target;

    [Header("Follow Toggles")]
    public bool followPosition = true;
    public bool followRotation = true;

    [Header("Offsets")]
    [Tooltip("Position offset relative to the target")]
    public Vector3 positionOffset = Vector3.zero;
    public Space positionOffsetSpace = Space.Self; // Self = relative to target, World = absolute world offset

    [Tooltip("Extra euler rotation offset applied on top of target rotation")]
    public Vector3 rotationOffsetEuler = Vector3.zero;

    [Header("Smoothing")]
    [Tooltip("Lower is snappier. Typical 0.06 to 0.15 for VR capture")]
    public float positionSmoothTime = 0.08f;

    [Tooltip("Exponential rotation follow speed in 1 per second. Typical 10 to 20")]
    public float rotationLerpSpeed = 12f;

    [Tooltip("Clamp the max position follow speed. Infinity disables the clamp")]
    public float maxPositionSpeed = Mathf.Infinity;

    [Header("Stability")]
    [Tooltip("If the target jumps farther than this in one frame, snap to it to avoid sliding")]
    public float teleportDistance = 3f;

    [Tooltip("Snap to target on Start")]
    public bool snapOnStart = true;

    [Header("Timing")]
    public UpdateMode updateMode = UpdateMode.LateUpdate;
    [Tooltip("Use unscaled delta time. Handy if you slow down time for effects")]
    public bool useUnscaledTime = false;

    // Internal state
    Vector3 _posVelocity; // for SmoothDamp
    bool _initialized;

    void Start()
    {
        if (snapOnStart) SnapToTarget();
    }

    void Update()
    {
        if (updateMode == UpdateMode.Update) Tick(GetDeltaTime());
    }

    void LateUpdate()
    {
        if (updateMode == UpdateMode.LateUpdate) Tick(GetDeltaTime());
    }

    void FixedUpdate()
    {
        if (updateMode == UpdateMode.FixedUpdate) Tick(GetDeltaTime());
    }

    float GetDeltaTime()
    {
        if (updateMode == UpdateMode.FixedUpdate)
            return Time.fixedDeltaTime;
        return useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
    }

    void Tick(float dt)
    {
        if (target == null) return;

        // Desired position
        Vector3 desiredPos = target.position;
        if (positionOffsetSpace == Space.Self)
            desiredPos = target.TransformPoint(positionOffset);
        else
            desiredPos = target.position + positionOffset;

        // Teleport handling
        if (!_initialized || Vector3.Distance(transform.position, desiredPos) > teleportDistance)
        {
            if (followPosition) transform.position = desiredPos;
            if (followRotation) transform.rotation = GetDesiredRotation();
            _posVelocity = Vector3.zero;
            _initialized = true;
            return;
        }

        // Smooth position
        if (followPosition)
        {
            if (positionSmoothTime <= 0.0001f)
            {
                transform.position = desiredPos;
            }
            else
            {
                Vector3 newPos = Vector3.SmoothDamp(
                    transform.position,
                    desiredPos,
                    ref _posVelocity,
                    positionSmoothTime,
                    maxPositionSpeed,
                    dt
                );
                transform.position = newPos;
            }
        }

        // Smooth rotation with exponential decay
        if (followRotation)
        {
            Quaternion desiredRot = GetDesiredRotation();
            float t = 1f - Mathf.Exp(-Mathf.Max(0f, rotationLerpSpeed) * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRot, t);
        }
    }

    Quaternion GetDesiredRotation()
    {
        Quaternion targetRot = target.rotation;
        if (rotationOffsetEuler != Vector3.zero)
            targetRot = targetRot * Quaternion.Euler(rotationOffsetEuler);
        return targetRot;
    }

    [ContextMenu("Snap To Target")]
    public void SnapToTarget()
    {
        if (target == null) return;

        // Position
        Vector3 desiredPos = (positionOffsetSpace == Space.Self)
            ? target.TransformPoint(positionOffset)
            : target.position + positionOffset;

        if (followPosition) transform.position = desiredPos;
        if (followRotation) transform.rotation = GetDesiredRotation();

        _posVelocity = Vector3.zero;
        _initialized = true;
    }

    // Optional helper if you change target at runtime and want an instant realign
    public void SetTargetAndSnap(Transform newTarget)
    {
        target = newTarget;
        SnapToTarget();
    }
}