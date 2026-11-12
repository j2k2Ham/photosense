# Prompt: build the phone and tablet layout

Unzip this folder into the repo as `docs/design_handoff_photosense_mobile/`. Then paste everything below the line into Claude in your IDE, from the repo root.

---

Load the `photosense-page` and `photosense-phone-apps` skills. Read `docs/design_handoff_photosense_mobile/README.md` in full, and look at the screenshots it lists in `screenshots/iphone`, `screenshots/android`, `screenshots/ipad` and `screenshots/android-tablet`. Jamie chose the **Native** look. Ignore the "PhotoSense look" frames (1b and 1d) in the prototype.

To see the behaviour, open `docs/design_handoff_photosense_mobile/prototype/PhotoSense Mobile.dc.html` in a browser. The logic in `prototype/MobileApp.dc.html` (`seed`, `vBase`, `vLib`, `vCU`, `vOrg`, `vSend`, `vSheets`, `vOver`) is the spec for state and transitions. Do not copy the prototype code. Rebuild it in our stack.

**Where it goes.** Both phone apps show `PhotoSense.ReactUI` in their web view. So this is a second layout of the page, chosen when `useAbout().data?.program` is `iphone` or `android`. Below 768 px wide it is the phone layout; from 768 up it is the tablet layout. The desktop layout, and every endpoint's contract, stay as they are. Ask me before changing an API contract. Only the thin layer goes in `PhotoSense.iOS` or `PhotoSense.Android`: safe-area insets, the status bar's colours, and handing Android's back button to the page's history.

**Build it in this order, one reviewable step at a time:**

1. **Skins.** Add the four Native skins from the README's "Design tokens" as CSS variables: `ios-native-dark`, `ios-native-light`, `and-native-dark`, `and-native-light`. Set them on the root by platform and theme, and make them usable from Tailwind. Add **Auto** to the theme (it follows `prefers-color-scheme`).
2. **The shell.** Build the iPhone floating glass tab bar, Android's navigation bar, the iPad sidebar (with its hidden state and floating top bar) and Android's navigation rail. The tabs are Library, Clean up, Organize, Send. Add the root and pushed headers, the gear that opens Settings as a sheet, push and back through history, and the tablet split view for Clean up and Organize. Add the shared parts too: pill buttons, segmented controls, chips, grouped lists for each platform, sheets, the toast, the photo grid and the selection bar.
3. **First run and scanning.** This is screens 01–04, reusing `SetupScreen`'s logic and the photo-access endpoints.
4. **Library** (05–10), which is new. Build it on `organize/files` for the whole library, with the filters All, Not organized, No location and Videos, grouped by month. Add select mode with Send, Add to album / Move to folder, and Delete; and the viewer.
5. **Clean up** (11–18): the groups list, review, full-screen compare, and the delete questions. Reuse `GroupList`, `ReviewPanel`, `Differences` and `MatchChip` logic.
6. **Organize** (19–28): suggestions, map (`FolderMap`'s plain map on phones), options, preview, where it goes, name taken, moved, and recently moved. Reuse `OrganizeView`, `MovePreview`, `NameClashWindow` and `RecentMoves` logic.
7. **Send** (29–34): its own tab now, over the existing `send/*` endpoints and `SendStateDto.stage`.
8. **Settings** (35–37): appearance, photos, removed files with Delete permanently and its second question, history, errors, help and about.

**Rules that apply here** (from `CLAUDE.md`):

- 100% branch coverage of the page with Vitest. Report the measured figures.
- TSDoc on everything exported.
- Every removal question and message names the file that goes and the file that stays. The README's copy already does; keep it exact.
- Nothing is erased except by Delete permanently, and by deleting sent photos after the computer has confirmed them.
- Keep `STEPS.md` current: add these steps and tick each one once it is done and verified.
- Commit or push only when I ask.

After each step, run the page's tests and build, open the page at 402 × 874 and 412 × 892 (and at 1180 × 820 for tablets), and compare it with the matching screenshots in both themes. Then tell me what changed, what was verified and how, what was not verified, and any decision I have not confirmed.
