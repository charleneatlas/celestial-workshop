using UnityEngine;

public class PinchSetColor : MonoBehaviour
{
    public OVRHand hand;
    public Renderer rend;

    public Color defaultColor;
    public Color pinchColor;

    void Update()
    {
        //Get Index Pinch Strength
        float pinchValue = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);

        rend.material.color = Color.Lerp(defaultColor, pinchColor, pinchValue);
    }
}
