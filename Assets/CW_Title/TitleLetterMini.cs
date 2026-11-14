using System;
using System.Collections;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// One letter of the constellation title: a tiny constellation shaped like a
/// letter that the player can grab (ISDK) and drop into a drop zone (the
/// puzzle sphere) to practise that letter. Released anywhere, it glides back
/// to its home pose on the shelf.
///
/// Hierarchy (built by Rebuild / TitlePreviewBuilder):
///   TitleLetterMini (this, Rigidbody kinematic, BoxCollider, Grabbable,
///                    ConstellationGenerator: its OWN, never the puzzle's)
///     Visual (scaled so the letter is letterHeight metres tall)
///       Generated Constellation (stars + connections, colliders removed)
///     ISDK Grab (HandGrabInteractable / GrabInteractable -> this Grabbable)
///
/// Custom constellation mode (UseConstellation): instead of a letter glyph the
/// mini shows a given star set (e.g. the Starter Pack's sculptures) with its
/// real depths, sized so its widest side is Letter Height. Grab, glide home
/// and drop zones work the same.
///
/// The visual is generated flat (displayDepthVariation 0: every star at the
/// same distance, a shallow spherical cap) so it reads as the letter from the
/// front; the practice puzzle itself uses LetterConstellation.Build with the
/// full depth spread. The letter faces the player when this transform's +Z
/// points away from the player (the generator looks down +Z).
/// </summary>
[DisallowMultipleComponent]
public sealed class TitleLetterMini : MonoBehaviour
{
    private const string VisualName = "Visual";

    [Header("Letter")]
    [SerializeField]
    private string letter = "C";

    [Tooltip("Depth seed handed on for the practice puzzle (the title display is flat).")]
    [SerializeField]
    private int seed = 1;

    [Header("Custom constellation (instead of a letter)")]
    [SerializeField]
    private bool useCustomConstellation;

    [SerializeField]
    private string customName = "";

    [SerializeField]
    private Vector2 customArtboard = new Vector2(1920f, 1080f);

    [SerializeField]
    private List<ConstellationGenerator.StarData> customStars = new List<ConstellationGenerator.StarData>();

    [Tooltip("Generator shape settings copied for a custom constellation (angular spread, radius, log distance). Empty = the letter settings.")]
    [SerializeField]
    private ConstellationGenerator customShapeSource;

    [Header("Generation")]
    [Tooltip("This mini's own generator. Empty = one on this object (added if missing). Never the scene's puzzle generator.")]
    [SerializeField]
    private ConstellationGenerator generator;

    [SerializeField]
    private ConstellationGenerator.AppearanceSettings appearance =
        new ConstellationGenerator.AppearanceSettings();

    [Tooltip("Letter height in metres.")]
    [SerializeField]
    [Min(0.005f)]
    private float letterHeight = 0.06f;

    [Tooltip("Star sphere diameter in metres (overrides appearance.starDiameter).")]
    [SerializeField]
    [Min(0.0005f)]
    private float worldStarDiameter = 0.006f;

    [Tooltip("Connection width in metres (overrides appearance.lineWidth).")]
    [SerializeField]
    [Min(0.0002f)]
    private float worldLineWidth = 0.0012f;

    [Tooltip("0 = flat and readable (default). 1 = the practice puzzle's full depth scramble.")]
    [SerializeField]
    [Range(0f, 1f)]
    private float displayDepthVariation = 0f;

    [SerializeField]
    private bool generateOnStart = true;

    [Header("Grab")]
    [Tooltip("The ISDK Grabbable that moves this object.")]
    [SerializeField]
    private Grabbable grabbable;

    [Tooltip("Fitted around the letter on Rebuild (the grab volume).")]
    [SerializeField]
    private BoxCollider grabCollider;

    [Tooltip("Extra margin around the letter for the grab volume, metres.")]
    [SerializeField]
    private float grabPadding = 0.01f;

    [Tooltip("Drop zones (components implementing ITitleDropZone). TitleShelf can also supply them.")]
    [SerializeField]
    private MonoBehaviour[] dropZones = new MonoBehaviour[0];

    [SerializeField]
    [Min(0.01f)]
    private float glideDuration = 0.35f;

    [Header("Highlight")]
    [SerializeField]
    private float highlightScale = 1.08f;

