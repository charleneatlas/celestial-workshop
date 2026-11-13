using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stroke polylines for the letters A-Z, drawn as small constellations.
///
/// Coordinates are on a unit grid: x to the right, y up, the letter's height
/// is 0..1 and its width is at most ~0.9. ConstellationGenerator connects stars
/// as ONE polyline in list order (star i -> i + 1), so each glyph is a single
/// stroke path:
///
/// - Where a letter branches (F, H, K, P, R, T, X, I, A ...), the path
///   retraces PART of a segment it already drew and stops at a NEW point that
///   lies on that segment (e.g. F goes down the whole spine, then back up to
///   its middle, then out along the bar). The overlapping connection
///   cylinders coincide, and no star is duplicated.
/// - A few letters cannot be drawn that way (E, Y, and the closed loops of
///   B, D, O, Q). They revisit an earlier point exactly. LetterConstellation
///   gives every revisit the same distance as the original, so the two star
///   spheres are exactly coincident in 3D and render as one star.
///
/// Every glyph has 4-9 points so it reads as a constellation, not a font.
/// </summary>
public static class LetterGlyphs
{
    private static readonly Dictionary<char, Vector2[]> Glyphs =
        new Dictionary<char, Vector2[]>
        {
            // Apex, then down the right leg, back up it to the bar, across.
            ['A'] = P(0f, 0f, 0.4f, 1f, 0.8f, 0f, 0.6f, 0.5f, 0.2f, 0.5f),

            // Spine, upper bowl, waist, lower bowl, back to the foot (closed).
            ['B'] = P(0f, 0f, 0f, 1f, 0.5f, 0.97f, 0.68f, 0.76f, 0.38f, 0.52f, 0.72f, 0.27f, 0.5f, 0.02f, 0f, 0f),

            ['C'] = P(0.75f, 0.85f, 0.45f, 1f, 0.1f, 0.82f, 0f, 0.5f, 0.1f, 0.18f, 0.45f, 0f, 0.75f, 0.15f),

            // Closed.
            ['D'] = P(0f, 0f, 0f, 1f, 0.42f, 0.95f, 0.72f, 0.66f, 0.72f, 0.34f, 0.42f, 0.05f, 0f, 0f),

            // Top bar, spine to middle, out along the bar, back (revisit), down, bottom bar.
            ['E'] = P(0.7f, 1f, 0f, 1f, 0f, 0.5f, 0.55f, 0.5f, 0f, 0.5f, 0f, 0f, 0.7f, 0f),

            // Top bar, full spine, back up to the middle (new point on the spine), bar.
            ['F'] = P(0.7f, 1f, 0f, 1f, 0f, 0f, 0f, 0.5f, 0.55f, 0.5f),

            ['G'] = P(0.75f, 0.85f, 0.45f, 1f, 0.1f, 0.82f, 0f, 0.5f, 0.1f, 0.18f, 0.45f, 0f, 0.75f, 0.2f, 0.75f, 0.45f, 0.45f, 0.45f),

            // Left post down, back up to its middle, bar, right post up, then all the way down.
            ['H'] = P(0f, 1f, 0f, 0f, 0f, 0.5f, 0.7f, 0.5f, 0.7f, 1f, 0.7f, 0f),

            // Top serif, back to its middle, stem, bottom serif.
            ['I'] = P(0.1f, 1f, 0.5f, 1f, 0.3f, 1f, 0.3f, 0f, 0.1f, 0f, 0.5f, 0f),

            ['J'] = P(0.3f, 1f, 0.65f, 1f, 0.65f, 0.25f, 0.4f, 0f, 0.1f, 0.1f, 0f, 0.3f),

            // Stem, back up to the joint, upper arm, back down the arm to a point on it, lower arm.
            ['K'] = P(0f, 1f, 0f, 0f, 0f, 0.45f, 0.65f, 1f, 0.26f, 0.67f, 0.7f, 0f),

            ['L'] = P(0f, 1f, 0f, 0.5f, 0f, 0f, 0.6f, 0f),

            ['M'] = P(0f, 0f, 0f, 1f, 0.4f, 0.45f, 0.8f, 1f, 0.8f, 0f),

            ['N'] = P(0f, 0f, 0f, 1f, 0.7f, 0f, 0.7f, 1f),

            // Closed.
            ['O'] = P(0.35f, 1f, 0.68f, 0.8f, 0.7f, 0.25f, 0.35f, 0f, 0.02f, 0.2f, 0f, 0.75f, 0.35f, 1f),

            // Spine, bowl, ends on the spine (a new point on it).
            ['P'] = P(0f, 0f, 0f, 1f, 0.55f, 0.95f, 0.68f, 0.73f, 0.5f, 0.5f, 0f, 0.5f),

            // Tail, then the loop (closed at the tail joint).
            ['Q'] = P(0.88f, -0.05f, 0.6f, 0.17f, 0.35f, 0f, 0.02f, 0.2f, 0f, 0.75f, 0.35f, 1f, 0.68f, 0.8f, 0.72f, 0.38f, 0.6f, 0.17f),

            // P, then back along the bowl's base to a point on it, leg.
            ['R'] = P(0f, 0f, 0f, 1f, 0.55f, 0.95f, 0.68f, 0.73f, 0.5f, 0.5f, 0f, 0.5f, 0.3f, 0.5f, 0.7f, 0f),

            ['S'] = P(0.7f, 0.85f, 0.4f, 1f, 0.05f, 0.8f, 0.15f, 0.55f, 0.55f, 0.45f, 0.7f, 0.2f, 0.35f, 0f, 0f, 0.15f),

            // Bar, back to its middle, stem.
            ['T'] = P(0f, 1f, 0.7f, 1f, 0.35f, 1f, 0.35f, 0f),

            ['U'] = P(0f, 1f, 0f, 0.3f, 0.2f, 0.02f, 0.5f, 0.02f, 0.7f, 0.3f, 0.7f, 1f),

            ['V'] = P(0f, 1f, 0.17f, 0.5f, 0.35f, 0f, 0.52f, 0.5f, 0.7f, 1f),

            ['W'] = P(0f, 1f, 0.2f, 0f, 0.45f, 0.6f, 0.7f, 0f, 0.9f, 1f),

            // One diagonal, back to the centre, up-right, then the full other diagonal.
            ['X'] = P(0f, 1f, 0.7f, 0f, 0.35f, 0.5f, 0.7f, 1f, 0f, 0f),

            // Left arm, right arm, back to the joint (revisit), stem.
            ['Y'] = P(0f, 1f, 0.35f, 0.5f, 0.7f, 1f, 0.35f, 0.5f, 0.35f, 0f),

            ['Z'] = P(0f, 1f, 0.7f, 1f, 0f, 0f, 0.7f, 0f),
        };

    /// <summary>True when a glyph exists for this character (case-insensitive).</summary>
    public static bool Has(char c)
    {
        return Glyphs.ContainsKey(char.ToUpperInvariant(c));
    }

    /// <summary>
    /// The stroke polyline for a letter (case-insensitive), as a new array.
    /// Returns null for characters without a glyph.
    /// </summary>
    public static Vector2[] Get(char c)
    {
        if (!Glyphs.TryGetValue(char.ToUpperInvariant(c), out Vector2[] points))
        {
            return null;
        }

        return (Vector2[])points.Clone();
    }

    private static Vector2[] P(params float[] xy)
    {
        Vector2[] points = new Vector2[xy.Length / 2];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
        }

        return points;
    }
}
