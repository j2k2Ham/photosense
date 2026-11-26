# What ties an iPhone's HEIC to the JPEG made from it, besides the pixels

Research for issue #44, part of the map in #41 (decoding pictures without Magick.NET). Measured on 2026-11-25 on Jamie's iPhone library, read only. Counts and figures only: no file names, dates, places or identifiers.

## Short answer

- **Apple's `PhotoIdentifier` (MakerNote tag `0x002b`) ties a JPEG to its HEIC.** It is equal in 250 of the 261 HEIC and JPEG copies in the library. It differs in 0 of them. In the other 11, neither file has it. Never in the library do two different shots share it. Every pair that shares it but is not a copy is an original and its edited render.
- **The capture moment ties them as well:** `DateTimeOriginal` with `SubSecTimeOriginal`, and with `OffsetTimeOriginal` where both files have it. It is equal in 260 of 261 copies, and the remaining one has no capture time at all. No two different shots share it except an original and its edit. It is also what separates burst frames, which pixels cannot.
- **`ContentIdentifier` (`0x0011`) does not tie them.** The conversion writes a new one into the JPEG. It differs in 240 of the 243 copies where both files have one. The HEIC's matches its Live Photo video, and the JPEG's matches no video in the library. It is only good for linking the HEIC to its MOV, which Trovolve already uses it for. `BurstUUID` is shared by burst frames on purpose. `LivePhotoVideoIndex`, GPS, lens and exposure settings are shared by many unrelated photos.
- **The one false tie is an edit.** Apple keeps the whole MakerNote and EXIF in an edited render (`IMG_E…`), the identifier and capture moment included. So the rule needs a guard against edits. The current guard, the edit name, works but depends on the file's name. A guard from metadata alone works just as well here: the two files differ in `SceneFlags` (`0x0025`), `CustomRendered`, `Orientation`, the EXIF dimensions or `PhotoIdentifier`. That guard caught 471 of 474 original-and-edit pairs and none of the 253 copies.
- **A metadata rule finds the copies without reading a pixel.** The rule: the same `PhotoIdentifier` (or, where neither file has one, the same capture moment), and no edit marks. On this library it tied 253 pairs, all of them true copies, with 0 false ties. That is 253 of the 254 copies that are not edits. The one it missed has no capture time.
- **Every copy in this library came through Apple's own conversion.** The JPEG kept the full size, all of the EXIF, every MakerNote tag, the GPS and the Display P3 profile. It lost the HDR gain map, and its `ContentIdentifier` was rewritten. No copy in the library came through a route that strips metadata, such as WhatsApp or Telegram's compressed send. For those routes, metadata cannot help, and only pixels can tie the JPEG to its HEIC.

## How it was measured

- **The library:** Jamie's `Phone Pictures`, read in place and never written. It holds 2,782 HEIC and 2,736 JPEG files, Trovolve's removed-files folder included. All 5,518 were decoded, with no failures.
- **The tools:** a small console program, kept beside this note (`heic-jpeg-metadata/Program.cs`). It reads every tag with MetadataExtractor 2.9.3 (the version Trovolve uses), including the Apple MakerNote, the RunTime and XMP. It decodes every picture with Magick.NET Q8 15.1.0 into a 32 × 32 grey signature and a 64-bit difference hash, with auto-orient on. Python scripts, also kept beside this note, pair the files and count.
- **What counts as a copy (pixel ground truth, independent of metadata):**
  - A HEIC and a JPEG are nominated as a pair when their hashes are 8 bits or fewer apart, their shapes are within 2% of each other, and their signatures are 2.0 grey levels or fewer apart. These are Trovolve's `PhotoMatcher` limits on a signature of my own.
  - A pair 0.5 grey levels or fewer apart counts as the same picture.
  - 286 HEIC and JPEG pairs were nominated. 262 of them are the same picture (median difference 0.04, largest 0.48), and 24 are only look-alikes (0.5 to 2.0).
  - One of the 262 is a different shot: 0.48 apart, taken less than a minute apart, with a different `PhotoIdentifier`. That leaves **261 true copies**, involving 259 HEICs, 3 of which have two JPEGs each.
  - 9 of the copies are panoramas (`CustomRendered` 6), the largest of them 0.05 apart through Magick.
