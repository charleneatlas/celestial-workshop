using System.Collections.Generic;
using UnityEngine;

public class SkyConstellationHighlightController : MonoBehaviour
{
    // The sky constellation material (SkyConstellation_Mat) is URP Unlit, additive:
    // alpha works as brightness, and overlaps (stars on lines, the sphere behind)
    // add up. A channel already near 1 saturates, so pale colours wash to white.
    // Keep at least one channel well below 1 to stay visibly coloured.
    [Tooltip("Before near-solve. Alpha = brightness (additive material).")]
    [SerializeField] private Color normalColor;

    [Tooltip("Inside the near-solve tolerance, until the solve is confirmed. Keep it saturated " +
             "(one channel low) so additive overlaps don't wash it toward the white solved colour.")]
    [SerializeField] private Color nearSolveColor;

    [Tooltip("Confirmed solve.")]
    [SerializeField] private Color solvedColor;

    private enum State
    {
        Normal,
        NearSolve,
        Solved
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private Renderer[] renderers;
    private readonly Dictionary<Renderer, GeneratedConstellationStar> starsByRenderer =
        new Dictionary<Renderer, GeneratedConstellationStar>();
    private MaterialPropertyBlock block;
    private State state = State.Normal;

    public void SetConstellation(GameObject constellationRoot)
    {
        renderers =
            constellationRoot.GetComponentsInChildren<Renderer>(true);

        starsByRenderer.Clear();
        foreach (GeneratedConstellationStar star in
                 constellationRoot.GetComponentsInChildren<GeneratedConstellationStar>(true))
        {
            if (star.StarRenderer != null)
            {
                starsByRenderer[star.StarRenderer] = star;
            }
        }

        SetNormal();
    }

    public void SetNormal()
    {
        state = State.Normal;
        Apply();
        Debug.Log("CONSTELLATION NO LONGER NEAR SOLVED!");
    }


    public void SetNearSolve()
    {
        state = State.NearSolve;
        Apply();
        Debug.Log("CONSTELLATION NEAR SOLVED!");
    }

    public void SetSolved()
    {
        state = State.Solved;
        Apply();
    }

    /// <summary>
    /// Re-applies the current state, e.g. after a star was marked or unmarked
    /// on the Star Chart (which swaps that star's material).
    /// </summary>
    public void Refresh()
    {
        Apply();
    }

    // The tint is a per-renderer property block, so it survives the
    // marked-star material swap. Marked stars (poked on the Star Chart) keep
    // their highlight colour until the solve is confirmed; solved tints every star.
    private void Apply()
    {
        if (renderers == null)
            return;

        Color color = state == State.Solved ? solvedColor
            : state == State.NearSolve ? nearSolveColor
            : normalColor;

        block ??= new MaterialPropertyBlock();

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            bool showMark =
                state != State.Solved &&
                starsByRenderer.TryGetValue(renderer, out GeneratedConstellationStar star) &&
                star.IsHighlighted;

            if (showMark)
            {
                renderer.SetPropertyBlock(null);
                continue;
            }

            block.Clear();
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }
    }
}