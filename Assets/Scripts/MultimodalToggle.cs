using Oculus.Interaction;
using TMPro;
using UnityEngine;

/// <summary>
/// Toggles Simultaneous Hands and Controllers (multimodal) tracking at runtime.
/// With multimodal enabled you can hold a controller in one hand and use hand
/// tracking with the other. Hooks the first poke button found in its children.
/// </summary>
public class MultimodalToggle : MonoBehaviour
{
    public InteractableUnityEventWrapper toggleButton;
    public TMP_Text statusText;

    private bool multimodalEnabled;

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
        multimodalEnabled = !multimodalEnabled;
        if (!OVRPlugin.SetSimultaneousHandsAndControllersEnabled(multimodalEnabled))
        {
            // The runtime refused (feature unsupported on this device or in the editor).
            multimodalEnabled = !multimodalEnabled;
        }
        UpdateStatusText();
    }

    void UpdateStatusText()
    {
        if (statusText == null)
            return;

        statusText.text = multimodalEnabled
            ? "Hands + Controllers\n<b><color=#8BC34A>ON</color></b>"
            : "Hands + Controllers\n<b><color=#FF7043>OFF</color></b>";
    }
}
