using UnityEngine;
using UnityEngine.UI;

public class UpdateSliderOnPinchAndTwist : MonoBehaviour
{
    [Header("References")]
    public PinchAndTwistCustomEvent pinchTwist; // your custom event script
    public Slider slider;                        // UI slider to drive
    public Slider debugSlider;                        // UI slider to drive

    [Header("Tuning")]
    [Tooltip("How much to change the slider per unit of twist value delta")]
    public float sensitivity = 1.0f;

    [Tooltip("Ignore tiny changes")]
    public float deadzone = 0.0025f;

    float _lastValue;
    bool _active;

    void Reset()
    {
        slider = GetComponentInChildren<Slider>();
    }

    void OnEnable()
    {
        if (!pinchTwist) return;
        pinchTwist.OnStartPinchAndTwist.AddListener(OnStart);
        pinchTwist.OnEndPinchAndTwist.AddListener(OnEnd);
        pinchTwist.OnPinchAndTwist.AddListener(OnTwistValue);
    }

    void OnDisable()
    {
        if (!pinchTwist) return;
        pinchTwist.OnStartPinchAndTwist.RemoveListener(OnStart);
        pinchTwist.OnEndPinchAndTwist.RemoveListener(OnEnd);
        pinchTwist.OnPinchAndTwist.RemoveListener(OnTwistValue);
    }

    void OnStart()
    {
        _active = true;
        _lastValue = 0f; // your custom event restarts at 0 on start
    }

    void OnEnd()
    {
        _active = false;
    }

    void OnTwistValue(float currentValue)
    {
        if(debugSlider)
            debugSlider.value = currentValue;

        if (!_active || slider == null) return;

        float delta = currentValue - _lastValue;
        _lastValue = currentValue;

        if (Mathf.Abs(delta) < deadzone) return;

        float newV = Mathf.Clamp01(slider.value + delta * sensitivity);
        slider.value = newV;
    }
}
