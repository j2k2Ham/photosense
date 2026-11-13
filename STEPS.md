# PhotoSense: what is left to do

Last updated 2026-11-11. Keep this file current: tick a step when it is done, add what comes up, and move a question to the step it settles once Jamie answers. CLAUDE.md loads it into every session.

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
- **Resilience** (2026-11-10, after a scan for fault tolerance, time limits and consistency; Jamie asked for all six gaps fixed, with a minute's limit):
  - A phone and a computer give up on each other after a minute's silence, with "The other device stopped answering. Try again."; connecting has the same minute. The two waits on the person comparing numbers have none. A side busy with its own work (an iCloud fetch, reading a file of the same name already there) says "wait" every 20 seconds (`TimedConnection`, `SecureChannel.MeanwhileAsync`).
  - Neither side can be left showing "Sending" or "Receiving": whatever error ends it, the state says it stopped. Photo access withdrawn mid-send is now said, not swallowed.
  - A file arriving on the computer is written through to the disk before the phone is told it is there.
  - Reading a file is asynchronous everywhere (`IPhotoFileSystem.OpenReadAsync`, `IsCopyOfAsync`, `IMediaDetailsReader.ReadAsync`, Live Photo ids, companions). On an iPhone a picture kept in iCloud is fetched without blocking a thread, can be stopped, and is given up after a minute without progress, counting as unreadable. This also removed a blocking wait the anti-pattern scan had missed.
  - Android answers no to a question of its own still up when the app's screen goes for good. Its blocking wait for that answer (`MainActivity.Ask`) is a named exception to rule 10, as `PageClient.Ask` is (Jamie, 2026-11-10).
  - Looking for an iPhone on a cable gives Apple's device service five seconds.
  - Consistency before availability held already: every change journaled and put back if cut short, removal checked against the scan, the phone deleting only what the computer confirmed by SHA-256. No circuit breakers: there is no shared remote service to protect.
- **Last measured:** service 830 tests at 100% of lines (3,573) and branches (1,984) (2026-11-11); page 805 tests at 100% (2026-11-11); end to end 81 tests, 62 in Playwright's Chromium and 19 in WebKit (2026-11-11); build tools 37 tests at 100%. On GitHub, `iphone.yml`, `android.yml` and `e2e.yml` passed on the Send push (daa8b20, 2026-11-11); `e2e.yml` now waits for the machine's own apt before installing browsers, after two failures of GitHub's machine, not the tests, on the Organize push (cca4e4a), whose phone checks passed.

## Remaining steps

### Now

- [x] See `iphone.yml` compile the iPhone app after the code-health work (pushed at Jamie's word, 2026-11-09; passed).
- [x] TSDoc on every export of the page (Jamie, 2026-11-09; done 2026-11-10).
- [x] Push the structure work (2026-11-10, at Jamie's word): `android.yml` passed.
- [x] See `e2e.yml` pass on GitHub: 70 of 70 on Ubuntu 24.04 (0a5d252, 2026-11-10). Its browser download failed under Node 26, which removed a call Playwright 1.56 made (`fs.rmdir` with `recursive`); the tests now use Playwright 1.64.1, which downloads both browsers here. It runs on Ubuntu 24.04 (26.04 had no browsers for 1.56), with three tries of five minutes for the download. Its first run past the download (68439d3) failed on the six WebKit tests fixed below and on two computer tests that had never run on Linux: one expected a folder chosen in the picker to be named with a backslash (Linux writes `Archive/Old trips`), and one read the picker's folder before the picker had gone there. Both are fixed; 70 of 70 pass here.
- [x] Fix what the `@phone` end-to-end tests find in WebKit (2026-11-10): all 14 pass, and all 70 in both projects.
  - Two faults in the page: the menu did not say "Runs on this device. Nothing is uploaded." where the bar has no room for it, though the bar's comment said it did; and Escape pressed the moment the phone said it was waiting closed the sending window without stopping the offer, as the window's key listener was still the one from before (it now always calls the window's latest close).
  - The rest were the tests expecting a computer's layout: on a phone they now open a group, go back to All groups, switch Organize between Files and Folders, and close Recently moved, as a person would (`allGroups`, `pane` in `fixtures/screens.ts`).
- [x] Push the commits that were waiting (done 2026-10-30, at Jamie's word).
- [x] See the main CI (`ci.yml`) pass on Linux, Windows and macOS (d5d6855, started by hand, 2026-11-10): 811 of 811 service tests on each, and on Linux 100% of lines and branches, the build tools and the page. The first run failed on one test that expected a video's time in Jamie's time zone; it now expects it in whichever zone the computer has. Linux's gate is now 100% (it was 90% and 85%).
- [ ] Merge the branch, or open a pull request, when Jamie decides.

### Phone and tablet design (Jamie, 2026-11-10)

The Native look of `docs/design_handoff_photosense_mobile` (iOS 26 on iPhone and iPad, Material 3 on Android), built as a second layout of `PhotoSense.ReactUI`, chosen when `about.program` is `iphone` or `android`: the phone layout below 768 px wide, the tablet layout from 768. The desktop layout and every endpoint's contract stay as they are; ask Jamie before changing a contract. Only safe areas, the status bar's colours and Android's back button go in the apps. One reviewable step at a time, each checked at 402 × 874, 412 × 892 and 1180 × 820 against the screenshots in both themes.

- [x] **1. Skins** (2026-11-10): the four Native skins as CSS variables in `app/globals.css` (`data-skin` on `<html>`: `ios-native-dark`, `ios-native-light`, `and-native-dark`, `and-native-light`), with each platform's type, radii and title, button and control sizes; usable from Tailwind (`bg-cell`, `text-acc`, `rounded-cell`, `font-code`, `shadow-bar`). `useTheme` has **Auto**, following the system and changing with it, applied before the first paint too; `useSkin` sets the skin by program and theme. Checked in Chromium as each phone and tablet in both system themes: the skin, background, text, accent, font and sizes are the handoff's.
- [x] **2. The shell** (2026-11-10), in `components/phone/`:
  - On a phone or tablet the page is `PhoneApp`, in place of the computer's bar and menu: the iPhone's floating glass tab bar, Android's navigation bar, the iPad's sidebar (with its hidden state, floating tabs and lowered titles) and Android's rail; each tab kept as it was when another is shown; root and pushed headers; the gear opening Settings as a sheet (`SettingsSheet`: theme with Auto, scan again, Delete permanently, errors, help, about).
  - Shared parts: buttons, the sticky footer, segmented controls, chips, grouped lists for each platform, sheets and confirm sheets, the toast (5.5 s on a phone), the photo grid with month headers, the selection bar, the line icons, the split view.
  - Back: `useBackLayer` gives each pushed screen and sheet an entry in the history; a review of a group is pushed over Clean up's list on a phone.
  - The apps: the page draws under the bars, with iOS's safe areas and Android's insets told as CSS variables; the status bar follows the page's theme on both; Android's Back and iOS's edge swipe go back in the page's history.
  - Clean up and Organize show today's screens inside the shell until steps 5 and 6; Library and Send have only their headers until steps 4 and 7.
  - Checked: page 642 tests at 100%; end to end 56 and 14 (the phone tests now go through the tabs); the Android app builds here with no warning; screenshots at 402 × 874, 412 × 892, 1180 × 820 and 1280 × 800, both themes, against the handoff's 11, 35 and the tablets' 02 and 05: bars, headers, gear, sidebar, rail and Settings match. On GitHub (4f6dc67) the iPhone app built and its self-check passed on the simulator, as did Android's on the emulator, and the end-to-end tests. Not checked: either app on a device.
- [x] **3. First run and scanning** (2026-11-10), in `components/phone/FirstRun.tsx`:
  - Welcome, the first time; **Continue** asks the phone's own question (new `POST library/ask`, Jamie's choice; the apps no longer ask as they start); allowed, the whole library is scanned at once. Refused, the photo access screen: **Open Settings**, **Ask again**; turned on and back in the app (iOS restarts it), the scan starts by itself.
  - Scanning covers the tabs: the percentage, time left, the count, and tiles (10 a row on a phone, 16 on a tablet) that fill with the pictures read (new `recent` on `scan/progress`, Jamie's choice). **Stop** (new `POST scan/stop/{id}`, Jamie's choice) keeps what was read. When it finishes the phone shows Library, with "Found 264 duplicates and 178 groups of similar shots." and **Review**. **Scan again** in Settings scans the library again.
  - In the apps the page is hidden until it knows it is the phone app (`data-app`, set before the first paint from the app's bridge; shown after three seconds whatever happens), which settles the computer's layout showing for a moment at launch.
  - The iPhone's photo question now says the design's words: "PhotoSense finds copies of the same photo and sorts photos into albums. Nothing leaves this phone unless you send it to your computer."
  - Checked: service 828 tests at 100% (3,547 lines, 2,008 branches); page 661 at 100%; end to end 58 and 16 (two new first-run tests); screenshots at 402 × 874, 412 × 892, 1180 × 820 and 1280 × 800 in both themes against the handoff's 01, 03 and 04; Stop on a real scan of Jamie's photo folder (read only, on a data folder of its own): stopped at 180 of 6,296, all 180 kept, the next scan carried on. On GitHub (8351dce) both apps built and passed their self-checks on the simulator and emulator, and the end-to-end tests passed. Not checked: a device; the real system questions; whether Android's WebView revalidates the access on coming back from Settings.
- [x] **4. Library** (2026-11-10), in `components/phone/Library.tsx` and `lib/library.ts`:
  - Every picture and video, latest first, by month, on `organize/files` for the library's one folder, shown as it is read; filters All, Not organized, No location, Videos, with counts; more drawn as the end comes into view. With only some photos allowed, the banner and **Choose more**.
  - Select: the count in the title, Cancel (iOS) or a cross (Android), Back stops it, the selection bar in place of the tabs. **Add to album** / **Move to folder**: the albums or folders there are, or a new one (`organize/plan` then `organize/apply`); **Delete** after the design's question, into PhotoSense Removed (`organize/remove`); both with Undo. **Send** opens today's sending window until step 7.
  - The viewer: the picture whole (the small preview first, as a computer takes 2–3 s to decode a large HEIC), when and where, where it is, its size, and the three actions.
  - Fixed on the way: a sheet that gave way to another (the album list to New album) was closed by the first one's late Back (`useBackLayer` now hands its entry over); Library's viewer and sheets are drawn over the whole page, as inside a tab they stayed under the tab bar.
  - Checked: page 690 tests at 100%; end to end 59 and 17 (a new Library test: viewer, new album, delete and Undo on the disk); screenshots at the four sizes in both themes against the handoff's 05–10 and the tablets' 01, with Jamie's photo folder (read only) as the library: 6,243 shown, the first after 6 s, all after 8 s. On GitHub (b22ddf0) both apps built and passed their self-checks, and the end-to-end tests passed. Not checked: on a device; a real phone's album rules on Android.
- [x] **5. Clean up** (2026-11-11), in `components/phone/CleanUp.tsx` and `lib/cleanup.ts`:
  - The list: how many duplicates and what they take, with **Delete all duplicates**; Duplicates / Similar; each group with two of its pictures, the file that stays, what there is to delete (or "all marked keep") and how many copies; 200 groups at a time, more read as the end comes into view (`useGroupsThrough`); when and how much the last scan read, with **Scan again**; what to say when nothing is left.
  - The review, pushed on a phone and beside the list on a tablet (the first group to start with, and the next once one is done): how they match and why, the original or best shot beside the copy, a chip for each of several copies, what differs (the copy's value marked) and what is the same, **Delete** *name* and **Keep this copy too** / **Marked keep. Tap to unmark**.
  - Full screen: a tap flips between the two, or the switch; previous and next between several copies; **Delete** from there too.
  - The questions and messages as the design words them, naming the file that stays and the file that goes, each with where it is; the message by where each is when the two share a name; Undo on each.
  - Clean up's state moved from the page into the phone layout (`useCleanUp`); the computer's screens are untouched. The access note moved with the design to Library alone.
  - Checked: page 727 tests at 100%; end to end 60 and 18 (a new Clean up test: review, full screen, delete with its question, Undo and Delete all, on the disk); screenshots at the four sizes in both themes against the handoff's 11–18 and the tablets' 02, over a scratch copy of 10 of Jamie's photos and videos with duplicates made among them. Fixed on the way: a video's preview in the list was pale in the light themes. On GitHub (193ca04) both apps built and passed their self-checks, and the end-to-end tests passed. Not checked: on a device; similar shots on real photos (the copy had none); a library of hundreds of groups.
- [x] **6. Organize** (2026-11-11), in `components/phone/Organize.tsx` and `lib/phoneOrganize.ts`, on Organize's own rules (`suggest`, `resolveNames`, `checkFolderName`, `mapLayout`):
  - Only what is in no album yet is sorted (`isUnsorted`: the top of the library; on Android also DCIM/Camera), so what was moved drops out of the suggestions by itself.
  - The list: Place / Date, Options, the options in a line; each suggestion with four of its pictures, size, dates and why it is named so, or the places inside a year; Moved on those moved; as dots on a plain map with the one picked under it; Your albums with New album; the files with no location, leading to Library's No location; Recently moved.
  - Options: group by, nearby places within 1–50 miles, landmark or town names (Town is new in the shared rules), what is inside each album or year, and which files.
  - The preview, pushed on a phone and beside the list on a tablet: the name, where it is made (Where it goes), what is inside, the names taken with Review each name (Going in and Already there, Add a number, another name, Leave it, or Delete either after a question naming the one that stays), the files by year, month or place, two rows and Show more, any tapped to stay; Move, with Undo; on a tablet what was done, with Undo.
  - Organize's computer screen is no longer shown on a phone; sending starts from Library's selection, as the design has it.
  - Fixed on the way: a pushed screen's subtitle was not shown on Android, Clean up's review included.
  - Checked: page 784 tests at 100%; end to end 60 and 18 (a new Organize test: a name taken numbered, the move and Undo on the disk; the sending tests now start from Library); screenshots at the four sizes in both themes against the handoff's 19–28 and the tablets' 03, over a scratch copy of 60 of Jamie's photos; over Jamie's whole photo folder (read only): 6,296 files read and 88 albums suggested after 7 s (1,307 with no location), the largest, 1,582 files, ready to move 4.5 s after opening. Not checked: on a device; Android's own question before a move (26); a move on a real phone's library.
- [x] **Distances in miles or kilometers** (Jamie, 2026-11-11: "always default to miles", with a setting used throughout, computer and phone): **Distances** in the computer's Settings window and the phone's Settings sheet, one setting (`distanceUnit` in `lib/settings.ts`, default miles); Organize's reach, its choices, the "Includes … (N km away)" notes and the map's scale all follow it (`lib/distance.ts`). Checked: page 791 tests at 100%; end to end, a new test in each layout (kilometers chosen in Settings regroups Organize and changes its words, and is kept); screenshots of Settings at the four sizes in both themes.
- [x] **7. Send** (2026-11-11), in `components/phone/Send.tsx` and `lib/send.ts`:
  - Its own tab: what to send (everything not in an album or folder yet, what was chosen in Library, or everything, each with how many files and their size, Live Photo videos with their pictures), Copy or move with what each does, how the computer finds the phone, and **Offer N files to a computer**. "Nothing chosen yet. Tap to choose." opens Library choosing; Library's Send (selection bar and viewer) comes back to Send with the choice.
  - Waiting for a computer (the pulse, what to do on the computer, **Stop offering**); the number (**It's the same number**, **It's different**); sending (the percentage, files and bytes, where they go, the file going now, **Stop sending**); sent (how many are on the computer, where, checked; with Copy **Delete N from this phone** after a question naming what goes and that the copies stay; with Move deleted by itself; **Done**); stopped (nothing sent or how many were, why, **Try again**).
  - The offer is held in PhoneApp (`useSend`), so it stands, and a move finishes, while other tabs show; the phone's state is asked every second only while Send shows or an offer stands.
  - New in the contract (Jamie, 2026-11-11): `current` on `send/state`, the name of the file going now (`SendState.Current`).
  - The old sending window and Send in the computer Organize's selection bar (shown only on a phone) are gone.
  - Checked: service 828 tests at 100% (3,551 lines, 2,008 branches); page 798 tests at 100%; end to end 61 and 18 (the three sending tests now go through the Send tab, one new: numbers said to be different, Try again and Stop offering); screenshots at the four sizes in both themes against the handoff's 29–33 and the tablets' 04, over the scratch copy of 60 of Jamie's photos, the phone's sending stood in for. Not checked: on a device, with a real computer; the system's own question before deleting (34).
- [x] **8. Settings** (2026-11-11), in `components/phone/SettingsSheet.tsx`:
  - Appearance (theme, Distances); Photos (**Photo access**: Full access or All photos and videos, Some chosen, None, opening the phone's own settings; Scan again); Removed files (**PhotoSense Removed** with how many items and their size, `GET removed` asked while the sheet shows; **Delete permanently…**, greyed when nothing waits); History (**Recently moved** with its count, opening the list with Undo; Errors); About (Help, About PhotoSense, **Privacy**, opening About at the privacy policy).
  - Delete permanently on a phone is its own two sheets: "Delete 37 items permanently?" (where they go, for how long, and that Undo can no longer bring them back), then "Are you sure?" with the switch "Do not ask me this second time again" (the computer's setting), unless it was turned off. The message: "37 items deleted. They wait in Recently Deleted for 30 days." The computer's dialogs are no longer shown on a phone.
  - Checked: page 805 tests at 100%; end to end 62 and 19 (a new phone test: Library's delete, Settings' count, both questions, the file erased on the disk, Settings empty after); screenshots at the four sizes in both themes against the handoff's 35–37 and the tablets' 05, PhotoSense Removed stood in for and every question cancelled. Not checked: on a device; the phone's own question before erasing.

### Security and safety (Jamie, 2026-11-10: for later)

After a review of security, the Delete permanently wording, complaints and transactions. Packages had no known vulnerabilities on 2026-11-10 (`dotnet list package --vulnerable`, `npm audit`), but nothing checks again by itself.

- [x] **Dependabot, CodeQL and secret scanning on GitHub** (turned on by Jamie, 2026-11-11; checked through GitHub's API the same day): Dependabot alerts and security updates; CodeQL's default setup (C#, JavaScript and TypeScript, Python, Actions; weekly and on pushes to `main` and its pull requests); secret scanning with push protection. Free while the repository is public; CodeQL and secret scanning need GitHub's paid plan once it is private. First results, on `main`: no Dependabot alerts, no secrets, and three CodeQL warnings (below).
- [x] **CodeQL's three warnings** (2026-11-11, at Jamie's word): every workflow's token may now only read the repository (`permissions: contents: read`): `ci.yml` and `benchmarks.yml`, which CodeQL flagged, and `e2e.yml`, `iphone.yml` and `android.yml`, which it would have flagged after the merge; none of their steps writes to GitHub. The partial escape of a file name made into a pattern was in three test helpers, not one (`tests/app/page.test.tsx`, `tests/components/phone/cleanup.test.tsx`, `e2e/fixtures/screens.ts`); all three now escape every special character, as `lib/organize.ts` does. Checked: page 805 tests at 100%, tsc clean for the page and the end-to-end tests, lint 0 errors and the 17 known warnings. Not checked: the workflows on GitHub, until pushed.
- [ ] **CodeQL on the feature branch** (Jamie, 2026-11-11): `codeql.yml` scans the four languages the default setup did, on pushes to `main` and `feature/mobile-and-mac`, pull requests to `main`, and on Mondays at 04:27 UTC (Jamie, 2026-11-11); C# without building it, as the default setup does. Written, not yet run. Before it is pushed, GitHub's default setup must be turned off (Settings › Code security › CodeQL analysis), as GitHub refuses an uploaded scan while it is on. Then see its first run, and read what it finds on the feature branch.
- [x] **The packages GitHub knows of** (2026-11-11, at Jamie's word, after `submit-nuget` failed on pull request #21): GitHub's automatic dependency submission restores every project it finds, and the phone apps cannot be restored on Linux without their workloads. `dependencies.yml` does the same for the solution only, with the action GitHub's own uses; Jamie turned the automatic one off (Settings › Code security › Dependency graph, no API for a personal account). Checked on f3b10af: `Dependencies` passed and no automatic submission started; `ci.yml` passed on Linux, Windows and macOS, as did CodeQL, the end-to-end tests, Android's emulator and the iPhone's simulator: every check on pull request #21 passes.
- [ ] **Dependabot's weekly updates** (Jamie, 2026-11-11): `.github/dependabot.yml` for NuGet (the solution and both phone apps), npm (the page and the end-to-end tests) and GitHub Actions, weekly, into `feature/mobile-and-mac` (the `target-branch` lines come out after the merge). Confirmed by Jamie (2026-11-11): the minor and patch updates of each ecosystem and folder come in one pull request, each major version in its own; the .NET SDK (`global.json`) is left out, as it moves with the iPhone's workload set and Xcode (`iphone.yml`). Dependabot reads the file from `main` only, so it does nothing from the feature branch: it needs committing to `main` too, or the merge. On `main` since 2026-11-11 (03b51ef); its first runs read all three ecosystems, the phone apps included, and opened pull requests. Its pull requests into the feature branch start no checks, as the workflows run on pull requests to `main` only: five majors merged that day (Magick.NET 15, Microsoft.Extensions 11, TypeScript 7 in `e2e`) left the branch not building, as Magick.NET 15 renamed `OrientationType` to `Orientation`. Fixed in the tree, with the notices brought up to date: service 830 tests at 100% (3,573 lines, 1,984 branches), build tools 37 at 100%, `e2e` type-checks under TypeScript 7. After pull request #21 was merged, nine more were merged into `main` (2026-11-12), failing pull request #22's checks: MetadataExtractor 2.9 made `GeoLocation` a struct, so the service did not build (fixed in `BasicExifMetadataExtractor`), and TypeScript 7 for the page stops its lint, as typescript-eslint refuses 7.0 (the page is back on 5.9.3, and `dependabot.yml` ignores TypeScript's major versions for now). `main` was merged into the feature branch to fix them there. The newer axe (4.14) also checks Label in Name (WCAG 2.5.3), which three of the computer's buttons failed, their names leaving out words shown on them: Organize's folder cards (now the name alone is the button, reaching over the card, with the counts and dates read as text beside it), Clean up's tiles of copies (the button now lies over the tile), and the folder-names menu (now "Folder names: Landmark or area"). Pull request #30 (2026-11-12) failed to install the page: `@vitest/coverage-istanbul` 5 came alone and requires Vitest 5 (back on 4.1.12; Dependabot now brings Vitest's packages together), and ESLint 10 is beyond `eslint-plugin-jsx-a11y`, which supports 9 at most (back on 9.39.5; its majors ignored for now). `@types/node` 26 (CI runs Node 26) and `actions/setup-node` 7 stay.
- [ ] **Harden the picture decoder:** Magick.NET decodes pictures from anywhere, the likeliest way in for a crafted file. Allow only the formats PhotoSense reads, refuse the rest (scripts, PostScript, SVG, files from a URL), and limit memory, area and size (`ResourceLimits` sets only one thread now), so that a bad file is only unreadable.
- [ ] **Firmer Delete permanently wording:** the first button names the count ("Delete 312 files permanently"); the second question's title says what happens ("Erase 312 files for good?") instead of "Are you sure?"; "This cannot be undone" stays at the top of the first question, as the second can be turned off.
- [ ] **A one-time "keep a backup" note on first launch:** "Keep a backup of photos you care about", with a link to the terms, recorded once seen. To ask Jamie: does it need a click to accept, or only to be put away?
- [x] **Two small transactions** (2026-11-11, at Jamie's word): replacing a second record of the same file, and the records of the photos a change or an undo moved, removed or brought back, are each written in one LiteDB transaction (`LiteDbTransaction.Together`; `IPhotoRepository.SaveAllAsync` and `DeleteAllAsync`, which `FileChanges` now calls once per change, reading every record first). All are kept or, should one fail, none. Moving files stays with the journal, which no database transaction can cover. Checked: service 830 tests at 100% (3,573 lines, 1,984 branches), a store test making the second of two writes fail and finding both records as they were; end to end 81 on a database file.

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
- **Resilience (2026-11-10):**
  - The minute's limit counts silence, not the whole send; "wait" every 20 seconds (a third of it) keeps a busy side's partner waiting. An iCloud fetch is given up after a minute without progress, not a fixed total. Apple's device service has five seconds to list the phones.
  - An error nothing expected ends a send with "Sending stopped. Try again." or "Receiving stopped. Try again.", without its own words.
  - Arriving files are written through to the disk as they arrive (`FileOptions.WriteThrough`); not measured on a slow external disk.
  - On Android, a question still up when the app's screen is closed for good counts as no, so nothing it asked about changes; a screen only redrawn still gets the answer.
- **Phone and tablet design, step 1 (2026-11-10):**
  - A phone with no theme chosen starts in **Auto**, as its own apps do; a computer still starts dark.
  - The skins use the handoff's own names, which the desktop's tokens share (`--bg`, `--t1` to `--t3`, `--stage`, `--sel`, `--keep`, `--amber`, `--ident`, `--ident-bg`, `--scrim`): on a phone the skin's values win, so the screens not yet rebuilt already take the skin's background, text and those colours. The desktop is untouched, having no skin.
  - The body text on a phone is the platform's size (17 px on iOS, 16 px on Android) instead of the desktop's 14 px.
- **Distances (2026-11-11):**
  - In kilometers the choices of reach are the same numbers, 1, 5, 15, 25 and 50 km, and the number chosen stays when the unit changes (5 miles becomes 5 km), rather than converted figures such as 8 km.
  - The setting is kept in the page's own storage, as the other settings are: on a phone in its web view, on a computer in each browser.
  - The service's "Near Anaconda" (more than 3 km from the town) shows no distance, so it is unchanged.
- **Phone and tablet design, step 8 (2026-11-11):**
  - Photo access says "Some chosen" when only some photos are allowed: the phone does not tell PhotoSense how many (the handoff shows "214 chosen").
  - Delete permanently's first question also says how much it frees ("Everything in PhotoSense Removed (212.0 MB) goes to Recently Deleted…").
  - Delete permanently… is greyed and does nothing while nothing waits, or before the service has said; the computer instead opens its warning, saying there is nothing to delete.
  - Settings has a Distances row under Appearance, which the handoff does not (Jamie's request of 2026-11-11).
- **Phone and tablet design, step 7 (2026-11-11):**
  - The computer's menu item is named as it is, "Choose **Receive from phone** in its menu.", where the handoff says "Choose Receive from a phone."
  - What was chosen in Library counts items (pictures and videos as Library shows them), as the handoff does; the other two count files, Live Photo videos included, with their size. What the phone then offers (its `total`) also counts edit files.
  - The question before deleting after Copy is titled by what goes ("Delete 58 photos and 2 videos from this phone?", or the file's name when one) and lists up to three names; its button says "Delete 58 photos and 2 videos". The card's button is the handoff's "Delete 60 from this phone".
  - "Stopped" says how many are on the computer when some were sent; the handoff's "The numbers did not match. Check that you picked this phone on the computer, then try again." is said when the person said they were different, and otherwise the phone's own reason. **Done** is offered beside **Try again**.
  - Sizes are written as the rest of the app writes them ("18.40 GB", where the handoff has "18.4 GB").
  - **Stop sending** forgets what was sent so far; what is on the computer stays there, and nothing is deleted from the phone.
- **Phone and tablet design, step 6 (2026-11-11):**
  - **New album** in Organize names an album that is made, in the default place, when files are first put in it from Library: the service makes no empty album. It is offered in Library's album sheet, and forgotten when the app closes.
  - Your albums lists only the albums named this way, as the prototype does, not every album there is.
  - The files with no location are listed by place only: by date they go into their year.
  - Where it goes offers, on an iPhone, the top of the library and every folder of albums made earlier; on Android, Pictures, DCIM, and the folders made inside DCIM, Pictures or Movies.
  - Show more adds five rows at a time until a section is all shown, then Show fewer; the handoff shows five rows and then Show fewer, which would leave the rest of a large year out of reach.
  - A move Android refuses says the service's words ("You chose not to let PhotoSense change these photos. Nothing was moved…") in place of "Nothing was moved."
  - The names-taken card names the album and year when all are in one ("Anaconda › 2023"), otherwise the album.
  - Undo of a move says "Put back. Every file is where it was." only when everything came back; otherwise how many could not.
  - On Android a destination leaves the phone unsaid ("Pictures/Anaconda"), as the handoff's words table has it.
- **Phone and tablet design, step 5 (2026-11-11):**
  - A copy marked keep cannot be deleted from its review until it is unmarked, as on the computer; the design shows its Delete button as usual.
  - Unmarking says "IMG_1.JPG is no longer marked keep."; the design says nothing. Marking a similar shot says only that it is marked keep, as Delete all is for duplicates.
  - Why a copy is in its group is worked out from the two files ("Same bytes, saved three times.", "Converted and resized.", "The same picture, as JPEG and as HEIC."); the design's notes, such as "The JPEG was made from the HEIC.", say which was made from which, which PhotoSense does not know.
  - "Delete IMG_1.HEIC" deletes only the copy shown, as the design has it; the computer's "Delete all N copies" of one group is not offered on a phone.
  - Undo says "Put IMG_4299.HEIC back where it was.", as the computer does.
  - A video full screen shows its play mark only, as in Library: the phone plays no video yet.
- **Phone and tablet design, step 4 (2026-11-10):**
  - "Not organized" is at the top of the library (the design's rule); on Android, where nothing is at the top, it also counts DCIM/Camera, where the camera puts photos.
  - The albums or folders offered are those the library's files are in; a new album is made at the top of an iPhone's library, a new folder in Pictures on Android.
  - A name already taken in the album gets a number ("IMG_1 (1).HEIC") without asking; Organize's own Name taken screen comes in step 6.
  - A Live Photo's video is not shown on its own; it goes with its picture.
  - A video opened shows "PhotoSense cannot play videos yet." with its details, as the phone serves no video for Organize's files.
  - Select is offered once the library has been read through.
  - After a move or deletion Undo says "Moved 3 items back."
- **Phone and tablet design, step 3 (2026-11-10):**
  - A phone scans its whole library, recursively, keeping earlier results; a first scan, "Scan again" and coming back with access all do the same.
  - "Found …" counts duplicate files (the copies a bulk delete would take) and groups of similar shots, as the design does; with neither, "Found no duplicates or similar shots." and no Review. Until the files are counted the scan says "Counting your photos and videos…"; time left uses the computer's wording ("About 4 minutes left", "Less than a minute left").
  - Ask again still refused says "PhotoSense still can't see your photos. Turn on access in Settings."
  - Welcome says "Runs on this iPad" on an iPad, as Settings does; the handoff says "tablet".
  - Where the first-run has got to is kept in the page's own storage (`photosense-first-run`); a phone that cannot keep it is welcomed again each time.
- **Phone and tablet design, step 2 (2026-11-10):**
  - Tapping the tab showing goes back to its first screen. An error on a phone stays until its OK is pressed, and is listed under Errors in Settings.
  - Settings on a tablet says "Runs on this tablet" for Android and "Runs on this iPad" for an iPad.
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
- The end-to-end accessibility checks run at a phone's size but not as the phone app, so no skin has been checked for contrast by them yet; the rebuilt phone screens need checks of their own.
- The minute's limit and iCloud fetching are tested with a stand-in clock and library; neither has met a real phone that sleeps, loses the Wi-Fi or fetches from iCloud. The iPhone's new fetch (`PhotoKitLibrary.OpenAsync`, using `RequestData` and `CancelDataRequest`) and Android's change are checked only on GitHub's simulator and emulator, where both passed with the end-to-end tests (9319afb, 2026-11-10).

## For later

- Sending from a computer to a phone, and from one phone to another; resuming a send cut short; sending while the app is in the background.
- Sending from Android by cable (through adb).
- A Recycle Bin option for Delete permanently on a computer.
- Replacing Magick.NET with the systems' own decoders (decision guide).
- Apple's maps on the iPhone, in a native map screen (no Google key is built into the phone app, so it shows the plain map).
- Moving the page's tests to Vitest 5.
