using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CW.Guidance
{
    /// <summary>
    /// Desktop test driver for GuidancePreview.unity.
    /// Space: complete the active step. N: show the next step (wraps, and
    /// forgets completions when it wraps). Left / Right arrows: turn the
    /// camera 15 degrees, to see the label stay inside the gaze cone.
    /// </summary>
    public class GuidancePreviewDriver : MonoBehaviour
    {
        public GuidanceDirector director;
        [Tooltip("Turned by the arrow keys. Empty uses the director's viewer.")]
        public Transform cameraToTurn;
        public float turnStep = 15f;
        public bool showFirstOnStart = true;

        private void Start()
        {
            if (director != null && showFirstOnStart)
            {
                director.RestartSequence();
                director.ShowNext();
            }
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || director == null)
            {
                return;
            }

            if (keyboard.spaceKey.wasPressedThisFrame && director.ActiveId != null)
            {
                director.Complete(director.ActiveId);
            }

            if (keyboard.nKey.wasPressedThisFrame && !director.ShowNext())
            {
                director.ResetMemory();
                director.RestartSequence();
                director.ShowNext();
            }

            Transform turn = cameraToTurn != null ? cameraToTurn : director.Viewer;
            if (turn != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame) turn.Rotate(0f, -turnStep, 0f, Space.World);
                if (keyboard.rightArrowKey.wasPressedThisFrame) turn.Rotate(0f, turnStep, 0f, Space.World);
            }
#endif
        }
    }
}
