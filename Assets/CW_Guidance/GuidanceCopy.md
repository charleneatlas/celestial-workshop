# Guidance copy: proposed steps

One cue at a time. Each line is 8 words or fewer. "1st only" = `onlyOnce`
(skipped after the first puzzle / first time it is completed in the session).

| id | Text | Target | Shown when | Completed when | 1st only |
|---|---|---|---|---|---|
| `title-letter` | Pinch a letter, drop it in the globe | the nearest letter mini | Title screen appears | A letter is released inside the glass sphere (title practice drop; event still to be written) | yes |
| `title-start` | Or poke Start to begin | Start button | After `title-letter` completes, or 8 s after it was shown with no drop (use `autoHideSeconds = 8` on `title-letter`, then Show) | Start poked | yes |
| `rotate` | Pinch inside the globe and turn | glass sphere | `ConstellationIntro.Finished` (rotation unlocked) | `VolumePinchRotation.AxisLocked` (first axis lock) | yes |
| `match` | Match the stars to your chart | star chart / Earth-view display | `rotate` completed, i.e. right after the first axis lock (deferred automatically until the rotate acknowledgement ends) | Near-solve entered (`ConstellationSolveController` onNearSolveEntered); fall back to onSolved | yes (later puzzles: optionally show again with `autoHideSeconds` = 4) |
| `sign` | Draw along the line to sign | left end of the signing line | `ConstellationSigning.CurrentState` becomes `Ready` | `ConstellationSigning.Signed` | yes |
| `menu` | Choose another sky | Choose Another button | `PostPuzzleMenu.ShowMenu()` (today it runs on Signed) | `PostPuzzleMenu.ChooseAnotherConstellation()` | yes |

Not shown on purpose:

- **Intro animation**: no text. The compression onto the Earth-view display is the cue.
- **Near-solve and solved**: call `Clear()` on solve (or keep `match`
  completing on near-solve). The colour change, stamp VFX and diorama
  celebration are the success feedback, so no label competes with them.
- **Pen**: the pen follows the hand by itself, so there is nothing to pick up. The `sign` line covers it.

After the first puzzle every step above is skipped. All of them are
`onlyOnce`, and the session memory is static, so it survives "Choose Another".
For a returning player across launches, tick `rememberAcrossLaunches` on the
director (PlayerPrefs, prefix `CW.Guidance.done.`).

Stuck-player fallback (optional): if the near-solve hasn't arrived 45 s after
`match` was shown, show a non-once step `match-again` with the same text
(`autoHideSeconds` = 5). Don't reuse `rotate`/`match` for this, because they
are `onlyOnce`.
