using UnityEngine;

/// <summary>
/// Connects the constellation title (TitleShelf) to GameFlow: Start opens the
/// constellation boxes, a letter placed at the workstation (or in the globe,
/// without one) becomes a practice puzzle. Shows the title only at the Title
/// stage and places it once the sky volume has been placed.
///
/// Layout Near Player (default): the title and its menu row, as one piece,
/// sit within comfortable seated reach in front of the head, along the line
/// toward the globe (which GameFlow hides at the title). Sampled when the
/// title is shown and after recenter / height changes, never every frame.
/// Above Globe: the original layout, floating over the globe.
/// </summary>
public sealed class TitleFlowBridge : MonoBehaviour
{
    public enum TitleLayout
    {
        [Tooltip("Close to the player at a comfortable seated height; the globe is hidden at the title.")]
        NearPlayer,

        [Tooltip("The original layout: floating above the globe.")]
        AboveGlobe
    }

    [SerializeField]
    private GameFlow gameFlow;

    [SerializeField]
    private TitleShelf shelf;

    [SerializeField]
    private NearFieldSkyProxy nearFieldSkyProxy;

    [Tooltip("The sphere volume: the title floats above it and letters are dropped into it.")]
    [SerializeField]
    private CircularWindow volume;

    [Tooltip("Player camera (CenterEyeAnchor).")]
    [SerializeField]
    private Camera viewer;

    [Tooltip("Drop zone on the sphere; its radius follows the volume.")]
    [SerializeField]
    private SphereDropZone dropZone;

    [Tooltip("Where title letters are placed to start a practice puzzle (the workstation's drop zone). Empty = the sphere's drop zone.")]
    [SerializeField]
    private SphereDropZone letterDropZone;

    [SerializeField]
    private TitleLayout layout = TitleLayout.NearPlayer;

    [Header("Near Player layout (relative to the head when placed)")]
    [Tooltip("Horizontal distance from the eyes to the title's center, toward the globe (m).")]
    [SerializeField]
    [Range(0.25f, 0.9f)]
    private float titleForward = 0.5f;

    [Tooltip("How far below eye height the title's center sits (m). The menu row hangs below it.")]
    [SerializeField]
    [Range(-0.2f, 0.5f)]
    private float titleBelowEyes = 0.08f;

    [Tooltip("Sideways offset (m, + = right).")]
    [SerializeField]
    [Range(-0.4f, 0.4f)]
    private float titleLateral;

    [Tooltip("Scale of the whole title and its menu row (1 = as authored for the old, farther layout).")]
    [SerializeField]
    [Range(0.4f, 1.5f)]
    private float titleScale = 0.8f;

    [Tooltip("Turn the title to face the eyes (tilted back when it sits below them). Off = upright.")]
    [SerializeField]
    private bool faceEyes = true;

    [Tooltip("Extra tilt (degrees): positive turns the face further up.")]
    [SerializeField]
    [Range(-30f, 30f)]
    private float titleTilt;

    [Tooltip("Eye height above the rig when the head isn't tracked (Editor without a headset).")]
    [SerializeField]
    private float fallbackEyeHeight = 1.2f;

    [Header("Above Globe layout")]
    [Tooltip("Gap between the top of the sphere and the title's center (metres).")]
    [SerializeField]
    private float heightAboveVolume = 0.1f;

    [Tooltip("How far the title sits toward the player from the sphere's center (metres).")]
    [SerializeField]
    private float towardViewer = 0.06f;

    [Tooltip("Tilt (degrees): positive turns the face up toward eyes above it.")]
    [SerializeField]
    private float tiltBack = 0f;

    [Tooltip("Drop zone radius as a multiple of the volume radius.")]
    [SerializeField]
    private float dropRadiusFactor = 1f;

    [Tooltip("Re-place the title in front of the player when this moves the rig (recenter, height). Found automatically if empty.")]
    [SerializeField]
    private WorkshopPlacement placement;

    private bool placed;

    private void OnEnable()
    {
        if (shelf != null)
        {
            shelf.StartPressed += OnStartPressed;
            shelf.LetterPlaced += OnLetterPlaced;
        }

        if (gameFlow != null)
        {
            gameFlow.StageChanged += OnStageChanged;
        }

        if (placement == null)
        {
            placement = FindFirstObjectByType<WorkshopPlacement>();
        }

        if (placement != null)
        {
            placement.RigMoved += OnRigMoved;
        }
    }

    // After a recenter the player sees the globe from a new side; face them again.
    private void OnRigMoved(Pose before, Pose after) => placed = false;

