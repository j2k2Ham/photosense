# Handoff: PhotoSense for iPhone, iPad and Android

## Overview

This is the phone and tablet design for PhotoSense: the same Clean up and Organize as the desktop, plus a Library of every photo and a Send to computer tab, with a bottom tab bar on phones and a sidebar or rail on tablets. Jamie chose the **Native** look. Each platform uses its own controls (iOS 26 Liquid Glass on iPhone and iPad, Material 3 Expressive on Android), with PhotoSense teal as the accent. The PhotoSense look shown in the prototype was not chosen and is not specified here.

## About the design files

The files in `prototype/` are **design references made in HTML**. They show the intended look and behaviour; they are not production code to copy. The job is to rebuild them in PhotoSense's existing stack.

Both phone apps already show `PhotoSense.ReactUI` (Next.js 16, React 19, SWR, Tailwind 3) in a web view, with `/api` answered in-process (`InProcessApi`). So this design is built **in the React page**, as a phone and tablet layout chosen when `useAbout().data?.program` is `iphone` or `android`. The desktop layout stays exactly as it is. "Native" here means the page reproduces each platform's look with CSS. The only parts that really are native are the system dialogs listed under "System dialogs".

## Fidelity

**High fidelity.** Colours, type, sizes, radii, spacing, copy and states are final. Rebuild them exactly, using the page's own components and patterns.

## How to view the prototype

Open `prototype/PhotoSense Mobile.dc.html` in a browser, with every file in `prototype/` kept beside it. The page shows the Native iPhone (1a) and Native Android (1c) frames; 1b and 1d are the PhotoSense look, which was not chosen, so ignore them. Use the **Device**, **Theme** and **Start at** switches at the top. `prototype/PhotoSense Mobile Screens.dc.html` lays out every state; set **looks** to "Native" in its Tweaks.

`prototype/MobileApp.dc.html` holds the behaviour. Its `class Component` is the spec for state and transitions: `seed()` (each named state), `vBase` (navigation), `vLib`, `vCU`, `vOrg`, `vSend`, `vSheets`, `vOver`. Use it as a spec, not as code to copy.

## Screenshots

`screenshots/` has every state, Native look only, in dark and light:

- `iphone/{dark,light}/NN-name.png`: 402 × 874 (iPhone 17 Pro)
- `android/{dark,light}/NN-name.png`: 412 × 892 (Pixel 9 Pro)
- `ipad/{dark,light}/NN-name.png`: 1180 × 820 (iPad Air 11″, landscape)
- `android-tablet/{dark,light}/NN-name.png`: 1280 × 800 (Pixel Tablet, landscape)

The capture tool does not draw `backdrop-filter`, so the iOS glass bars look flatter in the screenshots than they should. The CSS below is the truth. The striped tiles stand in for real thumbnails.

| # | State | Section below |
|---|---|---|
| 01 | Welcome | 1 |
| 02 | Photo access (system dialog) | 1 |
| 03 | Access refused | 1 |
| 04 | Scanning | 1 |
| 05 | Library | 2 |
| 06 | Only some photos allowed | 2 |
| 07 | Selecting | 2 |
| 08 | Add to album / Move to folder | 2 |
| 09 | Delete the selection | 2 |
| 10 | Photo viewer | 2 |
| 11 | Clean up: groups | 3 |
| 12 | Review a group | 3 |
| 13 | A group with several copies | 3 |
| 14 | Compare, full screen | 3 |
| 15 | Delete one copy | 3 |
| 16 | Delete all duplicates | 3 |
| 17 | After a delete, with Undo | 3 |
| 18 | Similar shots | 3 |
| 19 | Organize: by place | 4 |
| 20 | Organize: by date | 4 |
| 21 | Map | 4 |
| 22 | Options | 4 |
| 23 | Preview | 4 |
| 24 | Where it goes | 4 |
| 25 | Name taken | 4 |
| 26 | Android asks before moving (Android only) | 4 |
| 27 | Moved, with Undo | 4 |
| 28 | Recently moved | 4 |
| 29 | Send: choose | 5 |
| 30 | Waiting for a computer | 5 |
| 31 | Compare the number | 5 |
| 32 | Sending | 5 |
| 33 | Sent | 5 |
| 34 | Move: the system asks before deleting | 5 |
| 35 | Settings | 6 |
| 36 | Delete permanently | 6 |
| 37 | Are you sure? | 6 |

Tablets: `01-library`, `02-review`, `03-preview`, `04-send-code`, `05-settings`.

---

## Platform, form and theme

- **Platform:** `about.program === 'iphone'` gives the iOS skin and iOS words; `'android'` gives the Android skin and Android words. iPad also reports `iphone`.
- **Form:** below 768 CSS px wide it is the phone layout; from 768 up it is the tablet layout. Decide by viewport width, not by device name, so iPad Split View and a folded Android get the right one.
- **Theme:** Settings offers **Auto / Dark / Light**. Auto follows `prefers-color-scheme`. Today `lib/theme.ts` has only dark and light, so Auto is new. Desktop keeps what it has.
- Put the skin on the root as attributes, as the prototype does (`data-skin="ios-native-dark"`, `data-dev="iphone"`), and drive Tailwind from CSS variables (`bg-[var(--cell)]`, or a theme extension that maps names to the variables).

### Words that differ by platform

| | iOS | Android |
|---|---|---|
| A folder of photos | album / Album / albums | folder / Folder / folders |
| The whole library | Photo Library | Phone (in titles), Pictures (as a destination) |
| Path separator in copy | ` › ` | `/` |
| Where removed files wait | PhotoSense Removed | Pictures/PhotoSense Removed |
| The system's bin | Recently Deleted | the bin |
| Selection action | Add to album | Move to folder |
| Device name in copy | Jamie's iPhone / Jamie's iPad | Pixel 9 Pro / Pixel Tablet (use the real name from `SendStateDto.phone`) |

### Safe areas and system bars

- iOS: the page uses `viewport-fit=cover` and `env(safe-area-inset-top|bottom)`. The prototype hard-codes 50 px top and 34 px bottom (iPhone) and 30 / 20 px (iPad).
- Android: the prototype draws content between the 24 px status bar and the 24 px gesture bar, and paints the gesture bar the colour of the navigation bar (`--bar`) when the tab bar shows, or `--bg` when it does not. If `env()` gives 0 in Android's WebView, have `MainActivity` pass the insets in as CSS variables.
- The theme also sets the status bar's icons (light on dark, dark on light). That lives in each app's thin layer.

