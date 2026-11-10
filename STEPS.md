# PhotoSense: what is left to do

Last updated 2026-11-10. Keep this file current: tick a step when it is done, add what comes up, and move a question to the step it settles once Jamie answers. CLAUDE.md loads it into every session.

## What Jamie is asking for, and why

Jamie backs up the photos and videos from an iPhone, and has often backed up the same ones more than once by accident. PhotoSense is the tool to tidy that up:

- **Clean up** finds exact duplicates and similar shots, keeps the best copy of each, and lets the rest be removed after a review that shows what goes and what stays.
- **Organize** sorts a folder's files into folders by place and date, with a preview before anything moves.

**Accuracy comes first, and nothing is ever lost by mistake.** Removed files wait in `_PhotoSense_Removed`, where Undo can still bring them back, until **Delete permanently** erases them. Every question names the file that goes and the file that stays, because Jamie has twice taken two files for one. All three parts (service, page, build tools) are tested to 100% of branches.

On 2026-10-27 Jamie asked for more:

- PhotoSense on the phone itself, iPhone first, then Android, seeing the phone's library with its permission, with Clean up and Organize there.
- Moving photos from the phone to a Windows or Mac computer over Wi-Fi or a cable, with the choice to delete them from the phone afterwards.
- Every change journaled, so it can be undone cleanly.
- The computer program running on a Mac too.

On 2026-10-28 Jamie asked for it to be **ready to sell**: on the App Store, and for Windows and Mac. On 2026-10-30 Jamie asked for **end-to-end automation tests** of the whole product before moving on.

## Where it stands

Everything is on branch `feature/mobile-and-mac`, which has not been merged and has no pull request (Jamie: "don't create a PR").

