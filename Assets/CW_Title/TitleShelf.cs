using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The constellation title: rows of letter minis ("CELESTIAL" over
/// "WORKSHOP") and a Start button.
///
/// - Grab any letter and release it in a drop zone (the puzzle sphere):
///   LetterPlaced(letter, seed) fires; the coordinator loads that letter as
///   the live puzzle (LetterConstellation.Build / DisplayName / Artboard).
/// - Start (poke button or keyboard in Play): StartPressed fires; the
///   coordinator starts the main (Cassiopeia) puzzle.
///
/// Layout is in this object's XY plane, centred on it, row 0 on top. Place
/// this object in front of the player with its +Z pointing AWAY from them
/// (the letters face -Z). With maxHorizontalDegrees > 0 the whole title is
/// shrunk, if needed, to fit that angle at viewingDistance.
/// </summary>
public sealed class TitleShelf : MonoBehaviour
{
    private const string LettersName = "Letters";

    [Header("Text")]
    [SerializeField]
    private string[] rows = { "CELESTIAL", "WORKSHOP" };

    [Header("Layout (metres, before the FOV fit)")]
    [SerializeField]
    [Min(0.005f)]
    private float letterHeight = 0.04f;

    [Tooltip("Gap between neighbouring letters.")]
    [SerializeField]
    [Min(0f)]
    private float letterSpacing = 0.01f;

    [Tooltip("Advance for a space, as a fraction of the letter height.")]
    [SerializeField]
    [Min(0f)]
    private float spaceWidth = 0.4f;

    [Tooltip("Vertical gap between rows.")]
    [SerializeField]
    [Min(0f)]
    private float rowGap = 0.025f;

    [Tooltip("Expected eye-to-title distance; used by the FOV fit (and the preview builder).")]
    [SerializeField]
    [Min(0.1f)]
    private float viewingDistance = 0.7f;

    [Tooltip("The whole title is shrunk to fit this horizontal angle at the viewing distance. 0 = no fit.")]
    [SerializeField]
    [Range(0f, 90f)]
    private float maxHorizontalDegrees = 28f;

    [Header("Letters")]
    [SerializeField]
    private ConstellationGenerator.AppearanceSettings appearance =
        new ConstellationGenerator.AppearanceSettings();

    [Tooltip("Star diameter as a fraction of the letter height.")]
    [SerializeField]
    private float starDiameterRatio = 0.11f;

    [Tooltip("Connection width as a fraction of the letter height.")]
    [SerializeField]
    private float lineWidthRatio = 0.025f;

    [Tooltip("Optional authored letter (with its own ISDK grab setup). Empty = letters are built in code.")]
    [SerializeField]
    private TitleLetterMini letterTemplate;

    [Tooltip("When building letters in code, add Grabbable + HandGrabInteractable (+ GrabInteractable).")]
    [SerializeField]
    private bool addIsdkGrab = true;

    [SerializeField]
    private bool addControllerGrab = false;

    [SerializeField]
    private int baseSeed = 1;

    [Tooltip("Give a letter a fresh depth seed after each placement so each practice differs.")]
    [SerializeField]
    private bool reseedAfterPlacement = true;

    [Tooltip("Letters already built (filled by Rebuild Letters).")]
    [SerializeField]
    private List<TitleLetterMini> letters = new List<TitleLetterMini>();

    [Header("Drop Zones")]
    [Tooltip("Components implementing ITitleDropZone (e.g. SphereDropZone on the puzzle sphere).")]
    [SerializeField]
    private MonoBehaviour[] dropZones = new MonoBehaviour[0];

    [Header("Start")]
    [SerializeField]
    private MomentaryToggleButton startButton;

    [Tooltip("Shown / hidden with the title (e.g. the Start button's canvas). Optional.")]
    [SerializeField]
    private GameObject startButtonRoot;

#if ENABLE_INPUT_SYSTEM
    [Tooltip("Desktop testing in Play: this key presses Start while the title is shown.")]
    [SerializeField]
    private Key startKey = Key.Enter;
#endif

    [Header("Visibility")]
    [SerializeField]
    private bool showOnStart = true;

    [SerializeField]
    private bool logEvents = true;

    /// <summary>Start pressed (button, keyboard or PressStart()).</summary>
    public event Action StartPressed;

    /// <summary>A letter was released in a drop zone: (letter, depth seed).</summary>
    public event Action<char, int> LetterPlaced;

    public bool IsShown { get; private set; }

    public IReadOnlyList<TitleLetterMini> Letters => letters;

    public float ViewingDistance => viewingDistance;

    public ConstellationGenerator.AppearanceSettings Appearance
    {
        get => appearance;
        set => appearance = value;
    }

    public string[] Rows
    {
        get => rows;
        set => rows = value;
    }

    private Transform lettersRoot;
    private readonly List<TitleLetterMini> subscribedLetters = new List<TitleLetterMini>();
    private bool startSubscribed;
    private System.Random seedRandom;

    private void Awake()
    {
        seedRandom = new System.Random(baseSeed * 7907 + 17);
        lettersRoot = transform.Find(LettersName);

        letters.RemoveAll(l => l == null);
        if (letters.Count == 0)
        {
            RebuildLetters();
        }
        else
        {
            ApplyDropZones();
        }
    }

    private void Start()
    {
        if (showOnStart)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    private void OnEnable()
    {
        SubscribeLetters();

        if (startButton != null && !startSubscribed)
        {
            startButton.Pressed += PressStart;
            startSubscribed = true;
        }
    }

    private void OnDisable()
    {
        UnsubscribeLetters();

        if (startButton != null && startSubscribed)
        {
            startButton.Pressed -= PressStart;
        }

        startSubscribed = false;
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (!IsShown)
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && startKey != Key.None && keyboard[startKey].wasPressedThisFrame)
        {
            PressStart();
        }
#endif
    }

    // ---------------------------------------------------------------- Public API

    [ContextMenu("Show")]
    public void Show()
    {
        SetVisible(true);
    }

    [ContextMenu("Hide")]
    public void Hide()
    {
        SetVisible(false);
    }

    [ContextMenu("Press Start")]
    public void PressStart()
    {
        Log("Start pressed.");
        StartPressed?.Invoke();
    }

    /// <summary>Runtime drop zones (replaces the serialized list for all letters).</summary>
    public void SetDropZones(params ITitleDropZone[] zones)
    {
        foreach (TitleLetterMini letter in letters)
        {
            if (letter != null)
            {
                letter.SetDropZones(zones);
            }
        }
    }

    /// <summary>The letter mini for a character, or null.</summary>
    public TitleLetterMini FindLetter(char c)
    {
        char upper = char.ToUpperInvariant(c);
        foreach (TitleLetterMini letter in letters)
        {
            if (letter != null && letter.Letter == upper)
            {
                return letter;
            }
        }

        return null;
    }

    /// <summary>Destroys and recreates the letters from the rows (edit mode or Play).</summary>
    [ContextMenu("Rebuild Letters")]
    public void RebuildLetters()
    {
        UnsubscribeLetters();
        lettersRoot = EnsureLettersRoot();

        for (int i = lettersRoot.childCount - 1; i >= 0; i--)
        {
            GameObject child = lettersRoot.GetChild(i).gameObject;
            child.transform.SetParent(null, false);
            if (Application.isPlaying)
            {
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }

        letters.Clear();

        float scale = ComputeFitScale();
        float height = letterHeight * scale;
        float spacing = letterSpacing * scale;
        float gap = rowGap * scale;

        int rowCount = rows != null ? rows.Length : 0;
        float totalHeight = rowCount * height + Mathf.Max(0, rowCount - 1) * gap;
        float rowCenterY = totalHeight * 0.5f - height * 0.5f;
        int index = 0;

        for (int r = 0; r < rowCount; r++)
        {
            string row = rows[r] ?? "";
            float rowWidth = MeasureRow(row, height, spacing);
            float cursor = -rowWidth * 0.5f;
            bool first = true;

            foreach (char raw in row)
            {
                char c = char.ToUpperInvariant(raw);
                float width = AdvanceWidth(c, height);

                if (!first)
                {
                    cursor += spacing;
                }

                first = false;

                if (LetterGlyphs.Has(c))
                {
                    Vector3 position = new Vector3(cursor + width * 0.5f, rowCenterY, 0f);
                    TitleLetterMini letter = CreateLetter(c, index, height);
                    letter.SetHome(position, Quaternion.identity);
                    letters.Add(letter);
                    index++;
                }

                cursor += width;
            }

            rowCenterY -= height + gap;
        }

        ApplyDropZones();

        if (isActiveAndEnabled)
        {
            SubscribeLetters();
        }

        Log($"built {letters.Count} letters (fit scale {scale:0.00}).");
    }

    // ---------------------------------------------------------------- Building

    private TitleLetterMini CreateLetter(char c, int index, float height)
    {
        TitleLetterMini letter;

        if (letterTemplate != null)
        {
            letter = Instantiate(letterTemplate, lettersRoot);
            letter.gameObject.SetActive(true);
        }
        else
        {
            GameObject go = new GameObject();
            go.transform.SetParent(lettersRoot, false);

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            go.AddComponent<BoxCollider>();
            go.AddComponent<ConstellationGenerator>();
            letter = go.AddComponent<TitleLetterMini>();

            if (addIsdkGrab)
            {
                AddIsdkGrab(letter, body);
            }
        }

        letter.name = $"Letter {index:00} {c}";
        letter.Letter = c;
        letter.Seed = baseSeed + index * 101;
        letter.Appearance = appearance;
        letter.LetterHeight = height;
        letter.SetStrokeSizes(height * starDiameterRatio, height * lineWidthRatio);

        // In Play the letter builds its visual in its own Start.
        if (!Application.isPlaying)
        {
            letter.Rebuild();
        }

        return letter;
    }

    /// <summary>
    /// The standard ISDK hand grab: a Grabbable on the letter (default
    /// GrabFreeTransformer, generated at its Start) and a HandGrabInteractable
    /// on a child that points at it, sharing the letter's kinematic Rigidbody
    /// and BoxCollider. Uses the ISDK Inject API (meant for objects built in
    /// code); the values are serialized fields, so they persist in prefabs.
    /// </summary>
    private void AddIsdkGrab(TitleLetterMini letter, Rigidbody body)
    {
        Grabbable grabbable = letter.gameObject.AddComponent<Grabbable>();
        grabbable.InjectOptionalRigidbody(body);
        grabbable.InjectOptionalTargetTransform(letter.transform);
        grabbable.InjectOptionalThrowWhenUnselected(false);
        grabbable.MaxGrabPoints = 1;

        GameObject grabObject = new GameObject("ISDK Grab");
        grabObject.transform.SetParent(letter.transform, false);

        HandGrabInteractable handGrab = grabObject.AddComponent<HandGrabInteractable>();
        handGrab.InjectRigidbody(body);
        handGrab.InjectOptionalPointableElement(grabbable);

        if (addControllerGrab)
        {
            GrabInteractable controllerGrab = grabObject.AddComponent<GrabInteractable>();
            controllerGrab.InjectRigidbody(body);
            controllerGrab.InjectOptionalPointableElement(grabbable);
        }

        letter.Grabbable = grabbable;
    }

    private Transform EnsureLettersRoot()
    {
        Transform existing = transform.Find(LettersName);
        if (existing != null)
        {
            return existing;
        }

        GameObject go = new GameObject(LettersName);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    private float ComputeFitScale()
    {
        if (maxHorizontalDegrees <= 0f || rows == null)
        {
            return 1f;
        }

        float widest = 0f;
        foreach (string row in rows)
        {
            widest = Mathf.Max(widest, MeasureRow(row ?? "", letterHeight, letterSpacing));
        }

        if (widest <= 0f)
        {
            return 1f;
        }

        float allowed = 2f * viewingDistance * Mathf.Tan(maxHorizontalDegrees * 0.5f * Mathf.Deg2Rad);
        return Mathf.Min(1f, allowed / widest);
    }

    private float MeasureRow(string row, float height, float spacing)
    {
        float width = 0f;
        for (int i = 0; i < row.Length; i++)
        {
            if (i > 0)
            {
                width += spacing;
            }

            width += AdvanceWidth(char.ToUpperInvariant(row[i]), height);
        }

        return width;
    }

    // Letter width from the glyph's aspect ratio (the generated visual is
    // scaled to the letter height and keeps that ratio closely).
    private float AdvanceWidth(char c, float height)
    {
        Vector2[] glyph = LetterGlyphs.Get(c);
        if (glyph == null || glyph.Length == 0)
        {
            return height * spaceWidth;
        }

        Vector2 min = glyph[0];
        Vector2 max = glyph[0];
        foreach (Vector2 p in glyph)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        float aspect = (max.x - min.x) / Mathf.Max(0.0001f, max.y - min.y);
        return Mathf.Max(0.15f, aspect) * height;
    }

    // ---------------------------------------------------------------- Events

    private void SubscribeLetters()
    {
        foreach (TitleLetterMini letter in letters)
        {
            if (letter != null && !subscribedLetters.Contains(letter))
            {
                letter.PlacedInZone += HandlePlaced;
                subscribedLetters.Add(letter);
            }
        }
    }

    private void UnsubscribeLetters()
    {
        foreach (TitleLetterMini letter in subscribedLetters)
        {
            if (letter != null)
            {
                letter.PlacedInZone -= HandlePlaced;
            }
        }

        subscribedLetters.Clear();
    }

    private void HandlePlaced(TitleLetterMini letter)
    {
        char c = letter.Letter;
        int seed = letter.Seed;
        Log($"letter '{c}' placed (seed {seed}).");
        LetterPlaced?.Invoke(c, seed);

        if (reseedAfterPlacement)
        {
            if (seedRandom == null)
            {
                seedRandom = new System.Random(baseSeed * 7907 + 17);
            }

            letter.Seed = seedRandom.Next(1, int.MaxValue);
        }
    }

    private void ApplyDropZones()
    {
        List<ITitleDropZone> zones = new List<ITitleDropZone>();
        if (dropZones != null)
        {
            foreach (MonoBehaviour behaviour in dropZones)
            {
                if (behaviour != null && behaviour is ITitleDropZone zone)
                {
                    zones.Add(zone);
                }
            }
        }

        foreach (TitleLetterMini letter in letters)
        {
            if (letter != null)
            {
                letter.SetDropZones(zones);
            }
        }
    }

    private void SetVisible(bool visible)
    {
        Transform root = lettersRoot != null ? lettersRoot : transform.Find(LettersName);
        if (root != null)
        {
            root.gameObject.SetActive(visible);
        }

        if (startButtonRoot != null)
        {
            startButtonRoot.SetActive(visible);
        }

        IsShown = visible;
        Log(visible ? "shown." : "hidden.");
    }

    private void Log(string message)
    {
        if (logEvents)
        {
            Debug.Log("TitleShelf: " + message, this);
        }
    }
}
