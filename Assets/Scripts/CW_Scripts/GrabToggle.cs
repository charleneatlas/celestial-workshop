using UnityEngine;

/// <summary>
/// Inspector switch for whether an object can be grabbed. Turns its ISDK grab
/// interaction objects (e.g. ISDK_HandGrabInteraction) on or off, so a placed
/// object stays put without stripping its grab setup. Works live in Play mode.
/// </summary>
public sealed class GrabToggle : MonoBehaviour
{
    [Tooltip("On: the object can be grabbed. Off: its grab interactions are disabled.")]
    [SerializeField]
    private bool allowGrab;

    [Tooltip("Grab interaction objects to enable or disable (the children Quick Actions created).")]
    [SerializeField]
    private GameObject[] grabInteractions = new GameObject[0];

    public bool AllowGrab
    {
        get => allowGrab;
        set
        {
            allowGrab = value;
            Apply();
        }
    }

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        // SetActive isn't allowed inside OnValidate; apply right after.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null)
            {
                Apply();
            }
        };
#endif
    }

    private void Apply()
    {
        foreach (GameObject interaction in grabInteractions)
        {
            if (interaction != null && interaction.activeSelf != allowGrab)
            {
                interaction.SetActive(allowGrab);
            }
        }
    }
}
