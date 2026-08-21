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

    [Tooltip("How close the viewing direction must be to enter the near-solve state.")]
    [SerializeField] private float nearSolveToleranceDegrees = 20f;

    [Tooltip("How long the constellation must remain within tolerance.")]
    [SerializeField] private float requiredHoldTime = 0.4f;

    [Header("Near Solve")]
    [SerializeField] private UnityEvent onNearSolveEntered;
    [SerializeField] private UnityEvent onNearSolveExited;

    [Header("Solve Result")]
    [Tooltip("Optional object to reveal when solved, such as the constellation name.")]
    [SerializeField] private GameObject solvedVisual;

    [SerializeField] private UnityEvent onSolved;
    [SerializeField] private UnityEvent onReset;

    [Header("Reset")]
    [SerializeField]
    private ConstellationArcballTransformer tableArcballTransformer;

    [Header("Debug")]
    [SerializeField] private bool showDebugRays = true;
    [SerializeField] private float debugRayLength = 5f;

    private float solveTimer = 0f;
    private bool isSolved = false;
    private bool isNearSolve = false;

    public bool IsSolved => isSolved;
    public bool IsNearSolve => isNearSolve;

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

        UpdateNearSolveState(angle);

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

    private void UpdateNearSolveState(float angle)
    {
        // Near-solve remains active all the way through
        // the actual solve tolerance until Solve() completes.
        bool shouldBeNearSolve =
            angle <= nearSolveToleranceDegrees;

        if (shouldBeNearSolve == isNearSolve)
            return;

        isNearSolve = shouldBeNearSolve;

        if (isNearSolve)
        {
            Debug.Log("Constellation Near Solved");
            onNearSolveEntered?.Invoke();
        }
        else
        {
            Debug.Log("Constellation No Longer Near Solved");
            onNearSolveExited?.Invoke();
        }
    }

    private void Solve()
    {
        if (isSolved)
            return;

        isSolved = true;

        // Clear the internal near-solve state without firing
        // the exit event. The solved state should visually
        // replace the near-solve state directly.
        isNearSolve = false;

        Debug.Log("CONSTELLATION SOLVED!");

        if (solvedVisual != null)
        {
            solvedVisual.SetActive(true);
        }

        onSolved?.Invoke();
    }

    [ContextMenu("Reset Solve")]
    public void ResetSolve()
    {
        isSolved = false;
        solveTimer = 0f;

        // Reset visuals back to their normal state.
        if (isNearSolve)
        {
            isNearSolve = false;
            onNearSolveExited?.Invoke();
        }

        if (solvedVisual != null)
        {
            solvedVisual.SetActive(false);
        }

        if (tableArcballTransformer != null)
        {
            tableArcballTransformer.RandomizeRotation();
        }

        // Restore visual state after resetting.
        onReset?.Invoke();

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
            Quaternion.LookRotation(
                directionToObserver,
                Vector3.up
            );

        Debug.Log("Solve direction calibrated.");

        float angle = Vector3.Angle(
            solveDirection.forward,
            (skyObserverReference.position -
             solveDirection.position).normalized
        );

        Debug.Log($"Solve angle: {angle}");
    }
}