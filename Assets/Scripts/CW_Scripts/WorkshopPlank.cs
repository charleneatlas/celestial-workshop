using UnityEngine;

/// <summary>
/// A wooden plank (or any backing graphic) behind a row of poke buttons.
/// Fits itself to the buttons that are currently active, plus padding, so a
/// hidden button (e.g. Choose Another Box after practice) doesn't leave an
/// empty end. Purely visual: put it on a non-raycast graphic that is an
/// earlier sibling of the row, so it draws behind and never takes a poke.
/// Built by Tools > Celestial Workshop > Skin Navigation Buttons.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public sealed class WorkshopPlank : MonoBehaviour
{
    [Tooltip("The row whose active children the plank covers.")]
    [SerializeField]
    private RectTransform row;

    [Tooltip("Extra plank around the buttons, in canvas units (x = left/right, y = top/bottom).")]
    [SerializeField]
    private Vector2 padding = new Vector2(10f, 8f);

    [Tooltip("Turned 90 degrees, so vertical wood grain runs along the plank.")]
    [SerializeField]
    private bool rotateGrain = true;

    private int lastSignature = int.MinValue;

    public void Configure(RectTransform newRow, Vector2 newPadding, bool newRotateGrain)
    {
        row = newRow;
        padding = newPadding;
        rotateGrain = newRotateGrain;
        lastSignature = int.MinValue;
        Fit();
    }

    /// <summary>Fits now (e.g. after forcing a layout rebuild in the Editor).</summary>
    public void Refit()
    {
        lastSignature = int.MinValue;
        Fit();
    }

    private void OnEnable()
    {
        lastSignature = int.MinValue;
    }

    private void LateUpdate()
    {
        if (row == null)
        {
            return;
        }

        // Refit only when which buttons are active, or where the layout put them, changes.
        // (The layout group runs after LateUpdate, so positions settle a frame later.)
        int signature = 17;
        for (int i = 0; i < row.childCount; i++)
        {
            // Indexed, not foreach: a Transform enumerator allocates every frame.
            var child = (RectTransform)row.GetChild(i);
            signature = signature * 31 + (child.gameObject.activeSelf ? 1 : 0);
            signature = signature * 31 + Mathf.RoundToInt(child.rect.width);
            signature = signature * 31 + Mathf.RoundToInt(child.anchoredPosition.x * 4f);
        }

        if (signature != lastSignature)
        {
            lastSignature = signature;
            Fit();
        }
    }

    private void Fit()
    {
        var self = (RectTransform)transform;
        var parent = self.parent as RectTransform;
        if (row == null || parent == null)
        {
            return;
        }

        bool any = false;
        Vector2 min = Vector2.zero;
        Vector2 max = Vector2.zero;
        var corners = new Vector3[4];
        foreach (RectTransform child in row)
        {
            if (!child.gameObject.activeSelf)
            {
                continue;
            }

            child.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector2 local = parent.InverseTransformPoint(corner);
                if (!any)
                {
                    min = max = local;
                    any = true;
                }
                else
                {
                    min = Vector2.Min(min, local);
                    max = Vector2.Max(max, local);
                }
            }
        }

        if (!any)
        {
            return;
        }

        Vector2 size = max - min + 2f * padding;
        Vector2 center = 0.5f * (min + max);

        self.anchorMin = self.anchorMax = new Vector2(0.5f, 0.5f);
        self.pivot = new Vector2(0.5f, 0.5f);
        self.anchoredPosition = center - parent.rect.center;
        self.localRotation = rotateGrain ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
        self.sizeDelta = rotateGrain ? new Vector2(size.y, size.x) : size;
    }
}
