using System;
using UnityEngine;
using UnityEngine.Audio;

namespace CW.AudioFeedback
{
    /// <summary>
    /// Feedback cue keys. Inspector entries store the cue as its integer
    /// value, so append new cues at the end; never reorder or remove.
    /// </summary>
    public enum FeedbackCue
    {
        Select = 0,
        Release = 1,
        Place = 2,
        AxisLock = 3,
        NearSolve = 4,
        Solve = 5,
        Stamp = 6,
        Signed = 7,
        ButtonPress = 8,
        IntroStart = 9,
        TitleStart = 10,
    }

    /// <summary>One cue's clips and playback settings.</summary>
    [Serializable]
    public sealed class FeedbackCueEntry
    {
        public FeedbackCue cue;

        [Tooltip("One is picked at random per play. Empty = the cue is silent (warned once).")]
        public AudioClip[] variants = new AudioClip[0];

        [Range(0f, 1f)]
        public float volume = 0.7f;

        [Tooltip("Random pitch offset, +/- this amount around 1.")]
        [Range(0f, 0.3f)]
        public float pitchJitter = 0.03f;

        [Tooltip("Debounce: a play request within this many seconds of the last play of this cue is dropped.")]
        [Min(0f)]
        public float cooldownSeconds = 0.1f;

        [Tooltip("PlayAt() plays this cue in 3D at the given point. Play() is always 2D.")]
        public bool spatial;

        [Tooltip("Non-repeat window: a play within this many seconds of the previous one avoids the previous variant (needs 2+ variants). 0 = pure random.")]
        [Min(0f)]
        public float minInterval;

        [Tooltip("Free text: clip provenance, TEMP stand-ins, hookup point.")]
        [TextArea(1, 3)]
        public string notes;
    }

