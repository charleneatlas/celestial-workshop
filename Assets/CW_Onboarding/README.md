# Workshop Noticeboard (onboarding)

The handcrafted noticeboard shown at launch. Players read the three workshop rules,
put their controllers down, and trace their name to enter. After the welcome the
board fades and GameFlow opens the title (`Then Open` on Workshop Onboarding; Boxes is the
other choice). The sky globe (`Hide While Showing`) stays hidden while the board is up.
The board waits for head tracking before placing itself, and moves back in front of the
player if tracking arrives late or the head moves 0.3 m before they start tracing.

## Pieces

| What | Where |
|---|---|
| Prefab (the whole board) | `WorkshopNoticeboard.prefab` (in the scene as **Workshop Noticeboard**) |
| Flow, placement, fades, agreement line, welcome | `WorkshopOnboarding.cs` (prefab root) |
| Account name, timeout, fallback ("Sky Crafter") | `MetaDisplayName.cs` (prefab root) |
| Trace-to-reveal signature | `SignatureReveal.cs` (Board/Content/Signature Area/Signature) |
| Star trail (reusable) | `StarTrail.cs` (Board/Star Trail) |
| Ghost hand demo | `GhostHandDemo.cs` (Board/Ghost Demo) |
| Look | `Shaders/` (NoticeboardSurface, StarGlow, GhostHand), `Materials/`, `Art/` |
| Rebuild | **Tools > Celestial Workshop > Build Workshop Noticeboard** (`Editor/NoticeboardBuilder.cs`) |
| Scene wiring | **Tools > Celestial Workshop > Hook Up Workshop Noticeboard** |

GameFlow has an `Onboarding` stage. While the board is up, the title shelf, the
boxes and the puzzle are hidden and can't be used.

## Testing

- **Play** runs it every launch (`Run On Launch`). In the Editor without hands, right-click
  **Workshop Onboarding** > *Debug: Auto-Sign* (or *Skip Onboarding*).
- **Replay:** *Replay Onboarding* in the same context menu (works from any stage).
- **Self-test without a headset:** set `SessionState` float `CW_Onboarding_AutoSign` to N
  seconds (e.g. via the Unity CLI `eval`), enter Play, and read the log. Erase it afterwards.
- **Renders:** `NoticeboardCapture.Capture(outDir, tag, "hero,front,angle,sign,player,hand", width, reveal, fx)`
  renders the prefab without touching the open scene (fx stages the trail and the ghost hand).

## Swapping art

The builder regenerates `Art/Generated/*.png` on each rebuild. To keep a hand-made
replacement, either add its name to `KeepTextures` in `NoticeboardBuilder.cs`, or point
the material at a texture outside `Art/Generated`.

- **Proper Motion logo:** replace `Art/ProperMotion_Logo.png` (white on transparent; the
  material `NB_ProperMotionLogo` tints it ink-brown). Keep the proportions and adjust the quad
  size in `NoticeboardBuilder` (`Maker Mark`) if the aspect ratio changes.
- **Quest 3 controller illustration:** `Art/Generated/Controllers.png` (an ink render of the
  Touch Plus meshes from the Core SDK). Swap the texture on `NB_Controllers`, or tweak
  `ControllerViewEuler` / `ControllerTilt` in `NoticeboardArt.cs` and rebuild.
- **Other illustrations:** W/M = `WM`, sketch + globe = `SketchGlobe` (the pointing hand on the sketch is the ISDK hand posed in `PointingHandParts`; seen from the back so the palm faces the page; tune `PointTilt`, `PointSize`, `PointCurl`, `PointViewEuler` in `NoticeboardArt.cs`), raised hands = `Hands`
  (an ink render of the ISDK OpenXR hand), ornaments = `TitleStars`, `Ribbon`, `Divider`, `Flourish`, ...
- **Fonts:** constants `FontTitle` (Meddon: title, names), `FontHand` (Caveat: headings, tagline,
  hints) and `FontBody` (Architects Daughter: body) in `NoticeboardBuilder.cs`; sizes are cap heights
  in metres in the `Text(...)` calls. Rebuild after changing.
- **Signature styling:** `Materials/Signature_Gold.mat` (gold face + underlay glow; recreated by the
  builder in `Materials()`); the reveal look is on the **Signature** object (cap height, line width,
  wipe softness, completion threshold, auto-finish time); the trail on **Star Trail** (colours,
  intensity, lifetime, sizes, flourish).
- **Copy:** constants at the top of `NoticeboardBuilder.cs`; the agreement name and welcome text
  are set at runtime (`Welcome Format` on Workshop Onboarding).

## Account name

`MetaDisplayName` asks the Horizon Platform SDK (already installed with the All-in-One SDK) for the
logged-in user's display name, then their Oculus ID, with a 4 s timeout. It needs:

1. An App ID in **Meta > Platform > Edit Settings** (creates `Resources/OculusPlatformSettings.asset`).
   The project doesn't have one yet, so today the board always uses the fallback.
2. In the developer dashboard, the **User ID** and **User Profile** data approved through the
   Data Use Checkup.
3. The test account entitled to the app (installed from a release channel it belongs to).

No Android permission or SDK upgrade is needed. In the Editor the lookup is skipped unless
`Try In Editor` is on.
