using System;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// The Credits sheet on the title. The title row's Credits button swaps the
/// title letters and buttons for a small parchment board; its Back button
/// swaps them back. Nothing else changes: GameFlow stays at the Title stage
/// (so the music carries on), and leaving the title any other way just puts
/// the board away.
///
/// The text comes from <see cref="creditsSource"/> (Credits.txt): a line
/// starting "## " is a section heading, a blank line separates entries, and
/// every other line is shown exactly as written.
/// </summary>
public sealed class CreditsBoard : MonoBehaviour
{
    [Tooltip("The title shelf whose letters and buttons the board replaces. Found in the parents if empty.")]
    [SerializeField]
    private TitleShelf shelf;

    [Tooltip("Found automatically if empty.")]
    [SerializeField]
    private GameFlow gameFlow;

    [Tooltip("The Credits button in the title row.")]
    [SerializeField]
    private MomentaryToggleButton openButton;

    [Tooltip("The Back button under the board.")]
    [SerializeField]
    private MomentaryToggleButton backButton;

    [Tooltip("Shown while the credits are open (the parchment, wood and Back button).")]
    [SerializeField]
    private GameObject board;

    [Header("Text")]
    [SerializeField]
    private TextAsset creditsSource;

    [SerializeField]
    private TMP_Text creditsText;

    [Tooltip("Heading size relative to the body.")]
    [SerializeField]
    private float headingScale = 1.3f;

    [SerializeField]
    private Color headingColor = new Color(0.22f, 0.12f, 0.05f, 1f);

    [Header("Appear")]
    [Tooltip("Seconds for the board to settle in when opened.")]
    [SerializeField]
    [Min(0f)]
    private float appearSeconds = 0.22f;

    [SerializeField]
    private bool logEvents = true;

    /// <summary>True while any credits board is open (the title hints stay quiet).</summary>
    public static bool IsAnyOpen { get; private set; }

    public event Action<bool> OpenChanged;

    public bool IsOpen { get; private set; }

    private float appearT = 1f;
    private Vector3 boardScale = Vector3.one;

    private void Awake()
    {
        if (shelf == null)
        {
            shelf = GetComponentInParent<TitleShelf>(true);
        }

        if (gameFlow == null)
        {
            gameFlow = FindFirstObjectByType<GameFlow>();
        }

        if (board != null)
        {
            boardScale = board.transform.localScale;
            board.SetActive(false);
        }

        ApplyText();
    }

    private void OnEnable()
    {
        if (openButton != null) openButton.Pressed += Open;
        if (backButton != null) backButton.Pressed += Close;
        if (gameFlow != null) gameFlow.StageChanged += OnStageChanged;
    }

    private void OnDisable()
    {
        if (openButton != null) openButton.Pressed -= Open;
        if (backButton != null) backButton.Pressed -= Close;
        if (gameFlow != null) gameFlow.StageChanged -= OnStageChanged;

        // Hidden with the title (any stage change): put the board away quietly.
        SetOpen(false, false);
    }

    [ContextMenu("Open")]
    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        if (gameFlow != null && gameFlow.Current != GameFlow.Stage.Title)
        {
            return;
        }

        if (IsLetterHeld())
        {
            Log("not opened: a title letter is in hand.");
            return;
        }

        SetOpen(true, true);
    }

    [ContextMenu("Close")]
    public void Close()
    {
        SetOpen(false, true);
    }

    private void OnStageChanged(GameFlow.Stage stage)
    {
        if (stage != GameFlow.Stage.Title)
        {
            // The title flow shows / hides the shelf itself.
            SetOpen(false, false);
        }
    }

    private void SetOpen(bool open, bool driveShelf)
    {
        if (open == IsOpen)
        {
            return;
        }

        IsOpen = open;
        IsAnyOpen = open;

        if (driveShelf && shelf != null)
        {
            if (open)
            {
                shelf.Hide();
            }
            else if (gameFlow == null || gameFlow.Current == GameFlow.Stage.Title)
            {
                shelf.Show();
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

    private bool IsLetterHeld()
    {
        if (shelf == null || shelf.Letters == null)
        {
            return false;
        }

        foreach (TitleLetterMini letter in shelf.Letters)
        {
            if (letter != null && letter.IsGrabbed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Fills the text from the credits file (also run by the builder).</summary>
    [ContextMenu("Apply Text")]
    public void ApplyText()
    {
        if (creditsText == null || creditsSource == null)
        {
            return;
        }

        creditsText.text = Format(creditsSource.text, headingScale, headingColor);
    }

    public static string Format(string source, float headingScale, Color headingColor)
    {
        var sb = new StringBuilder();
        string heading = ColorUtility.ToHtmlStringRGBA(headingColor);
        int size = Mathf.RoundToInt(headingScale * 100f);
        bool pendingGap = false;
        bool any = false;
        foreach (string raw in source.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Length == 0)
            {
                pendingGap = any;
                continue;
            }

            if (any)
            {
                sb.Append('\n');
            }

            if (pendingGap)
            {
                // A short gap between entries rather than a full empty line.
                sb.Append("<size=55%> </size>\n");
                pendingGap = false;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                sb.Append("<size=").Append(size).Append("%><color=#").Append(heading).Append("><b>")
                  .Append(Escape(line.Substring(3).Trim())).Append("</b></color></size>");
            }
            else
            {
                sb.Append(Escape(line));
            }

            any = true;
        }

        return sb.ToString();
    }

    // Credits are shown verbatim, never parsed as rich text.
    private static string Escape(string text) => "<noparse>" + text + "</noparse>";

    private void Log(string message)
    {
        if (logEvents)
        {
            Debug.Log("CreditsBoard: " + message, this);
        }
    }
}