    [SerializeField]
    private bool logEvents = true;

    /// <summary>Raised when the letter is released inside a drop zone.</summary>
    public event Action<TitleLetterMini> PlacedInZone;

    /// <summary>True while held with its centre inside a drop zone (the zone shows it).</summary>
    public bool IsHeldInDropZone { get; private set; }

    public bool IsCustomConstellation => useCustomConstellation;

    public string ConstellationName => useCustomConstellation ? customName : Letter.ToString();

    public Vector2 CustomArtboard => customArtboard;

    /// <summary>A copy of the custom star set (null for a letter).</summary>
    public List<ConstellationGenerator.StarData> CopyCustomStars()
    {
        return useCustomConstellation ? new List<ConstellationGenerator.StarData>(customStars) : null;
    }

    /// <summary>Shows this star set instead of a letter (applied on the next Rebuild).</summary>
    public void UseConstellation(string displayName, Vector2 artboard, List<ConstellationGenerator.StarData> starSet, ConstellationGenerator shapeSource)
    {
        useCustomConstellation = true;
        customName = displayName;
        customArtboard = artboard;
        customStars = new List<ConstellationGenerator.StarData>(starSet);
        customShapeSource = shapeSource;
    }

    public char Letter
    {
        get => string.IsNullOrEmpty(letter) ? ' ' : char.ToUpperInvariant(letter[0]);
        set => letter = char.ToUpperInvariant(value).ToString();
    }

    public int Seed
    {
        get => seed;
        set => seed = value;
    }

    public bool IsGrabbed { get; private set; }

    public Grabbable Grabbable
    {
        get => grabbable;
        set => SetGrabbable(value);
    }

    /// <summary>Home pose in the parent's space.</summary>
    public Vector3 HomeLocalPosition { get; private set; }
    public Quaternion HomeLocalRotation { get; private set; } = Quaternion.identity;

    /// <summary>Home pose in world space.</summary>
    public Pose HomePose
    {
        get
        {
            Transform parent = transform.parent;
            if (parent == null)
            {
                return new Pose(HomeLocalPosition, HomeLocalRotation);
            }

            return new Pose(
                parent.TransformPoint(HomeLocalPosition),
                parent.rotation * HomeLocalRotation);
        }
    }

    public ConstellationGenerator.AppearanceSettings Appearance
    {
        get => appearance;
        set => appearance = value;
    }

    public float LetterHeight
    {
        get => letterHeight;
        set => letterHeight = Mathf.Max(0.005f, value);
    }

    /// <summary>Star diameter and connection width in metres (applied on the next Rebuild).</summary>
    public void SetStrokeSizes(float starDiameter, float lineWidth)
    {
        worldStarDiameter = Mathf.Max(0.0005f, starDiameter);
        worldLineWidth = Mathf.Max(0.0002f, lineWidth);
    }

    private readonly List<ITitleDropZone> extraDropZones = new List<ITitleDropZone>();
    private readonly List<GeneratedConstellationStar> stars = new List<GeneratedConstellationStar>();

    private Transform visualRoot;
    private float visualScale = 1f;
    private bool highlighted;
    private bool homeCaptured;
    private bool subscribed;
    private Coroutine glide;
    private SphereDropZone hoveredZone;

    private void Awake()
    {
        CaptureHomeIfNeeded();
    }

    private void Start()
    {
        // A visual baked in edit mode (prefab / builder) is kept; otherwise build it now.
        CacheExistingVisual();
        if (generateOnStart && stars.Count == 0)
        {
            Rebuild();
        }
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();

        if (glide != null)
        {
            StopCoroutine(glide);
            glide = null;
        }

        // Never leave a letter stranded off the shelf.
        if (homeCaptured && !IsGrabbed)
        {
            transform.localPosition = HomeLocalPosition;
            transform.localRotation = HomeLocalRotation;
        }

        IsGrabbed = false;
        SetHoveredZone(null);
        Highlight(false);
    }

    private void Update()
    {
        if (IsGrabbed)
        {
            SetHoveredZone(FindDropZone(transform.position) as SphereDropZone);
        }
    }

