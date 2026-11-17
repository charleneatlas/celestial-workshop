using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// EditMode regression tests: a Star Chart dot marks every 3D star at its
/// point (the E3/E5 and O1/O7 twins), and nothing stays marked afterwards.
/// Editor-only (Assembly-CSharp-Editor), so nothing here ships in the player.
/// </summary>
public class StarHighlightMappingTests
{
    // The scene's Cassiopeia (main puzzle and the Starter Pack sculptures),
    // copied from CelestialWorkshop_VRGlasses_Prototype.unity.
    // seginY: the scene holds two variants (161.9 and 59.7).
    private static List<ConstellationGenerator.StarData> Cassiopeia(float seginY)
    {
        return new List<ConstellationGenerator.StarData>
        {
            Star("Segin", 299.9f, seginY, 412f),
            Star("Ruchbah", 596.4f, 541f, 99f),
            Star("Gamma Cassiopeia", 949.3f, 492.5f, 549f),
            Star("Schedar", 1198.5f, 887.8f, 228f),
            Star("Caph", 1562.1f, 559.9f, 55f),
        };
    }

    private static ConstellationGenerator.StarData Star(string name, float x, float y, float ly)
    {
        return new ConstellationGenerator.StarData
        {
            starName = name,
            affinityPosition = new Vector2(x, y),
            distanceLightYears = ly,
        };
    }

    private static IEnumerable<char> Letters()
    {
        for (char c = 'A'; c <= 'Z'; c++)
        {
            if (LetterGlyphs.Has(c))
            {
                yield return c;
            }
        }
    }

    private static IEnumerable<TestCaseData> StarSets()
    {
        foreach (char c in Letters())
        {
            foreach (int seed in new[] { 0, 7 })
            {
                yield return new TestCaseData(LetterConstellation.Build(c, seed))
                    .SetName($"Letter {c} seed {seed}");
            }
        }

        yield return new TestCaseData(Cassiopeia(161.9f)).SetName("Cassiopeia");
        yield return new TestCaseData(Cassiopeia(59.7f)).SetName("Cassiopeia (Segin y 59.7)");
    }

    // Mirror of StarChartLayout.EnsureGeneratedTargets: one dot per rounded
    // affinity px, named after the first star there. Written out here (not via
    // ConstellationStarKeys) so a drift in the shared key is caught.
    private static List<(string id, Vector2Int key)> ChartDots(List<ConstellationGenerator.StarData> stars)
    {
        var dots = new List<(string, Vector2Int)>();
        var seen = new HashSet<Vector2Int>();
        foreach (ConstellationGenerator.StarData star in stars)
        {
            if (string.IsNullOrEmpty(star.starName))
            {
                continue;
            }

            var key = new Vector2Int(
                Mathf.RoundToInt(star.affinityPosition.x),
                Mathf.RoundToInt(star.affinityPosition.y));
            if (seen.Add(key))
            {
                dots.Add((star.starName, key));
            }
        }

        return dots;
    }

    private static HashSet<string> NamesAt(List<ConstellationGenerator.StarData> stars, Vector2Int key)
    {
        return new HashSet<string>(
            stars.Where(s => ConstellationStarKeys.PositionKey(s.affinityPosition) == key).Select(s => s.starName),
            StringComparer.OrdinalIgnoreCase);
    }

    [TestCaseSource(nameof(StarSets))]
    public void StarNamesAreUniqueIgnoringCase(List<ConstellationGenerator.StarData> stars)
    {
        var names = stars.Select(s => s.starName).ToList();
        foreach (string name in names)
        {
            Assert.IsFalse(string.IsNullOrEmpty(name), "A star has no name");
        }

        Assert.AreEqual(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "Duplicate star names: " + string.Join(", ", names));
    }

    [TestCaseSource(nameof(StarSets))]
    public void SharedKeyMatchesChartAndEveryStarHasADot(List<ConstellationGenerator.StarData> stars)
    {
        List<(string id, Vector2Int key)> dots = ChartDots(stars);
        foreach (ConstellationGenerator.StarData star in stars)
        {
            Vector2Int chartKey = new Vector2Int(
                Mathf.RoundToInt(star.affinityPosition.x),
                Mathf.RoundToInt(star.affinityPosition.y));
            Assert.AreEqual(chartKey, ConstellationStarKeys.PositionKey(star.affinityPosition));
            Assert.AreEqual(1, dots.Count(d => d.key == chartKey), $"{star.starName}: expected exactly one chart dot");
        }

        // Each co-located group starts with the dot's own name.
        foreach (List<string> group in ConstellationStarKeys.CoLocatedNameGroups(stars))
        {
            Assert.IsTrue(dots.Any(d => string.Equals(d.id, group[0], StringComparison.OrdinalIgnoreCase)),
                $"Group [{string.Join(", ", group)}] does not start with a chart dot id");
        }
    }

