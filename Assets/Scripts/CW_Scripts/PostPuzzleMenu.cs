using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// After the constellation is signed, swaps the puzzle tools (Earth View Disc,
/// Star Chart) for a small poke menu in the same spot under the sphere.
///
/// Lives on the shared "Puzzle Presentation" parent, which must stay active.
/// Hiding uses SetActive(false), so hidden displays and a hidden menu take no
/// input. Never touches the rotation driver, the Earth View Camera or the
/// observer; the invisible mini constellation's post-solve grab is suppressed
/// through ConstellationSetup while the menu is up.
/// </summary>
public sealed class PostPuzzleMenu : MonoBehaviour
{
    [Header("Sequence")]
    [SerializeField]
    private ConstellationSigning signing;

    [SerializeField]
    private ConstellationReveal reveal;

    [SerializeField]
    private ConstellationSolveController solveController;

    [SerializeField]
    private ConstellationSetup setup;

    [Tooltip("Optional: title / practice flow. After a practice letter the first button reads 'Practice another letter' and returns to the title; after the main puzzle the second button returns to the title.")]
    [SerializeField]
    private GameFlow gameFlow;

    [Header("Labels (title / practice flow)")]
    [Tooltip("Label of the Choose Another Constellation button.")]
    [SerializeField]
    private TMPro.TMP_Text chooseAnotherLabel;

    [SerializeField]
    private string practiceAnotherText = "Practice another letter";

    [Tooltip("The Choose Another Box button: hidden after practice (the first button already returns to the title).")]
    [SerializeField]
    private GameObject chooseAnotherBoxButton;

    [Tooltip("Label of the Choose Another Box button; reads Back To Title Text until there are boxes.")]
    [SerializeField]
    private TMPro.TMP_Text chooseAnotherBoxLabel;

    [SerializeField]
    private string backToTitleText = "Back to title";

    [Header("Back to title during a puzzle")]
    [Tooltip("Small poke button shown during a puzzle (hidden at the title and while this menu is up). Leaves the puzzle without solving it.")]
    [SerializeField]
    private GameObject puzzleBackButtonRoot;

    [SerializeField]
    private MomentaryToggleButton puzzleBackButton;

    [Header("Displays")]
    [Tooltip("Hidden while the menu is shown (Earth View Disc, Star Chart Blueprint).")]
    [SerializeField]
    private GameObject[] puzzleDisplays;

    [SerializeField]
    private GameObject menuRoot;

    [Header("Menu Layout")]
    [Tooltip("Menu position in this object's space (metres).")]
    [SerializeField]
    private Vector3 menuLocalPosition = new Vector3(0f, 0.03f, -0.02f);

    [Tooltip("Menu rotation in this object's space. X tilts the row back toward the player.")]
    [SerializeField]
    private Vector3 menuLocalEuler = new Vector3(40f, 0f, 0f);

    [Tooltip("Uniform size multiplier for the whole menu.")]
    [SerializeField]
    private float menuScale = 1f;

    [Tooltip("Gap between buttons, in canvas units.")]
    [SerializeField]
    private float buttonSpacing = 16f;

    [SerializeField]
    private HorizontalLayoutGroup buttonRow;

    [Header("Debug")]
    [SerializeField]
    private bool logEvents = true;

    public bool IsMenuShown { get; private set; }

    private bool resetting;
    private string mainChooseAnotherText;

    private void Awake()
    {
        ApplyLayout();
        if (menuRoot != null)
        {
            menuRoot.SetActive(false);
        }

        if (chooseAnotherLabel != null)
        {
            mainChooseAnotherText = chooseAnotherLabel.text;
        }

        if (chooseAnotherBoxLabel != null && gameFlow != null)
        {
            chooseAnotherBoxLabel.text = backToTitleText;
        }

        UpdatePuzzleBackButton();
    }

    private void OnEnable()
    {
        if (gameFlow != null)
        {
            gameFlow.StageChanged += HandleStageChanged;
        }

        if (puzzleBackButton != null)
        {
            puzzleBackButton.Pressed += BackToTitle;
        }

        if (signing != null)
        {
            signing.Signed += HandleSigned;
        }

        if (reveal != null)
        {
            reveal.Cleared += HandleCleared;
        }
    }

    private void OnDisable()
    {
        if (gameFlow != null)
        {
            gameFlow.StageChanged -= HandleStageChanged;
        }

        if (puzzleBackButton != null)
        {
            puzzleBackButton.Pressed -= BackToTitle;
        }

        if (signing != null)
        {
            signing.Signed -= HandleSigned;
        }

        if (reveal != null)
        {
            reveal.Cleared -= HandleCleared;
        }

        // Never leave the puzzle tools hidden.
        if (IsMenuShown)
        {
            HideMenu();
        }
    }

    private void HandleSigned()
    {
        ShowMenu();
    }

    private void HandleCleared()
    {
        // Any other reset path (Reset Solve, Preview Reveal, Stellar Forge)
        // also brings the puzzle tools back.
        if (IsMenuShown)
        {
            HideMenu();
        }
    }

    private void HandleStageChanged(GameFlow.Stage stage)
    {
        UpdatePuzzleBackButton();
    }

