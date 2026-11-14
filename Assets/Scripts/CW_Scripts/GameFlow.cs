using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Top-level flow:
///   Title  -> (letter placed)    -> Puzzle (practice letter)
///   Title  -> (Start)            -> Boxes (constellation box selection)
///   Boxes  -> (sculpture placed) -> Puzzle (a real constellation)
/// At the title and box selection the globe holds no puzzle (display dark,
/// rotation and solving off, via ConstellationIntro's hold) and the puzzle
/// visuals are hidden. The solve, stamp, signing and menu flow is unchanged;
/// PostPuzzleMenu asks this to move on.
/// </summary>
public sealed class GameFlow : MonoBehaviour
{
    public enum Stage
    {
        Title,
        Boxes,
        Puzzle,
        // Workshop noticeboard at launch: title, boxes and puzzle hidden and
        // not interactable until WorkshopOnboarding calls FinishOnboarding.
        Onboarding
    }

    [SerializeField]
    private ConstellationSetup setup;

    [SerializeField]
    private ConstellationIntro intro;

    [SerializeField]
    private ConstellationSolveController solveController;

    [Tooltip("Shown at the title, hidden during a puzzle (e.g. the constellation title shelf). Optional.")]
    [SerializeField]
    private GameObject titleRoot;

    [Tooltip("Constellation box selection (shown only at the Boxes stage). Optional: without it, Start goes straight to the main puzzle.")]
    [SerializeField]
    private BoxSelection boxSelection;

    [Tooltip("Optional: the workshop noticeboard. When it runs on launch, GameFlow waits in its Onboarding stage until it finishes.")]
    [SerializeField]
    private WorkshopOnboarding onboarding;

    [Tooltip("Off: skip the title and start the main puzzle as before.")]
    [SerializeField]
    private bool startAtTitle = true;

    [Header("Visibility by stage")]
    [Tooltip("Deactivated at the title, active during any puzzle (e.g. the Star Chart Blueprint).")]
    [SerializeField]
    private GameObject[] puzzleObjects = new GameObject[0];

    [Tooltip("Renderers under these are hidden at the title. For puzzle parts that must stay active for the puzzle logic (mini and sky constellations).")]
    [SerializeField]
    private Transform[] puzzleRendererRoots = new Transform[0];

    [Tooltip("Shown only for the main constellation: deactivated at the title and during letter practice (the observer diorama).")]
    [SerializeField]
    private GameObject[] mainOnlyObjects = new GameObject[0];

    public Stage Current { get; private set; } = Stage.Puzzle;

    /// <summary>True while the loaded puzzle is a practice letter rather than the main constellation.</summary>
    public bool IsPractice { get; private set; }

    public event Action<Stage> StageChanged;

    // The scene's own constellation, captured before anything swaps it.
    private string mainName;
    private Vector2 mainArtboard;
    private List<ConstellationGenerator.StarData> mainStars;

    // Renderers this hid at the title (only these are re-enabled).
    private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
    private readonly List<Renderer> rendererScratch = new List<Renderer>();
    private bool visibilityReady;
    private bool pendingOpenPack;

    private void Awake()
    {
        CaptureMain();

        // Before ConstellationSetup's first reset, so the startup intro is held.
        if (onboarding != null && onboarding.WillRunOnLaunch)
        {
            if (intro != null)
            {
                intro.SetHold(true);
            }

            SetStage(Stage.Onboarding);
        }
        else if (startAtTitle && intro != null)
        {
            intro.SetHold(true);
            SetStage(Stage.Title);
        }
        else
        {
            ShowTitle(false);
        }
    }

    private System.Collections.IEnumerator Start()
    {
        // The constellations are generated in ConstellationSetup.Start, and the
        // displays initialise in their own Awake/Start: apply the title's
        // visibility once everything exists.
        yield return null;
        visibilityReady = true;
        ApplyVisibility();
    }

    private void OnEnable()
    {
        if (setup != null)
        {
            setup.ConstellationLoaded += ApplyVisibility;
        }
    }

    private void OnDisable()
    {
        if (setup != null)
        {
            setup.ConstellationLoaded -= ApplyVisibility;
        }
    }

    private void CaptureMain()
    {
        ConstellationGenerator generator = setup != null ? setup.Generator : null;
        if (generator == null)
        {
            return;
        }

        mainName = generator.constellationName;
        mainArtboard = generator.artboardSize;
        mainStars = new List<ConstellationGenerator.StarData>(generator.stars);
    }

    /// <summary>Back to the noticeboard stage (for replaying the onboarding).</summary>
    public void EnterOnboarding()
    {
        EnterSelection(Stage.Onboarding, false);
    }

    /// <summary>The noticeboard was signed: on to the boxes (or the title).</summary>
    public void FinishOnboarding(bool toTitle)
    {
        if (Current != Stage.Onboarding)
        {
            return;
        }

        if (toTitle && startAtTitle)
        {
            ReturnToTitle();
        }
        else
        {
            OpenBoxes();
        }
    }

    /// <summary>Title Start: the box selection (or the main puzzle if there is none).</summary>
    [ContextMenu("Open Boxes")]
    public void OpenBoxes()
    {
        if (boxSelection == null)
        {
            StartMain();
            return;
        }

        EnterSelection(Stage.Boxes, false);
    }