- **The library's state:** in 256 of the 262 pairs the HEIC is already in the removed-files folder and the JPEG is kept. An earlier clean-up chose the JPEG.

## Which tags survive, pair by pair

All 262 pairs (the 261 copies and the one different shot), counted as equal / differ / only one file has it / neither has it:

| Tag | Equal | Differ | One lacks | Both lack |
|---|---:|---:|---:|---:|
| `PhotoIdentifier` (Apple `0x002b`) | 250 | 1 | 0 | 11 |
| `ImageCaptureRequestID` (Apple `0x0020`) | 250 | 1 | 0 | 11 |
| `RunTime` (Apple `0x0003`, time since boot) | 251 | 1 | 0 | 10 |
| `AccelerationVector` (Apple `0x0008`) | 250 | 2 | 0 | 10 |
| `LivePhotoVideoIndex` (Apple `0x0017`) | 252 | 0 | 0 | 10 |
| `ContentIdentifier` (Apple `0x0011`) | 3 | 240 | 0 | 19 |
| `BurstUUID` (Apple `0x000b`) | 0 | 0 | 0 | 262 |
| `DateTimeOriginal` | 260 | 1 | 0 | 1 |
| … with `SubSecTimeOriginal` | 260 | 1 | 0 | 1 |
| … and `OffsetTimeOriginal` | 259 | 1 | 0 | 2 |
| GPS latitude and longitude | 258 | 0 | 0 | 4 |
| Exposure time, f-number, ISO, focal length, brightness | 222 | 39 | 0 | 1 |
| Lens model | 261 | 0 | 0 | 1 |
| XMP `photoshop:DateCreated` | 63 | 0 | 131 | 68 |

The "differ" in each row is the one different shot. Where a tag differs in a copy, the row says why:

- **Brightness** (`BrightnessValue`) is re-rounded in 39 copies, so the exposure tuple is not exact.
- **GPS horizontal error** differs in 36 copies.
- **`ContentIdentifier`** is rewritten in 240 copies, and **XMP** is mostly dropped from the JPEG.

What the JPEG keeps, in all 261 copies:

- **The MakerNote:** every tag the HEIC has is in its JPEG too, none lost, and every value is equal except `ContentIdentifier`.
- **The other fields:** `Make`, `Model`, `Software` and `ModifyDate` are equal wherever both files have them (so is `HostComputer` in the 251 that have it). The EXIF dimensions are equal in all but 2 pairs, where the JPEG is smaller.
- **The colour profile:** equal (Display P3) in 253 copies; in 9, neither file has one.

What the JPEG loses or changes:

- **The HDR gain map:** 2,706 of the 2,782 HEICs carry the XMP of an HDR gain map; 0 of the JPEG copies do.
- **The orientation:** in 1 pair the orientation differs, and in 5 only the JPEG has one.

**`ImageCaptureRequestID`, `RunTime` and `AccelerationVector`** tie a HEIC to its JPEG as well as `PhotoIdentifier` does. However, they are also kept by an edited render more often than `PhotoIdentifier` is: 443 HEIC-to-HEIC pairs share the request id, but only 80 share `PhotoIdentifier`. So `PhotoIdentifier` is the better key.

**The 11 copies with no `PhotoIdentifier`:**

- In 10, the HEIC itself has no Apple MakerNote. These are probably saved by an older iOS, or re-saved by an app.
- In 10 of the 11, the capture moment with sub-seconds is equal.
- In 1, neither file has a capture time.

## False ties: what else shares each value

