# A colour-corrected WIC rendering of HEIC, scored on the ground truth

Prototype for issue #54, part of map #41. Measured on 2026-11-26 on Windows 11 (HEIF Image Extensions and HEVC Video Extensions installed), on the ground-truth set from issue #46 (2,164 files, 1,286 of them HEIC), which stays on Jamie's PC. Only counts and figures are given here.

## What the prototype does

`Program.cs` runs after `WicPictureDecoder` has decoded a HEIC whole, then measures the result as Trovolve does (`PictureAnalyzer`):

1. Adds WIC's fixed bias back (R +0.8, G +0.25, B +1.1 grey levels, issue #43), rounding against an 8 × 8 Bayer matrix so that a block's mean keeps the fraction. This prototype adds it after `WicPictureDecoder`'s colour transform, which is cruder than adding it before.
2. Reads the primary item's colour box (`pitm`, `ipco`, `ipma`) and undoes WIC's own conversion of nclx files:
   - **nclx 12/1/6:** the sRGB curve, sRGB to P3, then the BT.709 OETF gives the stream's values back.
   - **nclx 2/2/6:** primaries and transfer are unspecified. WIC applies the BT.709 curve alone, with no gamut conversion. This was found here: undoing the curve alone matches libheif within 0.46–0.64, while undoing the gamut conversion as well gives 1.8–2.7.
3. Renders nclx 12/1/6 two ways:
   - **wicfix:** the stream's values as they are, as libheif and Magick.NET show them.
   - **wicp3:** the stream's values read as Display P3 with the sRGB curve, then converted to sRGB.

## Scores (today's matching and limits)

| | Magick.NET | WIC | wicfix | wicp3 |
|---|---|---|---|---|
| Duplicates found, of 361 | 348 | 56 | 345 | 345 |
| iPhone HEIC/JPEG pairs (248), panoramas (9) | 248, 9 | 3, 0 | 248, 9 | 248, 9 |
| HEIC/JPEG signature difference, median / max | 0.06 / 0.15 | 0.66 / 0.89 | 0.20 / 0.49 | 0.20 / 0.49 |
| Similar shots called duplicates, of 993 | 2 | 2 | 2 | 2 |
| Random pairs called duplicates, of 20,000 | 1 | 0 | 1 | 1 |
| Edited versions shown as similar, of 554 | 151 | 139 | 150 | 282 |
| Blurry caught / sharp flagged | 42 of 59 / 21 of 38 | same | same | same |
| Time per HEIC, median (six at a time) | 1.9 s | 2.6 s | 3.0 s | 3.2 s |

- **The three duplicates wicfix misses** are copies with no capture time, held to 0.2, as Magick.NET's misses are.
- **Edited versions:** 232 pair an original HEIC that has an ICC profile with the nclx 12/1/6 render Photos saved after the edit.
  - Through Magick.NET and wicfix they are a median of 3.76–3.77 apart, with 8–9 within the similar band.
  - Through wicp3 they are a median of 1.59 apart, with 141 within the band.
  - Since an edit is meant to look like its original, Apple most likely renders 12/1/6 as Display P3 with the sRGB curve. This is inferred; it is not measured on a Mac.
- **Clipping:** WIC clipped a median of 2.3% of pixels in the nclx 12/1/6 files (at most 37%). What was clipped cannot be recovered.
- **Time:** the extra time is the prototype's per-pixel arithmetic, not a measured cost of the correction itself.

## Decided (Jamie, 2026-11-26)

- Trovolve corrects WIC's HEIC pixels itself: it adds the bias back before the colour transform, undoes WIC's nclx conversion, and checks at start-up, on a HEIC of known values, that the bias still holds.
- On every platform, nclx 12/1/6 is rendered as Display P3 with the sRGB curve, converted to sRGB. It is to be confirmed against ImageIO when a Mac is measured.
- nclx 2/2/6 is shown with its values as they are, with no conversion.
