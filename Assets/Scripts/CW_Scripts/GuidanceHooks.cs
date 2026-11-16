using System.Collections;
using CW.Guidance;
using UnityEngine;

/// <summary>
/// Drives the one-at-a-time guidance cues from game events. Step ids match the
/// GuidanceDirector's step list (see Assets/CW_Guidance/GuidanceCopy.md):
/// title-letter, title-start, boxes-open, boxes-place, place-model, rotate,
/// chart, match, sign, menu. boxes-place ("choose a model") completes when a
/// sculpture is picked up, then place-model ("place it at the workstation")
/// shows until it is placed; putting the sculpture back brings boxes-place back. The box steps (like the title ones) show on every visit.
/// The two title steps alternate for as long as the title is up (every
/// return to the title restarts them); the puzzle steps are onlyOnce in the
/// director, so after the first puzzle they stay quiet.
/// </summary>
public sealed class GuidanceHooks : MonoBehaviour
{
    [SerializeField]
    private GuidanceDirector director;

    [SerializeField]
    private GameFlow gameFlow;

    [SerializeField]
    private TitleShelf titleShelf;

    [SerializeField]
    private ConstellationIntro intro;

    [SerializeField]
    private VolumePinchRotation pinchRotation;

    [SerializeField]
    private ConstellationSolveController solveController;

    [SerializeField]
    private ConstellationReveal reveal;

    [SerializeField]
    private ConstellationSigning signing;

    [Tooltip("Constellation box selection: 'open the Starter Pack', then 'place a constellation'.")]
    [SerializeField]
    private BoxSelection boxSelection;

    [Tooltip("Star chart: its first star touch completes the chart hint.")]
    [SerializeField]
    private BlueprintController blueprint;

    [Tooltip("Seconds each title hint (letter, Start) shows before the other replaces it.")]
    [SerializeField]
    private float titleHintSeconds = 6f;

    [Tooltip("Menu hint text after a practice letter (the main puzzle keeps the step's own text).")]
    [SerializeField]
    private string practiceMenuText = "Try another letter, or enter the workshop";

    [Tooltip("Practice letters show the menu hint when their menu comes up (they have no signing). Found automatically if empty.")]
    [SerializeField]
    private PostPuzzleMenu postPuzzleMenu;

    [Tooltip("Hints fade out while the Recenter countdown owns the view. Found automatically if empty.")]
    [SerializeField]
    private WorkshopPlacement placement;

    private Coroutine titleRoutine;
    private string mainMenuText;

    private void Awake()
    {
        if (placement == null)
        {
            placement = FindFirstObjectByType<WorkshopPlacement>();
        }

        if (postPuzzleMenu == null)
        {
            postPuzzleMenu = FindFirstObjectByType<PostPuzzleMenu>();
        }
    }

    [Tooltip("After a sculpture is let go without being placed, wait this long (s) before asking to choose a model again.")]
    [SerializeField]
    [Min(0f)]
    private float releasedGrace = 0.6f;

    private float releasedAt = -1f;

    private void LateUpdate()
    {
        UpdateBoxPickup();

        // The Recenter prompt floats in the same place as the hints; let it speak alone.
        if (director != null && director.label != null)
        {
            director.label.Suppressed = (placement != null && placement.IsChoosing) || CreditsBoard.IsAnyOpen || FieldNoteReader.IsAnyOpen;
        }
    }

    private void OnEnable()
    {
        if (gameFlow != null) gameFlow.StageChanged += OnStageChanged;
        if (titleShelf != null) titleShelf.StartPressed += OnStartPressed;
        if (intro != null) intro.Finished += OnIntroFinished;
        if (pinchRotation != null) pinchRotation.AxisLocked += OnAxisLocked;
        if (solveController != null)
        {
            solveController.NearSolveEntered.AddListener(OnNearSolve);
            solveController.Solved.AddListener(OnSolved);
            solveController.PuzzleReset += OnPuzzleReset;
        }

        if (reveal != null) reveal.Presented += OnPresented;
        if (signing != null) signing.Signed += OnSigned;
        if (blueprint != null) blueprint.StarTouched += OnChartStarTouched;
        if (boxSelection != null) boxSelection.StateChanged += OnBoxStateChanged;
        if (postPuzzleMenu != null) postPuzzleMenu.MenuShown += OnMenuShown;
    }