### System dialogs (real, not built in HTML)

The design shows these only so the flow reads end to end. Each comes from the OS when the existing code asks for it:

- **Photo access, iOS** (02): PhotoKit's full-access prompt with **Limit Access…**, **Allow Full Access**, **Don't Allow**. The message text is `NSPhotoLibraryUsageDescription`: "PhotoSense finds copies of the same photo and sorts photos into albums. Nothing leaves this phone unless you send it to your computer."
- **Photo access, Android** (02): **Allow all**, **Select photos and videos**, **Don't allow**.
- **Android modify** (26): `MediaStore.createWriteRequest` before Organize moves files.
- **Delete from the phone** (34, and the end of Delete permanently): iOS's "Allow “PhotoSense” to delete N photos?" from PhotoKit; Android's `createTrashRequest`.

---

## Navigation

Four tabs, in this order: **Library**, **Clean up**, **Organize**, **Send**. Settings is a **gear** at the top right of every tab's root screen, opening a sheet.

Line icons, 24 px, stroke 1.9, round caps and joins (paths are in `MobileApp.dc.html`):

- Library: a 2 × 2 grid of rounded squares
- Clean up: two overlapping rounded squares
- Organize: a folder
- Send: a laptop with an up arrow

### iPhone tab bar (floating Liquid Glass)

- Position: absolute; left and right 16 px; bottom 22 px; height 64 px; padding 4 px; radius 999.
- Background `--bar`, border `--bar-bd`, shadow `--bar-sh`, `backdrop-filter: blur(24px) saturate(180%)`.
- Four equal cells (grid). Each is a column with a 2 px gap: icon, then label 10.5 px / 600.
- Active cell: pill background `--bar-hi`, icon and label `--bar-on`. Inactive: `--bar-off`. Background transitions in 0.2 s.
- Scrolling content gets 120 px bottom padding so the last row clears the bar.

### Android navigation bar (Material 3)

- Bottom 0, full width, height 80 px, padding-top 12 px, background `--bar`, no border.
- Each item is a column with a 4 px gap. The indicator is 56 × 32, radius 16: `--bar-hi` when active, transparent otherwise; the icon is `--bar-on` / `--bar-off`. Label 12 px, weight 700 active and 500 inactive, `--t1` active and `--bar-off` inactive.
- Content gets 104 px bottom padding.

### iPad sidebar (Liquid Glass)

- 300 px column. Padding: top safe area + 6 px, right 0, bottom 14 px, left 14 px. Inside, a floating panel: radius 26, `--side` with blur 24 px, border `--bar-bd`, shadow `--bar-sh`, padding 10 px, items 2 px apart.
- Header 50 px: app icon 28 × 28 (radius 8), "PhotoSense" 19 px / 700, and a Hide-sidebar button (40 × 40, a sidebar icon).
- Rows 46 px, radius 999, padding 0 14 px, icon 22 px, 12 px gap, label 16 px. Active: background `--bar-hi`, colour `--bar-on`, weight 600. On the right, a count 13 px `--t2` (tabular): Library 6,941, Clean up 264, Organize 4,212. Use the real totals.
- Foot: "Runs on this iPad. Nothing is uploaded." 13 px `--t2`.
- **Hidden sidebar:** a floating Show-sidebar button (44 × 44 glass circle, top-left at 16 px) and, centred at the top, a floating glass tab bar: height 44, padding 4, a pill per tab 36 px high with 18 px side padding, 15 px / 600. Large titles move down by 62 px.

### Android tablet navigation rail

- 96 px wide, background `--rail`. Content starts 24 px from the top with 14 px gaps: app icon 36 × 36 (radius 10, 16 px below it), then the four items. Each item: 56 × 32 indicator, 12 px label (the same as the bar).

### Screens, push and back

- **Phone:** each tab has a root screen. Clean up → a group's **Review**, and Organize → a suggestion's **Preview**, are pushed: they slide in (`translateX(44px)` and opacity 0.3 → none, 0.28 s, `cubic-bezier(.2,.8,.2,1)`), and the tab bar hides while they show.
- Tapping the active tab pops back to its root.
- Selecting in Library swaps the tab bar for the selection bar.
- **Tablet:** Clean up and Organize are split views. The list is 360 px on iPad and 400 px on Android, with a 1 px `--sep` line on its right; the detail fills the rest. Nothing is pushed. The selected row gets background `--sel` and no chevron.
- **Back:** push a history entry for each pushed screen, sheet and full-screen viewer, so that Android's system back and iOS's edge swipe close them in order. Android's `MainActivity` hands back to the web view's history.

### Headers

- **iOS root:** padding top = safe area, then 16 px at the sides and 12 px below. First a 50 px row of glass circle buttons, right-aligned with 8 px between them: 44 × 44, background `--ib-bg`, border `--ib-bd`, shadow `--ib-sh`, blur 12 px. Then the large title, 34 px / 700, letter-spacing .01em, line-height 1.15. Then a subtitle, 15 px `--t2`. A text button ("Select", "Cancel") is a glass pill, 44 px high, 18 px side padding, 16 px / 600, colour `--tb-fg`.
- **iOS pushed:** a 44 px glass back circle (chevron) on the left and a centred title 17 px / 600 with a subtitle 13 px `--t2`, both on one line with an ellipsis.
- **Android root:** a 64 px top app bar. Title 24 px / 400 on the left. Then 48 × 48 icon buttons; text buttons are tonal pills 40 px high, 14.5 px / 500, colour `--tb-fg`, background `--ib-bg`. Subtitle below at 14 px `--t2` with 16 px side padding.
- **Android pushed:** 64 px. A 48 px back arrow, then the title 22 px / 400.

Gear: every tab's root has a gear button. On tablets the gear sits in the detail column's header instead.

---

## Components

### Buttons

All buttons are pills (radius 999).

