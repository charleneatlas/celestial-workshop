using UnityEngine;

/// <summary>
/// The player's placement buttons on the title menu: Recenter brings the
/// workshop in front of them from where they are now; Raise and Lower move it
/// up or down one step (remembered between sessions). All of it goes through
/// <see cref="WorkshopPlacement"/>.
/// </summary>
public sealed class PlacementControls : MonoBehaviour
{
    [Tooltip("Found automatically if empty.")]
    [SerializeField]
    private WorkshopPlacement placement;

    [SerializeField]
    private MomentaryToggleButton recenterButton;

    [SerializeField]
    private MomentaryToggleButton raiseButton;

    [SerializeField]
    private MomentaryToggleButton lowerButton;

    private void OnEnable()
    {
        if (placement == null)
        {
            placement = FindFirstObjectByType<WorkshopPlacement>();
        }

        if (recenterButton != null) recenterButton.Pressed += OnRecenter;
        if (raiseButton != null) raiseButton.Pressed += OnRaise;
        if (lowerButton != null) lowerButton.Pressed += OnLower;
    }

    private void OnDisable()
    {
        if (recenterButton != null) recenterButton.Pressed -= OnRecenter;
        if (raiseButton != null) raiseButton.Pressed -= OnRaise;
        if (lowerButton != null) lowerButton.Pressed -= OnLower;
    }

    private void OnRecenter()
    {
        if (placement != null) placement.Recenter();
    }

    private void OnRaise()
    {
        if (placement != null) placement.Raise();
    }

    private void OnLower()
    {
        if (placement != null) placement.Lower();
    }
}
