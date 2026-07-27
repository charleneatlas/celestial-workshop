using TMPro;
using UnityEngine;

/// <summary>
/// Displays whether Fast Motion Mode is enabled for this app.
/// FMM is a build-time setting (OVRManager > Quest Features >
/// Hand Tracking Frequency: High or Max), so this just reports it:
/// - On device: reads the Android manifest value the app was built with.
/// - In the Editor: reads the current OVRProjectConfig setting.
/// </summary>
public class FastMotionModeStatus : MonoBehaviour
{
    public TMP_Text statusText;

    void Start()
    {
        string frequency = GetHandTrackingFrequency();
        bool fmmOn = frequency == "HIGH" || frequency == "MAX";

        if (statusText != null)
        {
            statusText.text = fmmOn
                ? "Fast Motion Mode is <b><color=#8BC34A>ON</color></b>\n<size=70%>(Frequency: " + frequency + ")</size>"
                : "Fast Motion Mode is <b><color=#FF7043>OFF</color></b>\n<size=70%>(Frequency: LOW)</size>";
        }
    }

    string GetHandTrackingFrequency()
    {
#if UNITY_EDITOR
        return OVRProjectConfig.CachedProjectConfig.handTrackingFrequency.ToString();
#elif UNITY_ANDROID
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
            using (var appInfo = packageManager.Call<AndroidJavaObject>("getApplicationInfo",
                       activity.Call<string>("getPackageName"), 128 /* GET_META_DATA */))
            using (var metaData = appInfo.Get<AndroidJavaObject>("metaData"))
            {
                string value = metaData != null
                    ? metaData.Call<string>("getString", "com.oculus.handtracking.frequency")
                    : null;
                return string.IsNullOrEmpty(value) ? "LOW" : value;
            }
        }
        catch
        {
            return "LOW";
        }
#else
        return "LOW";
#endif
    }
}