| | iOS | Android |
|---|---|---|
| Height | 50 | 52 |
| Text | 17 / 600 | 16 / 500 |
| Primary | `--acc` / `--on-acc` | same |
| Danger | `--danger` / `--on-danger` | same |
| Secondary | `--btn2-bg` / `--btn2-fg` | same (tonal) |
| Text link | `--acc`, 600 | same |

A screen's main button sits in a sticky footer: padding 16 px, bottom padding safe area + 12 px, background `linear-gradient(to bottom, transparent, var(--bg) 26%)`. A long label stays on one line and ends in an ellipsis.

### Segmented control

- **iOS:** height 34; padding 2; radius 999; background `--seg-bg`. The selected segment is an inner pill (`--seg-on`, `--seg-on-fg`, shadow `--seg-sh`). 14 px / 600.
- **Android:** height 40; 1 px `--seg-bd` outline; radius 999; segments divided by a 1 px line; the selected one filled `--seg-on`. 14 px / 600.

### Chips (filters, radius choices)

- Height 34, padding 0 14, 14 px / 500, a count after it at 12.5 px `--t2`, on one line.
- iOS: radius 999, background `--chip-bg`. Android: radius 8, 1 px outline `--chip-bd`.
- Selected: `--chip-on-bg` / `--chip-on-fg`.

### Lists

- **iOS (inset grouped):** one container with radius 24 and background `--cell` (`--sheet-cell` in sheets). Rows are separated by 0.5 px `--sep` lines.
- **Android (M3 Expressive):** each row is its own surface (`--cell`), 2 px apart, inner radius 4. The container clips to radius 20, so the group's outer corners are round.
- Section headers sit 4 px in. iOS: 13 px / 600, uppercase, letter-spacing .02em, `--t2`. Android: 14 px / 500, `--acc`, as typed.
- Rows have a minimum height of 52 px, padding 10–12 px by 14–16 px, and a chevron 8–9 × 14–15 in `--t3` on phones.

### Sheets

- **Phone:** from the bottom, full width, maximum height 100% − 56 px (iOS) or − 24 px (Android). iOS radius 36, Android 28, top corners only. Background `--sheet`. Grabber 36 × 5 (iOS) or 32 × 5 (Android), `--grab`, 8 px from the top.
- **Tablet:** a centred card 560 px wide, radius 28, at most 100% − 40 px high.
- Header: title 20 px / 700 and a secondary pill on the right, 36 px high: "Done", or "Close" for Name taken.
- Scrim `--scrim`; tapping it closes the sheet. The sheet enters with `translateY(40px)` and opacity 0 → none over 0.3 s, `cubic-bezier(.2,.8,.2,1)`; the scrim fades in over 0.2 s.
- **Confirm sheets:** title 21 px / 700 (breaks anywhere, so long file names wrap), lines 15 px `--t2` at line-height 1.45, then a danger button and Cancel.

### Toast (iOS) / Snackbar (Android)

- 12 px from the sides, at most 440 px wide, minimum 50 px high. Padding: 8 px top and bottom, 16 px left, 6 px right.
- iOS radius 22 with a 20 px blur; Android radius 8.
- Background `--toast`, text `--on-toast` 14 px. The action is 14.5 px / 600 in `--toast-act`.
- Bottom: 98 px (iOS phone) or 92 px (Android phone) above the bottom when the tab bar shows; safe area + 112 px on pushed screens; 28 px on tablets.
- Enters with `translateY(40px)` and fade over 0.25 s. Hides itself after about 5.5 s.

### Photo grid

- Square tiles. Phone: 3 columns. iPad: 6 with the sidebar showing and 8 with it hidden. Android tablet: 8.
- 2 px gaps and square corners, edge to edge.
- Video duration: bottom right, 12 px / 600, white, with a text shadow.
- Grouped by month: a header row with the month 17 px / 600 on the left and "N items" 13 px `--t2` on the right; 14 px top, 8 px bottom, 16 px sides.

### Selection

- Circle 24 × 24, 1.5 px white border, background `rgba(0,0,0,.2)`; filled `--acc` with a white tick when selected. iOS: bottom right, 6 px in. Android: top left, 6 px in.
- On Android a selected tile also shrinks to an 8 px inset with radius 14, in 0.15 s.

### Selection bar

- iPhone: floating glass, like the tab bar, 64 px high.
- Android: full width at the bottom, 64 px, `--bar`.
- Tablets: floating, at most 420 px wide, centred, 28 px from the bottom.
- Three actions with a 22 px icon over an 11.5 px / 600 label: **Send**; **Add to album** (iOS) or **Move to folder** (Android); **Delete**, in `--danger-t`. At 40% opacity while nothing is selected.

### Map (Organize)

- Plain map: background `--map` with a 40 px grid of 1 px `--map-line`; 360 px high on phones and 420 px on tablets; radius 24 / 20.
- One dot per suggestion. Size `26 + √files × 1.05` px; the count inside is 12 px / 700.
- A dot is `--cell` with a 2 px `--acc` ring; the selected dot is `--acc` with a 3 px `--cell` ring and a name label under it.
- Phones leave out the Google Maps key (CLAUDE.md rule 6), so they always get `FolderMap`'s plain map. The note under it reads "Drawn on this phone. Positions are never sent anywhere."

---

## Screens

The copy below is exact. Numbers come from the API. Where `[album]` appears, use the platform word.

### 1. First run (01–04)

- **Welcome (01):** full screen, `--bg`, content at most 440 px wide. App icon 88 × 88 (radius 22), then "Welcome to PhotoSense" 30 px / 700. Three rows, each a 40 × 40 icon tile (`--acc-soft` / `--acc`, radius 12), a title 16.5 px / 600 and a body 14.5 px `--t2`:
  - **Find copies**: "The same photo saved twice is kept once. You see what goes and what stays before anything is deleted."
  - **Sort by place and date**: "Suggested [albums] for each place and year, with a preview before anything moves."
  - **Send to your computer**: "Over Wi-Fi to PhotoSense on a PC or Mac. Each file is checked before anything is deleted here."

  Foot: "Runs on this phone. Nothing is uploaded." (13 px, centred), then **Continue**, which asks for photo access (02).