    private void OnDisable()
    {
        if (gameFlow != null) gameFlow.StageChanged -= OnStageChanged;
        if (titleShelf != null) titleShelf.StartPressed -= OnStartPressed;
        if (intro != null) intro.Finished -= OnIntroFinished;
        if (pinchRotation != null) pinchRotation.AxisLocked -= OnAxisLocked;
        if (solveController != null)
        {
            solveController.NearSolveEntered.RemoveListener(OnNearSolve);
            solveController.Solved.RemoveListener(OnSolved);
            solveController.PuzzleReset -= OnPuzzleReset;
        }

        if (reveal != null) reveal.Presented -= OnPresented;
        if (signing != null) signing.Signed -= OnSigned;
        if (blueprint != null) blueprint.StarTouched -= OnChartStarTouched;
        if (boxSelection != null) boxSelection.StateChanged -= OnBoxStateChanged;
        if (postPuzzleMenu != null) postPuzzleMenu.MenuShown -= OnMenuShown;
    }

    private IEnumerator Start()
    {
        // GameFlow may already be at the title before this subscribed.
        yield return null;
        if (gameFlow != null && gameFlow.Current == GameFlow.Stage.Title)
        {
            OnStageChanged(GameFlow.Stage.Title);
        }
    }

    private void OnStageChanged(GameFlow.Stage stage)
    {
        if (director == null)
        {
            return;
        }

        if (titleRoutine != null)
        {
            StopCoroutine(titleRoutine);
            titleRoutine = null;
        }

        if (stage == GameFlow.Stage.Onboarding)
        {
            // The noticeboard teaches by itself; no cues meanwhile.
            director.Clear();
            return;
        }

        if (stage == GameFlow.Stage.Title)
        {
            // Nothing from the puzzle points at what is now hidden.
            if (director.ActiveId != null && !director.IsActive("title-letter") && !director.IsActive("title-start"))
            {
                director.Clear();
            }

            titleRoutine = StartCoroutine(TitleHints());
            return;
        }

        // Leaving the title: a placed letter answers the letter hint.
        if (gameFlow != null && gameFlow.IsPractice)
        {
            director.Complete("title-letter");
        }

        director.Complete("title-start");
        if (director.IsActive("title-letter") || director.IsActive("title-start"))
        {
            director.Clear();
        }
    }

    // Alternates the letter and Start hints until the player leaves the title
    // (OnStageChanged stops this). While a letter is held the letter hint stays.
    private IEnumerator TitleHints()
    {
        // Wait for the title to be shown and placed.
        yield return new WaitForSeconds(1f);
        bool letterTurn = true;
        while (gameFlow == null || gameFlow.Current == GameFlow.Stage.Title)
        {
            // The credits board hides the letters and Start: no hint points at them meanwhile.
            if (CreditsBoard.IsAnyOpen)
            {
                director.Clear();
                while (CreditsBoard.IsAnyOpen)
                {
                    yield return null;
                }

                yield return new WaitForSeconds(0.5f);
                continue;
            }

            // The practice-selection title has no menu row: only the letter hint.
            bool lettersOnly = gameFlow != null && gameFlow.TitleLettersOnly;
            director.Show(letterTurn || lettersOnly ? "title-letter" : "title-start");
            float until = Time.time + Mathf.Max(1f, titleHintSeconds);
            while ((Time.time < until || IsLetterHeld()) && !CreditsBoard.IsAnyOpen)
            {
                if (IsLetterHeld() && !director.IsActive("title-letter"))
                {
                    director.Show("title-letter");
                    letterTurn = true;
                }

                yield return null;
            }

            letterTurn = !letterTurn;
        }

        titleRoutine = null;
    }

    private bool IsLetterHeld()
    {
        if (titleShelf == null || titleShelf.Letters == null)
        {
            return false;
        }

        foreach (TitleLetterMini letter in titleShelf.Letters)
        {
            if (letter != null && letter.IsGrabbed)
            {
                return true;
            }
        }

        return false;
    }

    private void OnStartPressed()
    {
        if (director != null)
        {
            director.Complete("title-start");
        }
    }

    private void OnIntroFinished()
    {
        if (director != null)
        {
            director.Show("rotate");
        }
    }

