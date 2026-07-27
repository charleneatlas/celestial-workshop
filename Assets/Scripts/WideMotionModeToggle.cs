using Oculus.Interaction;
using TMPro;
using UnityEngine;

/// <summary>
/// Toggles Wide Motion Mode at runtime (body tracking based hand estimation
/// when the hands leave the cameras' view). Hooks the first poke button found
/// in its children and shows the current state on a text panel.
/// </summary>
public class WideMotionModeToggle : MonoBehaviour
{
    public InteractableUnityEventWrapper toggleButton;
    public TMP_Text statusText;

    void Awake()
    {
        if (toggleButton == null)
            toggleButton = GetComponentInChildren<InteractableUnityEventWrapper>(true);
    }

    void OnEnable()
    {
        if (toggleButton != null)
            toggleButton.WhenSelect.AddListener(Toggle);
        UpdateStatusText();
    }

    void OnDisable()
    {
        if (toggleButton != null)
            toggleButton.WhenSelect.RemoveListener(Toggle);
    }

    public void Toggle()
    {
        OVRManager manager = OVRManager.instance;
        if (manager == null)
            return;

        manager.wideMotionModeHandPosesEnabled = !manager.wideMotionModeHandPosesEnabled;
        UpdateStatusText();
    }

    void UpdateStatusText()
    {
        if (statusText == null)
            return;

        bool enabled = OVRManager.instance != null && OVRManager.instance.wideMotionModeHandPosesEnabled;
        statusText.text = enabled
            ? "Wide Motion Mode\n<b><color=#8BC34A>ON</color></b>"
            : "Wide Motion Mode\n<b><color=#FF7043>OFF</color></b>";
    }
}
