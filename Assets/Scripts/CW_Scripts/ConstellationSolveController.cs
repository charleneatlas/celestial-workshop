using UnityEngine;
using UnityEngine.Events;

public class ConstellationSolveController : MonoBehaviour
{
    [Header("Solve References")]
    [Tooltip("Child transform whose forward direction represents the correct viewing axis.")]
    [SerializeField] private Transform solveDirection;

    [Tooltip("Fixed point representing the center of the workshop / observer area.")]
    [SerializeField] private Transform skyObserverReference;

    [Header("Solve Settings")]
    [Tooltip("How close the viewing direction must be to count as solved.")]
    [SerializeField] private float solveToleranceDegrees = 8f;

    [Tooltip("How long the constellation must remain within tolerance.")]
    [SerializeField] private float requiredHoldTime = 0.4f;

    [Header("Solve Result")]
    [Tooltip("Optional object to reveal when solved, such as the constellation name.")]
    [SerializeField] private GameObject solvedVisual;

    [SerializeField] private UnityEvent onSolved;

    [Header("Reset")]
    [SerializeField]
    private ConstellationArcballTransformer tableArcballTransformer;

    [Header("Debug")]
    [SerializeField] private bool showDebugRays = true;
    [SerializeField] private float debugRayLength = 5f;

    private float solveTimer = 0f;
    private bool isSolved = false;

    public bool IsSolved => isSolved;

    private void Update()
    {
        if (isSolved)
            return;

        if (solveDirection == null || skyObserverReference == null)
            return;

        // Direction from the sky constellation toward the workshop.
        Vector3 directionToObserver =
            (skyObserverReference.position - solveDirection.position).normalized;

        // The constellation's current "correct viewing" axis.
        Vector3 constellationViewDirection =
            solveDirection.forward.normalized;

        float angle = Vector3.Angle(
            constellationViewDirection,
            directionToObserver
        );

        if (angle <= solveToleranceDegrees)
        {
            solveTimer += Time.deltaTime;

            if (solveTimer >= requiredHoldTime)
            {
                Solve();
            }
        }
        else
        {
            solveTimer = 0f;
        }

        if (showDebugRays)
        {
            // Blue = constellation's solve direction
            Debug.DrawRay(
                solveDirection.position,
                constellationViewDirection * debugRayLength,
                Color.blue
            );

            // Green = actual direction toward the workshop
            Debug.DrawRay(
                solveDirection.position,
                directionToObserver * debugRayLength,
                Color.green
            );
        }
    }

    private void Solve()
    {
        if (isSolved)
            return;

        isSolved = true;

        Debug.Log("CONSTELLATION SOLVED!");

        if (solvedVisual != null)
            solvedVisual.SetActive(true);

        onSolved?.Invoke();
    }

    [ContextMenu("Reset Solve")]
    public void ResetSolve()
    {
        isSolved = false;
        solveTimer = 0f;

        if (solvedVisual != null)
        {
            solvedVisual.SetActive(false);
        }

        if (tableArcballTransformer != null)
        {
            tableArcballTransformer.RandomizeRotation();
        }

        Debug.Log("Constellation reset.");
    }

    [ContextMenu("Calibrate Solve Direction")]
    private void CalibrateSolveDirection()
    {
        if (solveDirection == null || skyObserverReference == null)
            return;

        Vector3 directionToObserver =
            (skyObserverReference.position - solveDirection.position).normalized;

        solveDirection.rotation =
            Quaternion.LookRotation(directionToObserver, Vector3.up);

        Debug.Log("Solve direction calibrated.");

        float angle = Vector3.Angle(
            solveDirection.forward,
            (skyObserverReference.position - solveDirection.position).normalized
        );

        Debug.Log($"Solve angle: {angle}");
    }
}