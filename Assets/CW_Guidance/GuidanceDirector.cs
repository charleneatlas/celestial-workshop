using System;
using System.Collections.Generic;
using UnityEngine;

namespace CW.Guidance
{
    /// <summary>One authored guidance cue.</summary>
    [Serializable]
    public class GuidanceStep
    {
        [Tooltip("Key the game uses with Show / Complete, e.g. \"rotate\".")]
        public string id;
        [Tooltip("Keep it short: 8 words or fewer.")]
        [TextArea(1, 2)] public string text;
        [Tooltip("What the hint is about. Can be replaced at runtime (Show(id, target) or SetTarget). Empty: the label floats in front of the viewer.")]
        public Transform target;
        [Tooltip("Label offset from the top-centre of the target's bounds, in viewer-relative axes (metres): " +
                 "x = viewer's horizontal right, y = world up, z = toward the viewer. The label is then pulled inside the gaze cone.")]
        public Vector3 labelOffset = new Vector3(0f, 0.05f, 0f);
        [Tooltip("Breathing halo around the target.")]
        public bool pulseTarget = true;
        [Tooltip("Optional: the halo surrounds this instead of the target (the label still follows the target). E.g. label by the globe, halo around the constellation inside it.")]
        public Transform pulseAround;
        [Tooltip("Halo radius in metres; 0 = from the renderer bounds of what it surrounds.")]
        public float pulseRadius;
        [Tooltip("0 = stays until completed. Otherwise the cue fades after this many seconds; the step stays logically active so Complete still records it.")]
        public float autoHideSeconds;
        [Tooltip("Once completed, Show(id) is ignored for the rest of the session (and across launches if the director remembers them).")]
        public bool onlyOnce;
    }

    /// <summary>
    /// Shows one guidance cue at a time: a <see cref="HintLabel"/> plus an
    /// optional <see cref="TargetPulse"/> on the step's target. Showing a new
    /// step replaces the current one. Complete(id) only acts on the active
    /// step and plays a short success acknowledgement (about 0.9 s); a step
    /// shown during that acknowledgement becomes active at once but appears
    /// when the acknowledgement ends, so Complete("a"); Show("b") reads well.
    /// </summary>
    [DisallowMultipleComponent]
    public class GuidanceDirector : MonoBehaviour
    {
        [Tooltip("Head / camera. Empty uses Camera.main.")]
        public Transform viewer;
        [Tooltip("Empty: found in children, or created.")]
        public HintLabel label;
        [Tooltip("Empty: found in children, or created.")]
        public TargetPulse pulse;
        public List<GuidanceStep> steps = new List<GuidanceStep>();

        [Header("Memory of onlyOnce steps")]
        [Tooltip("Also store completed onlyOnce steps in PlayerPrefs, so they stay skipped across launches.")]
        public bool rememberAcrossLaunches;
        public string playerPrefsPrefix = "CW.Guidance.done.";
        public bool logSteps;

        /// <summary>Raised with the step id when a step is shown.</summary>
        public event Action<string> StepShown;
        /// <summary>Raised with the step id when the active step is completed.</summary>
        public event Action<string> StepCompleted;

        // Completed onlyOnce ids for this session (shared by all directors).
        private static readonly HashSet<string> SessionDone = new HashSet<string>();

        private GuidanceStep active;
        private float shownAt;
        private bool autoHidden;
        // A new step waits for the previous step's success acknowledgement.
        private bool visualsPending;
        private int lastShownIndex = -1;

        /// <summary>Id of the step on screen (or auto-hidden but not yet completed); null when none.</summary>
        public string ActiveId => active != null ? active.id : null;

        public Transform Viewer
        {
            get
            {
                if (viewer != null) return viewer;
                Camera main = Camera.main;
                return main != null ? main.transform : null;
            }
        }

        public bool IsActive(string id)
        {
            return active != null && !string.IsNullOrEmpty(id) && active.id == id;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SessionDone.Clear();
        }

        private void Awake()
        {
            EnsureParts();
        }

        private void OnDisable()
        {
            Clear(immediate: true);
        }

        private void Update()
        {
            if (active != null && visualsPending && (label == null || !label.IsCompleting))
            {
                visualsPending = false;
                shownAt = Time.time;
                ShowVisuals(active);
            }

            if (active != null && !autoHidden && !visualsPending && active.autoHideSeconds > 0f
                && Time.time - shownAt >= active.autoHideSeconds)
            {
                autoHidden = true;
                if (label != null) label.Hide();
                if (pulse != null) pulse.Hide();
            }
        }

        /// <summary>Shows the step with this id, replacing any other. Returns false if unknown or already done (onlyOnce).</summary>
        public bool Show(string id)
        {
            return Show(id, null);
        }