- **Access refused (03):** an image icon in a 72 × 72 `--fill` tile, then "PhotoSense can't see your photos" 26 px / 700, then "PhotoSense needs your photos to find copies and sort them. Turn on access in Settings › Apps › PhotoSense › Photos." (on Android, "… › Permissions."). Buttons: **Open Settings** (`POST library/open-settings`) and **Ask again**.
- **Scanning (04):**
  - "Reading your photos" 26 px / 700.
  - The percentage 56 px / 700, tabular, with "About N minutes left" beside it (14 px `--t2`).
  - An 8 px progress bar.
  - "Read 2,637 of 6,941 photos and videos" (15 px, tabular).
  - A grid of tiles, 10 columns on phones and 16 on tablets, 4 px gaps, radius 4. Tiles fill with thumbnails as files are read.
  - Note: "Keep PhotoSense open until it finishes. Later scans skip photos already read, so they take seconds."
  - **Stop**: toast "Scan stopped. What was read so far is kept, and the next scan carries on from there."
  - When it finishes, it goes to Library with the toast "Found 264 duplicates and 178 groups of similar shots." and the action **Review**, which opens Clean up.

### 2. Library (05–10)

- **Header:** "Library", subtitle "6,941 photos and videos", and **Select**.
- **Filter chips:** All 6,941 · Not organized 4,212 · No location 381 · Videos 512. The row scrolls sideways.
- **Grid:** by month, latest first. Tap opens the viewer; in select mode, tap toggles the selection. With nothing left under a filter: "Nothing here with this filter."
- **Limited (06):** a banner above the chips, `--acc-soft` / `--on-acc-soft`, radius as cells, padding 14 × 16: "PhotoSense can see only the 214 photos you chose. Copies among the rest are not found." It has a **Choose more** pill (`POST library/choose-more`). The subtitle becomes "The 214 photos you chose".
- **Selecting (07):** the title becomes "N selected" ("Select items" at 0). iOS has a **Cancel** pill on the right; Android has a close × before the title. The selection bar replaces the tab bar.
- **Add to album / Move to folder (08):** a sheet titled "Add N to an album" (iOS) or "Move N to a folder" (Android). It lists the [albums] with a folder icon each, then "New [album]…", which opens a sheet with a name field and **Create**. Picking one moves the files there; the toast reads "Moved N items to Anaconda." with **Undo**.
- **Delete the selection (09):** a confirm sheet:
  - Title: "Delete 3 items?"
  - "IMG_9154.HEIC, IMG_9080.JPG, IMG_9043.HEIC go to PhotoSense Removed." (more than 3: "… and N more")
  - "Undo can bring them back. Delete permanently, in Settings, sends them to Recently Deleted."
  - Button: **Delete 3 items**.

  Afterwards the toast reads "3 items moved to PhotoSense Removed." with **Undo**.
- **Viewer (10):** a black stage (`--stage`).
  - Top: a 44 px back circle (`rgba(255,255,255,.14)`), the name in mono 14 px / 500, and the date and time 12.5 px at 72% white.
  - The photo, letterboxed (at most 680 px wide on tablets).
  - Under it: the place (15 px white), the [album] path, and "size · pixel size" ("· duration" for a video) at 13.5 px, 78% white.
  - Actions at the bottom: **Send**, **Add to album / Move to folder**, **Delete** (`#FF6961`).

### 3. Clean up (11–18)

- **Header:** "Clean up", with the subtitle "Copies of the same photo or video" (Duplicates) or "Shots that look alike" (Similar).
- **Summary card (11),** Duplicates only, radius as cells, padding 18:
  - "264 duplicates" 26 px / 700, tabular.
  - "They take 795 MB. The best copy of each picture or video is kept."
  - **Delete all duplicates** (danger).
- **Duplicates / Similar** segmented control.
- **Group rows:**
  - Two stacked 46 × 46 thumbnails (radius 9): the back one at 60% opacity, the front one with a 2 px `--cell` ring and a play icon for video.
  - The keeper's name in mono 14 px / 500.
  - A sub line, 13.5 px `--t2`: "1 duplicate · 12.0 MB" or "3 similar shots". When every copy is marked keep it reads "2 copies, all marked keep" in `--keep`.
  - A `+N` badge (24 px, `--fill`, 12.5 px / 600) and a chevron.
- **Under the list:** "Scanned 6,941 photos and videos today at 4:25 PM." and **Scan again**. When the list is empty: "No duplicates left. What you deleted waits in PhotoSense Removed until you delete it permanently."
- **Review (12, 13):**
  - A tier chip (24 px, `--ident-bg` / `--ident`, 11.5 px / 700, uppercase, .05em): "Same picture", "Identical file" or "Similar". The note beside it, 14 px `--t2`: "The JPEG was made from the HEIC."
  - Two pictures side by side, 12 px apart, radius 16, aspect 3 : 4 (16 : 11 for video).
    - Left, the keeper: a 2.5 px inset ring in `--keep` and the label "Original" (or "Best shot" for Similar) in a `--keep` pill.
    - Right, the copy: the label "Copy 1 of 2" (or "Shot 1 of 2") in a `rgba(0,0,0,.55)` pill.
    - Both have a format and size badge at the bottom right. Under each, the name in mono 12.5 px; under that, "Stays in Photo Library" (in `--keep`) or "In Butte › 2022".
  - With several copies (13): Copy 1 / Copy 2 chips choose which copy is compared.
  - **What differs:** a three-column table: label 78 px, Original, This copy. Each label has a 6 px `--amber` dot, and the copy's value is in `--amber`.
  - **The same in both:** two columns.
  - Then "Tap either picture to see it full screen and flip between them."
  - Footer: **Delete IMG_4299.HEIC** (danger), then a text button: **Keep this copy too**, or **Marked keep. Tap to unmark** (`POST photos/{id}/keep`; the toast reads "IMG_0377 (2C9F41A0).JPG is marked keep. Delete all duplicates skips it.").
- **Compare (14):** full screen on `--stage`.
  - Header: a close × circle, the name and where it is.
  - The picture, radius 14. Tapping it flips between the original and the copy. The label pill is `--keep` for the original and `rgba(255,255,255,.22)` for the copy.
  - With several copies: previous / next circles (44 px, `rgba(0,0,0,.45)`) on the sides.
  - Below: an Original / This copy segmented control (38 px, on `rgba(255,255,255,.12)`), "Tap the picture to flip between them.", and **Delete IMG_4299.HEIC**.
