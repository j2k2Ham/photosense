# Which signatures and sharpness measures stay steady across decoders?

Research for issue #45 (map #41). Measured 2026-11-25 on anonymous scratch copies of 160 pictures from Jamie's library (read only): 40 pairs of an iPhone's HEIC and the JPEG made from it, and 40 pairs of burst frames, in each of two random draws. Only counts and figures are given here.

## Short answer

1. **Reduce every picture by one route, Trovolve's own, from a full decode. Most of the difference comes from this.** The same file through Magick and through WIC as the branch reduces them today is 1.9 grey levels apart (median), and up to 11.5. Decoded at full size by each and then reduced by `Pixels`, it is 0.41 apart, and the sharpness of the two agrees within 0.3%. Panorama copies went from 5.3–8.3 apart to 0.64. Own reduction also leaves Magick's figures, which the limits were set on, almost as they were (0.09 apart).
2. **Compare signatures by brightness (luma) with each picture's mean and contrast taken out** (`affLuma` below), with a normalised luma gradient as a second check (`gradNorm`). On one route through WIC, a HEIC and its JPEG are then 0.24 apart at the median and 0.27 at p90, against 0.66 and 0.75 by today's RGB measure. An even colour shift like WIC's (#43) moves the normalised luma by only 0.11, against 1.26 for RGB.
3. **No pixel measure tells burst frames from copies through WIC.** The closest burst frames are 0.22–0.34 apart by every measure; WIC's copies are 0.24–0.34. Through Magick there is a clean gap (copies p90 at most 0.15, bursts at least 0.22). Capture time has to stay what separates bursts. `SameDifference` 0.5 holds for the normalised measures (37 of 38 copies). `UnconfirmedSameDifference` 0.2 (pictures with no capture time) cannot hold through WIC unless WIC's channel offset is removed first (#43) or the pair is tied by metadata (#44).
4. **Sharpness: use Tenengrad (Sobel), or the Laplacian at half size, measured on own-reduced pixels.** Today's measure, the Laplacian variance at the finest scale, is the one that moves with the decoder. On small JPEGs that were not resized at all, WIC read 1.30 times Magick's value; Tenengrad read 1.000 and the half-size Laplacian 0.995. Dividing Tenengrad by the tile's own contrast (`tenNorm`) also cancels gain and gamma and is the least sensitive to the scaler. Either way the 400 limit has to be measured again on the ground truth (#46).
5. **Cost is not the issue.** Every candidate measure takes under 2.3 ms on a 512-pixel picture, under 2 minutes of CPU for 48,000 files. Own reduction from full size takes 67–128 ms per 12-megapixel picture, about 1–1.7 CPU-hours for 48,000, spread over the cores. For iPhone files it costs no extra decoding: WIC decoded full size no slower than its reduced route (HEIC 852 ms against 990, JPEG 431 against 497), because their colour profiles already send them down the full path.
6. **pHash stays as the nominator.** It is already steady: it uses luma, leaves out the DC term and thresholds at the median, so an even shift or gain cannot move it. Once both decoders take one route, the same file is 0–2 bits apart, 4 at most. dHash moved up to 6 bits and aHash is too coarse. Meta's PDQ is the published, larger form of the same idea: luma, its own box-filter reduction to 64×64, a 16×16 DCT and 256 bits. It is worth trying only if the ground truth shows nominations missed.

## Ranked shortlist

### Signatures (how alike two pictures are)

| Rank | Candidate | Even colour shift | Scaler change | WIC copies p50 / p90 (one route) | Closest burst | Cost | Verdict |
|---|---|---|---|---|---|---|---|
| 0 | **One reduction route** (full decode, then `Pixels`) | n/a | removes it | n/a | n/a | 67–128 ms per 12 MP | Required whatever signature is chosen |
| 1 | **Luma, mean and contrast normalised** (`affLuma`) | 0.11 | 0.76 | 0.24 / 0.27 | 0.23 | < 0.1 ms | Prototype as the main signature measure |
| 2 | **Normalised luma gradient** (`gradNorm`) | 0.13 | 1.11 | 0.30 / 0.34 | 0.34 | < 0.1 ms | Prototype as a second check; best burst separation through WIC (1 of 40 bursts under the copies' p90, against 4) |
| 3 | Per-channel mean and contrast normalised RGB (`affRGB`) | 0.21 | 0.77 | 0.37 / 0.45 | 0.27 | < 0.1 ms | Keeps colour but leaves more of WIC's bias; fallback |
| 4 | Luma, offset only (`luma`) | 0.78 | 0.75 | 0.51 / 0.53 | 0.22 | < 0.1 ms | Not enough: copies straddle 0.5 |
| 5 | Today's RGB (`rgb`) | 1.26 | 0.75 | 0.66 / 0.75 | 0.25 | < 0.1 ms | Fails through WIC: no copy under 0.5 |
| 6 | Rank (histogram-equalised) luma | 0.20 | 1.43 (max 11.7) | 0.46 / 0.71 | 0.53 | < 0.1 ms | Handles any tone curve, but is noisy and the scaler moves it a lot; no |
| – | pHash (today, 64-bit DCT) | 0 bits (max 2) | 0 bits (max 4) | 0 bits (max 4) | 0 bits | 0.14 ms | Keep as nominator (8 bits); it cannot separate bursts |
| – | dHash, aHash | 0 (max 2), 0 (max 1) | 0 (max 6), 0 (max 3) | — | 0 | ~0.7 ms | No gain over pHash |

"Even colour shift" is the same picture with red and blue scaled by 0.985, the shape of WIC's bias. "Scaler change" is a plain bilinear reduction against Trovolve's area average. Both columns are median grey levels (or bits), same file before and after.

### Sharpness (blurry shots, the sharpest of a burst)

| Rank | Candidate | Magick vs WIC, today's routes | Magick vs WIC, one route | Small JPEG, no resize, WIC/Magick | Bilinear vs area scaler | Even shift; gamma 1.1 | Cost |
|---|---|---|---|---|---|---|---|
| 1 | **Tenengrad** (Sobel magnitude squared, sharpest 4×4 tile) | 0.88 (0.80–0.94) | 1.003 (0.998–1.006) | 1.000 | 1.53 | 0.988; 1.03 | 1.5 ms |
| 1= | **Tenengrad / tile variance** (`tenNorm`) | 0.93 (0.90–0.95) | 1.000 (0.998–1.002) | 0.984 | 1.06 | 1.000; 1.003 | 1.5 ms |
| 3 | Laplacian variance at 256 (own 2× reduction first) | 0.83 (0.76–0.90) | 1.004 (0.999–1.006) | 0.995 | 1.70 | 0.988; 1.03 | 2.2 ms |
| 4 | Brenner | 0.86 | 1.003 | 1.02 | 1.98 | 0.988; 1.03 | 0.6 ms |
| 5 | Laplacian variance at 512 (today) | 0.93 (0.78–1.07) | 1.003 (0.997–1.006) | **1.30** | **3.67** | 0.988; 1.04 | 1.0 ms |
| 6 | Laplacian variance / tile variance | 1.05 | 1.000 | 1.29 | 2.08 | 1.000; 1.004 | 0.9 ms |

Each cell is a ratio for the same file, as a median (p10–p90). Rank agreement (Spearman) between Magick and WIC on one route is 0.999–1.000 for every candidate, against 0.983–0.993 on today's routes. A HEIC and its JPEG measure alike (0.997–1.000) on every route.

## What to prototype

In a measuring tool that is never shipped (`Trovolve.Benchmarks`, as #47 plans), over the ground truth (#46):

1. `PictureAnalyzer` asks every decoder for full size (`DecodedPicture.MaxEdge`) and reduces with `Pixels` alone. With #43's fixes (EXIF ColorSpace 65535 as no profile; optionally WIC's per-channel offset taken out), WIC, ImageIO and Android's decoder all take that route.
2. Kept per picture: the 32×32 RGB grid as today (it holds luma and gradients both), pHash as today, Tenengrad and `tenNorm` on the 512-pixel reduction.
3. Pairs scored by `affLuma` and `gradNorm` next to today's `rgb`, for each kind (same picture, burst, similar, unrelated) and each decoder. Then the limits are chosen again: nominate by pHash ≤ 8; same picture at the same instant by a normalised limit (0.5 holds here); similar at 2.0 again; bursts by capture time.
4. The 400 blurry limit set again for the chosen sharpness measure, from the ground truth's blurry and sharp shots.
5. Timed on the 48,000-file disk: decode at full size plus own reduction, against today's route.

`AnalysisVersion` goes up once, for both changes together, so every picture is read again only once (#41's "moving existing libraries over").

## Detail

### What is computed today

- `PictureAnalyzer` asks the decoder for the picture at 512 on its long side (`WorkEdge`), then reduces that to a 32×32 RGB grid with `Pixels.Resize`, an area average, stretched to a square. `PhotoMatcher.SignatureDifference` is the mean absolute difference of the 3,072 bytes, in grey levels. Its limits: 0.5 for the same picture at the same instant, 0.2 with no capture time, 2.0 for similar.
- `ImageFingerprint.PerceptualHash` takes luma of the 32×32 grid and the 8×8 low DCT terms 1–8 (DC left out), with each bit set when above the median: 64 bits, 8 to nominate.
- `ImageFingerprint.Sharpness` is the variance of the 4-neighbour Laplacian on luma, in the sharpest of 4×4 tiles, on the 512-pixel decode; blurry below 400 (`MediaKinds.BlurryBelow`).
- The reduction to 512 is the decoder's own. Magick (the tests' yardstick) uses a JPEG size hint, then `Resize` (Lanczos). WIC reduces with its Fant scaler, either inside the HEIF or JPEG decoder (frame route, 8-bit) or after a colour transform at 16 bits (profile route). #43 found that one file of a pair can take each route.

### Method

- Pairs were found by capture time, read only, over 5,518 pictures (4,839 with sub-second times). A copy is the one HEIC and the one JPEG of a second, by the same camera, sub-second times within 1 ms: 244 found. A burst pair is two pictures of one format, by one camera, 30 ms to 1 s apart and adjacent in time: 630 found. Two random draws of 40 + 40 each were copied to scratch under neutral names. The draws were different and gave the same picture; figures above are from the second, which also holds the WIC-at-1024 route and the per-pair figures.
- Each copy was decoded by Magick.NET (the tests' decoder, Lanczos to 512) and by `WicPictureDecoder` as on `feature/os-decoders`, each at 512 (today) and at full size followed by `Pixels` to 512 (one route). Also: WIC at 1024 then `Pixels`; Magick full size with an even shift (R and B × 0.985), with gamma 1.1, and with a bilinear reduction in place of the area average.
- Measures: see the shortlist. `affLuma` z-scores each picture's 1,024 luma values and puts the difference back in grey levels at the pair's mean contrast. `gradNorm` does the same with the 1,984 horizontal and vertical luma differences of the grid. Sharpness uses the same 4×4 tiles as today.
- Two copies in the draw were panoramas (long side over 2.5 × short). One "copy" pair was 1.0–1.9 apart by every measure through both decoders, so it is probably an edited JPEG rather than a plain conversion; it is left in the counts above as "max", and the ground truth (#46) should label such pairs.

### Pairs within one decoder (ordinary photos; 38 copies, 40 bursts; grey levels)

| Route | Measure | Copies p50 | p90 | Bursts min | p10 | Bursts at or under copies' p90 |
|---|---|---|---|---|---|---|
| Magick 512 (today's yardstick) | rgb | 0.062 | 0.109 | 0.256 | 0.346 | 0 |
| | affLuma | 0.072 | 0.095 | 0.228 | 0.318 | 0 |
| | gradNorm | 0.100 | 0.123 | 0.347 | 0.439 | 0 |
| Magick, own reduction | rgb | 0.062 | 0.092 | 0.253 | 0.343 | 0 |
| | affLuma | 0.071 | 0.087 | 0.224 | 0.323 | 0 |
| WIC 512 (branch today) | rgb | 0.609 | 0.695 | 0.259 | 0.335 | 9 |
| | affLuma | 0.221 | 0.257 | 0.234 | 0.319 | 4 |
| | gradNorm | 0.292 | 0.333 | 0.338 | 0.420 | 0 |
| WIC, own reduction | rgb | 0.659 | 0.753 | 0.251 | 0.331 | 9 |
| | luma | 0.507 | 0.530 | 0.221 | 0.294 | 9 |
| | affRGB | 0.367 | 0.450 | 0.270 | 0.406 | 6 |
| | affLuma | 0.244 | 0.271 | 0.229 | 0.321 | 4 |
| | gradNorm | 0.303 | 0.341 | 0.336 | 0.439 | 1 |

Panorama copies (2 pairs): through Magick 0.05–0.13 rgb on either route. Through WIC at 512, 5.3 and 8.3 rgb (1.9 and 2.8 affLuma). Through WIC at 1024 then own reduction, still 4.6 and 7.1. Through WIC at full size with own reduction, 0.64 rgb and 0.22–0.24 affLuma, like ordinary photos. This agrees with #43: the panorama gap is the reduction route, not colour, and any reduction inside WIC brings it back.

Through WIC, the normalised measures leave a remainder (affLuma 0.24, gradNorm 0.30) beyond what a pure per-channel offset would. #43's bias of about R −0.8, G −0.25, B −1.1 would be cancelled entirely by mean normalisation, so part of the remainder varies within the picture. The cause is inferred, not measured: truncation that depends on the value, with clipping near black and white. Through Magick the same pairs are 0.07 and 0.10. Taking WIC's offset out before measuring (#43) is worth trying with these measures; with a remainder of about 0.1 or less, the 0.2 limit for pictures with no capture time would hold again.

### The same file through two routes (all 160 files; median, p90, max)

| From → to | rgb | affLuma | gradNorm | pHash bits |
|---|---|---|---|---|
| Magick 512 → WIC 512 (today) | 1.91, 6.40, 11.49 | 1.36, 3.10, 5.06 | 0.88, 1.77, 2.33 | 2, 4, 4 |
| Magick own → WIC own (one route) | 0.41, 0.74, 10.79 | 0.21, 0.33, 1.25 | 0.25, 0.35, 0.71 | 0, 2, 2 |
| WIC own → WIC 1024 then own | 1.20, 3.76, 7.50 | 0.86, 2.06, 3.44 | 0.55, 1.12, 1.52 | 0, 2, 4 |
| Magick 512 → Magick own | 0.09, 0.15, 0.26 | 0.10, 0.16, 0.27 | 0.16, 0.26, 0.38 | 0, 0, 2 |
| Even shift (R, B × 0.985) | 1.26, 1.49, 1.62 | 0.11, 0.12, 0.19 | 0.13, 0.15, 0.16 | 0, 0, 2 |
| Gamma 1.1 | 6.59, 7.08, 7.42 | 0.90, 1.28, 1.68 | 0.61, 0.76, 0.99 | 0, 2, 4 |
| Bilinear instead of area average | 0.75, 1.49, 2.82 | 0.76, 1.50, 2.84 | 1.11, 2.13, 4.04 | 0, 2, 4 |

(First draw for the last three rows; the second agreed.) By format, on one route: JPEGs 0.17 rgb at the median, 0.51 at most; HEICs 0.70 at the median. #43 found WIC's JPEG decode equal to libjpeg's, so the JPEG remainder is the two decoders' colour transforms at 16 bits. The HEIC remainder is WIC's YCbCr bias.

**Newer iPhones' HEIC with nclx and no ICC** (#43: 7–14 levels lighter through WIC than libheif, 239 files of the library and every new photo). The gamma 1.1 row is a tone-curve change of about the same size. Normalisation brings 6.6 levels down to about 0.9 (affLuma), not under 0.5. So these files need #43's corrected rendering; normalisation alone does not rescue them. None of these figures checks a measure on real nclx files, and #47 should score them as a kind of their own.

### Sharpness: why the finest-scale Laplacian moves

The 4-neighbour Laplacian at 512 pixels responds mostly to detail one or two pixels wide. That is where decoders and scalers differ most: chroma upsampling, IDCT rounding, and a kernel's ringing or aliasing. A bilinear reduction in place of an area average multiplied it by 3.7. Small JPEGs not resized at all read 1.30 times higher through WIC. #43 attributes the ×1.5 seen in the end-to-end test to the two reduction routes; the small-JPEG figure shows that decoding alone moves the finest scale too. Tenengrad's 3×3 Sobel smooths across the edge, and a Laplacian at half size looks one octave lower; both read the same through either decoder (1.000 and 0.995). Normalising by the tile's variance cancels contrast and gamma (`tenNorm` 1.000 and 1.003). The open question is whether a contrast-normalised measure still finds blurry shots as well. A dim, low-contrast but sharp scene and a soft, high-contrast one may change places, so that is for the ground truth (#46, #47), not for a guess. The literature agrees that no focus measure wins everywhere (Pertuz et al. 2013). Laplacian measures are the most sensitive to noise, and statistics-based ones the least.

### Literature and tools

- **DCT pHash** (pHash library; Zauner 2010) thresholds low-frequency DCT terms at their median. It is invariant to brightness offset and contrast gain in luma by construction; Trovolve's hash is this. Zauner's benchmark (Rihamark) found the block-mean hash fastest and the Marr-Hildreth one the most discriminating.
- **aHash and dHash** (Krawetz, Hacker Factor): mean-thresholded 8×8, and the signs of neighbour differences on 9×8. dHash ignores offset and gain, but sign flips in flat areas make it noisy; that matched here, where it moved up to 6 bits against pHash's 4.
- **PDQ** (Meta, ThreatExchange): luma, two passes of a Jarosz box filter to 64×64, a 16×16 DCT and a median threshold, 256 bits, and a quality score that flags flat pictures. It does its own reduction, the same principle as recommendation 1. Its README warns that hashes differ between platforms because of how images are turned into bytes, which is this ticket's problem.
- **OpenCV `img_hash`** (average, pHash, Marr-Hildreth, radial variance, block mean, colour moment) is benchmarked against contrast, blur, noise, JPEG and resizing. Colour-moment hashes trade discrimination for colour robustness.
- **imagededup** (idealo) finds hashes fine for exact and near-exact copies and weak on transformed near-duplicates; for those it recommends CNN embeddings. Apple's Vision feature prints (`VNGenerateImageFeaturePrintRequest`) and on-device embeddings are the platform equivalents. They are not the same on all four platforms, so they cannot be the one signature; they might help with "similar shots" later. This is inferred, not measured.
- **Focus measures** (Pertuz, Puig and Garcia 2013): 36 operators in six families; gradient (Tenengrad, Brenner) and Laplacian families are the most sensitive to window size, and Laplacian ones to noise. The variance of the Laplacian for blur (Pech-Pacheco et al. 2000; PyImageSearch) needs a threshold set for each domain. Not one source addresses resolution or resampling, so the figures above are this study's own.
- **WIC's scaler**: Fant is "a weighted average of all the pixels that map to the new pixel". A scaler placed straight after a frame can make the decoder reduce while it decodes (`IWICBitmapSourceTransform`), with JPEG limited to DCT multiples. That is why WIC's reduced route differs from its full one, and why it differs between formats.

### Limits of this study

- 40 + 40 pairs per draw, two draws, one iPhone library on one PC. These are candidates for the ground truth to judge, not limits to ship.
- Pairs were labelled by capture time only. One "copy" looks like an edit, and burst pairs may include a Live Photo neighbour.
- Not measured: ImageIO and Android's decoder, which run only on GitHub's machines; newer iPhones' nclx files as a kind of their own; the 48,000-file disk; whether `tenNorm` finds blurry shots as well as today's measure.
- Timings vary with the machine's load: the two draws differed by up to two times. Both are within the figures given.

## Sources

- pHash: https://www.phash.org/ ; C. Zauner, *Implementation and Benchmarking of Perceptual Image Hash Functions* (2010): https://phash.org/docs/pubs/thesis_zauner.pdf ; Rihamark (SPIE 2011): https://pure.fh-ooe.at/en/publications/rihamark-perceptual-image-hash-benchmarking/
- dHash, "Kind of Like That" (Hacker Factor): https://www.hackerfactor.com/blog/?/archives/529-Kind-of-Like-That.html
- State of the art in image hashing (arXiv 2108.11794): https://arxiv.org/pdf/2108.11794
- Meta PDQ: https://github.com/facebook/ThreatExchange/tree/main/pdq ; pipeline as described by implementations: https://pkg.go.dev/github.com/haileyok/gopdq , https://www.mintlify.com/ajdnik/imghash/algorithms/pdq
- OpenCV img_hash: https://docs.opencv.org/4.x/d4/d93/group__img__hash.html
- imagededup: https://github.com/idealo/imagededup ; benchmarks: https://idealo.github.io/imagededup/user_guide/benchmarks/
- Apple Vision feature prints: https://developer.apple.com/documentation/vision/vngenerateimagefeatureprintrequest
- Pertuz, Puig, Garcia, "Analysis of focus measure operators for shape-from-focus", *Pattern Recognition* 46(5), 2013: https://sites.google.com/view/cvia/focus-measure
- Blur detection with the variance of the Laplacian: https://pyimagesearch.com/2015/09/07/blur-detection-with-opencv/
- WIC interpolation modes: https://learn.microsoft.com/en-us/windows/win32/api/wincodec/ne-wincodec-wicbitmapinterpolationmode ; IWICBitmapSourceTransform: https://learn.microsoft.com/en-us/windows/win32/wic/-wic-imp-iwicbitmapsourcetransform
- Sibling findings: #43, https://github.com/j2k2Ham/trovolve/blob/research/wic-heic-colour/research/wic-heic-colour.md
