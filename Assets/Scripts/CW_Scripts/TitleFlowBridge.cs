using UnityEngine;

/// <summary>
/// Connects the constellation title (TitleShelf) to GameFlow: Start opens the
/// constellation boxes, a letter dropped in the globe becomes a practice puzzle. Shows
/// the title only at the Title stage and floats it above the globe, facing the
/// player, once the sky volume has been placed.
/// </summary>
public sealed class TitleFlowBridge : MonoBehaviour
{
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
    }

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
    }

    private System.Collections.IEnumerator Start()
    {
        if (shelf != null && dropZone != null)
        {
            shelf.SetDropZones(dropZone);
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

    // Above the globe, nudged toward the player, +Z pointing away from them
    // (the shelf reads correctly that way).
    [ContextMenu("Place Title")]
    private void PlaceTitle()
    {
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
}
