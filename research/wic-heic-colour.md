# Why WIC's HEIC pixels differ, and what can be done through WIC

Research for issue #43, part of map #41. Measured on 2026-11-25 on Windows 11 (HEIF Image Extensions and HEVC Video Extensions installed), against libheif through Magick.NET-Q8 15.1.0. Only counts and figures are given here; the photos stay on Jamie's PC.

## Short answer

There are **three separate causes**, and only one of them is about colour signalling.

| Kind of iPhone HEIC (Jamie's library, 2,782 HEIC) | What WIC does | Effect on Trovolve | Confidence |
|---|---|---|---|
| **ICC Display P3, no nclx** (2,522) | Right matrix (BT.601) and range (full). It passes the ICC through and does no colour conversion of its own. Its YCbCr→RGB has a **fixed bias**: about R −0.8, G −0.25, B −1.1 grey levels against libheif | The whole "evenly darker in red and blue" shift. HEIC to its JPEG is **0.50–0.71** apart through WIC and 0.05–0.11 through Magick | High: measured on 14 files, 13 pairs |
| **No colour box at all**, mostly panoramas (21) | The same as above. VUI matrix 2 (unspecified) is decoded as BT.601, like libheif, and WIC reports an EXIF sRGB colour context | **Not a colour problem.** The 3.4–10.4 comes from Trovolve's `WicPictureDecoder` reducing the HEIC and its JPEG by two different routes. Decoded the same way, the pair is 0.58–0.70 apart, like any other pair | High: 9 pairs, each figure reproduced |
| **nclx 12/1/6 full range, no ICC**, newer iPhones with gain maps (239, and every new photo) | WIC **colour-manages it itself**. It reads transfer 1 literally as the BT.709 camera curve, converts Display P3 to sRGB, and then reports sRGB. The result is 7–14 levels lighter than libheif's values | Unknown for matching: the library has no JPEG twin of these. Every platform renders them differently (below) | High for what WIC does (the model matches to within the fixed bias). **Not verified:** what Apple means by transfer 1 |

**What WIC's API lets a program do:** very little inside the decoder.

- The HEIF decoder offers **only 32bppBGR**. `GetClosestPixelFormat` returns 32bppBGR for every request: 48bppRGB, 64bppRGBA, half, float, 8bppY and 16bppCbCr.
- It does not implement `IWICPlanarBitmapSourceTransform`.

So a program can neither reach YCbCr, nor choose the matrix or range, nor avoid the rounding, nor turn off the nclx conversion, nor get unclipped values. What a program **can** do is in Trovolve's own pipeline, after decoding:

1. **Reduce every picture the same way.** This is the panorama fix, and it is a bug in Trovolve, not in WIC.
2. **Take the fixed bias out of HEIF output.**
3. **Undo WIC's nclx conversion**, then apply the rendering Trovolve chooses. This loses information where WIC clipped.

## 1. The fixed bias in WIC's HEIF decoding

The test decoded each HEIC whole through WIC (frame to 24bppBGR, no transform) and through libheif (Magick.NET, no transform). WIC's RGB was then fitted per channel as `a + b·Y + c·Cb + d·Cr`, where Y, Cb and Cr were worked back from libheif's RGB under BT.601 full range. Clipped pixels were left out.

| | ICC files (14) | No colour box (12) |
|---|---|---|
| Mean WIC − libheif, R | −0.57 to −0.93 | −0.67 to −0.85 |
| G | −0.14 to −0.42 | −0.22 to −0.38 |
| B | −0.58 to −1.10 | −0.77 to −1.03 |
| Intercept a, R / G / B | −0.15…−0.91 / −0.14…−0.44 / −0.70…−1.57 | −0.72…−0.96 / −0.12…−0.33 / −0.81…−1.38 |
| Cr→R (BT.601: 1.402) | 1.33–1.40 | 1.31–1.39 |
| Cb→B (BT.601: 1.772) | 1.72–1.78 | 1.70–1.77 |

Other models fitted worse in every file: BT.709 full, BT.601 limited and BT.709 limited. Limited range is ruled out outright, as it gives a mean absolute error of 6.8–9.2 against 0.5–1.1.

**The matrix and range are right. What differs is a constant offset.** It is small in G and larger in R and B. That pattern fits integer arithmetic that truncates rather than rounds: −0.5 in every channel from truncating the result, plus chroma biased by about −0.25, for instance from truncating while chroma is upsampled. That would give R −0.85, G −0.24 and B −0.94. This mechanism is **inferred**: Microsoft does not document the HEIF decoder's conversion.

The pairs confirm that this offset is the whole HEIC-to-JPEG gap. Through WIC on Trovolve's present path, HEIC minus JPEG is R −0.59…−1.01, G −0.12…−0.40, B −0.66…−1.25. Through Magick it is under ±0.17 in every channel. WIC's JPEG decoder matched libjpeg byte for byte on all 24 JPEGs tried (mean absolute error 0.00), so the gap is on the HEIC side.

Adding the offset back (whole levels +1, 0, +1 in this test) brings the mean of every channel to within about ±0.4 of libheif. The per-pixel error stays at 0.5–0.9, about what libheif's own HEIC-to-JPEG comparison shows. A fractional offset applied before the signature's averaging would do better (not yet measured).

The colour contexts are fine. For ICC files `GetColorContexts` returns one context, the 548-byte ICC profile, identical to the one in the JPEG made from the HEIC. Trovolve's transform of it to sRGB is therefore the same for both, and it cancels out.

## 2. Panoramas: two reduction routes in `WicPictureDecoder`

`WicPictureDecoder` chooses its route by colour context:

- **A picture with a profile** goes to 64bppRGBA, through `IWICColorTransform`, then through WIC's Fant scaler.
- **A picture without one** goes straight to the scaler.

Two facts, both measured, make these routes give different pictures.

1. **WIC's Fant scaler averages 64bppRGBA in linear light.**
   - On a JPEG, Fant over 64bppRGBA minus Trovolve's area average came to +8.9 / +9.1 / +11.1 levels.
   - Fant over 64bppRGBA minus an area average done in linear light came to 0.00.
   - Fant over 24bpp minus the area average came to 0.00.
   - The format converter itself does not linearise: 8-bit values come out ×257.
   - So the profile route makes reduced pictures brighter, by 0.05–11 levels across 14 files depending on detail. When a HEIC and its JPEG both take this route the brightening cancels out.
2. **A scaler placed straight on a HEIF frame lets the decoder reduce the picture itself.** The frame implements `IWICBitmapSourceTransform`, and `GetClosestSize` granted exactly the size asked for (for example 512×384). The decoder's own reduction came out −1.0 to −1.5 levels darker in every channel than Fant over a full decode.

The panoramas fall between the two routes:

- The panorama HEIC has no colour box, so WIC gives it an EXIF sRGB context and it takes the frame route.
- Its JPEG has no ICC but EXIF ColorSpace 65535 ("uncalibrated"). `Profile()` treats that as a profile to transform, so the JPEG takes the 64bpp linear-light route.

The signature difference between them, 32×32 as `PhotoMatcher` compares it, came to **3.37–10.36** on 9 pairs. That reproduces the map's 3.4–10.4. Decoding both at full size and area-averaging both the same way gives **0.58–0.70**, the same as ordinary pairs.

**The fix is in Trovolve, and needs no colour work:**

- Treat EXIF ColorSpace 65535 as no profile.
- Reduce every picture by one route. Either decode, or let the decoder reduce, to a fixed size and finish with `Pixels.Resize`, or always use one WIC route. Never mix linear-light and gamma-space averaging.

## 3. nclx HEICs: WIC converts them itself

The newer files have:

- an `nclx` box of 12/1/6 with full range (Display P3 primaries, BT.709 transfer, BT.601 matrix) and no ICC on the primary item;
- auxiliary images including a gain map, which carries an ICC of its own, and several mattes.

For these WIC reports one EXIF sRGB context, and its output is 6.5–14.3 levels above libheif's. The fit has intercepts of +13 to +19, a slope of 0.93–0.98 and enlarged chroma (Cr→R 1.58–1.67). That shape is what a gamut and tone conversion produces. Four models of libheif's values were tried against WIC on 4 files:

| Model applied to libheif's RGB | WIC − model, mean R / G / B | MAE |
|---|---|---|
| none | +6.5…+14.3 / +8.8…+11.5 / +6.5…+10.1 | 8.6–10.8 |
| **BT.709 inverse OETF → P3→sRGB matrix → sRGB curve** | **−0.80…−0.83 / −0.26…−0.41 / −0.76…−0.84** | **0.75–0.78** |
| BT.709 inverse OETF, primaries kept | −5.8…+4.7 / … | 2.7–4.6 |
| BT.1886 (γ 2.4) → P3→sRGB | +14…+21 | 15–18 |
| Display P3 proper (sRGB curve) → P3→sRGB | +8.6…+12.2 | 8.9–10.8 |

The literal reading matches exactly, up to the fixed bias of §1. **WIC linearises with the BT.709 camera curve, converts P3 to sRGB, and re-encodes with the sRGB curve.** It then reports sRGB, and it clips: 2.5–13.7% of pixels in the four files had a channel at 0 or 255 afterwards.

Whether that is what Apple means is **not verified**. The evidence that it is not, all **inferred**:

- In Jamie's library, the HEVC streams of the 2,035 ICC files whose VUI describes colour also say primaries 12 and transfer 1 (BT.709), while their ICC profile is Display P3 with the sRGB curve. The other 487 ICC files say 2/2 (unspecified). The same camera pipeline labels its streams "transfer 1" while declaring sRGB-curve colour.
- Apple's SDR base image is described as Display P3 with the sRGB transfer function ([jackchou00.com](https://jackchou00.com/en/posts/iphone-heic-hdr-format/)).

If Apple renders 12/1/6 as Display P3 with the sRGB curve, then WIC's rendering is about 9–12 levels lighter than Apple's (the last row above). libheif through Magick.NET is different again: it applies no colour conversion when there is no ICC, so it shows P3 values as if they were sRGB.

**Undoing WIC's conversion works.** The route back is the sRGB curve, the sRGB→P3 matrix, then the BT.709 OETF, with the §1 offset added first. It recovers libheif's values with a mean within ±0.2 per channel and a per-pixel error of 0.49–0.54 on three files. The fourth, with 13.7% clipped, gave B +1.5 and an error of 0.98. Trovolve could then apply whatever rendering it settles on. What WIC clipped cannot be got back.

The library holds no HEIC/JPEG pair of this kind, so how far this moves duplicate matching was not measured. What is clear is that the four platforms disagree on these files:

- **WIC:** the literal BT.709 reading.
- **libheif through Magick:** no conversion.
- **Android:** probably no conversion either (below).
- **Apple:** not measured.

## How the iPhone signals colour (survey of all 2,782 HEIC in Jamie's library, read only)

| Primary item's colour box(es) | HEVC VUI (primaries/transfer/matrix, range) | Files |
|---|---|---|
| ICC "Display P3" only | 12/1/6 full | 2,035 |
| ICC "Display P3" only | 2/2/6 full | 487 |
| nclx 12/1/6 full only | 12/1/6 full | 237 |
| nclx 2/2/6 full only | 2/2/6 full | 2 |
| none | 2/2/2 full | 21 (14 panoramas, 7 other wide pictures) |

Every file is 8-bit 4:2:0, full range, a grid of HEVC tiles. One file that this survey's parser listed with no colour box did carry an ICC according to both WIC and libheif, so treat the last row as approximate.

Under [H.273](https://www.itu.int/rec/T-REC-H.273), matrix 6 is BT.601, primaries 12 are P3-D65 and transfer 1 is the BT.709 OETF. HEIF allows a `prof`/`rICC` and an `nclx` colour box on one item ([imazen/heic #53](https://github.com/imazen/heic/issues/53) discusses a decoder that dropped one when both were present).

## How each decoder turns YCbCr into RGB and handles colour

**libheif** (Magick.NET in the tests)
- Takes the matrix and range from the `nclx` box when there is one.
- An unspecified matrix (2) falls back to BT.601: `get_Kr_Kb` has no case for 2, and `YCbCr_to_RGB_coefficients::defaults()` is 1.402 / −0.344136 / −0.714136 / 1.772 ([libheif nclx.cc](https://github.com/strukturag/libheif/blob/master/libheif/nclx.cc)).
- Its undefined profile is 2/2/2 with full range.
- It reports the ICC as the colour space when there is one ([imazen/heic #53](https://github.com/imazen/heic/issues/53)).
- Magick.NET converts to sRGB only when there is an ICC. With nclx alone it leaves the values as they are (measured: `GetColorProfile()` null).

**WIC's HEIF extension**
- Built on Media Foundation's HEVC decoder. The HEVC and AV1 codecs are separate Store extensions ([HEIF extension codec](https://learn.microsoft.com/en-us/windows/win32/wic/heif-codec)).
- Microsoft's documentation describes only the depth and gain pixel formats and encoding options, nothing about colour ([same page](https://learn.microsoft.com/en-us/windows/win32/wic/heif-codec); [native pixel formats](https://learn.microsoft.com/en-us/windows/win32/wic/-wic-codec-native-pixel-formats#heif-native-codec)).
- Measured here:
  - BT.601 full range for both VUI 6 and VUI 2;
  - the fixed bias of §1;
  - ICC passed through unchanged;
  - nclx converted to sRGB by the literal reading and reported as EXIF sRGB;
  - a context with no colour box reported as EXIF sRGB;
  - native output 32bppBGR only, with no planar interface.
- `IWICBitmapSourceTransform` is implemented, with any size granted, so the decoder reduces the picture itself when a scaler sits on the frame.
- For an unknown matrix, Media Foundation's own enumeration says to treat it as BT.709 ([MFVideoTransferMatrix](https://learn.microsoft.com/en-us/windows/win32/api/mfobjects/ne-mfobjects-mfvideotransfermatrix)). The HEIF decoder does **not** do that for these files, so it must read the stream's own signalling or default to BT.601 (inferred).

**Apple ImageIO** (`ImageIOPictureDecoder`)
- Trovolve asks for `CGImageSourceCreateThumbnailAtIndex` with the transform and a maximum size, then draws into an 8-bit sRGB `CGBitmapContext` with high-quality interpolation. ColorSync therefore converts whatever colour space ImageIO assigns, and ImageIO reduces the picture itself.
- Reports by others say that ImageIO follows a file's nclx when there is one ([rawmakase #962](https://github.com/pch/rawmakase/pull/962), for PQ/HLG HEIC) and assumes sRGB when there is no colour space ([cocoa-dev](https://lists.apple.com/archives/cocoa-dev/2017/Nov/msg00038.html)).
- **How it reads 12/1/6, and whether its rounding is biased, was not measured.** This needs a Mac (the map's "Measuring the Mac, iPhone and Android decoders").

**Android `ImageDecoder`**
- HEIF goes through `HeifDecoderImpl`, which asks `MediaMetadataRetriever` for an RGB frame (RGB565, RGBA8888, BGRA8888 or RGBA1010102) and passes on only the ICC data. It reads no nclx or colour aspects of its own ([HeifDecoderImpl.cpp](https://android.googlesource.com/platform/frameworks/av/+/refs/heads/main/media/libheif/HeifDecoderImpl.cpp)).
- So ICC files are probably colour-managed like libheif and nclx files probably shown unconverted (**inferred**). The YCbCr→RGB happens in the framework's frame decoder, unmeasured.

## What WIC's API offers, call by call

| Want | Call | Result on the HEIF decoder |
|---|---|---|
| YCbCr planes, to convert with Trovolve's own matrix and rounding | QueryInterface `IWICPlanarBitmapSourceTransform` | Not implemented |
| More precision, or unclipped values | `IWICBitmapSourceTransform::GetClosestPixelFormat` (48/64bpp, half, float) | 32bppBGR every time. Asking a converter for more only widens clipped 8-bit values |
| The file's colour description | `IWICBitmapFrameDecode::GetColorContexts` | ICC: the profile, unchanged (use it). nclx: an EXIF sRGB context after converting. No colour box: EXIF sRGB |
| A different nclx rendering | none | No decoder option. Undo it afterwards (§3) |
| Transform to sRGB | `IWICColorTransform::Initialize(source, carried, sRGB, 64bppRGBA)` | Works. Keep it for ICC files |
| Reduce without the linear-light shift | `IWICBitmapScaler` over 24/32bpp, or Trovolve's `Pixels.Resize` | Fant over 8-bit is a gamma-space area average (0.00 from Trovolve's). Over 64bppRGBA it averages in linear light |
| Decoder-side reduction (speed) | `IWICBitmapSourceTransform::CopyPixels` at a reduced size, or a scaler on the frame | Fast, but −1.0…−1.5 levels darker than reducing a full decode. Use it for all HEIC or for none |

## What to change in `WicPictureDecoder`, in order of certainty

1. **`Profile()`: treat EXIF ColorSpace 65535 ("uncalibrated") as no profile**, as it is not a colour space to convert from. Today such a JPEG is converted and reduced in linear light.
2. **One reduction route for every picture.** Do the colour transform at 8 bits, or after reducing, or make the reduction Trovolve's own `Pixels.Resize` from a common size, so that no picture is averaged in linear light while its copy is not. Decoder-side reduction (`IWICBitmapSourceTransform`) is fine if every format's decoder is used the same way. JPEG's DCT reduction and HEIF's are not alike, though, so measure before relying on it.
3. **A per-channel offset for HEIF output** of about +0.8 / +0.25 / +1.1, applied in floating point before the signature and sharpness are measured. It is tied to Microsoft's decoder version, so it would need a check that it still holds (for example, a HEIC known to Trovolve decoded at start). Only worth having if the ground truth shows that matching needs it.
4. **nclx files:** undo WIC's literal BT.709 reading and render as Display P3 with the sRGB curve, or as whatever Apple is measured to do. This waits on a measurement of ImageIO.

## Notes for the other tickets

- **#45 (steady signatures):**
  - A signature that ignores a global per-channel offset would make cause 1 harmless without any correction.
  - Reducing in linear light versus gamma space changes contrast measures. The 1.5× higher sharpness on small JPEGs through WIC is probably a route effect (DCT reduction or Fant), not a decoder one, since WIC's full-size JPEG decode equals libjpeg's. This is inferred and needs checking there.
- **#47 (score Magick and WIC):** score WIC only after the two-route bug (change 1 and 2) is fixed. Otherwise the panoramas measure a Trovolve bug, not WIC.
- **#44 (links besides pixels):** the 239 nclx photos from newer iPhones had no JPEG twin in this library. Pairing by metadata matters most where platforms disagree most.
- **#49 (how matching copes):** the bias of §1 (under 1.1 levels) can be corrected or tolerated. The nclx divergence (7–14 levels between platforms) cannot be tolerated by a 0.5-level limit. It needs one rendering chosen for all platforms, or a signature that ignores it.

## Method and limits

- **Data:** read-only. All 2,782 HEIC were surveyed by parsing their boxes and the SPS VUI. 30 HEIC and 24 JPEG were copied under anonymous names to a scratch folder: 14 with ICC, 12 with no colour box, 4 nclx.
- **Excluded:** two pairs that shared a name stem but were different pictures (differences of 64 and 96 levels).
- **Tool:** a probe written in C# for this research (WIC through COM, Magick.NET 15.1.0), built and run outside the repository. It is not committed.
- **Not measured:**
  - ImageIO and Android;
  - a Windows PC without the extensions;
  - other versions of the HEIF or HEVC extensions;
  - the 2 nclx 2/2/6 files;
  - matching outcomes, which are for #47 with the ground truth.
