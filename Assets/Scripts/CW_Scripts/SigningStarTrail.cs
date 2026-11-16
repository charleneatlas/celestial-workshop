using UnityEngine;

/// <summary>
/// Gives constellation signing the noticeboard's "magical ink": a StarTrail
/// glow and twinkling stars follow the name's reveal edge while the player
/// signs, a resting glow marks where to resume after a pause, and a flourish
/// runs along the line when the signature completes.
///
/// Only watches ConstellationSigning (state, progress, line geometry) and drives
/// a StarTrail the same way SignatureReveal does on the noticeboard; signing
/// itself is unchanged.
///
/// Follow Sphere Copy: while signing on the Guild Certificate, this trail runs
/// along the sphere copy of the name instead (a second trail, in step).
/// </summary>
public sealed class SigningStarTrail : MonoBehaviour
{
    [SerializeField]
    private ConstellationSigning signing;

    [Tooltip("The trail to drive (same component and materials as the noticeboard's).")]
    [SerializeField]
    private StarTrail trail;

    [Tooltip("Height of the trail as a fraction of the name's cap height (the noticeboard uses 0.45).")]
    [SerializeField]
    [Range(0f, 1.5f)]
    private float trailHeight = 0.45f;

    [Tooltip("Follow the sphere copy of the name (signing on the certificate) instead of the signing line.")]
    [SerializeField]
    private bool followSphereCopy;

    private ConstellationSigning.State lastState = ConstellationSigning.State.Off;
    private float lastProgress = -1f;

    private void LateUpdate()
    {
        if (signing == null || trail == null)
        {
            return;
        }

        ConstellationSigning.State state = signing.CurrentState;
        float progress = followSphereCopy ? signing.SkyProgress : signing.Progress;
        bool hasEdge = TryGetPoint(progress, out Vector3 edge);

        if (state != lastState)
        {
            OnStateChanged(lastState, state, hasEdge, edge);
            lastState = state;
            lastProgress = progress;
            return;
        }

        if (state == ConstellationSigning.State.Signing && hasEdge && progress > lastProgress + 1e-5f)
        {
            trail.StrokeTo(edge);
            lastProgress = progress;
        }
    }

    private void OnStateChanged(ConstellationSigning.State from, ConstellationSigning.State to, bool hasEdge, Vector3 edge)
    {
        switch (to)
        {
            case ConstellationSigning.State.Off:
                trail.Clear();
                break;

            case ConstellationSigning.State.Ready:
                trail.Clear();
                break;

            case ConstellationSigning.State.Signing:
                if (hasEdge)
                {
                    trail.BeginStroke(edge);
                }

                break;

            case ConstellationSigning.State.Paused:
                trail.EndStroke(true);
                if (hasEdge && (followSphereCopy ? signing.SkyProgress : signing.Progress) > 0f)
                {
                    trail.SetHead(true, edge, true);
                }

                break;

            case ConstellationSigning.State.Complete:
                // Completion jumps the reveal to the end: carry the trail there, then flourish.
                if (from == ConstellationSigning.State.Signing && hasEdge)
                {
                    trail.StrokeTo(edge);
                }

                trail.EndStroke(false);
                if (TryGetPoint(0f, out Vector3 start) && TryGetPoint(1f, out Vector3 end))
                {
                    trail.Flourish(start, end);
                }

                break;
        }
    }

    private bool TryGetPoint(float lineFraction, out Vector3 point)
    {
        return followSphereCopy
            ? signing.TryGetSkyLinePoint(lineFraction, trailHeight, out point)
            : signing.TryGetLinePoint(lineFraction, trailHeight, out point);
    }
}