- **Delete one copy (15):**
  - Title: "Delete IMG_4299.HEIC?"
  - "IMG_4299.JPG stays, in Photo Library."
  - "IMG_4299.HEIC, in Photo Library, goes to PhotoSense Removed, where Undo can still bring it back."
  - Button: **Delete IMG_4299.HEIC**.
- **Delete all (16):**
  - Title: "Delete all duplicates?"
  - "264 copies go to PhotoSense Removed (795 MB)."
  - "The best copy of each picture or video stays. Copies marked keep are skipped."
  - "Similar shots are not included."
  - Button: **Delete 264 duplicates**.
- **After a delete (17):** the toast "IMG_4299.HEIC deleted. IMG_4299.JPG stays." with **Undo**. The next group is selected.
- **Similar (18):** the same rows, with no summary card.

### 4. Organize (19–28)

- **Header:** "Organize", subtitle "Sort Photo Library into albums by place and date" (Android: "Sort Phone into folders by place and date"). It has a map / list toggle (Place only) and the gear.
- **Controls:** the Place / Date segmented control and an **Options** pill (tonal, an icon and "Options"). Under them, the summary, 13 px `--t2`: "Within 5 km · Landmark or area · Place › Year".
- **Suggestions (19, 20):**
  - Section header "Suggested albums" (Android: "Suggested folders"), or "Albums by year", with the count on the right.
  - Rows: a 60 × 60 mosaic of four thumbnails (radius 12, 2 px gaps); the name 16 px / 600; "724 files · 5.1 GB"; the date range; and a note when there is one, 12.5 px `--t2`: "No landmark known here, so it is named after the town." or "Includes Apgar, 6 km away."
  - A **Moved** tag (20 px, `--keep-soft` / `--keep`) on suggestions already done.
- **Your albums / Your folders:** the person's own [albums], then "+ New [album]". With none: "Select files in Library, then Put in [album]".
- **Then:** "381 files have no location. They stay where they are." (opens Library filtered to No location), and **Recently moved** with its count.
- **Map (21):** the map, then the selected suggestion as a card with a **Preview** pill.
- **Options (22):** a sheet titled "Album options" (Android: "Folder options"):
  - **Group by**: Place | Date.
  - **Nearby places**: Combine | Keep separate.
  - **Within**: 1 / 5 / 15 / 25 / 50 km chips (shown only when combining).
  - **Album names**: Landmark or area | Town, with the note "A landmark or area is used where a photo was taken at one. Otherwise the town is used."
  - **Inside each album**: Year | Month | Nothing. For Date it is **Inside each year**: Place | Nothing.
  - **Show**: Photos & videos | Photos | Videos. When not both: "Only the kind shown is sorted. A Live Photo's video always goes with its picture."
  - Foot card: "Albums are made in Photo Library. Files already in one of your albums are left out." (Android: "Folders are made in Pictures. …")
- **Preview (23):** pushed on phones; the detail column on tablets.
  - Title: the name. Subtitle: "Preview. Nothing moves until you tap Move."
  - "723 of 724 files will move. Tap a file to leave it where it is."
  - A list:
    - "[Album] name", an editable field 16 px / 600;
    - "Made inside", the destination, with **Change** (24);
    - "Inside it", "Place › Year".
  - Name-taken card (`--amber-soft`): "3 names are already taken in Anaconda › 2022. Each gets a number, as in IMG_6435 (1).JPG." with a **Review each name** pill (`--t1` / `--bg`). Once reviewed it becomes "3 names reviewed." (`--keep-soft`).
  - Sections per year or month (or "All files"): the title 17 px / 600 and "N files". A grid of 4 columns on phones and 4–6 on tablets, showing 2 rows; **Show more** gives 5 rows, then **Show fewer**. Tapping a file leaves it out: a `--bg` veil at 74% and a "Stays" pill.
  - Footer: **Move 723 files**, then "Into Photo Library › Anaconda. Undo puts them back."
  - On iOS the move happens at once. On Android the system asks first (26); **Deny** shows the toast "Nothing was moved."
- **Where it goes (24):** a radio list.
  - iOS: **Photo Library** "At the top level of your albums"; **Trips** and **Montana** "A folder of albums made earlier". Note: "A folder here is a folder of albums in the Photos app."
  - Android: **Pictures** "Pictures, on this phone"; **DCIM** "Beside the camera's own folder"; **Trips** "Pictures/Trips, made earlier". Note: "Android lets apps make folders only inside DCIM, Pictures or Movies."
- **Name taken (25):** a sheet titled "Name taken".
  - "1 of 3 · The file going in and the one already there".
  - Two columns, **Going in** and **Already there**. Each has a square picture (radius 14), the name in mono 12.5 px, "From Photo Library" or "In Anaconda › 2022", the date and size, and a **Delete** pill (`--danger-soft` / `--danger-t`).
  - Under the columns: "Byte for byte the same file." (`--ident-bg`), or "Two different files with the same name." (`--amber-soft`).
  - Delete asks inline: "Delete IMG_6435.JPG from Photo Library? The one in Anaconda › 2022 stays." with Cancel and Delete. Afterwards the toast reads "IMG_6435.JPG deleted from Photo Library. IMG_6435.JPG in Anaconda › 2022 stays." with **Undo**. This goes through `organize/remove`, into Removed; it is never erased.
  - The choices, as a radio list:
    - **Add a number**: "IMG_6435 (1).JPG" (the default)
    - **Give it another name**: "Type the new name below", which shows a mono text field
    - **Leave it where it is**: "It stays in Photo Library"
  - Button: **Next name**, and on the last one **Done**. That gives the toast "3 names reviewed. Nothing moves until you tap Move."
- **Moved (27):** the toast "Moved 724 files to Anaconda." with **Undo**, and a **Moved** tag on the row. On a tablet the detail shows a done state: a `--keep-soft` check, "724 files are in Anaconda", the path in mono, and an **Undo** pill.
- **Recently moved (28):** a sheet. Each row has the text 15 px, the time 13 px `--t2`, and an **Undo** pill. Undo gives the toast "Put back. Every file is where it was." Note: "Undo puts files back under the names they had. What was deleted permanently can't be put back." With nothing: "Nothing to undo."

