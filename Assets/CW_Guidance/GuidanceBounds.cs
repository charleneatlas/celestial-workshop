using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CW.Guidance
{
    /// <summary>
    /// World bounds of a guidance target, from the renderers under it. Text,
    /// particles and lines are skipped (they are labels, sparkles and guide
    /// strokes, not the object the player should look at).
    /// </summary>
    /// <summary>
    /// A target that knows where it appears better than its renderers do (for
    /// example something seen through a camera image). When the target has one,
    /// its bounds are used instead of the renderers'.
    /// </summary>
    public interface IGuidanceBoundsSource
    {
        bool TryGetBounds(out Bounds bounds);
    }

    public static class GuidanceBounds
    {
        /// <summary>Size used when the target has no usable renderer.</summary>
        public const float FallbackSize = 0.05f;

        public static void Collect(Transform target, List<Renderer> into)
        {
            into.Clear();
            if (target == null)
            {
                return;
            }

            target.GetComponentsInChildren(true, into);
            for (int i = into.Count - 1; i >= 0; i--)
            {
                Renderer r = into[i];
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer
                    || r.GetComponent<TMP_Text>() != null)
                {
                    into.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Combined bounds of the enabled, active renderers in the list; falls
        /// back to a small box at the target's position.
        /// </summary>
        public static Bounds Compute(Transform target, List<Renderer> renderers)
        {
            if (target != null && target.TryGetComponent(out IGuidanceBoundsSource source)
                && source.TryGetBounds(out Bounds sourced))
            {
                return sourced;
            }

            bool any = false;
            Bounds bounds = default;
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = r.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            if (!any)
            {
                Vector3 position = target != null ? target.position : Vector3.zero;
                bounds = new Bounds(position, Vector3.one * FallbackSize);
            }

            return bounds;
        }
    }
}