Across the whole library, here is every HEIC and JPEG pair that shares a value, split by what the pixels say (same picture / look-alike / unrelated):

| Key | Same | Look-alike | Unrelated |
|---|---:|---:|---:|
| `PhotoIdentifier` | 250 | 13 | 4 |
| `ImageCaptureRequestID` | 250 | 13 | 4 |
| `ContentIdentifier` | 3 | 4 | 1 |
| `DateTimeOriginal` (whole seconds) | 260 | 14 | 13 |
| `DateTimeOriginal` + `SubSecTimeOriginal` | 260 | 14 | 5 |
| GPS latitude and longitude | 258 | 24 | 195 |
| `LivePhotoVideoIndex` | 252 | 21 | 332,404 |
| Lens model | 261 | 24 | 761,329 |

**Every one of the 17 pairs that share `PhotoIdentifier` but are not copies is an original and its edited render.** The same holds for the 19 pairs that share the capture moment with sub-seconds. Counting every format (HEIC with HEIC, JPEG with JPEG):

- 362 pairs share a `PhotoIdentifier`. The pixels agree in 259 and disagree in 103, and all 103 are an original and its edit.
- 733 pairs share the capture moment. The pixels disagree in 390, and all 390 are an original and its edit.

Without sub-seconds, a whole second ties 13 unrelated HEIC and JPEG pairs. So sub-seconds are needed.

**Burst frames** are the danger for pixels, and metadata separates them:

- **The bursts:** 35 bursts of 10 frames each, all JPEG (`BurstUUID` is on 350 JPEGs and no HEIC).
- **Pixels cannot separate them:** frames of the same burst are taken in the same second. 326 such pairs are 0.5 grey levels or fewer apart through Magick, 597 are 0.5 to 2.0 apart, 126 more than 2.0, and 17 cannot be compared.
- **Metadata does:** no two frames share a capture moment with sub-seconds, nor a `PhotoIdentifier`.
- **Other quick shots:** outside bursts, 3 HEIC-to-HEIC pairs taken in the same second are 0.5 or fewer apart, and metadata separates them too.
- **The near miss** that the pixels call the same picture (0.48, a different shot) is separated by both the identifier and the capture time.

**File names are not enough.**

- 257 of the 261 copies share their file name's stem: the HEIC's name with `.JPG`.
- But 14 HEIC and JPEG pairs share a stem and are different shots. iPhone numbers wrap at 9999, and a second phone reuses them.

## Guarding against edits

Apple's edited render is a new file (`IMG_E1234.HEIC` or `.JPG`). It keeps the original's capture moment and request id, and in up to 119 of 474 cases its `PhotoIdentifier` too. `ModifyDate` and `Software` are never changed by an edit, so they are no guard.

There are 474 pairs of an original and its edit sharing a capture moment, and 253 copies (HEIC with JPEG, no edit name, pixels agree). How often each tag differs:

| Differs between the two files | Edits (of 474) | Copies (of 253) |
|---|---:|---:|
| `SceneFlags` (Apple `0x0025`) | 444 | 0 |
| `CustomRendered` | 438 | 0 |
| `Orientation` (the edit is rendered upright) | 430 | 0 |
| EXIF width and height | 359 | 0 |
| `PhotoIdentifier` | 355 | 0 |
| Any of these five | **471** | **0** |
| `ModifyDate`, `Software` | 0 | 0 |

How the guards compare:

