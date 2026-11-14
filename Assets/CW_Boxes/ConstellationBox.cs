using System;
using System.Collections;
using Oculus.Interaction;
using TMPro;
using UnityEngine;

/// <summary>
/// One constellation box (a project variant of the ISDK sample Box). The lid
/// keeps the sample's own interaction: a one-hand grab that rotates it about
/// the hinge. Once the player lifts it past Open Threshold the box counts as
/// opened: the lid stops being grabbable and eases to its open rest angle
/// (so a half-open lid can't fall back shut), and Opened fires.
///
/// Unavailable boxes ("Coming soon") use a dimmed material, show the status
/// label, and have their lid interaction turned off, so they never open.
///
/// Close() puts the lid back (shut, grabbable if available) for the next visit.
/// </summary>
[DisallowMultipleComponent]
public sealed class ConstellationBox : MonoBehaviour
{
    [Header("Box")]
    [SerializeField]
    private string displayName = "Starter Pack";

    [SerializeField]
    private bool available;

    /// <summary>Builder setup (edit mode).</summary>
    public void Configure(string boxName, bool isAvailable)
    {
        displayName = boxName;
        available = isAvailable;
    }

    [Header("Lid (from the sample Box)")]
    [Tooltip("The lid transform the sample's OneGrabRotateTransformer turns (BoxLid).")]
    [SerializeField]
    private Transform lid;

    [Tooltip("The lid's interactables (GrabInteractable, HandGrabInteractable objects): off when unavailable or once open.")]
    [SerializeField]
    private GameObject[] lidInteractables = new GameObject[0];

    [Tooltip("Lid hover/select tint (InteractableColorVisual + MaterialPropertyBlockEditor): off when unavailable, so the dimmed material shows.")]
    [SerializeField]
    private Behaviour[] lidTintComponents = new Behaviour[0];

    [Tooltip("Degrees the lid must be lifted (about its local X) to count as opened.")]
    [SerializeField]
    [Range(5f, 90f)]
    private float openThresholdDegrees = 30f;

    [Tooltip("Lid rest angle once opened (degrees about its local X).")]
    [SerializeField]
    [Range(30f, 170f)]
    private float openedAngle = 105f;

    [Tooltip("+1 or -1: which way about the lid's local X opens it (set by the builder from the sample's constraints).")]
    [SerializeField]
    private float openDirection = 1f;

    [Tooltip("Seconds the lid takes to ease to its open rest angle.")]
    [SerializeField]
    [Min(0.01f)]
    private float settleSeconds = 0.35f;

    [Header("Look")]
    [Tooltip("Box base and lid renderers.")]
    [SerializeField]
    private Renderer[] renderers = new Renderer[0];

    [SerializeField]
    private Material availableMaterial;

    [SerializeField]
    private Material unavailableMaterial;

    [SerializeField]
    private TMP_Text nameLabel;

    [Tooltip("Name colour on an available box (ink on the light box).")]
    [SerializeField]
    private Color nameColor = new Color(0.10f, 0.10f, 0.14f, 1f);

    [Tooltip("Name colour on an unavailable (dimmed) box.")]
    [SerializeField]
    private Color unavailableNameColor = new Color(0.80f, 0.80f, 0.84f, 1f);

    [Tooltip("Shows Coming Soon Text on unavailable boxes; hidden on available ones.")]
    [SerializeField]
    private TMP_Text statusLabel;

    [SerializeField]
    private string comingSoonText = "Coming soon";

    [Header("Contents")]
    [Tooltip("Shown only while the box is open (e.g. the Starter Pack's sculptures).")]
    [SerializeField]
    private GameObject contents;

    [SerializeField]
    private bool logEvents = true;

    public event Action<ConstellationBox> Opened;

    public string DisplayName => displayName;
    public bool IsAvailable => available;
    public bool IsOpen { get; private set; }

    /// <summary>The lid, for guidance (point at what to grab).</summary>
    public Transform Lid => lid;

    private Coroutine settle;

    private void Awake()
    {
        ApplyLook();
        Close();
    }

    private void Update()
    {
        if (!available || IsOpen || lid == null)
        {
            return;
        }

        if (LidAngle() >= openThresholdDegrees)
        {
            Open(false);
        }
    }

    // Signed lift about the lid's local X, positive = opening.
    private float LidAngle()
    {
        lid.localRotation.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f)
        {
            angle -= 360f;
        }

