using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Turns a UGUI Toggle (e.g. the ISDK UISet "ButtonShelf ... Toggle" prefab)
/// into a momentary button. The Toggle still drives the click and the
/// Animator's hover / press states; this component snaps it back off in the
/// same frame so it never latches, keeps it out of the UI selection, and
/// fires its own On Pressed event.
///
/// On the button instance, clear the Toggle reference on the prefab's
/// AnimatorOverrideLayerWeight so its "Selected" layer never engages.
/// </summary>
[RequireComponent(typeof(Toggle))]
public sealed class MomentaryToggleButton : MonoBehaviour
{
    [SerializeField]
    private Toggle toggle;

    [Tooltip("Presses closer together than this are ignored (one poke can register twice).")]
    [SerializeField]
    private float repeatGuard = 0.35f;

    [SerializeField]
    private UnityEvent onPressed = new UnityEvent();

    /// <summary>Fired once per press.</summary>
    public event Action Pressed;

    public UnityEvent OnPressed => onPressed;

    private float lastPressTime = float.NegativeInfinity;

    // Set when a poke release fired this button (PokeReleaseAnywhere). The
    // canvas reports the release before UGUI delivers its click, so a click
    // arriving within a short grace after it is the same press: ignored.
    private float pokeReleaseFiredTime = float.NegativeInfinity;
    private const float ReleaseGrace = 0.25f;

    private void Reset()
    {
        toggle = GetComponent<Toggle>();
    }

    private void Awake()
    {
        if (toggle == null)
        {
            toggle = GetComponent<Toggle>();
        }

        // Never become the EventSystem's selected object, so the Animator
        // doesn't sit in its Selected state after a press.
        Navigation nav = toggle.navigation;
        nav.mode = Navigation.Mode.None;
        toggle.navigation = nav;
    }

    private void OnEnable()
    {
        toggle.SetIsOnWithoutNotify(false);
        toggle.onValueChanged.AddListener(HandleValueChanged);
    }

    private void OnDisable()
    {
        toggle.onValueChanged.RemoveListener(HandleValueChanged);
        toggle.SetIsOnWithoutNotify(false);
    }

    private void HandleValueChanged(bool isOn)
    {
        toggle.SetIsOnWithoutNotify(false);

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem != null && eventSystem.currentSelectedGameObject == gameObject)
        {
            eventSystem.SetSelectedGameObject(null);
        }

        if (!isOn)
        {
            return;
        }

        if (Time.unscaledTime - pokeReleaseFiredTime < ReleaseGrace)
        {
            return; // this press already fired on the poke's release
        }

        Fire();
    }

    /// <summary>
    /// Fires when a poke that started on this button is released, wherever the
    /// finger is by then (a UGUI click would also need the release over the
    /// button). The Toggle click that may follow is ignored.
    /// </summary>
    public void FireFromPokeRelease()
    {
        pokeReleaseFiredTime = Time.unscaledTime;
        Fire();
    }

    private void Fire()
    {
        float now = Time.unscaledTime;
        if (now - lastPressTime < repeatGuard)
        {
            return;
        }

        lastPressTime = now;
        Pressed?.Invoke();
        onPressed.Invoke();
    }

    /// <summary>Press from code (debug / tests); same path as a real press.</summary>
    [ContextMenu("Debug: Press")]
    public void Press()
    {
        HandleValueChanged(true);
    }
}
