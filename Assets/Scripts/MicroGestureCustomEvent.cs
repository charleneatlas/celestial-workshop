using UnityEngine;
using UnityEngine.Events;

public class MicroGestureCustomEvent : MonoBehaviour
{
    [SerializeField]
    private OVRMicrogestureEventSource _ovrMicrogestureEventSource;

    public bool debugKeyboardInput = true;

    public UnityEvent OnTap;
    public UnityEvent OnLeft;
    public UnityEvent OnRight;
    public UnityEvent OnUp;
    public UnityEvent OnDown;
    public UnityEvent OnTwistRight;
    public UnityEvent OnTwistLeft;

    // Update is called once per frame
    void Update()
    {
        if (debugKeyboardInput)
        {
            if (Input.GetKeyDown(KeyCode.U))
            {
                OnUp.Invoke();
            }
            if (Input.GetKeyDown(KeyCode.H))
            {
                OnLeft.Invoke();
            }
            if (Input.GetKeyDown(KeyCode.J))
            {
                OnDown.Invoke();
            }
            if (Input.GetKeyDown(KeyCode.K))
            {
                OnRight.Invoke();
            }
            if (Input.GetKeyDown(KeyCode.Space))
            {
                OnTap.Invoke();
            }
        }
    }

    protected void OnEnable()
    {
        if (!_ovrMicrogestureEventSource)
            _ovrMicrogestureEventSource = FindFirstObjectByType<OVRMicrogestureEventSource>();
        _ovrMicrogestureEventSource.WhenGestureRecognized += HandleGesture;
    }

    protected void OnDisable()
    {
        _ovrMicrogestureEventSource.WhenGestureRecognized -= HandleGesture;
    }

    private void HandleGesture(OVRHand.MicrogestureType gesture)
    {
        if (gesture == OVRHand.MicrogestureType.SwipeRight)
        {
            OnRight.Invoke();
        }
        else if (gesture == OVRHand.MicrogestureType.SwipeLeft)
        {
            OnLeft.Invoke();
        }
        else if (gesture == OVRHand.MicrogestureType.SwipeForward)
        {
            OnUp.Invoke();
        }
        else if (gesture == OVRHand.MicrogestureType.SwipeBackward)
        {
            OnDown.Invoke();
        }
        else if (gesture == OVRHand.MicrogestureType.ThumbTap)
        {
            OnTap.Invoke();
        }
    }
}