### 5. Send to computer (29–34)

The content is at most 560 px wide and centred. Header: "Send to computer", subtitle "Copy or move to PhotoSense on a Windows PC or Mac".

- **Choose (29):**
  - **What to send**, a radio list:
    - "Everything not in an album yet" (Android: "… not in a folder yet"), "4,212 files · 18.4 GB"
    - "What you chose in Library", "N items", or "Nothing chosen yet. Tap to choose." (that opens Library in select mode)
    - "Everything", "6,941 files · 31.2 GB"
  - **Copy or move**, a segmented control, with its note:
    - Copy: "The photos stay on this phone. Once the computer has them all, you can delete them here."
    - Move: "Each photo is deleted from this phone once the computer has checked every file of it. It goes to Recently Deleted."
  - An info card (`--acc-soft`, Wi-Fi icon): "The computer finds this iPhone on the same Wi-Fi, or over a USB cable." (Android: "The computer finds this phone on the same Wi-Fi.")
  - **Offer 4,212 files to a computer** (40% opacity when there is nothing to send).
- **Waiting (30), stage `waiting`:**
  - A 150 px pulse: two rings, 2 px `--acc`, `scale .6 → 1.45` with opacity .6 → 0 over 2.2 s, the second ring 1.1 s behind. Inside, an 84 px `--acc` disc with a laptop icon.
  - "Waiting for a computer" 24 px / 700, then "Jamie's iPhone is offering 4,212 files. Keep PhotoSense open on this screen."
  - Numbered steps: "Open PhotoSense on the computer.", "Choose Receive from a phone.", "Pick Jamie's iPhone."
  - **Stop offering**.
- **Compare the number (31), stage `comparing`:**
  - "Does JAMIE-LAPTOP show this number?" 24 px / 700.
  - The code in a card: mono 44 px / 500, letter-spacing .06em, tabular.
  - "You compare it once for each computer. After that, everything sent between them is encrypted."
  - **It's the same number** and **It's different** (`send/confirm`). Different leads to the failed state: "Stopped. Nothing was sent." / "The numbers did not match. Check that you picked this phone on the computer, then try again." with **Try again**.
- **Sending (32):**
  - The percentage 56 px / 700 and an 8 px bar.
  - "1,938 of 4,212 files · 8.5 GB of 18.4 GB", "To JAMIE-LAPTOP, in D:\Phone Pictures", and the current file in mono.
  - A card: "Keep PhotoSense open and the screen on until it finishes. Nothing is deleted from this phone while sending."
  - **Stop sending**.
- **Sent (33):**
  - A 68 px `--keep-soft` check, "4,212 files are on JAMIE-LAPTOP", then "In D:\Phone Pictures. Each file was checked against the one on this phone."
  - With Copy, a card: "Free up space on this phone" / "The copies on JAMIE-LAPTOP stay. Deleted photos wait in Recently Deleted for 30 days." and **Delete 4,212 from this phone**. That opens a confirm sheet: "Delete 4,212 from this phone?" / "The copies on JAMIE-LAPTOP stay, in D:\Phone Pictures." / "Only files the computer has checked are deleted. They wait in Recently Deleted for 30 days." Then the system dialog (34), then `send/delete-sent`.
  - Afterwards: "Deleted from this phone. They wait in Recently Deleted for 30 days." (`--keep-soft`).
  - **Done** returns to Choose.
- **Move (34):** when the mode is Move, the system's delete dialog appears as soon as sending finishes.

### 6. Settings (35–37)

A sheet from the gear, titled "Settings".

- **Appearance:** Theme, Auto | Dark | Light (a 220 px segmented control).
- **Photos:** "Photo access", showing "Full access" / "All photos and videos" / "214 chosen", with a chevron to the OS settings; and "Scan again" (accent).
- **Removed files:** "PhotoSense Removed", "37 items · 212 MB" (from `GET removed`); and "Delete permanently…" in `--danger-t` (`--t3` when empty).
- **History:** "Recently moved" with its count; "Errors", "None" (opens `ErrorsPanel`).
- **About:** Help, "About PhotoSense 1.4.0" (the real version), Privacy.
- Foot: "PhotoSense 1.4.0 · Runs on this phone. Nothing is uploaded."
- **Delete permanently (36):**
  - Title: "Delete 37 items permanently?"
  - "Everything in PhotoSense Removed goes to Recently Deleted, where iOS keeps it for 30 days."
  - "Undo can no longer bring these back, and they leave Recently moved."
  - Button: **Delete 37 items**.
- **Are you sure? (37):**
  - "The 37 items in PhotoSense Removed go to Recently Deleted. Undo can no longer bring them back."
  - A switch, "Do not ask me this second time again" (51 × 31, `--sw-on` / `--sw-off`, a 27 px white knob). This is the existing setting.
  - **Delete 37 items** and Cancel. Then the system dialog, then `removed/erase`. The toast reads "37 items deleted. They wait in Recently Deleted for 30 days."

---

## State and data

| Design | Existing code or endpoint |
|---|---|
| Platform | `useAbout().data.program` |
| Photo access, Choose more, Open Settings | `GET library/access`, `POST library/choose-more`, `POST library/open-settings`; `PhotoAccessNote` |
| Library grid, filters, counts | `GET organize/files?root=<the phone's root>` (latest first). "Not organized" = `folder === root`; "No location" = no `place`; "Videos" = `isVideo`; duration = `durationSeconds`. Thumbnails: `organize/files/{id}/thumbnail` |
| Put in an album or folder | `organize/plan` then `organize/apply` (`folderName`, `direct`), the same as Organize's own folders (`CustomFolderCard`) |
| Delete from Library | `organize/remove` (into PhotoSense Removed), Undo by `organize/undo/{batchId}` |
| Scan | `scan/start`, `scan/progress/{id}`, `scan/status`; `SetupScreen`'s logic |
| Groups, Duplicates / Similar | `useGroups(mode, …)`; `GroupList`, `ReviewPanel`, `Differences`, `MatchChip` |
| Keep, delete one copy, delete all | `POST photos/{id}/keep`, `DELETE photos/{id}`, `POST photos/bulk/remove-duplicates` |
| Compare | `PhotoWindow`'s flip logic |
| Organize suggestions, options, map | `OrganizeView`, `SuggestionsPanel`, `SuggestionCard`, `FolderMap` (plain on phones), `lib/organize` |
| Preview, Name taken, Moved | `MovePreview`, `NameClashWindow` (it has Delete already), `organize/apply`. Phones only move: `mode={onPhone ? 'move' : mode}` stays |
| Recently moved, Undo | `organize/batches`, `organize/undo/{batchId}`; `RecentMoves` |
| Send | `send/start` (ids from `organize/files`), `send/state` (`stage` idle → waiting → comparing → sending → sent / failed), `send/confirm`, `send/stop`, `send/delete-sent`; `SendWindow`, `TransferParts` |
| Settings | `SettingsWindow`, `HelpWindow`, `AboutWindow`, `ErrorsPanel`, `useAppSettings` (ask-twice) |
| Delete permanently | `GET removed`, `POST removed/erase` |
| Toasts with Undo | `Toaster` / `useToasts` |

