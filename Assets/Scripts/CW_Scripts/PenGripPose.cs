using System;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEngine;

/// <summary>
/// A recorded pen-holding grip for each hand: the finger joint rotations
/// (ISDK HandPose) and where the wrist sits in the pen's space. Captured from
/// HandGrabPoses made with the ISDK Hand Grab Pose Recorder (see
/// ConstellationSigning > "Capture Grip From Pen Pose Reference").
/// </summary>
[CreateAssetMenu(menuName = "Celestial Workshop/Pen Grip Pose", fileName = "PenGrip")]
public sealed class PenGripPose : ScriptableObject
{
    [Serializable]
    public sealed class Side
    {
        public bool valid;
        public HandPose handPose = new HandPose();

        [Tooltip("Wrist (hand root) position in the pen's local space (pen units).")]
        public Vector3 wristPosition;

        [Tooltip("Wrist (hand root) rotation in the pen's local space.")]
        public Quaternion wristRotation = Quaternion.identity;

        /// <summary>The pen's world pose for a given wrist world pose.</summary>
        public Pose PenFromWrist(Pose wrist)
        {
            Quaternion penRotation = wrist.rotation * Quaternion.Inverse(wristRotation);
            return new Pose(wrist.position - penRotation * wristPosition, penRotation);
        }
    }

    public Side left = new Side();
    public Side right = new Side();

    public Side For(Handedness handedness)
    {
        return handedness == Handedness.Left ? left : right;
    }
}
