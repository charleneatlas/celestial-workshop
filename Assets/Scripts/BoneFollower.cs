using UnityEngine;
using System.Linq;

public class BoneFollower : MonoBehaviour
{
    [Header("Hand References")]
    public OVRSkeleton skeleton;
    public OVRHand hand;

    [Header("Bone Settings")]
    public OVRSkeleton.BoneId boneId = OVRSkeleton.BoneId.Hand_IndexTip;

    [Header("Options")]
    public bool followPosition = true;
    public bool followRotation = true;
    public Vector3 positionOffset;
    public Vector3 rotationOffset;

    private Transform targetBone;


    void Update()
    {
        if (!IsHandReady())
            return;

        if (targetBone == null)
            TryInitializeBone();

        if (targetBone == null)
            return;

        if (followPosition)
        {
            transform.position = targetBone.position + positionOffset;
        }

        if (followRotation)
        {
            transform.rotation = targetBone.rotation * Quaternion.Euler(rotationOffset);
        }
    }

    bool IsHandReady()
    {
        return hand != null &&
               skeleton.IsDataValid &&
               skeleton.IsDataHighConfidence;
    }

    void TryInitializeBone()
    {
        var bone = skeleton.Bones.FirstOrDefault(b => b.Id == boneId);

        if (bone != null)
        {
            targetBone = bone.Transform;
        }
    }
}