UI state the phone layout adds: the current tab; the pushed screen per tab (review group, preview suggestion); selecting plus the selected ids; the Library filter; the open sheet; the viewer or compare overlay; the sidebar hidden or shown (iPad). Keep each tab's state when switching tabs.

## What this design changes in the app

1. Phones get the tab layout. The desktop's `TopBar`, `ModeSwitch` and `AppMenu` are not shown on phones.
2. **Library** is a new tab, built on `organize/files` for the whole library.
3. **Send** moves out of Organize's selection bar into its own tab. Library's selection still offers Send.
4. Theme gains **Auto**.
5. On phones, Organize's distance choices stay in km, as on the desktop.

## Design tokens

### Metrics per platform

| | iOS | Android |
|---|---|---|
| Body font | `-apple-system, "SF Pro Text", system-ui` | `Roboto, system-ui` |
| Title font | `-apple-system, "SF Pro Display", system-ui` | `Roboto, system-ui` |
| Mono (file names) | `ui-monospace, "SF Mono", monospace` | `"DM Mono", monospace` (bundled, as on desktop) |
| Body / secondary text | 17 / 15 | 16 / 14 |
| Root title | 34 / 700, .01em | 24 / 400 |
| Cell and card radius | 24 | 20 |
| Sheet radius | 36 (top) | 28 (top) |
| Toast radius | 22 | 8 |
| Dialog radius | 30 | 28 |
| Icon button | 44, glass | 48, plain |
| Chip radius | 999 | 8 |
| Grid gap | 2 | 2 |

Animations: push 0.28 s, sheet 0.3 s, toast 0.25 s, all `cubic-bezier(.2,.8,.2,1)`; fades 0.2 s; dialog pop `scale(1.06) → 1` in 0.2 s.

### Colours

