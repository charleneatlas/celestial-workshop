using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Makes a poke canvas's MomentaryToggleButtons fire on release even when the
/// finger has drifted off the button. The button still goes down on the push
/// and fires when the finger lifts off, as a UGUI click does; the difference
/// is that a UGUI click also needs the release over the same button, and in
/// playtests a finger pulling back from the tilted menu sometimes slid off it
/// (33 mm), losing a press that had clearly landed. A press counts if it
/// started on a button. A cancelled poke (finger slid past the
/// PokeInteractable's Cancel Select Tangent) still doesn't fire. The UGUI
/// click that follows a release over the button is ignored
/// (MomentaryToggleButton.FireFromPokeRelease).
/// </summary>
public sealed class PokeReleaseAnywhere : MonoBehaviour
{
    [Tooltip("The menu's poke canvas (ISDK PointableCanvas). Empty = the first one under this object.")]
    [SerializeField]
    private PointableCanvas canvas;

    [Tooltip("Buttons handled. Empty = every one under this object.")]
    [SerializeField]
    private MomentaryToggleButton[] buttons = new MomentaryToggleButton[0];

    [SerializeField]
    private bool logPresses = true;

    private MomentaryToggleButton pressed;

    private void OnEnable()
    {
        if (canvas == null)
        {
            canvas = GetComponentInChildren<PointableCanvas>(true);
        }

        if (buttons == null || buttons.Length == 0)
        {
            buttons = GetComponentsInChildren<MomentaryToggleButton>(true);
        }

        if (canvas != null)
        {
            canvas.WhenPointerEventRaised += HandlePointer;
        }
        else
        {
            Debug.LogWarning("PokeReleaseAnywhere: no PointableCanvas; buttons fire on click only.", this);
        }
    }

    private void OnDisable()
    {
        if (canvas != null)
        {
            canvas.WhenPointerEventRaised -= HandlePointer;
        }

        pressed = null;
    }

    private void HandlePointer(PointerEvent evt)
    {
        switch (evt.Type)
        {
            case PointerEventType.Select:
                pressed = ButtonAt(evt.Pose.position);
                break;

            case PointerEventType.Unselect:
                if (pressed != null && pressed.isActiveAndEnabled)
                {
                    if (logPresses)
                    {
                        Debug.Log($"PokeReleaseAnywhere: '{pressed.name}' released.", this);
                    }

                    pressed.FireFromPokeRelease();
                }

                pressed = null;
                break;

            case PointerEventType.Cancel:
                pressed = null;
                break;
        }
    }

    // The active button whose rect contains the poke point (projected onto the canvas plane).
    private MomentaryToggleButton ButtonAt(Vector3 world)
    {
        foreach (MomentaryToggleButton button in buttons)
        {
            if (button == null || !button.isActiveAndEnabled)
            {
                continue;
            }

            var rect = (RectTransform)button.transform;
            Vector3 local = rect.InverseTransformPoint(world);
            if (rect.rect.Contains(new Vector2(local.x, local.y)))
            {
                return button;
            }
        }

        return null;
    }
}
