using UnityEngine;
using Oculus.Interaction;

public class BlueprintPokeRelay : MonoBehaviour
{
    [SerializeField]
    private PointableUnityEventWrapper eventWrapper;

    [SerializeField]
    private BlueprintController blueprintController;

    private bool isPoking;

    private void Awake()
    {
        if (eventWrapper == null)
        {
            eventWrapper =
                GetComponent<PointableUnityEventWrapper>();
        }
    }

    private void OnEnable()
    {
        if (eventWrapper == null)
        {
            return;
        }

        eventWrapper.WhenSelect.AddListener(HandleSelect);
        eventWrapper.WhenMove.AddListener(HandleMove);
        eventWrapper.WhenUnselect.AddListener(HandleUnselect);
        eventWrapper.WhenCancel.AddListener(HandleCancel);
    }

    private void OnDisable()
    {
        if (eventWrapper == null)
        {
            return;
        }

        eventWrapper.WhenSelect.RemoveListener(HandleSelect);
        eventWrapper.WhenMove.RemoveListener(HandleMove);
        eventWrapper.WhenUnselect.RemoveListener(HandleUnselect);
        eventWrapper.WhenCancel.RemoveListener(HandleCancel);

        isPoking = false;

        if (blueprintController != null)
        {
            blueprintController.EndPoke();
        }
    }

    private void HandleSelect(PointerEvent evt)
    {
        isPoking = true;

        if (blueprintController != null)
        {
            blueprintController.UpdatePokePosition(
                evt.Pose.position
            );
        }
    }

    private void HandleMove(PointerEvent evt)
    {
        if (!isPoking)
        {
            return;
        }

        if (blueprintController != null)
        {
            blueprintController.UpdatePokePosition(
                evt.Pose.position
            );
        }
    }

    private void HandleUnselect(PointerEvent evt)
    {
        EndPoke();
    }

    private void HandleCancel(PointerEvent evt)
    {
        EndPoke();
    }

    private void EndPoke()
    {
        if (!isPoking)
        {
            return;
        }

        isPoking = false;

        if (blueprintController != null)
        {
            blueprintController.EndPoke();
        }
    }
}