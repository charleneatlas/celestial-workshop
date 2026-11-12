using UnityEngine;

/// <summary>
/// Turns a display to face the player's head square on, but only when the rig
/// or the display's parent has moved: DeskAlign putting the player at the desk
/// (startup, recenter, headset back on) or NearFieldSkyProxy placing the
/// volume. Between those it stays put, so it doesn't follow the head around.
/// Picture frame is the Earth View Disc screen mesh's: normal = local +Y,
/// picture up = local +Z, picture right = local +X.
/// </summary>
public sealed class FaceViewerOnAlign : MonoBehaviour
{
    [Tooltip("Head to face. Defaults to the OVRCameraRig's center eye.")]
    [SerializeField]
    private Transform viewer;

    [Tooltip("Rig whose moves (DeskAlign) trigger a re-face. Defaults to the OVRCameraRig.")]
    [SerializeField]
    private Transform rig;

    [Header("Placement (relative to the parent's center)")]
    [Tooltip("Positions the display from the parent's center on each re-face. Off = only rotate, keep the authored position.")]
    [SerializeField]
    private bool placeFromParentCenter = true;

    [Tooltip("Height of the display's center above the parent's center (m, world up).")]
    [SerializeField]
    private float heightAboveCenter = 0.03f;

    [Tooltip("How far past the parent's center the display sits, horizontally away from the viewer (m).")]
    [SerializeField]
    private float distanceBehindCenter = 0.07f;

    [Header("Re-face only on a re-seat")]
    [Tooltip("The rig must move at least this far (m) since the last facing to re-face. DeskAlign re-runs nudge the rig by the head's drift; those are ignored, so the display is set once and stays.")]
    [SerializeField]
    [Min(0f)]
    private float rigMoveThreshold = 0.15f;

    [Tooltip("...or turn at least this much (degrees).")]
    [SerializeField]
    [Min(0f)]
    private float rigTurnThreshold = 15f;

    private Vector3 lastRigPosition, lastParentPosition;
    private Quaternion lastRigRotation, lastParentRotation;
    private bool faced;

    private void Start()
    {
        if (viewer == null || rig == null)
        {
            OVRCameraRig cameraRig = FindFirstObjectByType<OVRCameraRig>();
            if (cameraRig != null)
            {
                if (viewer == null)
                    viewer = cameraRig.centerEyeAnchor;
                if (rig == null)
                    rig = cameraRig.transform;
            }
        }

        if (viewer == null)
            Debug.LogWarning("FaceViewerOnAlign: no viewer found; the display keeps its authored rotation.", this);
    }

    private void LateUpdate()
    {
        if (viewer == null)
            return;

        Transform parent = transform.parent;
        Vector3 rigPosition = rig != null ? rig.position : Vector3.zero;
        Quaternion rigRotation = rig != null ? rig.rotation : Quaternion.identity;
        Vector3 parentPosition = parent != null ? parent.position : Vector3.zero;
        Quaternion parentRotation = parent != null ? parent.rotation : Quaternion.identity;

        bool rigReseated =
            Vector3.Distance(rigPosition, lastRigPosition) >= rigMoveThreshold
            || Quaternion.Angle(rigRotation, lastRigRotation) >= rigTurnThreshold;
        bool parentMoved = parentPosition != lastParentPosition || parentRotation != lastParentRotation;

        if (faced && !rigReseated && !parentMoved)
            return;

        FaceViewer();
        lastRigPosition = rigPosition;
        lastRigRotation = rigRotation;
        lastParentPosition = parentPosition;
        lastParentRotation = parentRotation;
        faced = true;
    }

    [ContextMenu("Face Viewer Now")]
    public void FaceViewer()
    {
        if (viewer == null)
            return;

        Transform parent = transform.parent;
        if (placeFromParentCenter && parent != null)
        {
            // Pushed back horizontally, so the height stays as set wherever the head is.
            Vector3 center = parent.position + Vector3.up * heightAboveCenter;
            Vector3 fromViewer = Vector3.ProjectOnPlane(center - viewer.position, Vector3.up);
            transform.position = fromViewer.sqrMagnitude > 1e-6f
                ? center + fromViewer.normalized * distanceBehindCenter
                : center;
        }

        Vector3 toViewer = viewer.position - transform.position;
        if (toViewer.sqrMagnitude < 1e-6f)
            return;

        Vector3 normal = toViewer.normalized;
        Vector3 pictureUp = Vector3.ProjectOnPlane(Vector3.up, normal);
        if (pictureUp.sqrMagnitude < 1e-6f)
            pictureUp = Vector3.ProjectOnPlane(viewer.up, normal); // head straight above or below

        // Local +Z (picture up) along pictureUp, local +Y (picture normal) toward the head;
        // local +X then lands on the player's right.
        transform.rotation = Quaternion.LookRotation(pictureUp.normalized, normal);
    }
}