        /// <summary>
        /// Shows the step, optionally pointing at a target other than the
        /// authored one (handy for objects created at runtime).
        /// </summary>
        public bool Show(string id, Transform targetOverride)
        {
            int index = IndexOf(id);
            if (index < 0)
            {
                Debug.LogWarning($"[Guidance] No step with id '{id}'.", this);
                return false;
            }

            GuidanceStep step = steps[index];
            if (step.onlyOnce && WasCompleted(id))
            {
                return false;
            }

            EnsureParts();
            lastShownIndex = index;
            if (targetOverride != null)
            {
                step.target = targetOverride;
            }

            if (active == step && !autoHidden)
            {
                // Already showing: just follow a new target if one was given.
                if (targetOverride != null && !visualsPending)
                {
                    ShowVisuals(step);
                }

                return true;
            }

            active = step;
            shownAt = Time.time;
            autoHidden = false;
            visualsPending = label != null && label.IsCompleting;
            if (!visualsPending)
            {
                ShowVisuals(step);
            }
            if (logSteps) Debug.Log($"[Guidance] Show '{id}'", this);
            StepShown?.Invoke(id);
            return true;
        }

        /// <summary>
        /// Completes the step if it is the active one: records it (onlyOnce),
        /// plays the success acknowledgement and raises StepCompleted.
        /// Returns false if it was not active.
        /// </summary>
        public bool Complete(string id)
        {
            if (!IsActive(id))
            {
                return false;
            }

            GuidanceStep step = active;
            active = null;
            if (step.onlyOnce)
            {
                RecordCompleted(id);
            }

            if (!autoHidden && !visualsPending)
            {
                if (label != null) label.PlayCompleted();
                if (pulse != null) pulse.PlayCompleted();
            }

            autoHidden = false;
            visualsPending = false;
            if (logSteps) Debug.Log($"[Guidance] Complete '{id}'", this);
            StepCompleted?.Invoke(id);
            return true;
        }

        /// <summary>Hides whatever is showing (fading), without completing it.</summary>
        public void Clear()
        {
            Clear(immediate: false);
        }

        public void Clear(bool immediate)
        {
            active = null;
            autoHidden = false;
            visualsPending = false;
            if (label != null)
            {
                if (immediate) label.HideImmediate(); else label.Hide();
            }

            if (pulse != null)
            {
                if (immediate) pulse.HideImmediate(); else pulse.Hide();
            }
        }

        /// <summary>Shows the first step after the last one shown that is not already done. False at the end.</summary>
        public bool ShowNext()
        {
            for (int i = lastShownIndex + 1; i < steps.Count; i++)
            {
                GuidanceStep step = steps[i];
                if (step == null || (step.onlyOnce && WasCompleted(step.id)))
                {
                    continue;
                }

                return Show(step.id);
            }

            return false;
        }

        /// <summary>Restarts ShowNext from the first step (does not forget completions).</summary>
        public void RestartSequence()
        {
            lastShownIndex = -1;
        }

        /// <summary>Changes a step's target; updates the visuals if it is showing.</summary>
        public void SetTarget(string id, Transform target)
        {
            int index = IndexOf(id);
            if (index < 0) return;
            steps[index].target = target;
            if (active == steps[index] && !autoHidden && !visualsPending)
            {
                ShowVisuals(active);
            }
        }

        /// <summary>
        /// Records an onlyOnce step as done without it having been shown (e.g.
        /// the player rotated before the hint appeared).
        /// </summary>
        public void RecordCompleted(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            SessionDone.Add(id);
            if (rememberAcrossLaunches)
            {
                PlayerPrefs.SetInt(playerPrefsPrefix + id, 1);
                PlayerPrefs.Save();
            }
        }

        public bool WasCompleted(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return SessionDone.Contains(id)
                || (rememberAcrossLaunches && PlayerPrefs.GetInt(playerPrefsPrefix + id, 0) == 1);
        }

        /// <summary>Forgets every completed step (session, and PlayerPrefs for this director's steps).</summary>
        public void ResetMemory()
        {
            SessionDone.Clear();
            foreach (GuidanceStep step in steps)
            {
                if (step != null && !string.IsNullOrEmpty(step.id))
                {
                    PlayerPrefs.DeleteKey(playerPrefsPrefix + step.id);
                }
            }

            lastShownIndex = -1;
        }

        private void ShowVisuals(GuidanceStep step)
        {
            Transform eye = Viewer;
            if (label != null)
            {
                label.Show(step.text, step.target, step.labelOffset, eye);
            }

            if (pulse != null)
            {
                Transform around = step.pulseAround != null ? step.pulseAround : step.target;
                if (step.pulseTarget && around != null)
                {
                    pulse.Show(around, step.pulseRadius, eye);
                }
                else
                {
                    pulse.Hide();
                }
            }
        }

        private int IndexOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] != null && steps[i].id == id) return i;
            }

            return -1;
        }

        private void EnsureParts()
        {
            if (label == null) label = GetComponentInChildren<HintLabel>(true);
            if (label == null)
            {
                var go = new GameObject("HintLabel");
                go.transform.SetParent(transform, false);
                label = go.AddComponent<HintLabel>();
            }

            if (pulse == null) pulse = GetComponentInChildren<TargetPulse>(true);
            if (pulse == null)
            {
                var go = new GameObject("TargetPulse");
                go.transform.SetParent(transform, false);
                pulse = go.AddComponent<TargetPulse>();
            }
        }
    }
}
