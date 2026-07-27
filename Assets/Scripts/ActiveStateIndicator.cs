using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Colors a renderer green while an Interaction SDK IActiveState is active
/// (for example a ShapeRecognizerActiveState, TransformRecognizerActiveState
/// or ActiveStateGroup). Used as a simple visual "check light" on the
/// Hand Pose Detection table.
/// </summary>
public class ActiveStateIndicator : MonoBehaviour
{
    [Tooltip("A component implementing IActiveState (ShapeRecognizerActiveState, ActiveStateGroup...)")]
    public MonoBehaviour activeStateSource;
    public Renderer indicatorRenderer;
    public Color activeColor = new Color(0.4f, 0.85f, 0.3f, 1f);
    public Color inactiveColor = new Color(0.35f, 0.35f, 0.4f, 1f);

    private IActiveState activeState;

    void Awake()
    {
        activeState = activeStateSource as IActiveState;
        if (indicatorRenderer == null)
            indicatorRenderer = GetComponent<Renderer>();
    }

    void Update()
    {
        if (indicatorRenderer == null)
            return;

        bool isActive = activeState != null && activeState.Active;
        indicatorRenderer.material.color = isActive ? activeColor : inactiveColor;
    }
}