    /// <summary>
    /// Small, reusable one-shot audio feedback player. Put one in the scene,
    /// fill the cue table, then call Play / PlayAt by reference or through the
    /// static <see cref="Instance"/> (or the null-safe <see cref="Fire"/>).
    /// No loops, nothing per frame: every call is a single one-shot on a
    /// pooled AudioSource; a cue fired again within its cooldown is dropped.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioFeedback : MonoBehaviour
    {
        private static readonly int CueCount = Enum.GetValues(typeof(FeedbackCue)).Length;

        [Header("Mix")]
        [Range(0f, 1f)]
        [SerializeField]
        private float masterVolume = 1f;

        [SerializeField]
        private bool muted;

        [Tooltip("Optional mixer group for all voices.")]
        [SerializeField]
        private AudioMixerGroup outputGroup;

        [Header("Voices")]
        [Tooltip("AudioSources created at startup. When all are busy, the oldest voice is reused.")]
        [Range(1, 16)]
        [SerializeField]
        private int poolSize = 6;

        [Tooltip("3D voices: full volume within this distance (tabletop scale).")]
        [Min(0.01f)]
        [SerializeField]
        private float spatialMinDistance = 0.3f;

        [Tooltip("3D voices: silent beyond this distance (linear rolloff).")]
        [Min(0.02f)]
        [SerializeField]
        private float spatialMaxDistance = 5f;

        [Tooltip("Load every cue clip's audio data on Awake, so the first play has no load hitch (several project clips have Preload Audio Data off).")]
        [SerializeField]
        private bool preloadClips = true;

        [Header("Cues")]
        [SerializeField]
        private FeedbackCueEntry[] entries = new FeedbackCueEntry[0];

        private AudioSource[] voices;
        private float[] voiceStartTimes;
        private float[] lastPlayTimes;
        private int[] lastVariants;
        private bool[] warned;

        /// <summary>The most recently enabled instance (null when none).</summary>
        public static AudioFeedback Instance { get; private set; }

        public float MasterVolume
        {
            get => masterVolume;
            set => masterVolume = Mathf.Clamp01(value);
        }

        public bool IsMuted => muted;

        /// <summary>Null-safe <c>Instance.Play(cue)</c>.</summary>
        public static void Fire(FeedbackCue cue)
        {
            if (Instance != null)
            {
                Instance.Play(cue);
            }
        }

        /// <summary>Null-safe <c>Instance.PlayAt(cue, position)</c>.</summary>
        public static void FireAt(FeedbackCue cue, Vector3 position)
        {
            if (Instance != null)
            {
                Instance.PlayAt(cue, position);
            }
        }

        /// <summary>Plays the cue as 2D (non-positional) sound.</summary>
        public void Play(FeedbackCue cue)
        {
            PlayInternal(cue, false, Vector3.zero);
        }

        /// <summary>Plays the cue at a world position (3D if the entry is spatial, else 2D).</summary>
        public void PlayAt(FeedbackCue cue, Vector3 position)
        {
            FeedbackCueEntry entry = GetEntry(cue);
            PlayInternal(cue, entry != null && entry.spatial, position);
        }

        /// <summary>UnityEvent-friendly: the cue's integer value (see FeedbackCue).</summary>
        public void PlayByIndex(int cueIndex)
        {
            if (cueIndex < 0 || cueIndex >= CueCount)
            {
                Debug.LogWarning($"[AudioFeedback] No cue with index {cueIndex}.", this);
                return;
            }

            Play((FeedbackCue)cueIndex);
        }

        /// <summary>UnityEvent-friendly: the cue's name, e.g. "Solve".</summary>
        public void PlayByName(string cueName)
        {
            if (Enum.TryParse(cueName, true, out FeedbackCue cue))
            {
                Play(cue);
            }
            else
            {
                Debug.LogWarning($"[AudioFeedback] No cue named '{cueName}'.", this);
            }
        }

        /// <summary>Muting stops any voice still playing and drops new plays until unmuted.</summary>
        public void SetMuted(bool value)
        {
            muted = value;
            if (muted && voices != null)
            {
                for (int i = 0; i < voices.Length; i++)
                {
                    if (voices[i] != null)
                    {
                        voices[i].Stop();
                    }
                }
            }
        }

        /// <summary>First entry for the cue, or null.</summary>
        public FeedbackCueEntry GetEntry(FeedbackCue cue)
        {
            if (entries == null)
            {
                return null;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].cue == cue)
                {
                    return entries[i];
                }
            }

