# Decoder-steady measures, scored on the ground truth

Prototype for issue #55, part of map #41. Measured on 2026-11-26 on Windows 11, on the ground-truth set from issue #46 (2,164 files: 1,286 HEIC, 878 JPEG), which stays on Jamie's PC. Only counts and figures are given here.

## What was measured

`steady/` decodes every file whole through three decoders:

- Magick.NET, the tests' yardstick;
- WIC, as on `feature/os-decoders`;
- WIC corrected as issue #54 decided:
  - the bias is added back;
  - nclx 12/1/6 is rendered as Display P3 with the sRGB curve;
  - nclx 2/2/6 is shown as it is.

It reduces each picture with Trovolve's own `Pixels` to 512 pixels, as `PictureAnalyzer` does. It keeps:

- the 32 × 32 signature and pHash, as today;
- four sharpness measures:
  - the Laplacian variance at 512, as today;
  - the same at 256;
  - Tenengrad;
  - Tenengrad divided by the tile's variance.

`steadyscore/` replays today's `PhotoMatcher` with only the difference measure swapped:

- pHash nominates at 8 bits;
- the shape check, the edited-render guard and the capture-time rules stay as they are;
- the limits are 0.5, 0.2 and 2.0.

The measures, as in issue #45:

- **rgb:** today's mean absolute difference of the 3,072 bytes.
- **affLuma:** the 1,024 luma values of each grid, each picture's mean and contrast taken out, with the difference given back in grey levels at the pair's mean contrast.
- **gradNorm:** the same, over the 1,984 horizontal and vertical luma differences of the grid.
- **affLuma/gradNorm:** gradNorm decides the similar band (2.0), and affLuma whether a pair is one capture (0.5, 0.2).

## Signatures

| | rgb | affLuma | gradNorm | affLuma/gradNorm |
|---|---|---|---|---|
| Duplicates found, of 361: Magick.NET / WIC / corrected WIC | 348 / 56 / 345 | 360 / 341 / 356 | 348 / 341 / 347 | 360 / 341 / 356 |
| HEIC/JPEG pairs, corrected WIC: median / max | 0.20 / 0.49 | 0.14 / 0.33 | 0.21 / 0.37 | 0.14 / 0.33 |
| Edited versions shown as similar, of 554 (corrected WIC) | 282 | 291 | 469 | 469 |
| Look-alikes of two captures shown as similar, of 118 | 69 | 74 | 79 | 79 |
| Burst frames shown as similar, of 320 | 308 | 314 | 311 | 311 |
| Closest burst frames | 0.22 | 0.19 | 0.30 | 0.19 |
| Similar shots called duplicates, of 993 | 2 | 2 | 2 | 2 |
| Random pairs taken over 10 minutes apart, of 20,000: duplicate / similar | 1 / 2 | 1 / 2 | 1 / 1 | 1 / 1 |
| Cost of comparing a nominated pair (5,743 pairs, unoptimised) | 7.0 µs | 8.1 µs | 12.3 µs | about 20 µs |

- Both new measures are worked out at compare time from the 32 × 32 grid already stored, so nothing new is stored for them.
- The 2 similar shots called duplicates are edits whose pixels did not change. The random duplicate is a copy whose capture time is missing, matched against another capture.

## Sharpness

On one route every measure reads alike through every decoder:

| Measure | Corrected WIC / Magick.NET, same file: p10, median, p90 |
|---|---|
| Laplacian at 512 | 0.997, 1.002, 1.006 |
| Laplacian at 256 | 0.997, 1.002, 1.006 |
| Tenengrad | 0.996, 1.002, 1.005 |
| Tenengrad / variance | 0.997, 1.000, 1.003 |

How well each finds the set's 59 blurry and 38 sharp photos is poor for all of them. AUC is the chance that a blurry photo reads lower than a sharp one:

| Measure | AUC | Caught, with at most 0 / 2 / 4 / 8 sharp flagged |
|---|---|---|
| Laplacian at 512 (today) | 0.69 | 17 / 22 / 29 / 34 |
| Laplacian at 256 | 0.63 | — |
| Tenengrad | 0.63 | — |
| Tenengrad / variance | 0.70 | 15 / 17 / 21 / 26 |

Today's limit of 400 catches 42 and flags 21 sharp photos. A limit of 170 catches 22 and flags 2.

## Decided (Jamie, 2026-11-26)

1. Duplicates are decided by affLuma and the similar band by gradNorm, both worked out at compare time from the stored grid; pHash still nominates.
2. The limits stay at 0.5 (same capture), 0.2 (neither photo has a capture time) and 2.0 (similar).
3. Sharpness stays today's Laplacian variance at 512. Blurry is below 170, in place of 400.

## Note

On Jamie's PC, two of the measuring runs left 3,760 Magick.NET cache files (156 GB) in the temporary folder and filled the disk. They were deleted at Jamie's word. Re-running 40 files, and tests of 30 and 60, left none, so the cause was not found. Magick.NET is in the tests only.
