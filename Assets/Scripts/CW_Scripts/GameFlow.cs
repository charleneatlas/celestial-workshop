using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Top-level flow: Title -> Puzzle (a practice letter or the main constellation).
/// At the title the globe holds no puzzle (display dark, rotation and solving
/// off, via ConstellationIntro's hold). Start loads the main constellation;
/// placing a letter loads that letter as a practice puzzle. The solve, stamp,
/// signing and menu flow is unchanged; PostPuzzleMenu asks this to move on.
/// </summary>
public sealed class GameFlow : MonoBehaviour
{
    public enum Stage
    {
        Title,
        Puzzle
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

    [Tooltip("Off: skip the title and start the main puzzle as before.")]
    [SerializeField]
    private bool startAtTitle = true;

    public Stage Current { get; private set; } = Stage.Puzzle;

    /// <summary>True while the loaded puzzle is a practice letter rather than the main constellation.</summary>
    public bool IsPractice { get; private set; }

    public event Action<Stage> StageChanged;

    // The scene's own constellation, captured before anything swaps it.
    private string mainName;
    private Vector2 mainArtboard;
    private List<ConstellationGenerator.StarData> mainStars;

    private void Awake()
    {
        CaptureMain();

        // Before ConstellationSetup's first reset, so the startup intro is held.
        if (startAtTitle && intro != null)
        {
            intro.SetHold(true);
            SetStage(Stage.Title);
        }
        else
        {
            ShowTitle(false);
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

    /// <summary>Start the main constellation puzzle (title Start, or after practice).</summary>
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

    /// <summary>Back to the title: the puzzle is reset and held hidden.</summary>
    [ContextMenu("Return To Title")]
    public void ReturnToTitle()
    {
        if (intro != null)
        {
            intro.SetHold(true);
        }

        // Clears any solved / revealed / signed state; the held intro won't play.
        if (solveController != null)
        {
            solveController.ResetSolve();
        }

        SetStage(Stage.Title);
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
        Debug.Log($"GameFlow: {stage}{(stage == Stage.Puzzle ? (IsPractice ? " (practice)" : " (main)") : "")}", this);
        StageChanged?.Invoke(stage);
    }

    private void ShowTitle(bool show)
    {
        if (titleRoot != null && titleRoot.activeSelf != show)
        {
            titleRoot.SetActive(show);
        }
    }
}
