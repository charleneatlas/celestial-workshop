using UnityEngine;

/// <summary>
/// Two-track background music, chosen by <see cref="GameFlow"/>'s stage:
///   Workshop ("The Britons"): onboarding noticeboard, title, letter practice,
///                             box selection, and anything else outside a puzzle.
///   Puzzle ("Morning"):       a real constellation puzzle, through the solve,
///                             stamp, signing and post-puzzle menu.
/// Switching is driven only by GameFlow.StageChanged (no polling of the
/// scene). Tracks crossfade with equal power; a faded-out track is paused,
/// not stopped, so it resumes where it left off. Both loop. Non-spatial (2D).
///
/// "Background Music Enabled" fades both tracks out (sound effects are
/// untouched; AudioListener volume is never changed) and stays off until it
/// is ticked again, when the track for the current stage fades back in.
/// </summary>
[DisallowMultipleComponent]
public sealed class BackgroundMusic : MonoBehaviour
{
    public enum Track
    {
        Workshop,
        Puzzle
    }

    [Tooltip("Untick to silence the music (e.g. for capturing footage). Sound effects keep playing.")]
    [SerializeField]
    private bool backgroundMusicEnabled = true;

    [Header("Tracks")]
    [Tooltip("Workshop, title, practice and box selection: \"The Britons\" (Kevin MacLeod).")]
    [SerializeField]
    private AudioClip workshopMusic;

    [Tooltip("Constellation puzzles, solve and signing: \"Morning\" (Kevin MacLeod).")]
    [SerializeField]
    private AudioClip puzzleMusic;

    [Header("Mix")]
    [Range(0f, 1f)]
    [SerializeField]
    private float workshopMusicVolume = 0.16f;

    [Range(0f, 1f)]
    [SerializeField]
    private float puzzleMusicVolume = 0.28f;

    [Tooltip("Seconds for one track to fade out while the other fades in.")]
    [Range(0.1f, 6f)]
    [SerializeField]
    private float crossfadeDuration = 1.75f;

    [Tooltip("Seconds to fade out / in when Background Music Enabled is toggled.")]
    [Range(0f, 3f)]
    [SerializeField]
    private float toggleFadeDuration = 0.4f;

    [Header("Flow")]
    [Tooltip("Found automatically if empty.")]
    [SerializeField]
    private GameFlow gameFlow;

    [Tooltip("Letter practice keeps the workshop track (it is part of the title). Tick to give practice puzzles the puzzle track instead.")]
    [SerializeField]
    private bool practiceUsesPuzzleMusic;

    [SerializeField]
    private bool logChanges = true;

    private sealed class Voice
    {
        public AudioSource source;
        public float fade;      // 0..1, equal-power curve applied on output
        public bool started;
    }

    private Voice workshop;
    private Voice puzzle;
    private float master;       // 0..1 enable fade
    private Track desired = Track.Workshop;
    private bool subscribed;

    public bool MusicEnabled
    {
        get => backgroundMusicEnabled;
        set => backgroundMusicEnabled = value;
    }

    public Track Current => desired;

    private void Awake()
    {
        if (gameFlow == null)
        {
            gameFlow = FindFirstObjectByType<GameFlow>();
        }

        workshop = CreateVoice("Workshop Music", workshopMusic);
        puzzle = CreateVoice("Puzzle Music", puzzleMusic);
        master = backgroundMusicEnabled ? 1f : 0f;
    }

    private void OnEnable()
    {
        if (gameFlow != null && !subscribed)
        {
            gameFlow.StageChanged += OnStageChanged;
            subscribed = true;
        }

        // GameFlow sets its first stage in its own Awake: pick up wherever it is.
        // The opening track fades in over one crossfade.
        desired = Choose();
    }

    private void OnDisable()
    {
        if (gameFlow != null && subscribed)
        {
            gameFlow.StageChanged -= OnStageChanged;
        }

        subscribed = false;
        Pause(workshop);
        Pause(puzzle);
    }

    private void OnStageChanged(GameFlow.Stage stage)
    {
        Track next = Choose();
        // (Before anything has played, GameFlow is still setting its first stage: no log.)
        if (next != desired && logChanges && (workshop.started || puzzle.started))
        {
            Debug.Log($"BackgroundMusic: {desired} -> {next} ({stage})", this);
        }

        desired = next;
    }

    private Track Choose()
    {
        if (gameFlow == null || gameFlow.Current != GameFlow.Stage.Puzzle)
        {
            return Track.Workshop;
        }

        return gameFlow.IsPractice && !practiceUsesPuzzleMusic ? Track.Workshop : Track.Puzzle;
    }

    private void Update()
    {
        Apply(Time.unscaledDeltaTime);
    }

    private void Apply(float dt)
    {
        if (workshop == null || puzzle == null)
        {
            return;
        }

        float masterRate = toggleFadeDuration > 0f ? dt / toggleFadeDuration : 1f;
        master = Mathf.MoveTowards(master, backgroundMusicEnabled ? 1f : 0f, dt > 0f ? masterRate : 0f);

        float rate = dt / Mathf.Max(0.01f, crossfadeDuration);
        Step(workshop, desired == Track.Workshop, workshopMusicVolume, rate);
        Step(puzzle, desired == Track.Puzzle, puzzleMusicVolume, rate);
    }

    private void Step(Voice voice, bool wanted, float volume, float rate)
    {
        voice.fade = Mathf.MoveTowards(voice.fade, wanted ? 1f : 0f, rate);

        // Equal power: the sum stays steady through the crossfade.
        float gain = Mathf.Sin(voice.fade * Mathf.PI * 0.5f) * master * volume;
        AudioSource source = voice.source;
        source.volume = gain;

        if (gain > 0.0001f)
        {
            if (!source.isPlaying && source.clip != null)
            {
                if (voice.started)
                {
                    source.UnPause();
                    if (!source.isPlaying)
                    {
                        source.Play(); // stopped elsewhere: start over rather than stay silent
                    }
                }
                else
                {
                    source.Play();
                    voice.started = true;
                }
            }
        }
        else if (source.isPlaying)
        {
            // Silent: pause so it resumes from here next time (one copy per track, never restarted).
            source.Pause();
        }
    }

    private static void Pause(Voice voice)
    {
        if (voice != null && voice.source != null && voice.source.isPlaying)
        {
            voice.source.Pause();
        }
    }

    private Voice CreateVoice(string name, AudioClip clip)
    {
        Transform existing = transform.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        go.transform.SetParent(transform, false);
        AudioSource source = go.GetComponent<AudioSource>();
        if (source == null)
        {
            source = go.AddComponent<AudioSource>();
        }

        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.bypassReverbZones = true;
        source.priority = 0; // never the voice Unity drops when many sounds play
        source.volume = 0f;
        return new Voice { source = source };
    }
}
