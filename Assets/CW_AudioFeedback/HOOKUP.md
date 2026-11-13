# Audio Feedback: hookup

`CW.AudioFeedback.AudioFeedback` is one scene component with a cue table. Drop
`Assets/CW_AudioFeedback/AudioFeedback.prefab` into the scene (once), then call it
from gameplay code:

```csharp
using CW.AudioFeedback;

AudioFeedback.Fire(FeedbackCue.Solve);                  // null-safe, 2D
AudioFeedback.FireAt(FeedbackCue.Select, worldPoint);   // 3D if the entry is spatial
// or by reference: [SerializeField] AudioFeedback audioFeedback; audioFeedback.Play(...)
```

From an inspector UnityEvent, use `AudioFeedback.PlayByName(string)` (e.g. `Solve`).
Enums can't be picked in UnityEvents, and names don't depend on enum order.
`PlayByIndex(int)` also works.

The assembly `CW.AudioFeedback` is auto-referenced, so Assembly-CSharp sees it.
It references no gameplay code.

## Event -> cue -> call site

| Game event | Cue | Where the call goes | Call |
|---|---|---|---|
| Pinch starts in the sphere | Select | `VolumePinchRotation.PinchStarted` (C# event) | `FireAt(Select, transform.position)`. The pinch point isn't public: use the sphere's transform, or expose the point. |
| First movement picks an axis | AxisLock | `VolumePinchRotation.AxisLocked` | `FireAt(AxisLock, sameSpot)` |
| Pinch ends | Release | `VolumePinchRotation.PinchEnded` | `FireAt(Release, sameSpot)` |
| Puzzle intro starts | IntroStart | `ConstellationIntro.PlayIntro()` has no start event: call at the top of `PlayIntro()` (or add a `Started` event next to `Finished`) | `Fire(IntroStart)` |
| Puzzle intro finishes | (none) | `ConstellationIntro.Finished` | No cue. The whoosh covers the intro. |
| Near-solve entered | NearSolve | `ConstellationSolveController.onNearSolveEntered` (UnityEvent) | `PlayByName("NearSolve")` |
| Near-solve exited | (none) | `onNearSolveExited` | No cue. A falling sound on exit feels like a penalty. |
| Solved | Solve | `ConstellationSolveController.onSolved` (UnityEvent) | `PlayByName("Solve")`, **and remove the existing `AudioSource.PlayOneShot(holy-spell-cast)` listener on onSolved** in the scene, or it plays twice |
| Stamp VFX plays | Stamp | `SkyStamp3DVFX.Play()` has no event: call inside `Play()` or at its caller | `Fire(Stamp)` |
| Signing complete | Signed | `ConstellationSigning.Signed` (C# event) / `onSigned` | `Fire(Signed)` |
| Menu poke button pressed | ButtonPress | `MomentaryToggleButton.Pressed` / `OnPressed` | `FireAt(ButtonPress, button.transform.position)` |
| Title letter grabbed | Select | title-letter grab handler (not in the repo yet) | `FireAt(Select, letterPos)` |
| Title letter placed in sphere | Place | title-letter place/snap handler | `FireAt(Place, letterPos)` |
| Title Start pressed | TitleStart | Title Start handler | `Fire(TitleStart)`. If Start is a `MomentaryToggleButton`, it also gets ButtonPress. That layering is fine, but drop one if it sounds busy. |

Existing sounds to leave alone or retire: `ConstellationImpactAudio` (table
impact) and `ScreenPower` on/off stay as they are. `StellarForgeTrigger`'s
`ForgeTriggerAudio` uses the same `_SHORTER` whoosh as IntroStart. That is fine
while the forge is unused. If both fire together, swap IntroStart to the
full-length `universfield-magic-teleport-whoosh-352764.wav`.

## Debounce rules

- **Fire once per state change, never per frame.** Select fires on
  `PinchStarted` only, not on rotation ticks or `Update` while pinching.
  AxisLock fires once per pinch, when the axis is chosen, not on every snap
  or angle step.
- Hook the **edge** events (`onNearSolveEntered`, `Signed`, `Pressed`). Never
  poll `IsNearSolve` or `IsComplete` and fire while it's true.
- Each cue's `cooldownSeconds` is a safety net. A repeat inside the window is
  dropped silently. It does not fix a call site that fires every frame.
  Defaults: Select/Release 0.12 s, Place 0.2 s, AxisLock 0.25 s, Stamp 0.5 s,
  ButtonPress 0.08 s, NearSolve/Signed/IntroStart/TitleStart 1 s, Solve 2 s.
- NearSolve's 1 s cooldown stops chatter when the player hovers on the
  tolerance edge. If it still chatters, raise the cooldown (2-3 s) rather than
  adding hysteresis in gameplay.
- `minInterval` is the non-repeat window: for cues with several variants
  (Select, Release), a play within 2 s of the last one avoids the last variant.
  Pitch jitter adds more variety.

## Volume defaults

`masterVolume` is 1. Entry volumes are relative, so small interaction ticks sit
under the big moments:

| Cue | Vol | 2D/3D | Notes |
|---|---|---|---|
| Select | 0.60 | 3D | 5 variants, +/-0.04 pitch |
| Release | 0.50 | 3D | 5 variants, +/-0.04 pitch |
| Place | 0.55 | 3D | |
| AxisLock | 0.45 | 3D | short detent |
| NearSolve | 0.35 | 2D | TEMP clip |
| Solve | 0.80 | 2D | the payoff, loudest |
| Stamp | 0.60 | 2D | |
| Signed | 0.60 | 2D | TEMP clip |
| ButtonPress | 0.70 | 3D | clip is only 20 ms, so it needs the level |
| IntroStart | 0.50 | 2D | 2.7 s whoosh under a ~3.2 s intro |
| TitleStart | 0.60 | 2D | TEMP clip |

3D voices use linear rolloff with full volume within 0.3 m and silence at 5 m
(tabletop scale). `Play()` is always 2D. Only `PlayAt()` uses 3D, and only
for entries marked spatial. 6 pooled voices; when all are busy, the oldest is
reused.

`SetMuted(true)` stops the voices and drops new plays (e.g. an options toggle).

## Preview

`Tools > Celestial Workshop > Build Audio Feedback Preview` (re)builds the
prefab and `AudioFeedbackPreview.unity`. It does this additively, then saves
and closes, so the open scene is untouched. Open the preview scene and press
Play. Keys 1..9, 0 and - fire Select..TitleStart, and M mutes. Buttons, a
master slider and a mute toggle are drawn on screen. Spatial cues play at an
emitter 1 m ahead and to the right of the camera. Run it again after you
change clips; it overwrites both assets in place.
