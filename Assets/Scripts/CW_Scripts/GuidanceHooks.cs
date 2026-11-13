using System.Collections;
using CW.Guidance;
using UnityEngine;

/// <summary>
/// Drives the one-at-a-time guidance cues from game events. Step ids match the
/// GuidanceDirector's step list (see Assets/CW_Guidance/GuidanceCopy.md):
/// title-letter, title-start, rotate, match, sign, menu. Steps are onlyOnce in
/// the director, so after the first puzzle they stay quiet.
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

    [Tooltip("Seconds the letter hint shows before the Start hint replaces it.")]
    [SerializeField]
    private float letterHintSeconds = 8f;

    private Coroutine titleRoutine;

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

        if (stage == GameFlow.Stage.Title)
        {
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

    private IEnumerator TitleHints()
    {
        // Wait for the title to be shown and placed.
        yield return new WaitForSeconds(1f);
        if (director.Show("title-letter"))
        {
            yield return new WaitForSeconds(letterHintSeconds);
        }

        if (gameFlow == null || gameFlow.Current == GameFlow.Stage.Title)
        {
            director.Show("title-start");
        }

        titleRoutine = null;
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
            // Shown after the rotate acknowledgement finishes.
            director.Show("match");
        }
    }

    private void OnNearSolve()
    {
        if (director != null)
        {
            director.Complete("match");
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
        if (director != null)
        {
            director.Show("sign");
        }
    }

    private void OnSigned()
    {
        if (director != null)
        {
            // Completing first plays the acknowledgement; the menu hint follows it.
            director.Complete("sign");
            director.Show("menu");
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
            if (director.IsActive("rotate") || director.IsActive("match") || director.IsActive("sign"))
            {
                director.Clear();
            }
        }
    }
}