    private void OnDisable()
    {
        if (shelf != null)
        {
            shelf.StartPressed -= OnStartPressed;
            shelf.LetterPlaced -= OnLetterPlaced;
        }

        if (gameFlow != null)
        {
            gameFlow.StageChanged -= OnStageChanged;
        }

        if (placement != null)
        {
            placement.RigMoved -= OnRigMoved;
        }
    }

    private System.Collections.IEnumerator Start()
    {
        SphereDropZone letters = letterDropZone != null ? letterDropZone : dropZone;
        if (shelf != null && letters != null)
        {
            shelf.SetDropZones(letters);
        }

        // GameFlow sets its stage in Awake, before this subscribed; and the
        // shelf applies its own showOnStart in Start. Sync after both.
        yield return null;
        if (gameFlow != null)
        {
            OnStageChanged(gameFlow.Current);
        }
    }

    private void LateUpdate()
    {
        if (dropZone != null && volume != null)
        {
            dropZone.Radius = volume.Radius * dropRadiusFactor;
        }

        if (!placed && shelf != null && shelf.IsShown &&
            (nearFieldSkyProxy == null || nearFieldSkyProxy.IsPlaced))
        {
            PlaceTitle();
        }
    }

    private void OnStageChanged(GameFlow.Stage stage)
    {
        if (shelf == null)
        {
            return;
        }

        if (stage == GameFlow.Stage.Title)
        {
            placed = false;
            shelf.Show();
        }
        else
        {
            shelf.Hide();
        }
    }

    private void OnStartPressed()
    {
        // Start opens the constellation boxes (GameFlow falls back to the
        // main puzzle when there is no box selection).
        if (gameFlow != null && gameFlow.Current == GameFlow.Stage.Title)
        {
            gameFlow.OpenBoxes();
        }
    }

    private void OnLetterPlaced(char letter, int seed)
    {
        if (gameFlow == null || gameFlow.Current != GameFlow.Stage.Title)
        {
            return;
        }

        gameFlow.StartPractice(
            LetterConstellation.DisplayName(letter),
            LetterConstellation.Artboard,
            LetterConstellation.Build(letter, seed));
    }

    // +Z pointing away from the player (the shelf reads correctly that way).
    [ContextMenu("Place Title")]
    private void PlaceTitle()
    {
        if (layout == TitleLayout.NearPlayer)
        {
            PlaceNearPlayer();
            return;
        }

        if (volume == null || viewer == null)
        {
            return;
        }

        Vector3 center = volume.transform.position;
        Vector3 eye = viewer.transform.position;
        Vector3 away = Vector3.ProjectOnPlane(center - eye, Vector3.up);
        away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward;

        Vector3 position =
            center +
            Vector3.up * (volume.Radius + heightAboveVolume) -
            away * towardViewer;
        Quaternion rotation = Quaternion.LookRotation(away, Vector3.up) * Quaternion.Euler(tiltBack, 0f, 0f);

        shelf.transform.SetPositionAndRotation(position, rotation);
        placed = true;
    }

    // In front of the head along the line toward the globe, like the workstation.
    private void PlaceNearPlayer()
    {
        if (shelf == null)
        {
            return;
        }

        OVRCameraRig rig = viewer != null ? viewer.GetComponentInParent<OVRCameraRig>() : null;
        bool tracked = OVRManager.tracker != null && OVRManager.tracker.isPositionTracked;
        Vector3 eye = viewer != null && (tracked || rig == null)
            ? viewer.transform.position
            : (rig != null ? rig.transform.position : transform.position) + Vector3.up * fallbackEyeHeight;

        Vector3 forward = volume != null
            ? Vector3.ProjectOnPlane(volume.transform.position - eye, Vector3.up)
            : Vector3.ProjectOnPlane(viewer != null ? viewer.transform.forward : Vector3.forward, Vector3.up);
        forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Vector3 position = eye + forward * titleForward + right * titleLateral + Vector3.down * titleBelowEyes;
        Vector3 away = faceEyes ? (position - eye).normalized : forward;
        Quaternion rotation = Quaternion.LookRotation(away, Vector3.up) * Quaternion.Euler(titleTilt, 0f, 0f);

        shelf.transform.SetPositionAndRotation(position, rotation);
        shelf.transform.localScale = Vector3.one * titleScale;
        placed = true;
        Debug.Log($"TitleFlowBridge: title placed {Vector3.Distance(eye, position):F2} m from the eyes, " +
                  $"{titleBelowEyes:F2} m below them (head {(tracked ? "tracked" : "NOT tracked, fallback eye")}).", this);
    }
}
