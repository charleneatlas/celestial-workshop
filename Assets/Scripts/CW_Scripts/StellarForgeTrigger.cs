using System.Collections;
using UnityEngine;

public sealed class StellarForgeTrigger : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Rigidbody constellationRigidbody;

    [SerializeField]
    private ConstellationSolveController solveController;

    [SerializeField]
    private ParticleSystem impactParticles;

    [SerializeField]
    private AudioSource forgeAudioSource;

    [Header("Detection")]
    [Tooltip("How close the constellation center must be horizontally to the forge center.")]
    [SerializeField]
    private float horizontalRadius = 0.35f;

    [Tooltip("Local Y position below which the constellation counts as dropped into the forge.")]
    [SerializeField]
    private float triggerLocalY = 0f;

    private bool constellationIsHeld;

    [Header("Reset")]
    [SerializeField]
    private float resetDelay = 0.25f;

    private bool hasTriggered;

    private void Update()
    {
        // Don't trigger if user is still holding the constellation, we want it to be dropped in.
        if (
            hasTriggered ||
            constellationRigidbody == null ||
            constellationIsHeld
        )
        {
            return;
        }

        Vector3 constellationPosition =
            constellationRigidbody.position;

        Vector3 forgePosition =
            transform.position;

        float horizontalDistance =
            Vector2.Distance(
                new Vector2(
                    constellationPosition.x,
                    constellationPosition.z
                ),
                new Vector2(
                    forgePosition.x,
                    forgePosition.z
                )
            );

        float triggerWorldY =
            forgePosition.y + triggerLocalY;

        bool isOverForge =
            horizontalDistance <= horizontalRadius;

        bool isLowEnough =
            constellationPosition.y <= triggerWorldY;

        if (isOverForge && isLowEnough)
        {
            Debug.Log(
                $"FORGE CHECK PASSED | " +
                $"Constellation: {constellationPosition} | " +
                $"Forge: {forgePosition} | " +
                $"Horizontal: {horizontalDistance:0.000} / {horizontalRadius:0.000} | " +
                $"Y: {constellationPosition.y:0.000} / {triggerWorldY:0.000}"
            );

            Debug.Log(
                $"Transform Y: {constellationRigidbody.transform.position.y:0.000} | " +
                $"Rigidbody Y: {constellationRigidbody.position.y:0.000}"
            );

            TriggerForge();
        }
    }

    private void TriggerForge()
    {
        Debug.Log(
            $"FORGE TRIGGERED by {name}",
            this
        );

        hasTriggered = true;

        if (impactParticles != null)
        {
            impactParticles.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear
            );

            impactParticles.Play(true);
        }

        if (forgeAudioSource != null)
        {
            forgeAudioSource.Play();
        }

        StartCoroutine(ResetAfterDelay());
    }

    public void HandleGrabbed()
    {
        constellationIsHeld = true;

        Debug.Log("CONSTELLATION GRABBED");
    }

    public void HandleReleased()
    {
        constellationIsHeld = false;

        constellationRigidbody.linearVelocity = Vector3.zero;

        Debug.Log("CONSTELLATION RELEASED");
    }

    private IEnumerator ResetAfterDelay()
    {
        yield return new WaitForSeconds(resetDelay);

        solveController.ResetSolve();
    }

    public void ResetTrigger()
    {
        hasTriggered = false;
        constellationIsHeld = false;
    }

    private void OnDrawGizmos()
    {
        const int segments = 64;

        Gizmos.color = Color.yellow;

        float triggerWorldY =
            transform.position.y + triggerLocalY;

        Vector3 center = new Vector3(
            transform.position.x,
            triggerWorldY,
            transform.position.z
        );

        Vector3 previousPoint = Vector3.zero;

        for (int i = 0; i <= segments; i++)
        {
            float angle =
                (float)i / segments * Mathf.PI * 2f;

            Vector3 worldPoint = new Vector3(
                center.x + Mathf.Cos(angle) * horizontalRadius,
                triggerWorldY,
                center.z + Mathf.Sin(angle) * horizontalRadius
            );

            if (i > 0)
            {
                Gizmos.DrawLine(
                    previousPoint,
                    worldPoint
                );
            }

            previousPoint = worldPoint;
        }

        if (constellationRigidbody != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(
                constellationRigidbody.position,
                0.05f
            );
        }

        Gizmos.DrawWireSphere(center, 0.03f);

        Gizmos.DrawLine(
            transform.position,
            center
        );
    }
}