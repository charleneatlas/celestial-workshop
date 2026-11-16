using UnityEngine;

/// <summary>
/// Where things sit on the constellation-selection table, in the box
/// selection's local frame (+X player's right, +Y up, -Z toward the player;
/// the globe's centre is at about (0, 0.215, 0), radius 0.22, so keep the
/// table in front of z = -0.21).
///
/// Edit the values here and use the context menu "Apply Layout" (Build
/// Selection Table also applies it). Nothing runs at play time: the layout is
/// baked into the transforms, and the whole table follows the workshop's
/// placement (Recenter, Raise / Lower) because it lives under WORKSHOP.
/// </summary>
[DisallowMultipleComponent]
public sealed class SelectionTableLayout : MonoBehaviour
{
    [System.Serializable]
    public struct Slot
    {
        public Transform target;
        public Vector3 position;
        [Tooltip("Turn about Y, degrees (positive turns the box's front toward the player's left).")]
        public float yaw;
    }

    [Header("Boxes")]
    [Tooltip("Uniform scale of every box (was 0.6 on the old stand).")]
    [Range(0.3f, 0.8f)]
    public float boxScale = 0.45f;

    public Slot starterPack;
    public Slot northernSky;
    public Slot southernSky;
    public Slot zodiac;

    [Tooltip("The Starter Pack's sculptures, kept at this offset from the Starter Pack box.")]
    public Transform starterContents;

    public Vector3 starterContentsOffset = new Vector3(0f, -0.054f, 0f);

    [Header("Field Note (folded, on the table)")]
    public Transform foldedNote;

    [Tooltip("Where the middle of the sheet's front (bottom) edge rests on the table.")]
    public Vector3 notePosition = new Vector3(0f, 0.003f, -0.47f);

    [Tooltip("Height of the folded sheet (set by the builder), so its front edge rests on the table.")]
    public float noteHeight = 0.11f;

    [Tooltip("How far the far edge is propped up off the table, degrees.")]
    [Range(0f, 60f)]
    public float noteTilt = 22f;

    [Header("Field Note (open, for reading)")]
    [Tooltip("The open sheet (FieldNoteReader's board).")]
    public Transform readingView;

    public Vector3 readingPosition = new Vector3(0f, 0.46f, -0.25f);

    [Tooltip("Tilt so the sheet faces down toward the player's eyes, degrees.")]
    [Range(-40f, 20f)]
    public float readingTilt = -14f;

    [Header("Table")]
    public Transform table;

    public Vector3 tableCentre = new Vector3(0f, 0f, -0.355f);

    public Vector2 tableSize = new Vector2(0.60f, 0.30f);

    [Tooltip("The raised step under the rear boxes.")]
    public Transform riser;

    public Vector3 riserCentre = new Vector3(0f, 0f, -0.29f);

    [Tooltip("Width, height, depth of the rear step.")]
    public Vector3 riserSize = new Vector3(0.40f, 0.035f, 0.15f);

    [ContextMenu("Apply Layout")]
    public void Apply()
    {
        foreach (Slot s in new[] { starterPack, northernSky, southernSky, zodiac })
        {
            if (s.target == null) continue;
            s.target.localPosition = s.position;
            s.target.localRotation = Quaternion.Euler(0f, s.yaw, 0f);
            s.target.localScale = Vector3.one * boxScale;
        }

        if (starterContents != null && starterPack.target != null)
        {
            starterContents.localPosition = starterPack.position + starterContentsOffset;
            starterContents.localRotation = Quaternion.Euler(0f, starterPack.yaw, 0f);
        }

        if (foldedNote != null)
        {
            Quaternion r = Quaternion.Euler(90f - noteTilt, 0f, 0f);
            foldedNote.localRotation = r;
            foldedNote.localPosition = notePosition + r * new Vector3(0f, noteHeight * 0.5f, 0f);
        }

        if (readingView != null)
        {
            readingView.localPosition = readingPosition;
            readingView.localRotation = Quaternion.Euler(readingTilt, 0f, 0f);
        }

        if (table != null)
        {
            table.localPosition = tableCentre;
            table.localRotation = Quaternion.identity;
            table.localScale = new Vector3(tableSize.x, 1f, tableSize.y);
        }

        if (riser != null)
        {
            riser.localPosition = riserCentre + Vector3.up * riserSize.y * 0.5f;
            riser.localRotation = Quaternion.identity;
            riser.localScale = riserSize;
        }
    }
}