            return null;
        }

        /// <summary>Replaces the cue table (editor tooling / runtime setup).</summary>
        public void ConfigureEntries(FeedbackCueEntry[] newEntries)
        {
            entries = newEntries ?? new FeedbackCueEntry[0];
            if (warned != null)
            {
                Array.Clear(warned, 0, warned.Length);
            }
        }

        private void Awake()
        {
            EnsureState();
            if (preloadClips)
            {
                PreloadClips();
            }
        }

        private void PreloadClips()
        {
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                AudioClip[] clips = entries[i] != null ? entries[i].variants : null;
                if (clips == null)
                {
                    continue;
                }

                for (int c = 0; c < clips.Length; c++)
                {
                    if (clips[c] != null && clips[c].loadState == AudioDataLoadState.Unloaded)
                    {
                        clips[c].LoadAudioData();
                    }
                }
            }
        }

        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Reset()
        {
            // One empty entry per cue so the table is ready to fill.
            entries = new FeedbackCueEntry[CueCount];
            for (int i = 0; i < CueCount; i++)
            {
                entries[i] = new FeedbackCueEntry { cue = (FeedbackCue)i };
            }
        }

        private void EnsureState()
        {
            if (lastPlayTimes == null)
            {
                lastPlayTimes = new float[CueCount];
                lastVariants = new int[CueCount];
                warned = new bool[CueCount];
                for (int i = 0; i < CueCount; i++)
                {
                    lastPlayTimes[i] = float.NegativeInfinity;
                    lastVariants[i] = -1;
                }
            }

            if (voices == null)
            {
                int count = Mathf.Max(1, poolSize);
                voices = new AudioSource[count];
                voiceStartTimes = new float[count];
                for (int i = 0; i < count; i++)
                {
                    var voiceObject = new GameObject("Voice_" + i);
                    voiceObject.transform.SetParent(transform, false);
                    AudioSource source = voiceObject.AddComponent<AudioSource>();
                    source.playOnAwake = false;
                    source.loop = false;
                    source.outputAudioMixerGroup = outputGroup;
                    source.dopplerLevel = 0f;
                    source.rolloffMode = AudioRolloffMode.Linear;
                    source.minDistance = spatialMinDistance;
                    source.maxDistance = Mathf.Max(spatialMaxDistance, spatialMinDistance + 0.01f);
                    source.spatialBlend = 0f;
                    voices[i] = source;
                    voiceStartTimes[i] = float.NegativeInfinity;
                }
            }
        }

        private void PlayInternal(FeedbackCue cue, bool spatial, Vector3 position)
        {
            if (muted || !isActiveAndEnabled)
            {
                return;
            }

            EnsureState();

            int cueIndex = (int)cue;
            if (cueIndex < 0 || cueIndex >= CueCount)
            {
                return;
            }

            FeedbackCueEntry entry = GetEntry(cue);
            if (entry == null || entry.variants == null)
            {
                WarnOnce(cueIndex, $"[AudioFeedback] Cue {cue} has no entry; it stays silent.");
                return;
            }

            float now = Time.unscaledTime;
            if (now - lastPlayTimes[cueIndex] < entry.cooldownSeconds)
            {
                return;
            }

            int variant = PickVariant(entry, cueIndex, now);
            if (variant < 0)
            {
                WarnOnce(cueIndex, $"[AudioFeedback] Cue {cue} has no clip; it stays silent.");
                return;
            }

            float volume = entry.volume * masterVolume;
            if (volume <= 0f)
            {
                return;
            }

            AudioSource voice = TakeVoice(now);
            if (voice == null)
            {
                return;
            }

            voice.Stop();
            voice.clip = entry.variants[variant];
            voice.volume = volume;
            voice.pitch = 1f + UnityEngine.Random.Range(-entry.pitchJitter, entry.pitchJitter);
            voice.spatialBlend = spatial ? 1f : 0f;
            voice.transform.position = spatial ? position : transform.position;
            voice.Play();

            lastPlayTimes[cueIndex] = now;
            lastVariants[cueIndex] = variant;
        }

        private int PickVariant(FeedbackCueEntry entry, int cueIndex, float now)
        {
            AudioClip[] clips = entry.variants;
            int valid = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                {
                    valid++;
                }
            }

            if (valid == 0)
            {
                return -1;
            }

            int exclude = -1;
            int last = lastVariants[cueIndex];
            if (valid > 1
                && entry.minInterval > 0f
                && now - lastPlayTimes[cueIndex] < entry.minInterval
                && last >= 0
                && last < clips.Length
                && clips[last] != null)
            {
                exclude = last;
            }

            int pick = UnityEngine.Random.Range(0, exclude >= 0 ? valid - 1 : valid);
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null || i == exclude)
                {
                    continue;
                }

                if (pick == 0)
                {
                    return i;
                }

                pick--;
            }

            return -1;
        }

        private AudioSource TakeVoice(float now)
        {
            int oldest = -1;
            float oldestTime = float.PositiveInfinity;
            for (int i = 0; i < voices.Length; i++)
            {
                AudioSource source = voices[i];
                if (source == null)
                {
                    continue;
                }

                if (!source.isPlaying)
                {
                    voiceStartTimes[i] = now;
                    return source;
                }

                if (voiceStartTimes[i] < oldestTime)
                {
                    oldestTime = voiceStartTimes[i];
                    oldest = i;
                }
            }

            if (oldest < 0)
            {
                return null;
            }

            voiceStartTimes[oldest] = now;
            return voices[oldest];
        }

        private void WarnOnce(int cueIndex, string message)
        {
            if (warned[cueIndex])
            {
                return;
            }

            warned[cueIndex] = true;
            Debug.LogWarning(message, this);
        }
    }
}
