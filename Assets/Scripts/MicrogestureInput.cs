using UnityEngine;

public class MicrogestureInput : MonoBehaviour
{
    public OVRHand ovrHand;
    private Vector2 moveInput = Vector2.zero;
    public StarterAssets.StarterAssetsInputs inputs;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        OVRHand.MicrogestureType microGesture = ovrHand.GetMicrogestureType();

        switch (microGesture)
        {
            case OVRHand.MicrogestureType.NoGesture:
                break;
            case OVRHand.MicrogestureType.SwipeLeft:
                moveInput = Vector2.left;
                break;
            case OVRHand.MicrogestureType.SwipeRight:
                moveInput = Vector2.right;
                break;
            case OVRHand.MicrogestureType.SwipeForward:
                moveInput = Vector2.up;
                break;
            case OVRHand.MicrogestureType.SwipeBackward:
                moveInput = Vector2.down;
                break;
            case OVRHand.MicrogestureType.ThumbTap:
                moveInput = Vector2.zero;
                break;
            case OVRHand.MicrogestureType.Invalid:
                break;
            default:
                break;
        }

        inputs.MoveInput(moveInput);
    }
}
