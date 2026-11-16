using System;
using UnityEngine;

/// <summary>
/// The Skycrafter's Field Note's reading view (the Credits board pattern, on
/// the constellation-selection table). A Field Note button in the box row
/// (beside Back to title, or beside All boxes while the Starter Pack is open)
/// opens a large, readable copy of the tri-fold sheet in front of the player;
/// its Back button closes it. Reading is optional and never changes the box
/// selection: the boxes stay as they are; only the button row and the folded
/// note on the table step aside while the sheet is up (the note is "open"),
/// as the title row does for Credits.
///
/// It opens with the boxes shut or the Starter Pack open, and puts the sheet
/// away quietly whenever the selection moves on (a box opens, All boxes, a
/// launch, the stage changes, the selection hides).
/// </summary>
public sealed class FieldNoteReader : MonoBehaviour
{
    [Tooltip("The box selection this note sits on. Found in the parents if empty.")]
    [SerializeField]
    private BoxSelection selection;

    [Tooltip("The Field Note buttons (one in each box row).")]
    [SerializeField]
    private MomentaryToggleButton[] openButtons = new MomentaryToggleButton[0];

    [Tooltip("The Back button under the open sheet.")]
    [SerializeField]
    private MomentaryToggleButton backButton;

    [Tooltip("The open sheet and its Back button (shown while reading).")]
    [SerializeField]
    private GameObject board;

    [Tooltip("The box rows (Back to title / All boxes, each with a Field Note button): whichever is showing steps aside while reading and comes back after.")]
    [SerializeField]
    private GameObject[] boxRows = new GameObject[0];

    [Tooltip("The folded note on the table: hidden while the sheet is open.")]
    [SerializeField]
    private GameObject foldedNote;

    [Header("Appear")]
    [Tooltip("Seconds for the sheet to settle in when opened.")]
    [SerializeField]
    [Min(0f)]
    private float appearSeconds = 0.25f;

    [SerializeField]
    private bool logEvents = true;

    /// <summary>True while any field note is open (the box hints stay quiet).</summary>
    public static bool IsAnyOpen { get; private set; }

    public event Action<bool> OpenChanged;

    public bool IsOpen { get; private set; }

    private float appearT = 1f;
    private Vector3 boardScale = Vector3.one;
    private BoxSelection.State openedIn;
    private readonly System.Collections.Generic.List<GameObject> hiddenRows = new System.Collections.Generic.List<GameObject>();

    private void Awake()
    {
        if (selection == null)
        {
            selection = GetComponentInParent<BoxSelection>(true);
        }

        if (board != null)
        {
            boardScale = board.transform.localScale;
            board.SetActive(false);
        }
    }

    private void OnEnable()
    {
        foreach (var b in openButtons) if (b != null) b.Pressed += Open;
        if (backButton != null) backButton.Pressed += Close;
        if (selection != null) selection.StateChanged += OnSelectionState;
    }

    private void OnDisable()
    {
        foreach (var b in openButtons) if (b != null) b.Pressed -= Open;
        if (backButton != null) backButton.Pressed -= Close;
        if (selection != null) selection.StateChanged -= OnSelectionState;

        // Hidden with the selection: put the sheet away quietly.
        SetOpen(false, false);
    }

    [ContextMenu("Open")]
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        if (selection != null && selection.Current != BoxSelection.State.Closed && selection.Current != BoxSelection.State.PackOpen)
        {
            return;
        }

        openedIn = selection != null ? selection.Current : BoxSelection.State.Closed;
        SetOpen(true, true);
    }

    [ContextMenu("Close")]
    public void Close()
    {
        SetOpen(false, true);
    }

    private void OnSelectionState(BoxSelection.State state)
    {
        if (IsOpen && state != openedIn)
        {
            // BoxSelection drives the box rows itself from here on.
            SetOpen(false, false);
        }
    }

    private void SetOpen(bool open, bool driveRow)
    {
        if (open == IsOpen)
        {
            return;
        }

        IsOpen = open;
        IsAnyOpen = open;

        if (open)
        {
            hiddenRows.Clear();
            foreach (GameObject row in boxRows)
            {
                if (row != null && row.activeSelf)
                {
                    hiddenRows.Add(row);
                    row.SetActive(false);
                }
            }
        }
        else
        {
            if (driveRow && (selection == null || selection.Current == openedIn))
            {
                foreach (GameObject row in hiddenRows)
                {
                    if (row != null) row.SetActive(true);
                }
            }

            hiddenRows.Clear();
        }

        // The note on the table is the one being read.
        if (foldedNote != null)
        {
            foldedNote.SetActive(!open);
        }

        if (board != null)
        {
            board.SetActive(open);
            appearT = open && appearSeconds > 0f ? 0f : 1f;
            board.transform.localScale = boardScale * AppearScale(appearT);
        }

        Log(open ? "opened." : "closed.");
        OpenChanged?.Invoke(open);
    }

    private void Update()
    {
        if (!IsOpen || appearT >= 1f || board == null)
        {
            return;
        }

        appearT = Mathf.MoveTowards(appearT, 1f, Time.deltaTime / Mathf.Max(0.01f, appearSeconds));
        board.transform.localScale = boardScale * AppearScale(appearT);
    }

    private static float AppearScale(float t)
    {
        float e = 1f - (1f - t) * (1f - t);
        return Mathf.Lerp(0.92f, 1f, e);
    }

    private void Log(string message)
    {
        if (logEvents)
        {
            Debug.Log("FieldNoteReader: " + message, this);
        }
    }
}
