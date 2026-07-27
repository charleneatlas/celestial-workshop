using UnityEngine;

public class MicroGestureUINavigatorInput : MonoBehaviour
{
    public OVRMicrogestureEventSource microGesture;
    public bool debugKeyboardInput = true;
    private UINavigator uiNavigator;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        uiNavigator = GetComponent<UINavigator>();
    }

    // Update is called once per frame
    void Update()
    {
        if(debugKeyboardInput)
        {
            if(Input.GetKeyDown(KeyCode.U))
            {
                uiNavigator.MoveUp();
            }
            if (Input.GetKeyDown(KeyCode.H))
            {
                uiNavigator.MoveLeft();
            }
            if (Input.GetKeyDown(KeyCode.K))
            {
                uiNavigator.MoveRight();
            }
            if (Input.GetKeyDown(KeyCode.J))
            {
                uiNavigator.MoveDown();
            }
            if (Input.GetKeyDown(KeyCode.Space))
            {
                uiNavigator.ClickSelected();
            }
        }
    }

    protected virtual void OnEnable()
    {
        microGesture.WhenGestureRecognized += HandleGesture;
    }

    protected virtual void OnDisable()
    {
        microGesture.WhenGestureRecognized -= HandleGesture;
    }

    private void HandleGesture(OVRHand.MicrogestureType gesture)
    {
        if (gesture == OVRHand.MicrogestureType.SwipeRight)
        {
            uiNavigator.MoveRight();
        }
        else if (gesture == OVRHand.MicrogestureType.SwipeLeft)
        {
            uiNavigator.MoveLeft();
        }
        else if (gesture == OVRHand.MicrogestureType.SwipeForward)
        {
            uiNavigator.MoveUp();
        }
        else if (gesture == OVRHand.MicrogestureType.SwipeBackward)
        {
            uiNavigator.MoveDown();
        }
        else if (gesture == OVRHand.MicrogestureType.ThumbTap)
        {
            uiNavigator.ClickSelected();
        }
    }
}
