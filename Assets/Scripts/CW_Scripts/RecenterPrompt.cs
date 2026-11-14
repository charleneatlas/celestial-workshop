using TMPro;
using UnityEngine;

/// <summary>
/// The in-view prompt for the Recenter countdown: "Sit back and look ahead"
/// with the seconds left, floating a little below the line of sight and
/// easing after the head so it stays readable. Shown only while
/// <see cref="WorkshopPlacement.IsChoosing"/>. The text is built at runtime.
/// </summary>
public sealed class RecenterPrompt : MonoBehaviour
{
    [Tooltip("Found automatically if empty.")]
    [SerializeField]
    private WorkshopPlacement placement;

    [Tooltip("Player camera (CenterEyeAnchor). Defaults to the main camera.")]
    [SerializeField]
    private Camera viewer;

    [Tooltip("Optional; TMP's default font when empty.")]
    [SerializeField]
    private TMP_FontAsset font;

    [SerializeField]
    private string message = "Sit back and look ahead";

    [Tooltip("Distance in front of the eyes (m).")]
    [SerializeField]
    private float distance = 0.6f;

    [Tooltip("How far below the line of sight (m).")]
    [SerializeField]
    private float below = 0.04f;

    [Tooltip("Message size (TMP world font size; about 0.1 m per 1).")]
    [SerializeField]
    private float messageSize = 0.22f;

    [Tooltip("Countdown number size.")]
    [SerializeField]
    private float numberSize = 0.5f;

    [SerializeField]
    private Color color = new Color(1f, 0.93f, 0.78f, 1f);

    [Tooltip("How quickly the prompt eases after the head (higher = snappier).")]
    [SerializeField]
    private float followSharpness = 8f;

    private TextMeshPro text;
    private int shownSeconds = -1;

    private void OnEnable()
    {
        if (placement == null)
        {
            placement = FindFirstObjectByType<WorkshopPlacement>();
        }

        if (placement != null)
        {
            placement.ChoosingStarted += OnStarted;
            placement.ChoosingEnded += OnEnded;
        }
    }

    private void OnDisable()
    {
        if (placement != null)
        {
            placement.ChoosingStarted -= OnStarted;
            placement.ChoosingEnded -= OnEnded;
        }

        if (text != null)
        {
            text.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (text != null)
        {
            Destroy(text.gameObject);
        }
    }

    private void OnStarted(float seconds)
    {
        EnsureText();
        shownSeconds = -1;
        text.gameObject.SetActive(true);
        MoveInFront(1f);
        Refresh();
    }

    private void OnEnded()
    {
        if (text != null)
        {
            text.gameObject.SetActive(false);
        }
    }

    private void LateUpdate()
    {
        if (text == null || !text.gameObject.activeSelf)
        {
            return;
        }

        MoveInFront(1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime));
        Refresh();
    }

    private void Refresh()
    {
        int seconds = Mathf.Max(1, Mathf.CeilToInt(placement != null ? placement.ChoosingSecondsLeft : 0f));
        if (seconds == shownSeconds)
        {
            return;
        }

        shownSeconds = seconds;
        text.text = $"{message}\n<size={numberSize / messageSize * 100f:0}%>{seconds}</size>";
    }

    private void MoveInFront(float k)
    {
        Camera cam = viewer != null ? viewer : Camera.main;
        if (cam == null)
        {
            return;
        }

        Transform eye = cam.transform;
        Vector3 forward = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
        forward = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
        Vector3 target = eye.position + forward * distance + Vector3.down * below;
        Quaternion rotation = Quaternion.LookRotation(target - eye.position, Vector3.up);

        Transform t = text.transform;
        t.SetPositionAndRotation(Vector3.Lerp(t.position, target, k), Quaternion.Slerp(t.rotation, rotation, k));
    }

    private void EnsureText()
    {
        if (text != null)
        {
            return;
        }

        // Unparented, so no parent scale affects its size.
        var go = new GameObject("Recenter Prompt");
        text = go.AddComponent<TextMeshPro>();
        if (font != null)
        {
            text.font = font;
        }

        text.fontSize = messageSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = new Vector2(0.5f, 0.2f);
        go.SetActive(false);
    }
}
