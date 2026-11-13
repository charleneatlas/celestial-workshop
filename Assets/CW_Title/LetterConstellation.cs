using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns a LetterGlyphs stroke into ConstellationGenerator data, framed like
/// Cassiopeia so the live puzzle (sphere, Earth view) needs no other changes.
///
/// WHY THE SOLVED PROJECTION IS EXACTLY THE LETTER, WHATEVER THE DEPTHS
/// ConstellationGenerator.GenerateConstellation places star i at
///     direction_i = normalize(nx_i * spread, ny_i * spread, 1)
///     position_i  = origin + direction_i * radius(distance_i)
/// where (nx, ny) come only from affinityPosition (ConvertAffinityPosition)
/// and `origin` is the generator root (mini) or the observer (sky). The
/// distance only slides a star along its own sight ray from that origin; it
/// never changes the ray. Seen from the origin (the projection the Earth view
/// shows at the solved orientation), star i therefore lands at
/// (nx_i * spread, ny_i * spread) on the z = 1 image plane, which is a linear
/// (uniform scale + offset) map of the glyph point. A perspective projection
/// maps each 3D connection segment to the 2D segment between its projected
/// endpoints, so every stroke is drawn exactly as the glyph, and a point that
/// lies on a glyph segment (the partial retraces in LetterGlyphs) still lies
/// on it. This holds for linear and logarithmic distance alike: both are
/// positive radii along the ray.
/// Away from the solved orientation, the depth spread (neighbouring stars
/// alternate between a near and a far band) makes the 3D shape a scramble
/// that does not read as the letter, which is what makes it a puzzle.
/// </summary>
public static class LetterConstellation
{
    /// <summary>Same artboard as Cassiopeia (Affinity Designer px, y down).</summary>
    public static readonly Vector2 Artboard = new Vector2(1920f, 1080f);

    /// <summary>Letter height on the artboard (px). Cassiopeia spans y 160..890.</summary>
    public const float LetterHeightPx = 800f;

    /// <summary>Centre of the letter's bounding box on the artboard (px).</summary>
    public static readonly Vector2 LetterCenterPx = new Vector2(930f, 525f);

    // Cassiopeia's distances run 55..549 ly (linear). Neighbouring stars are
    // put in alternating bands so their depths always differ noticeably.
    public const float NearMin = 60f;
    public const float NearMax = 230f;
    public const float FarMin = 380f;
    public const float FarMax = 550f;

    /// <summary>"Letter C".</summary>
    public static string DisplayName(char c)
    {
        return "Letter " + char.ToUpperInvariant(c);
    }

    /// <summary>
    /// Star data for a letter. The seed only changes the depths; the solved
    /// projection is the same for every seed. depthVariation 1 = the full
    /// near/far bands; 0 = every star at the same distance (a flat, readable
    /// spherical cap, used for the title display). Returns an empty list for
    /// characters without a glyph.
    /// </summary>
    public static List<ConstellationGenerator.StarData> Build(char c, int seed)
    {
        return Build(c, seed, 1f);
    }

    public static List<ConstellationGenerator.StarData> Build(char c, int seed, float depthVariation)
    {
        List<ConstellationGenerator.StarData> stars = new List<ConstellationGenerator.StarData>();

        char letter = char.ToUpperInvariant(c);
        Vector2[] glyph = LetterGlyphs.Get(letter);
        if (glyph == null || glyph.Length == 0)
        {
            Debug.LogWarning($"LetterConstellation: no glyph for '{c}'.");
            return stars;
        }

        // Glyph bounding box, so each letter is centred on LetterCenterPx.
        Vector2 min = glyph[0];
        Vector2 max = glyph[0];
        foreach (Vector2 p in glyph)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        Vector2 glyphCenter = (min + max) * 0.5f;
        float glyphHeight = Mathf.Max(0.0001f, max.y - min.y);
        float pxPerUnit = LetterHeightPx / glyphHeight;

        depthVariation = Mathf.Clamp01(depthVariation);
        float flatDistance = (NearMin + FarMax) * 0.5f;

        System.Random random = new System.Random(unchecked(seed * 486187739 + letter * 7919));
        int bandParity = random.Next(2);

        // A revisited point gets the same distance as its first visit, so the
        // two spheres coincide exactly and render as one star.
        Dictionary<Vector2Int, float> distanceByPoint = new Dictionary<Vector2Int, float>();
        int uniqueIndex = 0;

        for (int i = 0; i < glyph.Length; i++)
        {
            Vector2 p = glyph[i];
            Vector2Int key = new Vector2Int(
                Mathf.RoundToInt(p.x * 1000f),
                Mathf.RoundToInt(p.y * 1000f));

            if (!distanceByPoint.TryGetValue(key, out float distance))
            {
                bool far = ((uniqueIndex + bandParity) & 1) == 1;
                float t = (float)random.NextDouble();
                float banded = far
                    ? Mathf.Lerp(FarMin, FarMax, t)
                    : Mathf.Lerp(NearMin, NearMax, t);

                distance = Mathf.Lerp(flatDistance, banded, depthVariation);
                distanceByPoint.Add(key, distance);
                uniqueIndex++;
            }

            // Affinity Y increases downward (ConvertAffinityPosition flips it).
            Vector2 affinity = new Vector2(
                LetterCenterPx.x + (p.x - glyphCenter.x) * pxPerUnit,
                LetterCenterPx.y - (p.y - glyphCenter.y) * pxPerUnit);

            stars.Add(new ConstellationGenerator.StarData
            {
                starName = letter.ToString() + (i + 1),
                affinityPosition = affinity,
                distanceLightYears = distance,
            });
        }

        return stars;
    }

    /// <summary>Loads a letter into a generator (for the title minis, never the puzzle's generator).</summary>
    public static void ApplyTo(ConstellationGenerator generator, char c, int seed)
    {
        ApplyTo(generator, c, seed, 1f);
    }

    public static void ApplyTo(ConstellationGenerator generator, char c, int seed, float depthVariation)
    {
        if (generator == null)
        {
            return;
        }

        generator.constellationName = DisplayName(c);
        generator.artboardSize = Artboard;
        generator.stars = Build(c, seed, depthVariation);
    }
}