    private void OnAxisLocked()
    {
        if (director != null && director.Complete("rotate"))
        {
            // Shown after the rotate acknowledgement finishes; the chart hint
            // first, unless the chart has already been used.
            if (!director.Show("chart"))
            {
                director.Show("match");
            }
        }
    }

    // Pack open: picking up a sculpture moves "choose a model" on to "place it
    // at the workstation"; letting go without placing it goes back.
    private void UpdateBoxPickup()
    {
        if (director == null || boxSelection == null || boxSelection.Current != BoxSelection.State.PackOpen)
        {
            releasedAt = -1f;
            return;
        }

        if (boxSelection.IsSculptureHeld)
        {
            releasedAt = -1f;
            director.Complete("boxes-place");
            if (!director.IsActive("place-model"))
            {
                // Also on later visits where "choose" was already used up
                // (onlyOnce): ignored once place-model itself is done.
                director.Show("place-model");
            }

            return;
        }

        if (director.IsActive("place-model"))
        {
            if (releasedAt < 0f)
            {
                releasedAt = Time.time;
            }
            else if (Time.time - releasedAt >= releasedGrace)
            {
                releasedAt = -1f;
                director.Show("boxes-place");
            }
        }
    }

    private void OnBoxStateChanged(BoxSelection.State state)
    {
        if (director == null)
        {
            return;
        }

        switch (state)
        {
            case BoxSelection.State.Closed:
                director.Show("boxes-open");
                break;

            case BoxSelection.State.PackOpen:
                // Completing first plays the acknowledgement; the next hint follows it.
                director.Complete("boxes-open");
                director.Show("boxes-place");
                break;

            case BoxSelection.State.Launching:
                // A sculpture was accepted: the placement hint is done (both
                // are onlyOnce-friendly: they're only recorded when it happens).
                if (!director.Complete("place-model") && !director.Complete("boxes-place") &&
                    director.IsActive("boxes-open"))
                {
                    director.Clear();
                }

                break;

            default:
                // Left the boxes without placing anything: hide the box hints
                // without completing them, so they come back next time.
                if (director.IsActive("boxes-open") || director.IsActive("boxes-place") || director.IsActive("place-model"))
                {
                    director.Clear();
                }

                break;
        }
    }

    private void OnChartStarTouched(string starId)
    {
        if (director == null)
        {
            return;
        }

        if (director.Complete("chart"))
        {
            director.Show("match");
        }
        else
        {
            // Used before the hint came up: no need to show it later.
            director.RecordCompleted("chart");
        }
    }

    private void OnNearSolve()
    {
        if (director != null)
        {
            director.Complete("match");
            if (director.IsActive("chart"))
            {
                director.Clear();
            }
        }
    }

    private void OnSolved()
    {
        // Nothing competes with the stamp and celebration.
        if (director != null && !director.Complete("match"))
        {
            director.Clear();
        }
    }

    private void OnPresented()
    {
        // Practice letters have no signing (their menu hint follows the menu).
        if (director != null && (signing == null || !signing.SkipsSigning))
        {
            director.Show("sign");
        }
    }

    private void OnMenuShown()
    {
        if (director != null && gameFlow != null && gameFlow.IsPractice)
        {
            SetMenuText(true);
            director.Show("menu");
        }
    }

    private void OnSigned()
    {
        if (director != null)
        {
            // Completing first plays the acknowledgement; the menu hint follows it.
            director.Complete("sign");
            SetMenuText(gameFlow != null && gameFlow.IsPractice);
            director.Show("menu");
        }
    }

    private void SetMenuText(bool practice)
    {
        foreach (GuidanceStep step in director.steps)
        {
            if (step != null && step.id == "menu")
            {
                mainMenuText ??= step.text;
                step.text = practice ? practiceMenuText : mainMenuText;
                return;
            }
        }
    }

    private void OnPuzzleReset()
    {
        if (director == null)
        {
            return;
        }

        if (!director.Complete("menu"))
        {
            // Any other reset mid-cue: drop it rather than leave it pointing at nothing.
            if (director.IsActive("rotate") || director.IsActive("chart") || director.IsActive("match") || director.IsActive("sign"))
            {
                director.Clear();
            }
        }
    }
}