    [ContextMenu("Show Menu")]
    public void ShowMenu()
    {
        ApplyLayout();
        ApplyFlowLabels();
        SetDisplaysVisible(false);
        if (menuRoot != null)
        {
            menuRoot.SetActive(true);
        }

        if (setup != null)
        {
            setup.SetPostSolveGrabSuppressed(true);
        }

        IsMenuShown = true;
        UpdatePuzzleBackButton();
        Log("menu shown.");
    }

    [ContextMenu("Hide Menu")]
    public void HideMenu()
    {
        HideMenu(true);
    }

    private void HideMenu(bool releaseGrabSuppression)
    {
        if (menuRoot != null)
        {
            menuRoot.SetActive(false);
        }

        SetDisplaysVisible(true);
        if (releaseGrabSuppression && setup != null)
        {
            // Hands the post-solve grab back to ConstellationSetup's own state.
            setup.SetPostSolveGrabSuppressed(false);
        }

        IsMenuShown = false;
        UpdatePuzzleBackButton();
        Log("menu hidden.");
    }

    // ---- Button callbacks (wired on each button's On Pressed; replace freely) ----

    /// <summary>Temporary: reset this constellation and play again. After a practice letter: back to the title to pick another.</summary>
    public void ChooseAnotherConstellation()
    {
        if (!IsMenuShown || resetting)
        {
            return;
        }

        if (gameFlow != null && gameFlow.IsPractice)
        {
            Log("Practice another letter: back to the title.");
            ReturnToTitleFromMenu();
            return;
        }

        resetting = true;
        Log("Choose Another Constellation: resetting the puzzle.");

        // Show the displays before the reset so the screen's CRT power-on plays.
        HideMenu(false);
        if (solveController != null)
        {
            solveController.ResetSolve();
        }

        // The reset has withdrawn the post-solve grab; now stop suppressing.
        if (setup != null)
        {
            setup.SetPostSolveGrabSuppressed(false);
        }

        resetting = false;
    }

    public void ChooseAnotherBox()
    {
        if (gameFlow == null)
        {
            Log("Choose Another Box pressed (not implemented yet: would open box selection).");
            return;
        }

        if (!IsMenuShown || resetting)
        {
            return;
        }

        // Until there are boxes, the title (letters + Start) is the way back.
        Log("Back to title (Choose Another Box).");
        ReturnToTitleFromMenu();
    }

    /// <summary>
    /// The in-puzzle Back to title button: leaves the puzzle at any point
    /// (intro, rotating, solving, signing) without solving it.
    /// </summary>
    public void BackToTitle()
    {
        if (gameFlow == null || resetting || gameFlow.Current != GameFlow.Stage.Puzzle)
        {
            return;
        }

        if (IsMenuShown)
        {
            ReturnToTitleFromMenu();
            return;
        }

        resetting = true;
        Log("Back to title during the puzzle.");
        gameFlow.ReturnToTitle();
        resetting = false;
    }

    private void ReturnToTitleFromMenu()
    {
        resetting = true;
        HideMenu(false);
        gameFlow.ReturnToTitle();
        if (setup != null)
        {
            setup.SetPostSolveGrabSuppressed(false);
        }

        resetting = false;
    }

    private void ApplyFlowLabels()
    {
        if (gameFlow == null)
        {
            return;
        }

        bool practice = gameFlow.IsPractice;
        if (chooseAnotherLabel != null)
        {
            chooseAnotherLabel.text = practice ? practiceAnotherText : mainChooseAnotherText;
        }

        if (chooseAnotherBoxButton != null)
        {
            chooseAnotherBoxButton.SetActive(!practice);
        }

        if (chooseAnotherBoxLabel != null)
        {
            chooseAnotherBoxLabel.text = backToTitleText;
        }
    }

    private void UpdatePuzzleBackButton()
    {
        if (puzzleBackButtonRoot == null)
        {
            return;
        }

        bool show = !IsMenuShown && (gameFlow == null || gameFlow.Current == GameFlow.Stage.Puzzle);
        if (puzzleBackButtonRoot.activeSelf != show)
        {
            puzzleBackButtonRoot.SetActive(show);
        }
    }

    public void ViewConstellations()
    {
        Log("View Constellations pressed (not implemented yet: would open the constellation collection).");
    }

    // ---- Layout ----

    [ContextMenu("Apply Layout")]
    public void ApplyLayout()
    {
        if (menuRoot != null)
        {
            Transform t = menuRoot.transform;
            t.localPosition = menuLocalPosition;
            t.localRotation = Quaternion.Euler(menuLocalEuler);
            t.localScale = Vector3.one * Mathf.Max(0.01f, menuScale);
        }

        if (buttonRow != null && !Mathf.Approximately(buttonRow.spacing, buttonSpacing))
        {
            buttonRow.spacing = buttonSpacing;
        }
    }

    private void SetDisplaysVisible(bool visible)
    {
        if (puzzleDisplays == null)
        {
            return;
        }

        foreach (GameObject display in puzzleDisplays)
        {
            if (display != null)
            {
                display.SetActive(visible);
            }
        }
    }

    private void Log(string message)
    {
        if (logEvents)
        {
            Debug.Log("PostPuzzleMenu: " + message, this);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Apply after validation so Inspector tweaks (also in Play) show live.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                ApplyLayout();
            }
        };
    }
#endif
}
