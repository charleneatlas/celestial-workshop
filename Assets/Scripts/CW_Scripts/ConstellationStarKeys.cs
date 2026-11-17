using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which stars of a star set share one point on the artboard.
///
/// Letters that revisit a point (E, O, and B, D, Q, Y) generate two star
/// spheres there under different names (E3 and E5, O1 and O7). The Star Chart
/// shows one dot per point, named after the first star there
/// (StarChartLayout.EnsureGeneratedTargets), so marking that dot must mark
/// every sphere at the point, or the unmarked twin hides the mark.
///
/// PositionKey must stay identical to the key StarChartLayout uses for its
/// dots (affinity px, Mathf.RoundToInt per axis), so the two always agree.
/// </summary>
public static class ConstellationStarKeys
{
    /// <summary>The Star Chart's one-dot-per-point key (see StarChartLayout.EnsureGeneratedTargets).</summary>
    public static Vector2Int PositionKey(Vector2 affinityPosition)
    {
        return new Vector2Int(
            Mathf.RoundToInt(affinityPosition.x),
            Mathf.RoundToInt(affinityPosition.y));
    }

    /// <summary>
    /// The star names at each point that has more than one, in star-list
    /// order (the first is the name the chart dot uses). Names are compared
    /// case-insensitively, like the star lookup.
    /// </summary>
    public static List<List<string>> CoLocatedNameGroups(IReadOnlyList<ConstellationGenerator.StarData> stars)
    {
        var groups = new List<List<string>>();
        if (stars == null)
        {
            return groups;
        }

        var byKey = new Dictionary<Vector2Int, List<string>>();
        var order = new List<Vector2Int>();
        foreach (ConstellationGenerator.StarData star in stars)
        {
            if (star == null || string.IsNullOrEmpty(star.starName))
            {
                continue;
            }

            Vector2Int key = PositionKey(star.affinityPosition);
            if (!byKey.TryGetValue(key, out List<string> names))
            {
                names = new List<string>();
                byKey.Add(key, names);
                order.Add(key);
            }

            if (!names.Exists(n => string.Equals(n, star.starName, StringComparison.OrdinalIgnoreCase)))
            {
                names.Add(star.starName);
            }
        }

        foreach (Vector2Int key in order)
        {
            if (byKey[key].Count > 1)
            {
                groups.Add(byKey[key]);
            }
        }

        return groups;
    }

    /// <summary>
    /// After stars were registered by their own id: every name at a shared
    /// point now maps to all the generated stars at that point (mini and sky).
    /// </summary>
    public static void MergeCoLocated(
        Dictionary<string, List<GeneratedConstellationStar>> starsById,
        IReadOnlyList<ConstellationGenerator.StarData> stars)
    {
        if (starsById == null)
        {
            return;
        }

        foreach (List<string> names in CoLocatedNameGroups(stars))
        {
            var all = new List<GeneratedConstellationStar>();
            foreach (string name in names)
            {
                if (!starsById.TryGetValue(name, out List<GeneratedConstellationStar> matching))
                {
                    continue;
                }

                foreach (GeneratedConstellationStar star in matching)
                {
                    if (!all.Contains(star))
                    {
                        all.Add(star);
                    }
                }
            }

            if (all.Count == 0)
            {
                continue;
            }

            foreach (string name in names)
            {
                starsById[name] = all;
            }
        }
    }
}