    // Lets the zone glow while this is held inside it.
    private void SetHoveredZone(SphereDropZone zone)
    {
        bool inside = zone != null;
        if (zone == hoveredZone && inside == IsHeldInDropZone)
        {
            return;
        }

        if (hoveredZone != null)
        {
            hoveredZone.SetHovered(this, false);
        }

        hoveredZone = zone;
        IsHeldInDropZone = inside;
        if (hoveredZone != null)
        {
            hoveredZone.SetHovered(this, true);
        }
    }

    // ---------------------------------------------------------------- Home

    /// <summary>Sets the home pose (parent space) and snaps there.</summary>
    public void SetHome(Vector3 localPosition, Quaternion localRotation)
    {
        HomeLocalPosition = localPosition;
        HomeLocalRotation = localRotation;
        homeCaptured = true;
        transform.localPosition = localPosition;
        transform.localRotation = localRotation;
    }

    private void CaptureHomeIfNeeded()
    {
        if (homeCaptured)
        {
            return;
        }

        HomeLocalPosition = transform.localPosition;
        HomeLocalRotation = transform.localRotation;
        homeCaptured = true;
    }

    public void GlideHome()
    {
        CaptureHomeIfNeeded();

        if (glide != null)
        {
            StopCoroutine(glide);
            glide = null;
        }

        if (!isActiveAndEnabled || !Application.isPlaying)
        {
            transform.localPosition = HomeLocalPosition;
            transform.localRotation = HomeLocalRotation;
            return;
        }

        glide = StartCoroutine(GlideHomeRoutine());
    }

    private IEnumerator GlideHomeRoutine()
    {
        Vector3 startPosition = transform.localPosition;
        Quaternion startRotation = transform.localRotation;
        float elapsed = 0f;

        while (elapsed < glideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / glideDuration);
            t = t * t * (3f - 2f * t);

            transform.localPosition = Vector3.LerpUnclamped(startPosition, HomeLocalPosition, t);
            transform.localRotation = Quaternion.Slerp(startRotation, HomeLocalRotation, t);
            yield return null;
        }

