# Credits board and background music

## Credits

The title's button row ends with **Credits**. Pressing it swaps the title letters
and buttons for a small parchment board, built from the noticeboard's wood, paper,
ink and fonts. **Back** swaps them back. GameFlow stays at the Title stage the whole
time, so the music, the title state and the letters are untouched. The title hints
pause while the board is open. A letter in hand blocks opening.

| What | Where |
|---|---|
| Words (edit this) | `Credits.txt` |
| Board prefab | `CreditsBoard.prefab` (in the scene under **Constellation Title / Credits**) |
| Open/close logic | `CreditsBoard.cs` |
| Rebuild layout, button, music object | **Tools > Celestial Workshop > Build Credits Board** (`Editor/CreditsBoardBuilder.cs`) |

### Editing the credits

`Credits.txt` is read at runtime. You don't need to rebuild after editing it.

- `## Heading` starts a section (for example `## Music`, `## Created by`).
- A blank line separates entries.
- Every other line is shown exactly as written. It is never treated as rich text.

The body text autosizes from 7.4 mm cap height down to 5.2 mm. If the builder
reports `TEXT OVERFLOWS`, enlarge `PaperW`/`PaperH`/`BackW`/`BackH` in the builder
and rebuild, or move older entries to a second sheet.

## Background music

`BackgroundMusic` is on the scene root object **Background Music**, next to
AudioFeedback.

- **Background Music Enabled:** untick it to silence the music, for example for
  capturing footage. Sound effects are unaffected.
- **Workshop Music Volume / Puzzle Music Volume:** set independently. The Britons is
  mastered about 6 dB louder than Morning, so the defaults (0.16 and 0.28) match
  their perceived levels.
- **Crossfade Duration:** 1.75 s by default.

The track is chosen on `GameFlow.StageChanged`. A main constellation puzzle (Puzzle
stage, not practice) plays **Morning**. Everything else plays **The Britons**: the
onboarding noticeboard, the title, letter practice and the boxes. A faded-out track
is paused, not stopped, so it resumes where it left off.
