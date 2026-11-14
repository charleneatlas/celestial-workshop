using System;
using UnityEngine;

/// <summary>
/// Constellation box selection (GameFlow's Boxes stage).
///
///   Closed:    all four boxes; only available ones can be opened (lid grab).
///   PackOpen:  the opened box stays, the others are hidden; its sculptures
///              can be grabbed and placed in the globe. "All boxes" goes back.
///   Launching: a sculpture was accepted; everything else is ignored until
///              GameFlow has started the puzzle and hidden this.
///
/// A sculpture (TitleLetterMini in custom-constellation mode) released in the
/// globe's drop zone starts its constellation through GameFlow; released
/// anywhere else it glides back to its slot (TitleLetterMini does that).
/// GameFlow calls Show / Hide on stage changes; this never changes the stage
/// itself except through GameFlow's own entry points.
/// </summary>
[DisallowMultipleComponent]
public sealed class BoxSelection : MonoBehaviour
{
    public enum State
    {
        Hidden,
        Closed,
        PackOpen,
        Launching
    }

    [SerializeField]
    private GameFlow gameFlow;

    [Tooltip("All boxes on the selection screen (available and Coming soon).")]
    [SerializeField]
    private ConstellationBox[] boxes = new ConstellationBox[0];

    [Tooltip("The box opened by 'Choose another constellation' (and whose sculptures are wired below).")]
    [SerializeField]
    private ConstellationBox starterPack;

    [Tooltip("Grabbable constellation sculptures inside the Starter Pack.")]
    [SerializeField]
    private TitleLetterMini[] sculptures = new TitleLetterMini[0];

    [Tooltip("Where sculptures are accepted (the globe).")]
    [SerializeField]
    private SphereDropZone dropZone;

    [Header("Buttons")]
    [Tooltip("Shown while a pack is open: back to all boxes.")]
    [SerializeField]
    private GameObject allBoxesButtonRoot;

    [SerializeField]
    private MomentaryToggleButton allBoxesButton;

    [Tooltip("Shown with all boxes: back to the title.")]
    [SerializeField]
    private GameObject backToTitleButtonRoot;

    [SerializeField]
    private MomentaryToggleButton backToTitleButton;

    [SerializeField]
    private bool logEvents = true;

    public State Current { get; private set; } = State.Hidden;

    /// <summary>Raised on every state change (guidance listens).</summary>
    public event Action<State> StateChanged;

    public ConstellationBox StarterPack => starterPack;

    /// <summary>A sculpture to point guidance at (the middle one).</summary>
    public Transform GuidanceSculpture =>
        sculptures != null && sculptures.Length > 0 && sculptures[sculptures.Length / 2] != null
            ? sculptures[sculptures.Length / 2].transform
            : null;

    private void OnEnable()
    {
        foreach (ConstellationBox box in boxes)
        {
            if (box != null)
            {
                box.Opened += HandleBoxOpened;
            }
        }

        foreach (TitleLetterMini sculpture in sculptures)
        {
            if (sculpture != null)
            {
                sculpture.PlacedInZone += HandleSculpturePlaced;
                if (dropZone != null)
                {
                    sculpture.SetDropZones(new ITitleDropZone[] { dropZone });
                }
            }
        }

        if (allBoxesButton != null)
        {
            allBoxesButton.Pressed += ShowAllBoxes;
        }

        if (backToTitleButton != null)
        {
            backToTitleButton.Pressed += BackToTitle;
        }
    }

    private void OnDisable()
    {
        foreach (ConstellationBox box in boxes)
        {
            if (box != null)
            {
                box.Opened -= HandleBoxOpened;
            }
        }

        foreach (TitleLetterMini sculpture in sculptures)
        {
            if (sculpture != null)
            {
                sculpture.PlacedInZone -= HandleSculpturePlaced;
            }
        }

        if (allBoxesButton != null)
        {
            allBoxesButton.Pressed -= ShowAllBoxes;
        }

        if (backToTitleButton != null)
        {
            backToTitleButton.Pressed -= BackToTitle;
        }
    }

    /// <summary>Shows the selection: all boxes shut, or (openPack) straight into the opened Starter Pack.</summary>
    public void Show(bool openPack)
    {
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        foreach (ConstellationBox box in boxes)
        {
            if (box != null)
            {
                box.gameObject.SetActive(true);
                box.Close();
            }
        }

        foreach (TitleLetterMini sculpture in sculptures)
        {
            if (sculpture != null)
            {
                sculpture.GlideHome();
            }
        }

        SetState(State.Closed);
        if (openPack && starterPack != null)
        {
            // Raises Opened -> HandleBoxOpened -> PackOpen.
            starterPack.Open(true);
        }
    }

    /// <summary>Hides the whole selection (sculptures snap back to their slots as they deactivate).</summary>
    public void Hide()
    {
        SetState(State.Hidden);
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>"All boxes": shut the open pack and show every box again.</summary>
    [ContextMenu("Show All Boxes")]
    public void ShowAllBoxes()
    {
        if (Current != State.PackOpen)
        {
            return;
        }

        Log("back to all boxes.");
        Show(false);
    }

    public void BackToTitle()
    {
        if (Current == State.Hidden || Current == State.Launching || gameFlow == null)
        {
            return;
        }

        Log("back to the title.");
        gameFlow.ReturnToTitle();
    }

    [ContextMenu("Open Starter Pack")]
    private void OpenStarterPackDebug()
    {
        if (starterPack != null && Current == State.Closed)
        {
            starterPack.Open(false);
        }
    }

    private void HandleBoxOpened(ConstellationBox opened)
    {
        if (Current != State.Closed)
        {
            return;
        }

        // The opened box becomes the selection area; the rest step aside.
        foreach (ConstellationBox box in boxes)
        {
            if (box != null && box != opened)
            {
                box.gameObject.SetActive(false);
            }
        }

        Log($"'{opened.DisplayName}' opened.");
        SetState(State.PackOpen);
    }

    private void HandleSculpturePlaced(TitleLetterMini sculpture)
    {
        // Once only: a second sculpture (or the same one again) during the
        // transition is ignored.
        if (Current != State.PackOpen || gameFlow == null)
        {
            return;
        }

        var stars = sculpture.CopyCustomStars();
        if (stars == null || stars.Count < 2)
        {
            Debug.LogWarning($"BoxSelection: sculpture '{sculpture.name}' has no constellation.", sculpture);
            return;
        }

        Log($"sculpture '{sculpture.name}' placed: starting {sculpture.ConstellationName}.");
        SetState(State.Launching);
        gameFlow.StartConstellation(sculpture.ConstellationName, sculpture.CustomArtboard, stars);
    }

    private void SetState(State state)
    {
        Current = state;
        if (allBoxesButtonRoot != null)
        {
            allBoxesButtonRoot.SetActive(state == State.PackOpen);
        }

        if (backToTitleButtonRoot != null)
        {
            backToTitleButtonRoot.SetActive(state == State.Closed);
        }

        StateChanged?.Invoke(state);
    }

    private void Log(string message)
    {
        if (logEvents)
        {
            Debug.Log("BoxSelection: " + message, this);
        }
    }

#if UNITY_EDITOR
    /// <summary>Editor checks: place a sculpture in the zone as if released there.</summary>
    public void DebugPlaceSculpture(int index)
    {
        if (index >= 0 && index < sculptures.Length && sculptures[index] != null)
        {
            sculptures[index].SimulatePlaceInZone();
        }
    }
#endif
}
