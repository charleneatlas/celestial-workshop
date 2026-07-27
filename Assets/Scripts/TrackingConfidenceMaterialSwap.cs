using Oculus.Interaction.Input;
using UnityEngine;

public class TrackingConfidenceMaterialSwap : MonoBehaviour
{
    public OVRHand hand;
    public HandRef handi;
    public Renderer handRenderer;

    public Material highConfidenceMat;
    public Material lowConfidenceMat;

    // Update is called once per frame
    void Update()
    {
        if(hand.HandConfidence == OVRHand.TrackingConfidence.High)
        {
            handRenderer.material = highConfidenceMat;
        }
        else if(hand.HandConfidence == OVRHand.TrackingConfidence.Low)
        {
            handRenderer.material = lowConfidenceMat;
        }
    }
}
