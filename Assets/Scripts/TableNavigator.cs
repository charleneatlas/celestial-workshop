using System.Collections.Generic;
using System.Text.RegularExpressions;
using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Lives on each table prefab root. Hooks the "Table Button Previous" / "Table Button Next"
/// poke buttons and teleports the player to the neighbouring table's Teleport Hot Spot.
/// Tables are ordered by the number in their name ("Table 3 - ..."), wrapping at both ends.
/// </summary>
public class TableNavigator : MonoBehaviour
{
    [Header("Auto-found by name when left empty")]
    public Transform teleportHotspot;
    public InteractableUnityEventWrapper nextButton;
    public InteractableUnityEventWrapper previousButton;

    private int tableIndex = int.MaxValue;

    void Awake()
    {
        if (teleportHotspot == null)
        {
            Transform hotspot = transform.Find("TeleportHotspot");
            if (hotspot != null)
                teleportHotspot = hotspot;
        }

        if (nextButton == null)
            nextButton = FindButton("Table Button Next");
        if (previousButton == null)
            previousButton = FindButton("Table Button Previous");

        Match match = Regex.Match(gameObject.name, @"Table\s*(\d+)");
        if (match.Success)
            tableIndex = int.Parse(match.Groups[1].Value);
    }

    void OnEnable()
    {
        if (nextButton != null)
            nextButton.WhenSelect.AddListener(GoToNextTable);
        if (previousButton != null)
            previousButton.WhenSelect.AddListener(GoToPreviousTable);
    }

    void OnDisable()
    {
        if (nextButton != null)
            nextButton.WhenSelect.RemoveListener(GoToNextTable);
        if (previousButton != null)
            previousButton.WhenSelect.RemoveListener(GoToPreviousTable);
    }

    public void GoToNextTable() => GoTo(1);
    public void GoToPreviousTable() => GoTo(-1);

    void GoTo(int step)
    {
        List<TableNavigator> tables = new List<TableNavigator>(
            FindObjectsByType<TableNavigator>(FindObjectsSortMode.None));
        tables.Sort((a, b) => a.tableIndex.CompareTo(b.tableIndex));

        int selfIndex = tables.IndexOf(this);
        if (selfIndex < 0 || tables.Count == 0)
            return;

        TableNavigator target = tables[(selfIndex + step + tables.Count) % tables.Count];
        if (target.teleportHotspot != null)
            TeleportPlayerTo(target.teleportHotspot);
    }

    InteractableUnityEventWrapper FindButton(string childName)
    {
        Transform child = transform.Find(childName);
        return child != null ? child.GetComponent<InteractableUnityEventWrapper>() : null;
    }

    /// <summary>
    /// Moves the OVRCameraRig so the player stands on the target transform, facing its forward direction.
    /// </summary>
    public static void TeleportPlayerTo(Transform target)
    {
        OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
        if (rig == null || target == null)
            return;

        CharacterController controller = rig.GetComponentInChildren<CharacterController>(true);
        bool controllerWasEnabled = controller != null && controller.enabled;
        if (controller != null)
            controller.enabled = false;

        Transform head = rig.centerEyeAnchor;

        // Rotate the rig around the player's head so they face the hotspot's forward direction.
        float yawDelta = Mathf.DeltaAngle(head.eulerAngles.y, target.eulerAngles.y);
        rig.transform.RotateAround(head.position, Vector3.up, yawDelta);

        // Move the rig so the player's feet land on the hotspot.
        Vector3 groundedHead = new Vector3(head.position.x, rig.transform.position.y, head.position.z);
        rig.transform.position += target.position - groundedHead;

        if (controller != null)
            controller.enabled = controllerWasEnabled;
    }
}