    [Test]
    public void RevisitingTitleLettersHaveTheExpectedTwins()
    {
        CollectionAssert.AreEqual(new[] { "E3", "E5" },
            ConstellationStarKeys.CoLocatedNameGroups(LetterConstellation.Build('E', 0)).Single());
        CollectionAssert.AreEqual(new[] { "O1", "O7" },
            ConstellationStarKeys.CoLocatedNameGroups(LetterConstellation.Build('O', 0)).Single());
    }

    // ------------------------------------------------------------ integration

    private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
    private Material normalMaterial;
    private Material highlightMaterial;

    [SetUp]
    public void SetUp()
    {
        Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/Internal-Colored");
        Assert.IsNotNull(shader, "No built-in shader for the test materials");
        normalMaterial = Track(new Material(shader) { name = "Test Normal" });
        highlightMaterial = Track(new Material(shader) { name = "Test Highlight" });
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(created[i]);
            }
        }

        created.Clear();
    }

    private T Track<T>(T obj) where T : UnityEngine.Object
    {
        created.Add(obj);
        return obj;
    }

    [TestCaseSource(nameof(StarSets))]
    public void EveryChartDotMarksAllColocatedStarsAndClears(List<ConstellationGenerator.StarData> stars)
    {
        GameObject host = Track(new GameObject("StarHighlightTest"));
        ConstellationGenerator generator = host.AddComponent<ConstellationGenerator>();
        generator.showDebugLabels = false;
        generator.showEarthViewMarker = false;
        generator.artboardSize = LetterConstellation.Artboard;
        generator.stars = stars;

        var appearance = new ConstellationGenerator.AppearanceSettings
        {
            starMaterial = normalMaterial,
            starHighlightMaterial = highlightMaterial,
            lineMaterial = normalMaterial,
        };

        // Mini and sky, as ConstellationSetup.GenerateConstellations builds them.
        Transform miniParent = new GameObject("Mini").transform;
        Transform skyParent = new GameObject("Sky").transform;
        miniParent.SetParent(host.transform, false);
        skyParent.SetParent(host.transform, false);
        GameObject mini = generator.GenerateConstellation(miniParent, appearance);
        GameObject sky = generator.GenerateConstellation(skyParent, appearance);
        Assert.IsNotNull(mini);
        Assert.IsNotNull(sky);

        // The real lookup and SetStarHighlighted (Start isn't run in edit mode).
        ConstellationSetup setup = host.AddComponent<ConstellationSetup>();
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo generatorField = typeof(ConstellationSetup).GetField("constellationGenerator", Private);
        MethodInfo buildLookup = typeof(ConstellationSetup).GetMethod("BuildStarLookup", Private);
        Assert.IsNotNull(generatorField, "ConstellationSetup.constellationGenerator not found");
        Assert.IsNotNull(buildLookup, "ConstellationSetup.BuildStarLookup not found");
        generatorField.SetValue(setup, generator);
        buildLookup.Invoke(setup, new object[] { mini, sky });

        GeneratedConstellationStar[] all = host.GetComponentsInChildren<GeneratedConstellationStar>(true);
        Assert.AreEqual(stars.Count * 2, all.Length);

        foreach ((string id, Vector2Int key) in ChartDots(stars))
        {
            HashSet<string> expected = NamesAt(stars, key);

            setup.SetStarHighlighted(id, true);
            foreach (GeneratedConstellationStar star in all)
            {
                bool shouldMark = expected.Contains(star.StarId);
                Assert.AreEqual(shouldMark, star.IsHighlighted,
                    $"dot {id}: star {star.StarId} marked={star.IsHighlighted}, expected {shouldMark}");
                Assert.AreSame(shouldMark ? highlightMaterial : normalMaterial, star.StarRenderer.sharedMaterial,
                    $"dot {id}: star {star.StarId} has the wrong material");
            }

            setup.SetStarHighlighted(id, false);
            foreach (GeneratedConstellationStar star in all)
            {
                Assert.IsFalse(star.IsHighlighted, $"dot {id}: star {star.StarId} still marked after release");
                Assert.AreSame(normalMaterial, star.StarRenderer.sharedMaterial,
                    $"dot {id}: star {star.StarId} kept the highlight material");
            }
        }
    }
}