        transform.localPosition = HomeLocalPosition;
        transform.localRotation = HomeLocalRotation;
        glide = null;
    }

    // ---------------------------------------------------------------- Drop zones

    /// <summary>Drop zones supplied at runtime (e.g. by TitleShelf), in addition to the serialized ones.</summary>
    public void SetDropZones(IEnumerable<ITitleDropZone> zones)
    {
        extraDropZones.Clear();
        if (zones == null)
        {
            return;
        }

        foreach (ITitleDropZone zone in zones)
        {
            if (zone != null)
            {
                extraDropZones.Add(zone);
            }
        }
    }

    public bool IsInAnyDropZone(Vector3 worldPoint)
    {
        return FindDropZone(worldPoint) != null;
    }

    private ITitleDropZone FindDropZone(Vector3 worldPoint)
    {
        if (dropZones != null)
        {
            foreach (MonoBehaviour behaviour in dropZones)
            {
                if (behaviour != null && behaviour is ITitleDropZone zone && zone.Contains(worldPoint))
                {
                    return zone;
                }
            }
        }

        foreach (ITitleDropZone zone in extraDropZones)
        {
            // Unity-destroyed components compare equal to null only through UnityEngine.Object.
            if (zone is UnityEngine.Object unityObject && unityObject == null)
            {
                continue;
            }

            if (zone != null && zone.Contains(worldPoint))
            {
                return zone;
            }
        }

        return null;
    }

    // ---------------------------------------------------------------- Grab

    private void SetGrabbable(Grabbable value)
    {
        if (value == grabbable)
        {
            return;
        }

        Unsubscribe();
        grabbable = value;
        if (isActiveAndEnabled)
        {
            Subscribe();
        }
    }

    private void Subscribe()
    {
        if (subscribed || grabbable == null)
        {
            return;
        }

        grabbable.WhenPointerEventRaised += HandlePointerEvent;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        if (grabbable != null)
        {
            grabbable.WhenPointerEventRaised -= HandlePointerEvent;
        }

        subscribed = false;
    }

    // Raised by the Grabbable after it has updated its point lists, so the
    // counts below already include / exclude this event's pointer.
    private void HandlePointerEvent(PointerEvent evt)
    {
        switch (evt.Type)
        {
            case PointerEventType.Select:
                if (!IsGrabbed)
                {
                    IsGrabbed = true;
                    if (glide != null)
                    {
                        StopCoroutine(glide);
                        glide = null;
                    }

                    Log("grabbed.");
                }

                break;

            case PointerEventType.Unselect:
            case PointerEventType.Cancel:
                // Two-hand grabs: only the last hand letting go is a release.
                if (IsGrabbed && grabbable.SelectingPointsCount == 0)
                {
                    IsGrabbed = false;
                    SetHoveredZone(null);
                    HandleReleased();
                }

                break;
        }

        Highlight(IsGrabbed || (grabbable != null && grabbable.PointsCount > 0));
    }

    private void HandleReleased()
    {
        if (IsInAnyDropZone(transform.position))
        {
            Log("released in a drop zone.");
            PlacedInZone?.Invoke(this);
        }
        else
        {
            Log("released outside the drop zones.");
        }

        GlideHome();
    }

    [ContextMenu("Simulate Place In Zone")]
    public void SimulatePlaceInZone()
    {
        Log("simulated place in zone.");
        PlacedInZone?.Invoke(this);
        GlideHome();
    }

    // ---------------------------------------------------------------- Highlight

    /// <summary>Subtle: star highlight material + a slight swell.</summary>
    public void Highlight(bool on)
    {
        if (highlighted == on)
        {
            return;
        }

        highlighted = on;

        foreach (GeneratedConstellationStar star in stars)
        {
            if (star != null)
            {
                star.SetHighlighted(on);
            }
        }

        if (visualRoot != null)
        {
            visualRoot.localScale = Vector3.one * visualScale * (on ? highlightScale : 1f);
        }
    }

    // ---------------------------------------------------------------- Visual

    [ContextMenu("Rebuild Visual")]
    public void Rebuild()
    {
        if (useCustomConstellation)
        {
            RebuildCustom();
            return;
        }

        if (!LetterGlyphs.Has(Letter))
        {
            Debug.LogWarning($"TitleLetterMini: no glyph for '{letter}'.", this);
            return;
        }

        if (generator == null)
        {
            generator = GetComponent<ConstellationGenerator>();
            if (generator == null)
            {
                generator = gameObject.AddComponent<ConstellationGenerator>();
            }
        }

        // Cassiopeia's sculpture settings, so the letter has the puzzle's shape.
        generator.maximumRadius = 1f;
        generator.angularSpread = 0.7f;
        generator.useLogarithmicDistance = false;
        generator.showDebugLabels = false;
        generator.showEarthViewMarker = false;

        LetterConstellation.ApplyTo(generator, Letter, seed, displayDepthVariation);
        BuildVisual(false);
    }

    private void RebuildCustom()
    {
        if (customStars == null || customStars.Count < 2)
        {
            Debug.LogWarning("TitleLetterMini: custom constellation needs at least two stars.", this);
            return;
        }

        EnsureGenerator();
        if (customShapeSource != null)
        {
            generator.maximumRadius = customShapeSource.maximumRadius;
            generator.angularSpread = customShapeSource.angularSpread;
            generator.useLogarithmicDistance = customShapeSource.useLogarithmicDistance;
            generator.logarithmicMinimumRadius = customShapeSource.logarithmicMinimumRadius;
        }
        else
        {
            generator.maximumRadius = 1f;
            generator.angularSpread = 0.7f;
            generator.useLogarithmicDistance = false;
        }

        generator.showDebugLabels = false;
        generator.showEarthViewMarker = false;
        generator.constellationName = customName;
        generator.artboardSize = customArtboard;
        generator.stars = new List<ConstellationGenerator.StarData>(customStars);
        BuildVisual(true);
    }

    private void EnsureGenerator()
    {
        if (generator == null)
        {
            generator = GetComponent<ConstellationGenerator>();
            if (generator == null)
            {
                generator = gameObject.AddComponent<ConstellationGenerator>();
            }
        }
    }

    // sizeByWidestSide: custom constellations are sized by their largest
    // extent (a wide, short shape like Cassiopeia); letters by their height.
    private void BuildVisual(bool sizeByWidestSide)
    {
        visualRoot = EnsureVisualRoot();
        visualRoot.localPosition = Vector3.zero;
        visualRoot.localRotation = Quaternion.identity;
        visualRoot.localScale = Vector3.one;
        ClearChildren(visualRoot);

        GameObject generated = generator.GenerateConstellation(
            visualRoot,
            appearance,
            null,
            1f,
            default,
            1f,
            1f,
            false);

        if (generated == null)
        {
            return;
        }

        // Bounds of the star centres in the generated root's space.
        GeneratedConstellationStar[] generatedStars =
            generated.GetComponentsInChildren<GeneratedConstellationStar>(true);

        if (generatedStars.Length == 0)
        {
            return;
        }

        Bounds bounds = new Bounds(generatedStars[0].transform.localPosition, Vector3.zero);
        foreach (GeneratedConstellationStar star in generatedStars)
        {
            bounds.Encapsulate(star.transform.localPosition);
        }

        float measured = sizeByWidestSide
            ? Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z))
            : bounds.size.y;
        visualScale = letterHeight / Mathf.Max(0.0001f, measured);
        visualRoot.localScale = Vector3.one * visualScale;

        // Centre the letter on this object (the grab / glide pivot).
        generated.transform.localPosition = -bounds.center;

        // Metre-sized stars and lines regardless of the visual's scale.
        float starLocal = worldStarDiameter / visualScale;
        float lineLocal = worldLineWidth / visualScale;

        foreach (Transform child in generated.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Sphere")
            {
                child.localScale = Vector3.one * starLocal;
            }
            else if (child.name == "Connection")
            {
                Vector3 s = child.localScale;
                child.localScale = new Vector3(lineLocal, s.y, lineLocal);
            }
        }

        // One grab volume on the root instead of a collider per star: the
        // star spheres are primitives with their own SphereColliders.
        // Immediate even in Play: a HandGrabInteractable collects its
        // Rigidbody's child colliders in Start, and must never see these.
        foreach (Collider c in generated.GetComponentsInChildren<Collider>(true))
        {
            DestroyImmediate(c);
        }

        FitGrabCollider(bounds.size * visualScale);

        stars.Clear();
        stars.AddRange(generatedStars);

        bool wasHighlighted = highlighted;
        highlighted = !wasHighlighted;
        Highlight(wasHighlighted);
    }

    private void CacheExistingVisual()
    {
        visualRoot = transform.Find(VisualName);
        stars.Clear();
        if (visualRoot != null)
        {
            visualScale = visualRoot.localScale.x;
            stars.AddRange(visualRoot.GetComponentsInChildren<GeneratedConstellationStar>(true));

            // GeneratedConstellationStar keeps its renderer and materials in
            // non-serialized fields, so a baked star must be re-initialized
            // for Highlight to swap materials.
            foreach (GeneratedConstellationStar star in stars)
            {
                Renderer sphere = star.GetComponentInChildren<Renderer>(true);
                if (sphere != null && appearance != null)
                {
                    star.Initialize(star.StarId, sphere, appearance.starMaterial, appearance.starHighlightMaterial);
                }
            }
        }
    }

    private void FitGrabCollider(Vector3 letterSize)
    {
        if (grabCollider == null)
        {
            grabCollider = GetComponent<BoxCollider>();
        }

        if (grabCollider == null)
        {
            return;
        }

        float pad = Mathf.Max(0f, grabPadding) * 2f;
        grabCollider.center = Vector3.zero;
        grabCollider.size = new Vector3(
            letterSize.x + pad,
            letterSize.y + pad,
            Mathf.Max(letterSize.z, 0.01f) + pad);
    }

    private Transform EnsureVisualRoot()
    {
        Transform existing = transform.Find(VisualName);
        if (existing != null)
        {
            return existing;
        }

        GameObject go = new GameObject(VisualName);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            // Detach first: Destroy is deferred in Play, and the generator
            // looks its old root up by name.
            child.SetParent(null, false);
            DestroySafely(child.gameObject);
        }
    }

    private static void DestroySafely(UnityEngine.Object obj)
    {
        if (obj == null)
        {
            return;
        }

        if (obj is Collider collider)
        {
            collider.enabled = false;
        }

        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    private void Log(string message)
    {
        if (logEvents)
        {
            Debug.Log($"TitleLetterMini '{ConstellationName}': {message}", this);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!string.IsNullOrEmpty(letter) && letter.Length > 1)
        {
            letter = letter.Substring(0, 1);
        }
    }
#endif
}
