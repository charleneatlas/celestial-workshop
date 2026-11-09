using UnityEngine;

/// <summary>
/// Little toy hops for the diorama's people and sheep when the puzzle is
/// solved. Wire Celebrate() to the solve controller's On Solved event and
/// StopCelebration() to On Reset.
///
/// Each figure root (person or sheep, with all its parts) is lifted along the
/// diorama's local up, on top of its resting pose. ObserverDiorama re-seats
/// every figure on the ground in its own LateUpdate (execution order 110), so
/// this runs after it and adds the hop offset; the figures can't drift, and
/// their orientation is never touched.
/// </summary>
[DefaultExecutionOrder(200)]
public sealed class DioramaCelebration : MonoBehaviour
{
    [System.Serializable]
    private sealed class FigureGroup
    {
        [Tooltip("Include this group in the celebration.")]
        public bool enabled = true;

        [Tooltip("Hop height, in the diorama's local units (figures are about 0.024 tall).")]
        [Min(0f)]
        public float jumpHeight = 0.006f;

        [Tooltip("Figure roots to hop (each whole person or sheep).")]
        public Transform[] figures = new Transform[0];
    }

    [Tooltip("Master switch for the solve celebration.")]
    [SerializeField]
    private bool celebrationEnabled = true;

    [Tooltip("The diorama whose local up the figures hop along. Defaults to this object.")]
    [SerializeField]
    private Transform diorama;

    [Header("Groups")]
    [SerializeField]
    private FigureGroup people = new FigureGroup { jumpHeight = 0.006f };

    [SerializeField]
    private FigureGroup sheep = new FigureGroup { jumpHeight = 0.004f };

    [Header("Timing")]
    [Tooltip("How long each figure keeps hopping (s). A hop already in the air when this runs out still lands.")]
    [SerializeField]
    [Min(0f)]
    private float totalDuration = 1f;

    [Tooltip("Time for one hop, takeoff to landing (s).")]
    [SerializeField]
    [Min(0.05f)]
    private float hopDuration = 0.33f;

    [Tooltip("Wait after the solve before the first hop (s).")]
    [SerializeField]
    [Min(0f)]
    private float startDelay = 0f;

    [Tooltip("Extra start delay per figure, in list order, people first (s). 0 = everyone hops together.")]
    [SerializeField]
    [Min(0f)]
    private float stagger = 0.06f;

    private struct Hopper
    {
        public Transform figure;
        public float height;
        public float startTime;
        public int hops;
        public Vector3 rest;
        public Vector3 lastWritten;
        public bool wrote;
    }

    private Hopper[] hoppers = new Hopper[0];
    private bool playing;
    private bool celebratedThisSolve;

    public bool IsPlaying => playing;

    /// <summary>Solve hook: plays once per solve (until StopCelebration / reset).</summary>
    public void Celebrate()
    {
        if (!celebrationEnabled || celebratedThisSolve)
        {
            return;
        }

        celebratedThisSolve = true;
        Play();
    }

    /// <summary>Reset hook: stops any hops, puts every figure back, and re-arms for the next solve.</summary>
    public void StopCelebration()
    {
        RestoreAll();
        celebratedThisSolve = false;
    }

    [ContextMenu("Preview Celebration")]
    private void PreviewCelebration()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("DioramaCelebration: preview works in Play mode.", this);
            return;
        }

        Play();
    }

    private void Play()
    {
        RestoreAll();

        int count = Count(people) + Count(sheep);
        hoppers = new Hopper[count];
        int index = 0;
        float now = Time.time;
        int hops = Mathf.Max(1, Mathf.CeilToInt(totalDuration / hopDuration - 1e-4f));
        Add(people, ref index, now, hops);
        Add(sheep, ref index, now, hops);
        if (index < hoppers.Length)
        {
            System.Array.Resize(ref hoppers, index);
        }

        playing = index > 0;
    }

    private static int Count(FigureGroup group)
    {
        return group.enabled && group.figures != null ? group.figures.Length : 0;
    }

    private void Add(FigureGroup group, ref int index, float now, int hops)
    {
        if (Count(group) == 0)
        {
            return;
        }

        foreach (Transform figure in group.figures)
        {
            if (figure == null)
            {
                continue;
            }

            hoppers[index] = new Hopper
            {
                figure = figure,
                height = group.jumpHeight,
                startTime = now + startDelay + stagger * index,
                hops = hops,
                rest = figure.localPosition,
            };
            index++;
        }
    }

    private void LateUpdate()
    {
        if (!playing)
        {
            return;
        }

        Transform up = diorama != null ? diorama : transform;
        float now = Time.time;
        bool anyActive = false;

        for (int i = 0; i < hoppers.Length; i++)
        {
            ref Hopper h = ref hoppers[i];
            if (h.figure == null)
            {
                continue;
            }

            // If something else re-seated the figure since our last write
            // (ObserverDiorama does every frame), that is the resting pose.
            Vector3 current = h.figure.localPosition;
            if (!h.wrote || current != h.lastWritten)
            {
                h.rest = current;
            }

            float t = (now - h.startTime) / hopDuration;
            float lift = 0f;
            if (t >= 0f && t < h.hops)
            {
                float phase = t - Mathf.Floor(t);
                lift = 4f * phase * (1f - phase); // parabola: 0 at takeoff and landing, 1 at the top
                anyActive = true;
            }
            else if (t < 0f)
            {
                anyActive = true;
            }

            Vector3 offset = Vector3.zero;
            if (lift > 0f && h.figure.parent != null)
            {
                Vector3 world = up.TransformVector(Vector3.up * (h.height * lift));
                offset = h.figure.parent.InverseTransformVector(world);
            }

            h.figure.localPosition = h.rest + offset;
            h.lastWritten = h.figure.localPosition;
            h.wrote = true;
        }

        if (!anyActive)
        {
            RestoreAll();
        }
    }

    private void RestoreAll()
    {
        for (int i = 0; i < hoppers.Length; i++)
        {
            ref Hopper h = ref hoppers[i];
            if (h.figure == null)
            {
                continue;
            }

            Vector3 current = h.figure.localPosition;
            if (!h.wrote || current != h.lastWritten)
            {
                h.rest = current;
            }

            h.figure.localPosition = h.rest;
            h.wrote = false;
        }

        playing = false;
    }

    private void OnDisable()
    {
        RestoreAll();
    }
}
