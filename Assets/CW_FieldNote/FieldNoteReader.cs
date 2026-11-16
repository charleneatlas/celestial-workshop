using System;
using UnityEngine;

/// <summary>
/// The Skycrafter's Field Note's reading view (the Credits board pattern, on
/// the constellation-selection table). The Field Note button in the box row
/// opens a large, readable copy of the tri-fold sheet in front of the player;
/// its Back button closes it. Reading is optional and never changes the box
/// selection: the boxes stay as they are, only the box row (Back to title,
/// Field Note) steps aside while the sheet is up, as the title row does for
/// Credits.
///
/// It only opens while all boxes are shut (BoxSelection Closed), and puts the
/// sheet away quietly whenever the selection moves on (a box opens, the stage
/// changes, the selection hides).
/// </summary>
public sealed class FieldNoteReader : MonoBehaviour
{
    [Tooltip("The box selection this note sits on. Found in the parents if empty.")]
    [SerializeField]
    private BoxSelection selection;

    [Tooltip("The Field Note button (in the box row).")]
    [SerializeField]
    private MomentaryToggleButton openButton;

    [Tooltip("The Back button under the open sheet.")]
    [SerializeField]
    private MomentaryToggleButton backButton;

    [Tooltip("The open sheet and its Back button (shown while reading).")]
    [SerializeField]
    private GameObject board;

    [Tooltip("The box row (Back to title + Field Note): hidden while reading, restored after if the boxes are still shut.")]
    [SerializeField]
    private GameObject boxRow;

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
        if (openButton != null) openButton.Pressed += Open;
        if (backButton != null) backButton.Pressed += Close;
        if (selection != null) selection.StateChanged += OnSelectionState;
    }

    private void OnDisable()
    {
        if (openButton != null) openButton.Pressed -= Open;
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

        if (selection != null && selection.Current != BoxSelection.State.Closed)
        {
            return;
        }

        SetOpen(true, true);
    }

    [ContextMenu("Close")]
    public void Close()
    {
        SetOpen(false, true);
    }

    private void OnSelectionState(BoxSelection.State state)
    {
        if (state != BoxSelection.State.Closed)
        {
            // BoxSelection drives the box row itself from here on.
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

        if (driveRow && boxRow != null)
        {
            if (open)
            {
                boxRow.SetActive(false);
            }
            else if (selection == null || selection.Current == BoxSelection.State.Closed)
            {
                boxRow.SetActive(true);
            }
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
