using CW.AudioFeedback;
using UnityEngine;

/// <summary>
/// Connects game events to AudioFeedback cues (one place, so the cue list in
/// AudioFeedback stays the only thing to tune). Every hook is a discrete event;
/// nothing here fires per frame. All references are optional.
/// </summary>
public sealed class AudioFeedbackHooks : MonoBehaviour
{
    [SerializeField]
    private AudioFeedback audioFeedback;

    [SerializeField]
    private VolumePinchRotation pinchRotation;

    [Tooltip("Where pinch sounds come from (the sphere volume).")]
    [SerializeField]
    private Transform pinchSoundOrigin;

    [SerializeField]
    private ConstellationIntro intro;

    [SerializeField]
    private ConstellationSolveController solveController;

    [SerializeField]
    private SkyStamp3DVFX stamp;

    [SerializeField]
    private ConstellationSigning signing;

    [SerializeField]
    private GameFlow gameFlow;

    [Tooltip("Menu / title buttons that click on press.")]
    [SerializeField]
    private MomentaryToggleButton[] buttons = new MomentaryToggleButton[0];

    private void OnEnable()
    {
        if (pinchRotation != null)
        {
            pinchRotation.PinchStarted += OnPinchStarted;
            pinchRotation.AxisLocked += OnAxisLocked;
            pinchRotation.PinchEnded += OnPinchEnded;
        }

        if (intro != null)
        {
            intro.Started += OnIntroStarted;
        }

        if (solveController != null)
        {
            solveController.NearSolveEntered.AddListener(OnNearSolve);
            solveController.Solved.AddListener(OnSolved);
        }

        if (stamp != null)
        {
            stamp.Played += OnStamp;
        }

        if (signing != null)
        {
            signing.Signed += OnSigned;
        }

        if (gameFlow != null)
        {
            gameFlow.StageChanged += OnStageChanged;
        }

        foreach (MomentaryToggleButton button in buttons)
        {
            if (button != null)
            {
                button.Pressed += OnButtonPressed;
            }
        }
    }

    private void OnDisable()
    {
        if (pinchRotation != null)
        {
            pinchRotation.PinchStarted -= OnPinchStarted;
            pinchRotation.AxisLocked -= OnAxisLocked;
            pinchRotation.PinchEnded -= OnPinchEnded;
        }

        if (intro != null)
        {
            intro.Started -= OnIntroStarted;
        }

        if (solveController != null)
        {
            solveController.NearSolveEntered.RemoveListener(OnNearSolve);
            solveController.Solved.RemoveListener(OnSolved);
        }

        if (stamp != null)
        {
            stamp.Played -= OnStamp;
        }

        if (signing != null)
        {
            signing.Signed -= OnSigned;
        }

        if (gameFlow != null)
        {
            gameFlow.StageChanged -= OnStageChanged;
        }

        foreach (MomentaryToggleButton button in buttons)
        {
            if (button != null)
            {
                button.Pressed -= OnButtonPressed;
            }
        }
    }

    private void Cue(FeedbackCue cue)
    {
        if (audioFeedback != null)
        {
            audioFeedback.Play(cue);
        }
    }

    private void CueAt(FeedbackCue cue, Vector3 position)
    {
        if (audioFeedback != null)
        {
            audioFeedback.PlayAt(cue, position);
        }
    }

    private Vector3 PinchOrigin => pinchSoundOrigin != null ? pinchSoundOrigin.position : transform.position;

    private void OnPinchStarted() => CueAt(FeedbackCue.Select, PinchOrigin);
    private void OnAxisLocked() => CueAt(FeedbackCue.AxisLock, PinchOrigin);
    private void OnPinchEnded() => CueAt(FeedbackCue.Release, PinchOrigin);
    private void OnIntroStarted() => Cue(FeedbackCue.IntroStart);
    private void OnNearSolve() => Cue(FeedbackCue.NearSolve);
    private void OnSolved() => Cue(FeedbackCue.Solve);
    private void OnStamp() => Cue(FeedbackCue.Stamp);
    private void OnSigned() => Cue(FeedbackCue.Signed);

    private void OnStageChanged(GameFlow.Stage stage)
    {
        if (stage != GameFlow.Stage.Puzzle)
        {
            return;
        }

        Cue(gameFlow.IsPractice ? FeedbackCue.Place : FeedbackCue.TitleStart);
    }

    private void OnButtonPressed()
    {
        Cue(FeedbackCue.ButtonPress);
    }
}
