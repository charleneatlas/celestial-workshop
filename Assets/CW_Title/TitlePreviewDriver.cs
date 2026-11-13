using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Preview-scene helper (TitlePreview.unity): hands the stand-in drop zone to
/// the shelf, logs StartPressed / LetterPlaced, and in Play lets keys 1..9
/// simulate placing the 1st..9th letter in the zone. Not for production.
/// </summary>
public sealed class TitlePreviewDriver : MonoBehaviour
{
    public TitleShelf shelf;

    public SphereDropZone dropZone;

    private void OnEnable()
    {
        if (shelf != null)
        {
            shelf.StartPressed += HandleStart;
            shelf.LetterPlaced += HandleLetterPlaced;
        }
    }

    private void OnDisable()
    {
        if (shelf != null)
        {
            shelf.StartPressed -= HandleStart;
            shelf.LetterPlaced -= HandleLetterPlaced;
        }
    }

    private void Start()
    {
        if (shelf != null && dropZone != null)
        {
            shelf.SetDropZones(dropZone);
        }

        Debug.Log("TitlePreviewDriver: keys 1..9 place a letter, Enter presses Start.", this);
    }

#if ENABLE_INPUT_SYSTEM
    private static readonly Key[] DigitKeys =
    {
        Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5,
        Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
    };

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || shelf == null)
        {
            return;
        }

        for (int i = 0; i < DigitKeys.Length; i++)
        {
            if (keyboard[DigitKeys[i]].wasPressedThisFrame)
            {
                SimulatePlace(i);
            }
        }
    }
#endif

    public void SimulatePlace(int letterIndex)
    {
        if (shelf == null || letterIndex < 0 || letterIndex >= shelf.Letters.Count)
        {
            Debug.Log($"TitlePreviewDriver: no letter #{letterIndex + 1}.", this);
            return;
        }

        TitleLetterMini letter = shelf.Letters[letterIndex];
        if (letter != null)
        {
            letter.SimulatePlaceInZone();
        }
    }

    private void HandleStart()
    {
        Debug.Log("TitlePreviewDriver: StartPressed -> would start the Cassiopeia puzzle.", this);
    }

    private void HandleLetterPlaced(char letter, int seed)
    {
        Debug.Log(
            $"TitlePreviewDriver: LetterPlaced '{letter}' seed {seed} -> would load " +
            $"'{LetterConstellation.DisplayName(letter)}' with {LetterConstellation.Build(letter, seed).Count} stars.",
            this);
    }
}