```css
[data-skin="ios-native-dark"]{--bg:#000;--cell:#1C1C1E;--fill:rgba(118,118,128,.24);--fill2:#3A3A3C;--t1:#fff;--t2:rgba(235,235,245,.66);--t3:rgba(235,235,245,.5);--sep:rgba(84,84,88,.55);--acc:oklch(0.76 0.11 205);--on-acc:#00242b;--acc-soft:oklch(0.76 0.11 205 / .17);--on-acc-soft:oklch(0.87 0.08 205);--danger:#FF453A;--on-danger:#fff;--danger-t:#FF6961;--danger-soft:rgba(255,69,58,.16);--keep:#30D158;--on-keep:#04170c;--keep-soft:rgba(48,209,88,.16);--amber:#FFD60A;--amber-soft:rgba(255,214,10,.13);--ident:oklch(0.86 0.09 210);--ident-bg:oklch(0.74 0.11 210 / .2);--sel:rgba(118,118,128,.32);--bar:rgba(30,30,32,.62);--bar-bd:.5px solid rgba(255,255,255,.14);--bar-sh:0 10px 30px rgba(0,0,0,.5);--bar-hi:rgba(255,255,255,.13);--bar-on:oklch(0.80 0.11 205);--bar-off:#fff;--side:rgba(30,30,32,.7);--sheet:#1C1C1E;--sheet-cell:#2C2C2E;--scrim:rgba(0,0,0,.5);--toast:rgba(48,48,50,.94);--on-toast:#fff;--toast-act:oklch(0.82 0.11 205);--btn2-bg:rgba(118,118,128,.24);--btn2-fg:#fff;--seg-bg:rgba(118,118,128,.24);--seg-on:#5A5A5E;--seg-on-fg:#fff;--seg-sh:0 2px 6px rgba(0,0,0,.25);--chip-bg:rgba(118,118,128,.24);--chip-on-bg:#fff;--chip-on-fg:#000;--ib-bg:rgba(120,120,128,.3);--ib-bd:.5px solid rgba(255,255,255,.14);--ib-sh:0 2px 8px rgba(0,0,0,.3);--tb-fg:#fff;--grab:rgba(235,235,245,.3);--sw-on:#30D158;--sw-off:rgba(120,120,128,.32);--map:#161d21;--map-line:rgba(255,255,255,.045);--stage:#000;--input:#2C2C2E;--alert:rgba(40,40,42,.96)}
[data-skin="ios-native-light"]{--bg:#F2F2F7;--cell:#fff;--fill:rgba(118,118,128,.12);--fill2:#E5E5EA;--t1:#000;--t2:rgba(60,60,67,.78);--t3:rgba(60,60,67,.62);--sep:rgba(60,60,67,.18);--acc:oklch(0.50 0.10 215);--on-acc:#fff;--acc-soft:oklch(0.50 0.10 215 / .1);--on-acc-soft:oklch(0.40 0.09 215);--danger:#E0342A;--on-danger:#fff;--danger-t:#D70015;--danger-soft:rgba(255,59,48,.1);--keep:#1F7A35;--on-keep:#fff;--keep-soft:rgba(52,199,89,.14);--amber:#8A5A00;--amber-soft:rgba(255,204,0,.16);--ident:oklch(0.42 0.09 215);--ident-bg:oklch(0.94 0.035 210);--sel:rgba(0,0,0,.06);--bar:rgba(255,255,255,.72);--bar-bd:.5px solid rgba(0,0,0,.06);--bar-sh:0 6px 24px rgba(0,0,0,.12);--bar-hi:rgba(0,0,0,.065);--bar-on:oklch(0.50 0.10 215);--bar-off:#1c1c1e;--side:rgba(255,255,255,.78);--sheet:#F2F2F7;--sheet-cell:#fff;--scrim:rgba(0,0,0,.28);--toast:rgba(28,28,30,.92);--on-toast:#fff;--toast-act:oklch(0.82 0.10 205);--btn2-bg:rgba(118,118,128,.12);--btn2-fg:#000;--seg-bg:rgba(118,118,128,.12);--seg-on:#fff;--seg-on-fg:#000;--seg-sh:0 2px 6px rgba(0,0,0,.12);--chip-bg:rgba(118,118,128,.12);--chip-on-bg:#000;--chip-on-fg:#fff;--ib-bg:rgba(255,255,255,.78);--ib-bd:.5px solid rgba(0,0,0,.06);--ib-sh:0 1px 6px rgba(0,0,0,.1);--tb-fg:#000;--grab:rgba(60,60,67,.3);--sw-on:#34C759;--sw-off:rgba(120,120,128,.16);--map:#E6EBEA;--map-line:rgba(0,0,0,.045);--stage:#000;--input:#fff;--alert:rgba(250,250,252,.96)}
[data-skin="and-native-dark"]{--bg:#0F1415;--cell:#1B2122;--fill:#252B2C;--fill2:#303637;--t1:#DEE3E5;--t2:#BFC8CA;--t3:#99A2A4;--sep:#3F484A;--acc:#82D3E0;--on-acc:#00363D;--acc-soft:#004F58;--on-acc-soft:#9EEFFD;--danger:#FFB4AB;--on-danger:#690005;--danger-t:#FFB4AB;--danger-soft:rgba(255,180,171,.12);--keep:#8BD6A0;--on-keep:#00391A;--keep-soft:rgba(139,214,160,.14);--amber:#E9C46A;--amber-soft:rgba(233,196,106,.13);--ident:#9EEFFD;--ident-bg:#004F58;--sel:#334B4F;--bar:#1B2122;--bar-hi:#334B4F;--bar-on:#CDE7EC;--bar-off:#BFC8CA;--rail:#0F1415;--sheet:#1B2122;--sheet-cell:#252B2C;--scrim:rgba(0,0,0,.45);--toast:#DEE3E5;--on-toast:#2B3133;--toast-act:#006874;--btn2-bg:#334B4F;--btn2-fg:#CDE7EC;--seg-bd:#899294;--seg-on:#334B4F;--seg-on-fg:#CDE7EC;--chip-bd:#899294;--chip-on-bg:#334B4F;--chip-on-fg:#CDE7EC;--tb-fg:#82D3E0;--grab:#899294;--sw-on:#82D3E0;--sw-off:#3F484A;--map:#172022;--map-line:rgba(255,255,255,.04);--stage:#000;--input:#252B2C;--alert:#252B2C}
[data-skin="and-native-light"]{--bg:#F5FAFB;--cell:#E7EDEE;--fill:#DEE4E5;--fill2:#D3DADB;--t1:#171D1E;--t2:#3F484A;--t3:#556062;--sep:#BFC8CA;--acc:#006874;--on-acc:#fff;--acc-soft:#CDEFF4;--on-acc-soft:#001F24;--danger:#BA1A1A;--on-danger:#fff;--danger-t:#BA1A1A;--danger-soft:#FFDAD6;--keep:#1E6B38;--on-keep:#fff;--keep-soft:#C8EFD0;--amber:#7A5900;--amber-soft:#FFEFC2;--ident:#004F58;--ident-bg:#CDE7EC;--sel:#CDE7EC;--bar:#E9EFF0;--bar-hi:#CDE7EC;--bar-on:#051F23;--bar-off:#3F484A;--rail:#F5FAFB;--sheet:#EFF5F6;--sheet-cell:#E3E9EA;--scrim:rgba(0,0,0,.32);--toast:#2B3133;--on-toast:#ECF2F3;--toast-act:#82D3E0;--btn2-bg:#CDE7EC;--btn2-fg:#051F23;--seg-bd:#6F797A;--seg-on:#CDE7EC;--seg-on-fg:#051F23;--chip-bd:#6F797A;--chip-on-bg:#CDE7EC;--chip-on-fg:#051F23;--tb-fg:#006874;--grab:#6F797A;--sw-on:#006874;--sw-off:#DEE4E5;--map:#E2E9E8;--map-line:rgba(0,0,0,.045);--stage:#000;--input:#fff;--alert:#E3E9EA}
```

On Android, the variables the iOS skin has but Android leaves out are `transparent` or `none`: `--seg-bg`, `--chip-bg`, `--ib-bg`, `--ib-bd`, `--ib-sh`, `--bar-bd`, `--bar-sh`, `--seg-sh`. On iOS, `--seg-bd` and `--chip-bd` are `transparent`. The Android colours are a Material 3 tonal scheme made from PhotoSense teal (`#006874` light primary, `#82D3E0` dark primary).

## Assets

- `prototype/app-icon.png`: the same file as `branding/app-icon.png` in the repo.
- Icons: inline SVG line icons in `MobileApp.dc.html` (24 px, stroke 1.9). Recreate them as small React components, or use the page's existing icons where one matches.
- Photos: real thumbnails from the API. The prototype's striped tiles are placeholders.

## Files

- `prototype/PhotoSense Mobile.dc.html`: the clickable side-by-side (1a = iPhone Native, 1c = Android Native).
- `prototype/PhotoSense Mobile Screens.dc.html`: every state, laid out.
- `prototype/MobileApp.dc.html`: the app itself; the props `platform`, `form`, `look`, `theme`, `start` (a state name from `seed()`), `still`.
- `prototype/ios-frame.jsx`, `prototype/android-frame.jsx`: the device frames (reference only).
- `prototype/support.js`: what the `.dc.html` files need to open in a browser.
- `screenshots/`: as listed above.
- `MOBILE_PROMPT.md`: the brief for Claude in the IDE.
