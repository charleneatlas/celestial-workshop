using TMPro;
using UnityEngine;

/// <summary>
/// Draws the system pointer pose ray of a hand and reports whether it is currently valid.
/// OVRHand.PointerPose gives a filtered, system-consistent ray; always check
/// IsPointerPoseValid before using it.
/// </summary>
public class PointerPoseVisualizer : MonoBehaviour
{
    public Transform startPosition;
    public OVRHand hand;
    public LineRenderer line;
    public TMP_Text statusText;
    public float rayLength = 0.7f;

    void Update()
    {
        bool valid = hand != null && hand.IsPointerPoseValid;

        if (statusText != null)
        {
            statusText.text = valid
                ? "IsPointerPoseValid\n<b><color=#8BC34A>TRUE</color></b>"
                : "IsPointerPoseValid\n<b><color=#FF7043>FALSE</color></b>";
        }

        if (line != null)
        {
            line.enabled = valid;
            if (valid)
            {
                Transform pose = hand.PointerPose;
                Vector3 forward = startPosition.TransformDirection(pose.forward);
                line.SetPosition(0, startPosition.position);
                line.SetPosition(1, startPosition.position + forward * rayLength);
            }
        }
    }
}