- **Computer program** (Windows and Mac): Clean up, Organize, Delete permanently, About, the legal pages, versioning, notices, an accessibility pass. Done.
- **iPhone app:** library access, scanning, Clean up, Organize into albums, removal into a PhotoSense Removed album, and Delete permanently into Recently Deleted. Its self-check passes on GitHub's simulator. Never run on a real iPhone.
- **Android app:** the phone's own folders, removal into `_PhotoSense_Removed`, and Delete permanently into Android's bin. Its self-check passes on GitHub's Android 14 emulator. Never run on a real phone.
- **Sending from a phone to a computer:**
  - Finding each other on Wi-Fi (Bonjour), or by cable for an iPhone (Apple's usbmux).
  - Pairing once with a six-digit number shown on both, then encrypted, each file checked by SHA-256.
  - Copy or Move; Move deletes the photos from the phone once they are all on the computer.
  - Checked on this PC with a stand-in phone. Never tried with a real phone.
- **End-to-end tests** (`e2e/`, Playwright):
  - 56 tests on the computer project pass, accessibility checks of every screen in both themes included.
  - The `@phone` tests in WebKit have not run yet: the WebKit download was still finishing.
- **Code health** (2026-11-09, after a health check graded code quality C, the API surface C and documentation D):
  - Every type and member is documented (CS1591 now an error in CI); test classes and helpers too, test methods by their names.
  - No `InternalsVisibleTo` (Jamie, 2026-11-09): each project's public front door is all the tests use (CLAUDE.md). Endpoints are tested through the router, the stores through `AddPhotoSenseCore`, the server through `PhotoSenseServer.Build`.
  - Structure (2026-11-10): each layer registers its own services (`AddPhotoSenseApplication` is new); Domain has no packages and holds the pure rules (`PhotoMatcher`, `NameRules`, `PhotoNaming`, `PhotoFileEnumerator`); `CompanionFileFinder` and `ScanRunner` moved to Application; the settings validator to the server; the in-memory repository to the tests; the scan log is the registered service's own, no longer one static queue.
  - The page: TSDoc on every export, checked by `tests/documentation.test.ts`.
  - No test touches the database (Jamie, 2026-11-10) but the LiteDB stores' own, which use LiteDB in memory; checked by making the opening of a database file throw, with every test still passing. Opening the file is left out of coverage.
  - Confirmed by Jamie (2026-11-10): the newly public types, the two guards left out of coverage, the public MetadataExtractor helpers, and Android's blocking `PageClient` as a named exception to rule 10.
  - Every async method takes a required token and passes it on (CA2016, CA1068); clocks come from `TimeProvider`.
  - Anti-patterns removed, apart from those Jamie accepted (CLAUDE.md rule 10). A picture a decoder cannot read is an `UnreadableImageException`.
- **Last measured:** service 811 tests at 100% of lines (3,435) and branches (1,978) (2026-11-10); page 590 tests at 100% (2026-11-10); end to end 70 tests, 56 in Playwright's Chromium and 14 in WebKit (2026-11-10); build tools 37 tests at 100%. On GitHub, `iphone.yml` and `android.yml` passed on the code-health push (2026-11-09).

## Remaining steps

### Now

- [x] See `iphone.yml` compile the iPhone app after the code-health work (pushed at Jamie's word, 2026-11-09; passed).
- [x] TSDoc on every export of the page (Jamie, 2026-11-09; done 2026-11-10).
- [x] Push the structure work (2026-11-10, at Jamie's word): `android.yml` passed.
- [ ] See `e2e.yml` pass on GitHub. Its browser download failed under Node 26, which removed a call Playwright 1.56 made (`fs.rmdir` with `recursive`); the tests now use Playwright 1.64.1, which downloads both browsers here. It runs on Ubuntu 24.04 (26.04 had no browsers for 1.56), with three tries of five minutes for the download. Its first run past the download (68439d3) failed on the six WebKit tests fixed below and on two computer tests that had never run on Linux: one expected a folder chosen in the picker to be named with a backslash (Linux writes `Archive/Old trips`), and one read the picker's folder before the picker had gone there. Both are fixed; 70 of 70 pass here.
- [x] Fix what the `@phone` end-to-end tests find in WebKit (2026-11-10): all 14 pass, and all 70 in both projects.
  - Two faults in the page: the menu did not say "Runs on this device. Nothing is uploaded." where the bar has no room for it, though the bar's comment said it did; and Escape pressed the moment the phone said it was waiting closed the sending window without stopping the offer, as the window's key listener was still the one from before (it now always calls the window's latest close).
  - The rest were the tests expecting a computer's layout: on a phone they now open a group, go back to All groups, switch Organize between Files and Folders, and close Recently moved, as a person would (`allGroups`, `pane` in `fixtures/screens.ts`).
- [x] Push the commits that were waiting (done 2026-10-30, at Jamie's word).
- [ ] See the main CI (`ci.yml`) pass on Linux. It runs only for `main` and pull requests, so it has never run on this branch: several service tests that rely on file behaviour have only run on Windows.
- [ ] Merge the branch, or open a pull request, when Jamie decides.

### Trying the apps on real devices (by hand, as Jamie agreed)

- [ ] **iPhone:**
  - Enroll in the Apple Developer Program ($99 a year, as an individual). Set up TestFlight's secrets in GitHub (the workflow skips that job until then), then install the app.
  - Try: the permission questions, scan speed on a real library, Organize into albums, removal and Undo, Delete permanently and iOS's question.
- [ ] **iPhone to computer:**
  - Over Wi-Fi: the local-network permission question, pairing, Copy and Move, and whether Move's deletion question appears.
  - Over a cable: through Apple Devices or iTunes on Windows.
  - Sending a large video, and what happens when the app goes to the background or the screen locks.
- [ ] **Android:**
  - Install the APK on a phone, and try the permission questions, including choosing some photos only on Android 14.
  - Move and delete camera photos, which Android asks about once per batch, and check Undo.
  - Try photos in Download and in WhatsApp's folders, whose moves Android may refuse.
  - Send to a computer over Wi-Fi.
- [ ] **Mac:** run the computer program on a real Mac (only GitHub's macOS machines have run it), and receive photos from a phone there.

### Before selling (packaging is on hold until the app is fully built and tested)

- [ ] Windows installer and code signing. Mac signing and notarization.
- [ ] App Store listing, privacy labels and review; Google Play listing (an app bundle, an upload key, the permission declarations).
- [ ] A lawyer's review of the terms and privacy policy. A licence file. Hosting for the web site (`tools/site.mjs`).
- [ ] Make the repository private before the apps are public (Jamie's decision).
- [ ] A Google Maps map ID for release (`NEXT_PUBLIC_GOOGLE_MAPS_ID`). Not needed if Apple's maps are chosen.
- [ ] A screen-reader pass by a person (VoiceOver, NVDA, TalkBack), and Dynamic Type on the iPhone.

## Questions waiting for Jamie

1. **The decision guide:** https://claude.ai/artifact/TtTWByv3zcyTDfzn672Ew1 (not read yet; held until then).
   - **HEIC on Windows:** keep Magick.NET (LGPL) or move to the systems' own decoders?
   - **Maps:** Apple's (free with the developer membership on the phone; MapKit JS on a computer, unconfirmed for a page on the user's own machine) or Google's?
   - **How to sell:** subscription, free with purchases, or one price?
   - **Which licence?**
2. **Export compliance:** the apps now encrypt what they send to a computer (AES-GCM, ECDH), but the iPhone app still tells Apple it uses no encryption that needs declaring (`ITSAppUsesNonExemptEncryption` is false). Apple asks about this before release.
3. **Delete all duplicates:** its message says "Moved 3 duplicates (…) to _PhotoSense_Removed" without naming the originals that stay. Should it, as every other removal message does?
4. **Android, fewer questions:** Android's "media management" access (MANAGE_MEDIA) would let PhotoSense move and delete without asking each time, if the person allows it in Settings. Should it be offered?
5. **Android cable:** sending to a computer by cable works for an iPhone only. Is Wi-Fi enough on Android?
6. **Android Organize:** a folder made at the top of the phone's storage is refused, as Android requires. Should Organize start in DCIM/Camera on Android instead of the whole phone?
7. **Paired devices:** there is no way yet to forget a paired phone or computer. Is one wanted?

## Call-outs

### Decisions made that Jamie has not confirmed

- **Sending:**
  - The phone waits for a connection, on port 47615 or any free one, and the computer connects to it.
  - The number is compared once per pair of devices.
  - Only photos the computer confirmed whole, every file of each, are deleted from the phone.
  - With Move the deletion happens by itself at the end, with the phone's own question but no extra one from PhotoSense.
  - With Copy, PhotoSense asks first, naming what goes and that the copies on the computer stay.
- **iPhone:**
  - An album emptied by Undo stays in Photos: iOS asks the person before an app deletes an album, which hung the simulator's check once.
  - Removed photos are not hidden: with the Hidden album locked, as it is by default since iOS 16, apps cannot see hidden photos at all.
- **Android:**
  - The storage is shown as **Phone**, and folders go only inside DCIM, Pictures or Movies.
  - What is removed from the whole phone waits in `Pictures/PhotoSense Removed`.
  - Copies are refused, as on the iPhone (Jamie: copy only from a phone to a computer).
  - Android 11 is the oldest version supported.
- **Code health (2026-11-09):**
  - A copy an undo could not delete, nor give back its name, is now named by the hidden name it is under ("left as .IMG_2.JPG….undo").
  - An empty picture is counted as unreadable without being decoded; before, the decoder's error said so.
  - The phone no longer answers a computer that has said the numbers do not match: the computer never read it, and the answer could fail as the computer hung up, hiding the real reason.
- **Page:** the light theme's faintest text (`--t3`) is a little darker (#646a6e), so it passes WCAG AA on the compare window's grey.
- **End-to-end tests:**
  - What only a phone or another device answers is stood in for in the browser: being the phone app, its photo access, a phone on the network, sending.
  - Chrome and WebKit run; Firefox does not.
- **From earlier rounds:** the decisions listed in Claude's notes for those rounds are also unconfirmed. Among them: Organize's ids, the name-clash rules, the bin's question in the name-clash window, and searching for `_PhotoSense_Removed` folders beyond the ones named.

### Known limits

- Videos have no thumbnail picture; that needs a video decoder (ffmpeg), which is not bundled.
- A first scan of many HEIC files is slow on a computer (about 13 minutes for 6,940 files on Jamie's laptop); later scans skip unchanged files. On the simulator, one scan of 9 photos took 170 seconds and another took 6.
- A video's shown date is in this computer's time zone, while a picture's is the wall-clock time where it was taken.
- When a Live Photo's picture is removed and the kept copy is in another folder, the video goes with the removed picture.
- Heavily re-compressed copies are listed as similar shots, not duplicates, on purpose.
- The iPhone and Android apps cannot be built on Jamie's PC without admin rights, for the missing Mono pack. The Android app builds with the workaround in CLAUDE.md, and neither has an emulator or simulator here.
- 17 react-hooks compiler warnings are known and left.

## For later

- Sending from a computer to a phone, and from one phone to another; resuming a send cut short; sending while the app is in the background.
- Sending from Android by cable (through adb).
- A Recycle Bin option for Delete permanently on a computer.
- Replacing Magick.NET with the systems' own decoders (decision guide).
- Apple's maps on the iPhone, in a native map screen (no Google key is built into the phone app, so it shows the plain map).
- Moving the page's tests to Vitest 5.
