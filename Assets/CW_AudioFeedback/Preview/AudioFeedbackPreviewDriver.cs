using UnityEngine;
using UnityEngine.InputSystem;

namespace CW.AudioFeedback
{
    /// <summary>
    /// Preview-only driver: keys 1..9, 0 and - fire the cues in FeedbackCue
    /// order, M toggles mute, and an IMGUI panel offers the same as buttons.
    /// Spatial cues play at <see cref="spatialEmitter"/> when it is set.
    /// Not meant for the production scene.
    /// </summary>
    public sealed class AudioFeedbackPreviewDriver : MonoBehaviour
    {
        private static readonly Key[] CueKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
            Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0,
            Key.Minus,
        };

        private static readonly int CueCount =
            Mathf.Min(11, System.Enum.GetValues(typeof(FeedbackCue)).Length);

        private static readonly string[] KeyLabels =
        {
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-",
        };

        [SerializeField]
        private AudioFeedback feedback;

        [Tooltip("Spatial cues play here (PlayAt). Empty = everything plays 2D.")]
        [SerializeField]
        private Transform spatialEmitter;

        [SerializeField]
        private bool showGui = true;

        private string lastFired = "-";

        public void Configure(AudioFeedback target, Transform emitter)
        {
            feedback = target;
            spatialEmitter = emitter;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || feedback == null)
            {
                return;
            }

            int count = CueCount;
            for (int i = 0; i < count; i++)
            {
                if (keyboard[CueKeys[i]].wasPressedThisFrame)
                {
                    Fire((FeedbackCue)i);
                }
            }

            if (keyboard[Key.M].wasPressedThisFrame)
            {
                feedback.SetMuted(!feedback.IsMuted);
            }
        }

        private void Fire(FeedbackCue cue)
        {
            if (spatialEmitter != null)
            {
                feedback.PlayAt(cue, spatialEmitter.position);
            }
            else
            {
                feedback.Play(cue);
            }

            lastFired = cue.ToString();
        }

        private void OnGUI()
        {
            if (!showGui || feedback == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(10f, 10f, 460f, Screen.height - 20f), GUI.skin.box);
            GUILayout.Label("Audio Feedback Preview  (keys 1..0, -  |  M = mute)");

            int count = CueCount;
            for (int i = 0; i < count; i++)
            {
                var cue = (FeedbackCue)i;
                FeedbackCueEntry entry = feedback.GetEntry(cue);
                string info;
                if (entry == null)
                {
                    info = "no entry";
                }
                else
                {
                    int clips = 0;
                    if (entry.variants != null)
                    {
                        for (int c = 0; c < entry.variants.Length; c++)
                        {
                            if (entry.variants[c] != null)
                            {
                                clips++;
                            }
                        }
                    }

                    info = clips == 0
                        ? "MISSING"
                        : $"{clips} clip(s), vol {entry.volume:0.00}{(entry.spatial ? ", 3D" : ", 2D")}";
                    if (!string.IsNullOrEmpty(entry.notes) && entry.notes.Contains("TEMP"))
                    {
                        info += ", TEMP";
                    }
                }

                if (GUILayout.Button($"[{KeyLabels[i]}] {cue}  -  {info}"))
                {
                    Fire(cue);
                }
            }

            GUILayout.Space(6f);
            GUILayout.Label($"Master volume: {feedback.MasterVolume:0.00}");
            feedback.MasterVolume = GUILayout.HorizontalSlider(feedback.MasterVolume, 0f, 1f);
            bool mute = GUILayout.Toggle(feedback.IsMuted, "Muted");
            if (mute != feedback.IsMuted)
            {
                feedback.SetMuted(mute);
            }

            GUILayout.Label("Last fired: " + lastFired);
            GUILayout.EndArea();
        }
    }
}