    /// <summary>
    /// Back to box selection after a puzzle (or mid-puzzle): the puzzle is
    /// reset and held hidden. openPack: straight back into the opened
    /// Starter Pack ("Choose another constellation") instead of all boxes.
    /// </summary>
    public void ReturnToBoxes(bool openPack)
    {
        if (boxSelection == null)
        {
            ReturnToTitle();
            return;
        }

        EnterSelection(Stage.Boxes, openPack);
    }

    /// <summary>
    /// Loads and starts a real constellation (e.g. a box sculpture placed in
    /// the globe), always through ConstellationSetup.LoadConstellation, so
    /// the puzzle, chart and stamp are rebuilt and the normal intro plays.
    /// </summary>
    public void StartConstellation(string displayName, Vector2 artboardSize, List<ConstellationGenerator.StarData> stars)
    {
        if (setup == null || stars == null || stars.Count < 2)
        {
            Debug.LogWarning("GameFlow: a constellation needs at least two stars.", this);
            return;
        }

        IsPractice = false;
        BeginPuzzle();
        setup.LoadConstellation(displayName, artboardSize, stars);
    }

    /// <summary>The scene's own (main) constellation, as captured at startup.</summary>
    public bool TryGetMainConstellation(out string displayName, out Vector2 artboardSize, out List<ConstellationGenerator.StarData> stars)
    {
        displayName = mainName;
        artboardSize = mainArtboard;
        stars = mainStars != null ? new List<ConstellationGenerator.StarData>(mainStars) : null;
        return stars != null;
    }

    /// <summary>Start the main constellation puzzle directly (fallback when there is no box selection).</summary>
    [ContextMenu("Start Main")]
    public void StartMain()
    {
        bool reload = IsPractice;
        IsPractice = false;
        BeginPuzzle();

        if (reload && mainStars != null)
        {
            setup.LoadConstellation(mainName, mainArtboard, mainStars);
        }
        else if (solveController != null)
        {
            solveController.ResetSolve();
        }
    }

    /// <summary>Load a practice puzzle (e.g. a title letter placed in the globe).</summary>
    public void StartPractice(string displayName, Vector2 artboardSize, List<ConstellationGenerator.StarData> stars)
    {
        if (stars == null || stars.Count < 2)
        {
            Debug.LogWarning("GameFlow: practice constellation needs at least two stars.", this);
            return;
        }

        IsPractice = true;
        BeginPuzzle();
        setup.LoadConstellation(displayName, artboardSize, stars);
    }

    /// <summary>
    /// Back to the title, from the post-puzzle menu or mid-puzzle: stops the
    /// intro, resets the solve (which cancels the settle, reveal, stamp,
    /// signing, celebration and menu) and hides the puzzle until the next one.
    /// </summary>
    [ContextMenu("Return To Title")]
    public void ReturnToTitle()
    {
        EnterSelection(Stage.Title, false);
    }

    // Title and Boxes are both "waiting" states: the puzzle is reset and held.
    private void EnterSelection(Stage stage, bool openPack)
    {
        if (intro != null)
        {
            intro.SetHold(true);
        }

        // Clears any solved / revealed / signed state; the held intro won't play.
        if (solveController != null && Current == Stage.Puzzle)
        {
            solveController.ResetSolve();
        }

        pendingOpenPack = openPack;
        SetStage(stage);
    }

    private void BeginPuzzle()
    {
        if (intro != null)
        {
            intro.SetHold(false);
        }

        SetStage(Stage.Puzzle);
    }

    private void SetStage(Stage stage)
    {
        Current = stage;
        ShowTitle(stage == Stage.Title);
        if (boxSelection != null)
        {
            if (stage == Stage.Boxes)
            {
                boxSelection.Show(pendingOpenPack);
            }
            else
            {
                boxSelection.Hide();
            }
        }

        pendingOpenPack = false;
        ApplyVisibility();
        Debug.Log($"GameFlow: {stage}{(stage == Stage.Puzzle ? (IsPractice ? " (practice)" : " (main)") : "")}", this);
        StageChanged?.Invoke(stage);
    }

    private void ApplyVisibility()
    {
        if (!visibilityReady)
        {
            return;
        }

        bool puzzle = Current == Stage.Puzzle;
        SetActive(puzzleObjects, puzzle);
        SetActive(mainOnlyObjects, puzzle && !IsPractice);
        SetPuzzleRenderersVisible(puzzle);
    }

    private static void SetActive(GameObject[] objects, bool active)
    {
        if (objects == null)
        {
            return;
        }

        foreach (GameObject go in objects)
        {
            if (go != null && go.activeSelf != active)
            {
                go.SetActive(active);
            }
        }
    }

    private void SetPuzzleRenderersVisible(bool visible)
    {
        if (visible)
        {
            foreach (Renderer r in hiddenRenderers)
            {
                if (r != null)
                {
                    r.enabled = true;
                }
            }

            hiddenRenderers.Clear();
            return;
        }

        if (puzzleRendererRoots == null)
        {
            return;
        }

        foreach (Transform root in puzzleRendererRoots)
        {
            if (root == null)
            {
                continue;
            }

            root.GetComponentsInChildren(true, rendererScratch);
            foreach (Renderer r in rendererScratch)
            {
                if (r.enabled)
                {
                    r.enabled = false;
                    hiddenRenderers.Add(r);
                }
            }
        }

        rendererScratch.Clear();
    }

    private void ShowTitle(bool show)
    {
        if (titleRoot != null && titleRoot.activeSelf != show)
        {
            titleRoot.SetActive(show);
        }
    }
}
