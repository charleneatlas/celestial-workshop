using UnityEngine;
using UnityEngine.Events;

public class PinchAndTwistCustomEvent : MonoBehaviour
{
    [SerializeField]
    private PinchAndTwistEventSource pinchAndTwistEventSource;

    public bool debugKeyboardInput = true;

    public UnityEvent OnStartPinchAndTwist;
    public UnityEvent<float> OnPinchAndTwist;
    public UnityEvent OnEndPinchAndTwist;
    public float incrementKeyboardValue;
    private float keyboardTwistValue = 0;

    // Update is called once per frame
    void Update()
    {
        if (debugKeyboardInput)
        {
            //TWIST RIGHT
            if (Input.GetKeyDown(KeyCode.I))
            {
                keyboardTwistValue = 0;
                OnStartPinchAndTwist.Invoke();
            }
            if (Input.GetKey(KeyCode.I))
            {
                keyboardTwistValue += Time.deltaTime * incrementKeyboardValue;
                OnPinchAndTwist.Invoke(Mathf.Clamp(keyboardTwistValue, -1, 1));
            }
            if (Input.GetKeyUp(KeyCode.I))
            {
                OnEndPinchAndTwist.Invoke();
            }

            //TWIST LEFT
            if (Input.GetKeyDown(KeyCode.O))
            {
                keyboardTwistValue = 0;
                OnStartPinchAndTwist.Invoke();
            }
            if (Input.GetKey(KeyCode.O))
            {
                keyboardTwistValue += Time.deltaTime * incrementKeyboardValue * -1;  //TWIST LEFT
                OnPinchAndTwist.Invoke(Mathf.Clamp(keyboardTwistValue,-1,1));
            }
            if (Input.GetKeyUp(KeyCode.O))
            {
                OnEndPinchAndTwist.Invoke();
            }
        }
    }

    protected void OnEnable()
    {
        pinchAndTwistEventSource.OnStartPinchAndTwist.AddListener(OnStartPinchAndTwist.Invoke);
        pinchAndTwistEventSource.OnEndPinchAndTwist.AddListener(OnEndPinchAndTwist.Invoke);
        pinchAndTwistEventSource.OnPinchAndTwist.AddListener(OnPinchAndTwist.Invoke);
    }

    protected void OnDisable()
    {
        pinchAndTwistEventSource.OnStartPinchAndTwist.RemoveListener(OnStartPinchAndTwist.Invoke);
        pinchAndTwistEventSource.OnEndPinchAndTwist.RemoveListener(OnEndPinchAndTwist.Invoke);
        pinchAndTwistEventSource.OnPinchAndTwist.RemoveListener(OnPinchAndTwist.Invoke);
    }
}
