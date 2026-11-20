# Celestial Workshop wordmark (video end card)

The in-game title treatment, re-rendered on its own. Nothing under Assets/ was changed,
and nothing in this folder goes into a Unity build.

* Lettering: Meddon-Regular.ttf (Assets/CW_Art/Fonts), text `Celestial<space=0.85em>Workshop`,
  no kerning, thickened to match the Title_Ink material (_FaceDilate 0.14).
* Star band: a port of `NoticeboardArt.TitleStars(1024, 200, seed 3)` that matches the shipped
  TitleStars.png, redrawn at about 3.5x.
* Layouts: `noticeboard` = NoticeboardBuilder header (cap 34 mm under a 440 mm band);
  `credits` = CreditsBoardBuilder header (cap 19 mm under a 300 mm band).
* Colours: `ivory` = lettering and ink stars #F4E9D2, feature star #DDBB78;
  `champagne` = #E6CB94, feature star #F2D9A2. The ink on parchment was #38200D, with the feature star in gold.

To regenerate: `python Marketing/Wordmark/render_wordmark.py . Marketing/Wordmark/Export`
(`--verify` compares the star band with the shipped texture.)

Font licence: Meddon (c) 2010-11 Vernon Adams, SIL Open Font License 1.1 (OFL-Meddon.txt).
The OFL allows artwork, logos and video made with the font, including commercial and promotional use.
Credit is not required.

## Legible "r" (`--r-fix`)

Meddon's copperplate "r" drops its right stroke nearly to the baseline, so "Workshop" can read as
"Wonkshop". `--r-fix` raises the foot of that stroke by `R_LIFT` (300 font units, smoothly, top fixed)
in a temp copy of the font, and writes `*_rfix` exports alongside the originals. The game font is unchanged.
`python Marketing/Wordmark/render_wordmark.py . Marketing/Wordmark/Export --r-fix`

The YouTube thumbnail (Marketing/Thumbnail/make_thumbnail.py) uses the noticeboard champagne rfix PNG.
