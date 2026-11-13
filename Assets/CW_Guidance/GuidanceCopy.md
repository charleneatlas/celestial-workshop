# Guidance copy: steps (as wired in the prototype scene)

One cue at a time. Each line is 8 words or fewer. "1st only" = `onlyOnce`
(skipped after the first puzzle / first time it is completed in the session).

| id | Text | Target | Shown when | Completed when | 1st only |
|---|---|---|---|---|---|
| `title-letter` | Drop a letter into the globe to practice. | letter C | Title screen appears; alternates with `title-start` every 6 s (`GuidanceHooks.titleHintSeconds`) and stays while a letter is held | A letter is placed (practice starts) | no (restarts on every return to the title) |
| `title-start` | Press Start to explore constellations. | Start button | Alternates with `title-letter` | Start pressed | no (restarts on every return to the title) |
| `rotate` | Pinch inside the sky sphere to rotate the constellation. | label: glass sphere; halo: the sky constellation (`pulseAround`) | `ConstellationIntro.Finished` (rotation unlocked) | `VolumePinchRotation.AxisLocked` (first axis lock) | yes |
| `match` | Match the stars to your chart | star chart / Earth-view display | `rotate` completed, i.e. right after the first axis lock (deferred automatically until the rotate acknowledgement ends) | Near-solve entered (`ConstellationSolveController` onNearSolveEntered); fall back to onSolved | yes (later puzzles: optionally show again with `autoHideSeconds` = 4) |
| `sign` | Draw along the line to sign | left end of the signing line | `ConstellationSigning.CurrentState` becomes `Ready` | `ConstellationSigning.Signed` | yes |
| `menu` | Choose another sky (after practice: Practice another letter) | Choose Another button | `ConstellationSigning.Signed` | Next puzzle reset | yes |

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
