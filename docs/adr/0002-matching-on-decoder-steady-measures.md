---
status: accepted
---

# Matching on measures that stay steady across decoders

Each platform decodes pictures with the system's own decoder (WIC, ImageIO, Android's), and no two render a HEIC to exactly the same pixels. Trovolve's limits for the same picture had been measured on Magick.NET's pixels, so through WIC it found 9 duplicate groups where Magick.NET found 263. Rather than measure the limits again for each decoder, Trovolve compares pictures in ways a decoder's colour cannot move, and corrects the one decoder whose HEIC rendering is known to be off. Judged on a ground-truth set from Jamie's library, this finds as many duplicates through corrected WIC as through Magick.NET, with no more false matches (Jamie, 2026-11-26 and 2026-11-27; issues 45, 47, 53, 54, 55 and 49).

## The decision

- **One way of reducing:** every decoder hands back the picture whole, and Trovolve's own `Pixels` reduces it, so the same file reads alike through every decoder.
- **Duplicates** are decided by luma with each picture's mean and contrast taken out. **Similar shots** are decided by a normalised luma gradient. Both are worked out when two pictures are compared, from the 32×32 grid already stored, and the perceptual hash still nominates the pairs to compare.
- **The limits stay as they were:**
  - 8 hash bits to nominate, and shapes within 2%.
  - 0.5 for the same picture when both files have a capture time, or only one does.
  - 0.2 when neither has one.
  - 2.0 for similar shots.
- **Blurry** is today's Laplacian variance at 512 pixels, below 170 in place of 400.
- **WIC's HEIC is corrected after decoding:** its fixed bias is added back before the colour transform, with a check at start-up on a HEIC of known values. Every platform renders nclx 12/1/6 as Display P3 with the sRGB curve, converted to sRGB. nclx 2/2/6 is shown as it is.
- **An iPhone's HEIC and JPEG are paired by metadata** (Apple's `PhotoIdentifier`, or the capture time to the sub-second with a guard against edits) only where a HEIC cannot be decoded, on a PC without the HEVC extension. Everywhere else the pixels decide.
- **A file saved again with every pixel unchanged** (only its date, place or Live Photo settings changed) is a duplicate, not an edited version.

## Considered options

- **Measuring the limits again for each decoder:** four sets of limits, each to be measured again whenever Apple, Google or Microsoft changes a decoder.
- **Correcting every decoder's rendering to match one:** needed for WIC's known bias, but it alone left iPhone pairs up to 0.49 apart against a limit of 0.5.
- **Magick.NET everywhere:** it ships LGPL code and an HEVC decoder of Trovolve's own (ADR 0001).

## Consequences

- With corrected WIC: 356 of 361 duplicates are found (Magick.NET 348 by the old measure). 469 of 554 edited versions show as similar shots. 2 of 993 similar pairs are called duplicates, both re-saves with unchanged pixels and so duplicates by this decision. 1 of 20,000 random pairs is matched as a duplicate.
- A missed duplicate is shown as a similar shot, where the person chooses. Accuracy comes before catching every copy: the limit with no capture time stays at 0.2 because the closest burst frames are 0.19 apart.
- Cropped edits are not matched: a crop fails the shape check. Matching them needs a comparison of its own, a later effort.
- Which copy Clean up keeps is unchanged: a re-save whose details were corrected can lose to its original on file size or folder. Preferring the corrected copy is left for later.
- Changing the measures means every library reads each picture again (`AnalysisVersion`).
- The same file through two decoders is too far apart to compare (a median of 0.22, 1.30 at the 99th percentile), so each record names the decoder and measures that made it, and two pictures are compared by their pixels only when both were made the same way; otherwise only by checksum and metadata. A re-read left unfinished therefore misses duplicates across the line until it is finished, and Clean up says how many pictures are still to be read again (decided by Jamie, 2026-11-27).
- ImageIO and Android's decoder are still to be measured on the set: the rendering chosen for nclx files is to be confirmed against ImageIO on a Mac.