        return angle * Mathf.Sign(Vector3.Dot(axis, Vector3.right)) * Mathf.Sign(openDirection);
    }

    /// <summary>Opens the box (immediate: lid snaps open, e.g. returning straight into the pack).</summary>
    public void Open(bool immediate)
    {
        if (!available || IsOpen)
        {
            return;
        }

        IsOpen = true;
        SetLidInteractable(false);
        if (contents != null)
        {
            contents.SetActive(true);
        }

        Quaternion openRotation = Quaternion.AngleAxis(openedAngle * Mathf.Sign(openDirection), Vector3.right);
        if (settle != null)
        {
            StopCoroutine(settle);
            settle = null;
        }

        if (immediate || !isActiveAndEnabled)
        {
            if (lid != null)
            {
                lid.localRotation = openRotation;
            }
        }
        else
        {
            settle = StartCoroutine(SettleLid(openRotation));
        }

        Log("opened.");
        Opened?.Invoke(this);
    }

    /// <summary>Lid shut, contents hidden, grabbable again if available.</summary>
    public void Close()
    {
        if (settle != null)
        {
            StopCoroutine(settle);
            settle = null;
        }

        IsOpen = false;
        if (lid != null)
        {
            lid.localRotation = Quaternion.identity;
            ResetRotateTransformer();
        }

        if (contents != null)
        {
            contents.SetActive(false);
        }

        SetLidInteractable(available);
    }

    private IEnumerator SettleLid(Quaternion target)
    {
        // Let ISDK finish releasing the grab before taking over the lid.
        yield return null;
        Quaternion start = lid.localRotation;
        float elapsed = 0f;
        while (elapsed < settleSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / settleSeconds);
            t = t * t * (3f - 2f * t);
            lid.localRotation = Quaternion.Slerp(start, target, t);
            yield return null;
        }

        lid.localRotation = target;
        settle = null;
    }

    // The sample's OneGrabRotateTransformer remembers the lid angle between grabs (_constrainedRelativeAngle)
    // and starts the next grab from it. After we snap the lid shut that memory is stale (still "open"), so the
    // max-angle clamp stops the lid lifting at all. Zero it whenever we reset the lid.
    private static readonly System.Reflection.FieldInfo[] RotateAngleFields =
    {
        typeof(Oculus.Interaction.OneGrabRotateTransformer).GetField("_relativeAngle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance),
        typeof(Oculus.Interaction.OneGrabRotateTransformer).GetField("_constrainedRelativeAngle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance),
    };

    private void ResetRotateTransformer()
    {
        foreach (var rotate in lid.GetComponentsInChildren<Oculus.Interaction.OneGrabRotateTransformer>(true))
        {
            foreach (var field in RotateAngleFields)
            {
                if (field != null)
                {
                    field.SetValue(rotate, 0f);
                }
                else
                {
                    Debug.LogWarning("ConstellationBox: OneGrabRotateTransformer angle field not found; lid may not reopen after a reset.", this);
                }
            }
        }
    }

    private void SetLidInteractable(bool on)
    {
        foreach (GameObject interactable in lidInteractables)
        {
            if (interactable != null && interactable.activeSelf != on)
            {
                interactable.SetActive(on);
            }
        }
    }

    /// <summary>Materials, tint and labels for the Available setting (also run by the builder in edit mode).</summary>
    [ContextMenu("Apply Look")]
    public void ApplyLook()
    {
        Material material = available ? availableMaterial : unavailableMaterial;
        if (material != null)
        {
            foreach (Renderer r in renderers)
            {
                if (r != null && r.sharedMaterial != material)
                {
                    r.sharedMaterial = material;
                }
            }
        }

        foreach (Behaviour tint in lidTintComponents)
        {
            if (tint != null)
            {
                tint.enabled = available;
            }
        }

        if (!available)
        {
            foreach (Renderer r in renderers)
            {
                if (r != null)
                {
                    r.SetPropertyBlock(null);
                }
            }
        }

        if (nameLabel != null)
        {
            nameLabel.text = displayName;
            nameLabel.color = available ? nameColor : unavailableNameColor;
        }

        if (statusLabel != null)
        {
            statusLabel.text = comingSoonText;
            statusLabel.gameObject.SetActive(!available);
        }
    }

    private void Log(string message)
    {
        if (logEvents)
        {
            Debug.Log($"ConstellationBox '{displayName}': {message}", this);
        }
    }
}
