using System;
using System.Collections;
using Oculus.Platform;
using UnityEngine;

/// <summary>
/// Looks up the signed-in Meta account's display name through the Horizon
/// Platform SDK (com.meta.xr.sdk.platform, already part of the All-in-One SDK),
/// with a timeout and a fallback name. Never blocks: it starts in Awake and
/// raises Resolved once, with the real name or the fallback.
///
/// Requirements for a real name (otherwise the fallback is used, by design):
///   - An App ID in Meta > Platform > Edit Settings (Resources/OculusPlatformSettings.asset).
///   - The app registered in the Meta developer dashboard, with the User ID and
///     User Profile data approved in its Data Use Checkup.
///   - The account entitled to the app (installed from a release channel the
///     tester belongs to). No Android permission or SDK upgrade is needed.
/// In the Editor it is skipped unless Try In Editor is on (that needs a Meta
/// Quest Link session or a standalone test-user token).
/// </summary>
[DisallowMultipleComponent]
public sealed class MetaDisplayName : MonoBehaviour
{
    public enum Status
    {
        Pending,
        Resolved,
        Fallback
    }

    [Tooltip("Shown when the account name can't be retrieved.")]
    [SerializeField]
    private string fallbackName = "Sky Crafter";

    [Tooltip("Off: always use the fallback (e.g. for demos).")]
    [SerializeField]
    private bool usePlatformSdk = true;

    [Tooltip("Give up and use the fallback after this long (s).")]
    [SerializeField]
    [Min(0.5f)]
    private float timeout = 4f;

    [Tooltip("Check the viewer is entitled before asking for the name (the Platform SDK expects it).")]
    [SerializeField]
    private bool checkEntitlement = true;

    [Tooltip("Use the Oculus ID (username) when the account has no display name.")]
    [SerializeField]
    private bool useUsernameIfNoDisplayName = true;

    [Tooltip("Longest name used as-is (characters); longer names are cut at a word.")]
    [SerializeField]
    [Min(4)]
    private int maxLength = 24;

    [Tooltip("Editor only: also try the Platform SDK in the Editor.")]
    [SerializeField]
    private bool tryInEditor = false;

    [SerializeField]
    private bool logEvents = true;

    public Status CurrentStatus { get; private set; } = Status.Pending;

    /// <summary>The account name, or the fallback once resolution has given up.</summary>
    public string Name => CurrentStatus == Status.Resolved ? resolvedName : fallbackName;

    public string FallbackName => fallbackName;
    public bool IsDone => CurrentStatus != Status.Pending;

    /// <summary>Raised once with the final name (real or fallback).</summary>
    public event Action<string> Resolved;

    private string resolvedName;
    private Coroutine timeoutRoutine;

    private void Start()
    {
        Begin();
    }

    private void Begin()
    {
        if (!usePlatformSdk)
        {
            Fail("Platform SDK lookup is off");
            return;
        }

#if UNITY_EDITOR
        if (!tryInEditor)
        {
            Fail("skipped in the Editor");
            return;
        }
#endif

        string appId;
        try
        {
            appId = PlatformSettings.MobileAppID;
        }
        catch (Exception e)
        {
            Fail("no platform settings (" + e.Message + ")");
            return;
        }

        if (string.IsNullOrEmpty(appId) && !Core.IsInitialized())
        {
            Fail("no App ID set (Meta > Platform > Edit Settings)");
            return;
        }

        timeoutRoutine = StartCoroutine(Timeout());
        try
        {
            if (Core.IsInitialized())
            {
                AfterInit();
            }
            else
            {
                Core.AsyncInitialize().OnComplete(message =>
                {
                    if (message == null || message.IsError)
                    {
                        Fail("platform initialisation failed: " + ErrorText(message));
                        return;
                    }

                    AfterInit();
                });
            }
        }
        catch (Exception e)
        {
            Fail("platform initialisation threw: " + e.Message);
        }
    }

    private void AfterInit()
    {
        if (IsDone)
        {
            return;
        }

        if (!checkEntitlement)
        {
            RequestUser();
            return;
        }

        try
        {
            Entitlements.GetIsViewerEntitled().OnComplete(message =>
            {
                if (message == null || message.IsError)
                {
                    Fail("not entitled: " + ErrorText(message));
                    return;
                }

                RequestUser();
            });
        }
        catch (Exception e)
        {
            Fail("entitlement check threw: " + e.Message);
        }
    }

    private void RequestUser()
    {
        if (IsDone)
        {
            return;
        }

        try
        {
            Users.GetLoggedInUser().OnComplete(message =>
            {
                if (message == null || message.IsError || message.Data == null)
                {
                    Fail("user lookup failed: " + ErrorText(message));
                    return;
                }

                string name = message.Data.DisplayName;
                if (string.IsNullOrWhiteSpace(name) && useUsernameIfNoDisplayName)
                {
                    name = message.Data.OculusID;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    Fail("the account has no display name");
                    return;
                }

                Succeed(Tidy(name));
            });
        }
        catch (Exception e)
        {
            Fail("user lookup threw: " + e.Message);
        }
    }

    private static string ErrorText(Oculus.Platform.Message message)
    {
        if (message == null)
        {
            return "no response";
        }

        var error = message.GetError();
        return error != null ? error.Message : "unknown error";
    }

    private string Tidy(string name)
    {
        name = name.Trim();
        if (name.Length <= maxLength)
        {
            return name;
        }

        int cut = name.LastIndexOf(' ', maxLength);
        return (cut > 3 ? name.Substring(0, cut) : name.Substring(0, maxLength)).Trim();
    }

    private IEnumerator Timeout()
    {
        yield return new WaitForSecondsRealtime(timeout);
        timeoutRoutine = null;
        if (!IsDone)
        {
            Fail($"timed out after {timeout:0.#} s");
        }
    }

    private void Succeed(string name)
    {
        if (IsDone)
        {
            return;
        }

        resolvedName = name;
        CurrentStatus = Status.Resolved;
        StopTimeout();
        if (logEvents)
        {
            Debug.Log("MetaDisplayName: using the account's display name.", this);
        }

        Resolved?.Invoke(Name);
    }

    private void Fail(string reason)
    {
        if (IsDone)
        {
            return;
        }

        CurrentStatus = Status.Fallback;
        StopTimeout();
        if (logEvents)
        {
            Debug.Log($"MetaDisplayName: using \"{fallbackName}\" ({reason}).", this);
        }

        Resolved?.Invoke(Name);
    }

    private void StopTimeout()
    {
        if (timeoutRoutine != null)
        {
            StopCoroutine(timeoutRoutine);
            timeoutRoutine = null;
        }
    }
}
