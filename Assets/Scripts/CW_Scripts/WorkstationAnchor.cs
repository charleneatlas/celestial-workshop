using UnityEngine;

/// <summary>
/// Keeps this object at a fixed pose in the workstation's frame, so it is laid
/// out around the miniature's comfortable spot (the selection table around the
/// placement ring, the Star Chart beside it). The pose is applied each time
/// the workstation is placed (startup, Recenter, Raise / Lower), not every
/// frame, so grabbing the object afterwards still works. The workstation never
/// follows this object: hiding or moving it leaves the ring and the miniature
/// exactly where they are.
///
/// Frame: the workstation's (x player's right, y up, z away from the player),
/// centred on the miniature's centre or on the placement ring's centre.
/// Tune Offset / Rotation live in Play: changes apply at once.
/// </summary>
[DisallowMultipleComponent]
public sealed class WorkstationAnchor : MonoBehaviour
{
    private enum Reference
    {
        MiniCenter,
        RingCenter
    }

    [Tooltip("Found automatically if empty.")]
    [SerializeField]
    private WorkstationMini workstation;

    [Tooltip("Point the offset is measured from: the miniature's centre, or the placement ring's centre (Marker Drop below it).")]
    [SerializeField]
    private Reference reference = Reference.RingCenter;

    [Tooltip("Where this object's origin sits, in the workstation's frame (m): x right, y up, z away from the player.")]
    [SerializeField]
    private Vector3 offset;

    [Tooltip("Rotation in the workstation's frame (degrees): x tilts the top away (positive) or toward the player, y turns, z rolls.")]
    [SerializeField]
    private Vector3 rotation;

    [SerializeField]
    private bool logPlacement;

    public bool IsPlaced { get; private set; }

    private void Awake()
    {
        if (workstation == null)
        {
            workstation = FindFirstObjectByType<WorkstationMini>(FindObjectsInactive.Include);
        }
    }

    private void OnEnable()
    {
        if (workstation == null)
        {
            return;
        }

        workstation.Placed += Apply;
        if (workstation.IsReady)
        {
            Apply();
        }
    }

    private void OnDisable()
    {
        if (workstation != null)
        {
            workstation.Placed -= Apply;
        }
    }

    private void OnValidate()
    {
        if (Application.isPlaying && isActiveAndEnabled && workstation != null && workstation.IsReady)
        {
            Apply();
        }
    }

    [ContextMenu("Apply Now")]
    public void Apply()
    {
        if (workstation == null)
        {
            return;
        }

        Transform station = workstation.transform;
        Vector3 origin = reference == Reference.RingCenter ? workstation.RingCenter : workstation.Center;
        Quaternion frame = station.rotation;
        transform.SetPositionAndRotation(origin + frame * offset, frame * Quaternion.Euler(rotation));
        IsPlaced = true;

        if (logPlacement)
        {
            Debug.Log($"WorkstationAnchor: {name} placed at {transform.position:F3}", this);
        }
    }
}