- **The five tags together** catch 471 of the 474 edits, every cross-format edit among them (22 of 22), and no copy.
- **The edit name** (`PhotoNaming.IsEditedRender`, today's guard in `PhotoMatcher`) catches all of them. But it relies on the name surviving the route: an export from Photos names the edit without the `E`.
- **A `.AAE` file** sits beside the original in 452 of the 474 pairs.

**The metadata rule, tried on the whole library.** The rule ties a HEIC and a JPEG when:

- they have the same `PhotoIdentifier`, or, where neither has one, the same `DateTimeOriginal` with `SubSecTimeOriginal` (and the same `OffsetTimeOriginal` where both have it);
- and none of `SceneFlags`, `CustomRendered`, `Orientation` and the EXIF dimensions differ.

The result:

- **253 pairs tied, every one a true copy.** No false tie, and none that the edit-name rule would refuse.
- **One copy missed** of the 254 that are not edits: the one with no capture time.
- **Seven copies missed that involve an edit.** Four are copies of an edit (the edited HEIC and the edited JPEG), where one file was rendered upright and the other was not. The other three pair an original with a JPEG named as an edit, which `PhotoMatcher` already refuses by name. To tie the four, compare the dimensions after orientation, not the raw `Orientation` tag. That needs care, as a turned-upright edit then looks the same.

## Which routes keep what

**What the library shows.** All 261 copies came out of Apple's own conversion, the image framework (ImageIO) carrying the metadata dictionaries across:

- the full size;
- every EXIF and MakerNote value, apart from a new `ContentIdentifier` and a re-rounded brightness;
- GPS and the Display P3 profile;
- a JFIF header (244), and XMP in only 66;
- no HDR gain map.

That fits every route where the iPhone or a Mac makes the JPEG: Photos' export as "Most compatible", AirDrop or iCloud set to "Most compatible", and "Transfer to Mac or PC: Automatic" on an import over a cable to Windows. The library cannot say which of them it was.

**What sources say about the other routes:**

- **iPhone "Transfer to Mac or PC: Automatic".** It converts to JPEG on import only when the computer is judged unable to read HEIC. Users of iOS 18 and Windows 11 report receiving HEIC instead, so how many JPEGs it makes varies. No source states what metadata it keeps; this library's copies show what an Apple conversion keeps. Sources: Apple Community threads, below.
- **iCloud (web and iCloud for Windows), "Most compatible".** It makes a JPEG of the photo, edits included. One user reports a download through iCloud for Windows missing most tags compared with the same file from the web, so metadata may depend on the app. Not measured here.
- **AirDrop and Photos' export as "Most compatible".** Both use ImageIO. Apple documents a MakerApple dictionary that ImageIO reads and writes, and this library's copies show it carried whole. No Apple page states it for each route.
- **WhatsApp**, sent as a photo: shrunk and stripped of EXIF, GPS included. Sent as a document: the file unchanged. So a WhatsApp copy keeps nothing to tie it to its HEIC, and only pixels can.
- **Telegram:** a compressed photo is stripped; a file sent uncompressed keeps its metadata (Telegram's own bug tracker).
- **Google Photos:** a download gives back the format stored, which is HEIC for an iPhone's original. Takeout keeps the file but puts edited dates and places in JSON sidecars. Sources disagree, and none was measured here.
- **Converters on Windows** (the Photos app's "Save as", ImageMagick, online converters): each decides on its own. ImageMagick now copies EXIF. None was measured here.

## What this means for the map

- **#49 (how matching copes with each decoder).** Pairing by metadata ties every Apple-made copy in this library without depending on the decoder: 253 of 254 non-edit copies, 0 false ties. That holds when WIC renders the HEIC 0.6 grey levels darker, or 3 to 10 levels off for panoramas.
  - It is a natural first test before the pixels. Pixels are still needed for copies whose route stripped the metadata, and as a loose backstop against an identifier that collides.
  - The rule's edit guard can replace the edit name, or sit beside it.
  - Today `PhotoMatcher` already uses the capture moment within 10 ms to refuse burst frames. The measurements confirm sub-seconds are needed: 13 false ties in whole seconds against 5, which are all edits.
- **#48 (a PC that cannot decode HEIC).** MetadataExtractor reads a HEIC's metadata without decoding HEVC. So on such a PC, Trovolve can still find a HEIC that has a JPEG copy, by metadata alone. That is 253 HEIC and JPEG pairs here, already found as copies. It cannot compare sharpness, nor find similar shots.
- **#46 (the ground truth).**
  - **Labels from metadata:** `PhotoIdentifier` and the capture moment label the 261 HEIC and JPEG copies (panoramas included). `BurstUUID` labels 35 bursts, 350 frames, as different shots that look the same. The edit name, `.AAE` files and the edit tags label 474 pairs of an original and its edit. The near miss at 0.48 is a labelled different shot.
  - **What it cannot supply:** these labels do not depend on any decoder. But the library has no copy that went through a stripping route (WhatsApp, Telegram compressed, a resize). The set needs such copies made from scratch copies of its own pictures, re-encoded, shrunk and stripped, to cover them.
  - **Burst frames in HEIC:** the library has none.
- **#43 (WIC's colour).** Every HEIC here carries a Display P3 profile (2,760 of 2,782) and an HDR gain map (2,706). Its Apple-made JPEG keeps the profile and drops the gain map. Whether WIC applies the gain map, or treats the profile differently, may be part of the even shift.

## Sources

- ExifTool's Apple MakerNote tag names (`BurstUUID`, `ContentIdentifier`, `ImageUniqueID`, `LivePhotoVideoIndex`, `ImageCaptureRequestID`, `PhotoIdentifier`, `RunTime`): https://exiftool.org/TagNames/Apple.html
- ExifTool's Apple module source: https://github.com/exiftool/exiftool/blob/master/lib/Image/ExifTool/Apple.pm
- `ContentIdentifier` must match between a Live Photo's still and its video (Apple Community): https://discussions.apple.com/thread/255584737
- Apple's ImageIO, `kCGImagePropertyMakerAppleDictionary` and image destinations: https://developer.apple.com/documentation/imageio and https://developer.apple.com/documentation/imageio/cgimagedestination
- HEIC to JPEG through ImageIO turns the gain map into Apple's own format or drops ISO 21496-1 (Apple Developer Forums): https://developer.apple.com/forums/thread/791283
- "Automatic" no longer converting on iOS 18 and Windows 11 (Apple Community): https://discussions-jp-prz.apple.com/thread/256087805, https://discussions-jp-prz.apple.com/thread/256121184, https://discussions.apple.com/thread/255973242
- Keep Originals and Automatic (Apple Community user guide): https://discussions.apple.com/docs/DOC-250002765
- iCloud "Most compatible" and "Unmodified original": https://discussions.apple.com/thread/255169252 ; tags missing from iCloud for Windows downloads: https://discussions.apple.com/thread/252110183
- WhatsApp and Telegram: https://www.fast.io/resources/whatsapp-telegram-signal-photo-metadata-preservation/ , https://bugs.telegram.org/c/1411 , https://citizenevidence.org/2020/04/20/sending-encrypted-photos-while-preserving-metadata/
- Google Photos downloads and Takeout: https://www.multcloud.com/explore/why-does-google-photos-download-as-heic-2223-ac.amp.html , https://discussions.apple.com/thread/256157477
- ImageMagick and EXIF on HEIC conversion: https://forensenellanebbia.blogspot.com/2018/09/converting-from-heic-to-jpg.html

The web sources on routes are mostly user reports and vendor pages. Where they disagree, this library's measurements are the evidence, and routes it does not contain are marked as not measured.

## The measuring code

These files are in `heic-jpeg-metadata/`, and they hold no personal data.

- **`Probe.csproj` and `Program.cs`** dump every picture's tags, and with `pixels`, its signature and hash, as JSON lines (`dotnet run -- <folder> <out.jsonl> [pixels|mov]`).
- **`pairs.py`** pairs by pixels and makes the tables above.
- **`guards.py`** compares the edit guards.
- **`edits.py`** finds the tags that mark an edit.

They were run on scratch output outside the repository. Nothing they write belongs in it.
