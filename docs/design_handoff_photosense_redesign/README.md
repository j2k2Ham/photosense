# Handoff: PhotoSense UI redesign

## Overview
This is a redesign of the PhotoSense web UI in `PhotoSense.ReactUI`, which uses Next.js 14, React 18, Tailwind 3, SWR and SignalR. The scanning, matching and deletion back end stays the same, and so do the DTOs and API calls. This handoff changes only the look, the layout and some of the behavior.

Main changes from the current UI:
- **A first-run screen.** On first run, a full-screen setup page is shown instead of the left setup column. You pick folders on it and the scan progress shows there too. When results exist, the folders and rescan shrink to a chip in the top bar.
- **Two columns.** The group gallery is on the left and the review desk on the right.
- **Review desk.** The original and the selected copy are shown side by side. Below them, a "What differs" table compares the two and puts the rows that differ first, highlighted in amber.
- **Comparison window.** It has three modes: Side by side, This copy and Original. The last two show one file in the same spot, so you can flip between them.
- **Errors.** Every error shows as a toast first. It then stays in an **Errors** panel until you clear it. The panel opens from a red chip in the top bar and from the menu.
- **Menu.** The header has a menu button (☰). It holds the **theme toggle (Dark/Light)**, Errors, and "Change folders or rescan".
- **No GitHub link** in the header.
- **All buttons are pills** (`border-radius: 9999px`).

## About the design files
The files in this bundle are **design references written in HTML**. They are prototypes that show the intended look and behavior. They are not production code to copy. Rebuild the designs in the existing `PhotoSense.ReactUI` codebase using its patterns: React function components, Tailwind classes, the `lib/` API helpers, SWR and SignalR. Keep the existing data flow and replace only the presentation and the interaction behavior described here.

`PhotoSense Prototype.dc.html` opens directly in a browser and needs `support.js` next to it. It is fully clickable and uses fake data (263 duplicate groups and 170 similar groups). Its logic lives in the `class Component` block near the bottom of the file and is useful as a behavior spec. Two props change the starting state: `startAt` ("Results" or "First run") and `offline` (shows the service-down bar and makes every action fail with an error toast).

`reference/PhotoSense Redesign (explorations).dc.html` holds the three earlier directions, 1a, 1b and 1c. The final design is **1b's layout plus 1a's color scheme, logo mark and "Differs from the original in" content**. Use it for context only.

## Fidelity
**High fidelity.** Colors, type, spacing, radii and copy are final. Match them closely using Tailwind. The striped tiles are placeholders. Use real thumbnails and images from the existing thumbnail and viewing endpoints.

## Mapping to existing components
| Existing component | What changes |
|---|---|
| `TopBar.tsx` | New header: logo mark, folder chip, local-only pill, error chip, menu button. Remove the GitHub link and the blue bar. |
| `SettingsPanel.tsx` + `ProgressPanel.tsx` + `LogsPanel.tsx` | Merge them into a new **FirstRun / Setup screen**: hero, folder card, progress, mosaic, log tail plus an expandable full log. |
| `GroupList.tsx` | A thumbnail **gallery grid** instead of a list of rows. |
| `ReviewPanel.tsx` + `PhotoDetails.tsx` + `Differences.tsx` | The **review desk**: side-by-side panes, the copy strip with a hover card, the "What differs" table and the action bar. |
| `DuplicateStrip.tsx` | Copy tiles (120×84) with a format chip and a match/KEEPING label. The hover card shows "Differs from the original in". |
| `PhotoWindow.tsx` | The **comparison window** with the three-mode toggle, the Space key to flip and inline confirms. |
| `FolderPicker.tsx` | The restyled folder browser dialog. |
| `ConfirmDialog.tsx` | The restyled confirmation dialog. |
| `Toaster.tsx` | Pill toasts. Errors are also written to a persistent **errors store** that feeds the Errors panel. |
| `Footer.tsx` | Remove it. The redesign has no footer. |
| (new) `AppMenu.tsx`, `ErrorsPanel.tsx`, `ThemeProvider` | Menu popover, errors popover, and the theme stored in `localStorage`. Use Tailwind `darkMode: 'class'`, which is already configured. |

---

## Design tokens

### Fonts
- **UI font:** `DM Sans` (Google Fonts, `opsz` 9..40, weights 400/500/600/700). Load it with `next/font/google`.
- **Paths, file counts, logs, formats:** `DM Mono` (weights 400/500).
- The base size is 14px with line-height 1.45.

### Colors
Define these as CSS variables on `:root` (Dark) and `.light` (or the reverse, depending on which is the default). Then map them in `tailwind.config.ts` → `theme.extend.colors` as `bg: 'var(--bg)'` and so on. **Dark is the default.** The source oklch values are in the prototype's `<style>` block; the hex values below are their sRGB conversions.

| Token | Dark | Light | Use |
|---|---|---|---|
| `--bg` | `#0c0f11` | `#f5f7f9` | App background |
| `--s1` | `#121518` | `#fdfeff` | Gallery column, cards, dialogs, search |
| `--s2` | `#1a1d20` | `#eef0f3` | Secondary buttons, hover background |
| `--s3` | `#252a2d` | `#dee2e5` | Pressed/active controls, empty mosaic tiles, toggle track |
| `--pop` | `#171b1e` | `#ffffff` | Popovers, toasts, hover card, dialogs |
| `--line` | `rgba(255,255,255,.08)` | `rgba(27,36,36,.11)` | 1px borders and dividers |
| `--t1` | `#eff2f4` | `#171b1f` | Primary text |
| `--t2` | `#acb2b7` | `#50565a` | Secondary text |
| `--t3` | `#878d92` | `#676d71` | Tertiary text and labels |
| `--stage` | `#050708` | `#e8ecee` | Behind images (letterbox), log box |
| `--brand` | `#3ebfc6` | `#007283` | Logo mark, primary Scan button, selection ring, links, checkbox |
| `--on-brand` | `#061415` | `#ffffff` | Text on brand |
| `--sel` | `#1e292b` | `#d9f0f4` | Selected item in folder browser |
| `--keep` | `#68d7a1` | `#006a42` | ORIGINAL/BEST/KEEPING text, "Original preferred" reason |
| `--keep-bg` | `rgba(106,213,163,.14)` | `#cff6e0` | ORIGINAL badge background |
| `--keep-line` | `rgba(104,215,160,.55)` | `rgba(43,150,103,.60)` | Original image ring, Keep button border |
| `--rose` | `#d3384e` | `#c5293d` | Destructive buttons (white text) |
| `--rose-t` | `#ff9da0` | `#ac1730` | Destructive text on a light or outline background |
| `--rose-bg` | `rgba(224,70,93,.13)` | `#ffeae9` | Inline confirm box, error chip, offline bar |
| `--rose-line` | `rgba(240,110,120,.40)` | `rgba(198,40,60,.35)` | Their borders |
| `--amber` | `#f7c15f` | `#8a5600` | "Differs from the original in" label, diff dots, Similar match |
| `--amber-bg` | `rgba(240,185,93,.13)` | `#ffedc1` | Differs box, Similar chip |
| `--diff` | `rgba(245,186,88,.10)` | `#fff1cc` | Highlighted rows in the diff table |
| `--ident` / `--ident-bg` | `#80dbeb` / `rgba(62,187,211,.16)` | `#00586b` / `#d1f2f8` | "Identical file" chip |
| `--same` / `--same-bg` | `#d8dfe4` / `rgba(245,255,255,.10)` | `#292f32` / `rgba(26,38,38,.08)` | "Same picture" chip |
| `--shadow` | `rgba(0,0,0,.5)` | `rgba(28,33,44,.18)` | Popover and dialog shadow color |
| `--scrim` | `rgba(0,1,1,.72)` | `rgba(30,35,37,.40)` | Modal backdrop |

Match kinds use these colors: **Identical file** → ident, **Same picture** → same, **Similar** → amber, **Keeping** → keep.

### Radii
- Buttons, search, toggles, chips in the header, toasts: **9999px (pill)**. Use this for every button.
- Cards and dialogs: 20px (confirm and folder browser), 18px (comparison window), 16px (menu and errors popovers), 14px (image panes, inline confirm, hover card).
- Inputs: 12px. Thumbnails: 10px (gallery and copy tiles). Badges: 6px. Mosaic tiles: 4px. Checkbox: 5px.

### Shadows
- Popovers and hover card: `0 18px 50px var(--shadow)` (menu `0 20px 50px`).
- Dialogs: `0 30px 90px var(--shadow)`.
- Toasts: `0 14px 40px var(--shadow)`.
- Setup card: `0 10px 40px var(--shadow)`.
- Selected gallery tile: `0 0 0 3px var(--s1), 0 0 0 5px var(--brand)` (a gap, then the ring).
- Original image pane: `inset 0 0 0 2px var(--keep-line)`. Copy pane: `inset 0 0 0 1px var(--line)`.

### Type scale
| Use | Size / weight |
|---|---|
| First-run headline | `clamp(40px, 4.2vw, 60px)` / 600, letter-spacing −0.03em, line-height 1.05 |
| Scan percent | 40px / 600, tabular numbers |
| Dialog title | 21px / 600 (folder browser 20px) |
| Review title (file name) | 20px / 600, −0.01em |
| Comparison title | 18px / 600 |
| Wordmark | 17px / 600 |
| Tabs | 16px / 600 |
| Body, buttons | 14–15px / 500–600 |
| Secondary | 13–13.5px |
| Small notes | 12.5px |
| Section labels | 12px / 600, uppercase, letter-spacing 0.07–0.08em |
| Badges (ORIGINAL etc.) | 11px / 700, uppercase, 0.08em |
| Mono (paths) | DM Mono 12.5–14px |

### Spacing
The layout uses a 4px base. Common values are 8, 10, 12, 14, 16, 18, 20, 22, 24 and 28px. Page gutters are 24px (header and toolbar) and 28px (review desk).

---

## Screens

### 0. App shell (all screens)
- Root: `height: 100vh`, `min-width: 1100px`, flex column, background `--bg`.
- **Header**, 64px tall, padding `0 24px`, 1px bottom border `--line`, gap 16px. From left to right:
  1. **Logo mark:** a 26×26 square with radius 8 and fill `--brand`. Inside it is a 10×10 ring with a 2px `--on-brand` border. Next to it is the wordmark "PhotoSense" at 17px/600, with an 11px gap.
  2. **Folder chip** (results only): pill, 40px tall, 1px border `--line`, padding `0 5px 0 16px`. Text at 13px `--t2`: **Phone Pictures** + **Jamie's Phone** · 6,941 files · scanned today at 4:25 PM. The folder names are `--t1`/500 and show only the last path segment. Long text gets an ellipsis. At the end is the inner pill button **"Change or rescan"** (30px tall, `--s2`, hover `--s3`), which opens the Setup screen.
  3. A spacer.
  4. **Local-only pill:** `--s2` background, 13px `--t2`, a 7px `--keep` dot, and the text "Runs on this computer. Nothing is uploaded."
  5. **Error chip:** only shown when there are errors. Pill, 36px tall, `--rose-bg`, 1px `--rose-line` border, `--rose-t` text at 13px/600, a 7px `--rose` dot, and "N error(s)". Clicking it toggles the Errors panel.
  6. **Menu button:** a 40×40 circle with `--s1` background (`--s3` when open) and a 1px `--line` border. It shows three 16×2 bars with a 4px gap.
- **Menu popover:** anchored top 58px, right 24px. 300px wide, padding 8, radius 16, `--pop` background.
  - "THEME" label, then a segmented pill with **Dark** and **Light**. The active segment has a `--pop` background and weight 600, and each option has a small swatch dot. The choice is saved to `localStorage` and applied right away.
  - A divider, then **Errors** with its count on the right (opens the Errors panel), then **Change folders or rescan**.
  - A divider, then the note "Removed files are in _PhotoSense_Removed inside each scanned folder." at 12.5px `--t3`.
  - Clicking outside or pressing Esc closes it.
- **Errors popover:** same anchor, 440px wide, max height 480px, scrolls.
  - Header: "Errors" at 15px/600, and a "Clear all" pill when the list isn't empty.
  - Each row has an 8px rose dot, the message at 14px, the time at 12px `--t3`, and a "Clear" pill.
  - Empty state: "No errors. Problems stay listed here until you clear them."
- **Offline bar:** shown when the service doesn't answer. Full width under the header, padding `10px 24px`, `--rose-bg` background with a `--rose-line` bottom border. Text: "The PhotoSense service is not answering, so what is shown here may be out of date." On the right is a **Retry** pill.
- **Toasts:** fixed at bottom-right (24px), stacked with a 10px gap, at most 4 shown.
  - Each toast is a pill with `--pop` background, max width 540px, and padding `10px 10px 10px 16px`. It has an 8px dot (green `--keep` for success, `--brand` for info, `--rose` for errors), the message, and a 28px round × button.
  - Success and info toasts disappear after 4.5s.
  - **Error toasts stay until dismissed.** They have a `--rose-line` border and the note "Kept in Errors". Every error is also added to the Errors list.

### 1. First run / Setup
This replaces the left setup column. It is shown on first launch, from "Change or rescan", and from the menu. The content is a centered column with top padding `clamp(32px, 8vh, 96px)` and a 40px gap.
- **Headline:** "Find duplicate photos and videos".
- **Subtext** (18px `--t2`, max width 680px): "PhotoSense compares the pictures and videos in your folders, keeps the best copy of each, and lets you check the rest before anything moves."
- **Folder card:** `min(1240px, 100%)` wide, padding 22, radius 20, `--s1` background, 1px border, and the setup shadow. It is a grid `1fr 1fr auto` with a 16px gap, aligned to the bottom.
  - **Root folder** and **Secondary folder · optional, for example a backup**. Each label is 13px `--t2`. Each input is 50px tall with radius 12, `--bg` background and a 1px border, in DM Mono 14px. The **Browse** pill (38px, `--s2`) sits inside the input on the right.
  - An input whose folder isn't found gets a `--rose` border.
  - The third column holds the **Recursive · include subfolders** checkbox (18px, brand fill when checked) and above it the **Scan** pill: 50px tall, padding `0 34px`, `--brand` background, `--on-brand` text, 15px/600.
  - The Scan button reads "Scan", then "Starting…", then "Scanning…", and is disabled at 55% opacity while running.
- **Validation:**
  - An empty root folder gives an error toast: "Choose a root folder to scan."
  - A folder that doesn't exist gives an error toast: "Folder not found: <path>", and the field gets a red border.
  - Typing in the field clears the red border.
- **Progress** (shown while scanning), in a block the same width as the card:
  - One row with the percent (40px), then "2,984 of 6,941 files", then each folder's count ("Phone Pictures 2,104 / 4,212" in mono), then "About 13 minutes for 7,000 files" on the right.
  - A **mosaic** grid of 30 × 4 = 120 square tiles with a 6px gap and radius 4. The tiles fill left to right in proportion to progress. Filled tiles show small thumbnail colors (real thumbnails of processed files would work well). Empty tiles are `--s3` at 60% opacity.
  - **Log tail:** the last 3 lines in DM Mono 12.5px, with the timestamp in `--t3` and the message in `--t2`. A "Show full log" link expands a scrollable box (max 240px, `--stage` background) with up to the last 200 lines.
- When the scan finishes, switch to the Results screen, select the first group and show the toast "Scan finished: 263 duplicate groups and 170 similar groups".
- If results already exist, show a **Back to results** pill.

### 2. Results (main screen)
**Toolbar row:** at least 72px tall, padding `0 24px`, 1px bottom border, gap 20. From left to right:
- **Tabs:** "Duplicates 263" and "Similar 170", 16px/600, with the count in DM Mono 12px `--t3`, 24px apart. The active tab has a 2px `--t1` underline across the full row height. The inactive tab is `--t2`.
- **Search pill:** `clamp(220px, 22vw, 380px)` wide, 42px tall, `--s1` background, 1px border. It has a small circle icon and the placeholder "Search by file name or folder". It filters by file name or folder of the original or any copy and resets to page 1.
- **Hide reviewed** toggle (Duplicates only): a 32×18 track with a 14px knob. On: `--brand` track and `--on-brand` knob. Off: `--s3` track and `--t1` knob. It hides groups where every copy is marked keep.
- **Summary** (Duplicates), right-aligned: "**264** duplicate files taking **795 MB**. The best copy of each picture or video is kept." at 14.5px. Below it at 12.5px `--t3`: "Deleted files move to _PhotoSense_Removed and can be moved back."
- **Delete all duplicates:** pill, 44px tall, padding `0 22px`, `--rose` background, white 15px/600 text. Disabled when there's nothing left to delete.
- **Similar tab:** the summary and delete button are replaced by "Similar shots are burst frames or edited versions. Nothing here is removed in bulk; review them one at a time."

**Body:** a grid `clamp(380px, 32vw, 620px) | 1fr`.

**Left: group gallery.** `--s1` background with a right border.
- Header row (padding `14px 20px`, 13px `--t2`): "**263** groups" on the left. On the right are **Prev** and **Next** pills (30px, 1px border, 40% opacity when disabled) around "Page 1 of 6". Pages hold 50 groups each.
- The grid scrolls with padding `6px 20px 24px`, uses `repeat(auto-fill, minmax(150px, 1fr))`, and has gaps of 20px between rows and 14px between columns.
- **Tile:**
  - A square thumbnail with radius 10. In the top-right corner is a count badge ("+1", "+2"): a pill, `rgba(0,0,0,.55)` background, white DM Mono 11.5px.
  - Below it are the file name (14px/500, ellipsis) and the summary (12.5px `--t2`): "1 duplicate · 12.0 MB", "2 copies, all marked keep" (in `--keep`), or "3 similar shots".
  - **Video tile:** dark (`#1c2124`-ish) with a white play triangle and the duration in DM Mono 13px. "VIDEO" sits top-left as an 11px/700 label on `rgba(255,255,255,.14)`.
  - **Selected tile:** the ring described under Shadows.
- **Empty states:** "No duplicates to show", "No similar shots found", "No groups match "x"". While loading, show "Loading…".

**Right: review desk.** Scrolls. Padding `22px 28px 28px`, flex column, gap 18.
1. **Title row:** the file name (20px/600) and the sub line (14px `--t2`): "1 duplicate · 12.0 MB to free", "All copies marked keep", or "3 similar shots".
2. **Two panes** in a `1fr 1fr` grid with a 20px gap.
   - **Left:** the **ORIGINAL** badge (**BEST** on the Similar tab) on `--keep-bg` in `--keep` text, plus "The best copy of this picture. It stays." The image box is `clamp(200px, 28vh, 300px)` tall with radius 14, `--stage` background and the keep-line inset ring. The image is letterboxed at its true aspect ratio. A bottom-right pill (`rgba(0,0,0,.55)`, 12px) shows "JPEG · 24.0 MB".
   - **Right:** a badge for the selected copy's match kind (or KEEPING), plus "Copy 1 of 1 · HEIC" and "Click either to compare" on the right in `--t3`. The image box is the same with the `--line` ring.
   - **Video:** the image is a dark tile with a 56px translucent play circle, plus an **Open in default player** pill in the top right.
   - Clicking either pane opens the Comparison window in side-by-side mode.
3. **Copy strip:**
   - Title "Duplicates of this picture (N)" (15px/600; "Similar shots (N)" on the Similar tab), plus "Hover for details, click to show it beside the original" in 13px `--t3`.
   - Tiles are 120×84 with radius 10. Each has a format chip in the top left (DM Mono 11px on `rgba(0,0,0,.55)`) and a bottom label band of 10px/700 caps: IDENTICAL FILE, SAME PICTURE, SIMILAR, or **KEEPING** on a green band. The selected tile has a 2px `--brand` ring.
   - Clicking a tile makes it the copy in the right pane.
   - **Hover card:** 420px wide, `--pop` background, radius 14, positioned above the tile. It shows:
     - name + match chip, date, folder (mono), place, and the file line (pixel size · format · size, plus duration for video);
     - a divider, then the amber label **"DIFFERS FROM THE ORIGINAL IN"** and a list such as "**Name:** the original is IMG_4299.JPG", "**Format:** HEIC, the original is JPEG", "**File size:** 12.0 MB, the original is 24.0 MB", "**Folder:** Jamie's Phone, the original is in Phone Pictures";
     - a divider, then "Original preferred: <reason in --keep>".
4. **"What differs" table.** Its columns are `150px | 1fr | 1fr`.
   - Header row at 12px/600 caps in `--t3`: WHAT DIFFERS · ORIGINAL (in `--keep`) · THIS COPY.
   - Rows are at least 36px tall with radius 8 and 14px horizontal padding. The rows are: Name, Format, File size, Folder (mono 13px), Taken, Place (with a "Show on map" link), Pixel size (for video: "Video", showing px · duration), and Camera.
   - **Rows that differ come first.** They have a `--diff` background and a 6px amber dot before the label, and the copy's value is shown at the **same 14px regular size as the original** (not bold).
   - Rows that match show "Same" in `--t3` in the copy column.
5. **Action bar:** 18px top padding with a top border, and wraps when narrow.
   - Left: "Original preferred: <reason>" (14px `--t2`, reason in `--keep`/500), and below it the note "Deleting moves files to _PhotoSense_Removed inside the scanned folder. Move them back to restore them."
   - **Keep this copy / Stop keeping this copy:** pill, 44px tall, `--keep-line` border, `--keep` text. When kept, the background is `--keep-bg`.
   - **Delete this duplicate · 12.0 MB:** solid rose pill. This is used when the group has 1 copy, and it asks to confirm the group deletion.
   - When the group has more than 1 copy, the button reads **Delete this copy · X MB** with a rose **outline** (`--rose-line` border, `--rose-t` text), and there is also a solid **Delete these N duplicates · X MB** button.
   - On the Similar tab, "Delete this copy" is solid and there is no group delete.
   - Delete-this-copy is disabled (40%) when that copy is marked keep.
- **Empty state** (no selection): "Select a group to review", centered in `--t3` at 16px.

### 3. Comparison window
A fixed overlay with a `--scrim` backdrop and 40px inset. The panel is `--s1` with radius 18 and the dialog shadow. **Esc or clicking outside closes it.**
- **Header** (72px tall, bottom border):
  - The file name (18px/600; the original's name when in Original mode) and a match chip.
  - In the center is a **3-segment pill toggle**: **Side by side | This copy | ● Original** (the dot is `--keep`). Each segment is 36px tall with padding `0 18px`. The active segment has a `--t1` background with `--bg` text.
  - On the right are an "Esc" key hint and a 38px round × button.
- **Body:** a grid `1fr | 420px`.
  - **Stage** (`--stage` background):
    - **Side by side:** two images at their true aspect ratio. The original has a 3px keep-line ring and the caption "ORIGINAL IMG_4299.JPG · JPEG · 24.0 MB". The copy has a 1px line ring and the caption "THIS COPY IMG_4299.HEIC · HEIC · 12.0 MB".
    - **This copy / Original:** one large image in the same spot. The Original has a solid 4px `--keep` ring and a top-left label "ORIGINAL · IT STAYS" (green fill, dark text). The copy has the label "THIS COPY · SAME PICTURE" on a dark translucent background. There is a caption line underneath.
    - The hint "Press Space to flip between this copy and the original in the same spot" is always shown. **Space flips between This copy and Original**; from Side by side it goes to Original.
  - **Side panel** (padding 24, gap 20, scrolls):
    - MATCH: "**Same picture.** The same shot converted, resized or re-compressed." (Identical: "Byte-for-byte the same file." Similar: "A burst frame or an edited version. Never removed in bulk.") Plus a KEEPING badge if the copy is kept.
    - A details grid (72px | 1fr): Name, Taken, Folder (mono, wraps anywhere), Place (with coordinates and "Show on map"), File, Camera.
    - An amber box (`--amber-bg`, radius 12) with **"DIFFERS FROM THE ORIGINAL IN"** and the list.
    - "Original preferred: <reason>".
    - Actions, pinned to the bottom, all pills:
      - **Open in default viewer** (`--s2`).
      - **Keep this copy too / Stop keeping this copy** (keep outline).
      - **Delete this copy · 12.0 MB** (solid rose, 46px). This is **two-step**: clicking it swaps the button for an inline confirm box (`--rose-bg`, radius 14) reading "Move **IMG_4299.HEIC** (12.0 MB) to _PhotoSense_Removed? You can move it back later." with [Cancel] and a [Delete this copy] rose pill that reads "Working…" while running.
      - The recovery note.
      - **Delete the original instead:** a quiet 13px `--t3` underlined text link. It also has an inline confirm (neutral `--s2` box): "Move IMG_4299.JPG (24.0 MB) to _PhotoSense_Removed and keep IMG_4299.HEIC as the original instead?" with [Cancel] and a [Delete the original] rose outline pill. On success, the copy becomes the group's original.
    - After deleting, the window stays open on the same group if copies remain. Otherwise it closes, and the selection moves to the next group.

### 4. Folder browser dialog
Centered, 720×560, radius 20, `--pop` background.
- Header: the title "Choose the root folder" (or "Choose the secondary folder") at 20px/600. Below it, an **Up** pill (40% when at a drive root) and the current path in a mono box (40px, radius 12, `--bg`).
- Body: a grid `180px | 1fr`.
  - **Start from:** Pictures, Home, Disk C:, Disk D:. These are pill rows, and the current one has a `--sel` background and weight 600.
  - **Subfolder list:** rows of 10px 12px with radius 10 and `--s2` on hover. Each row has a small brand-colored folder block, the name and a "›". Clicking a row opens that folder.
  - Empty state: "No folders inside this one."
- Footer (top border): [Cancel] outline pill and [Use this folder] brand pill. Esc closes it.

### 5. Confirmation dialog
Centered, 520px, padding 28, radius 20, `--pop` background, gap 16.
- Title: "Delete all duplicates?", "Delete the duplicates of IMG_4299.JPG?" or "Delete IMG_4299.HEIC?"
- Body lines (15px `--t2`):
  - "264 files taking 795 MB will be removed."
  - "The best copy of each picture or video stays." (for a single group: "The best copy, IMG_4299.JPG, stays.")
  - If any copies are kept: "N copies you marked keep are skipped."
- A note box (`--s2`, radius 12): "Files go to `_PhotoSense_Removed` inside the scanned folder. Moving a file back restores it."
- Buttons: [Cancel] outline pill and [Delete 264 files] rose pill, which reads **"Working…"** while running. Both are disabled while running, and the backdrop and Esc can't close the dialog then.
- Success toast: "Moved 3 duplicates (8.7 MB) to _PhotoSense_Removed".

---

## Interactions and behavior
- **Busy state:** while any delete or keep request is in flight, every other destructive action is disabled (50% opacity, `pointer-events: none`).
- **Keep toggle:** toast "Keeping X. Bulk deletion will skip it." or "Stopped keeping X." The strip label changes to KEEPING, and bulk and group deletes skip that copy.
- **Selection after delete:** if the group still has copies, it stays selected. Otherwise the next group in the list is selected.
- **Errors:** every failed request, validation error or service-down failure becomes `toast(kind: 'error')` **and** is added to `errors[]` with a timestamp. Error toasts don't time out. The header chip shows how many errors there are. Errors can be cleared one at a time or all together.
- **Theme:** saved in `localStorage` (the prototype uses the key `ps-proto-theme`). Apply it by toggling a class on `<html>` before the first paint to avoid a flash.
- **Hover states:** secondary pills use `--s2` → `--s3` on hover. Solid pills use `filter: brightness(1.08)`. Menu and list rows use a `--s2` background.
- **Keyboard:** Esc closes the top-most layer (folder browser, then confirm when not busy, then comparison, then popovers). Space flips the comparison window when focus isn't in an input.
- **Long paths:** paths in the folder chip, the diff table and the inputs use an ellipsis. In the side panel and the hover card they wrap with `overflow-wrap: anywhere`.
- **Responsive:** the minimum width is 1100px. The gallery width, search width and image heights use `clamp()` as described above, and the action bar wraps.

## State
```ts
theme: 'dark' | 'light'
screen: 'setup' | 'results'
root, second: string; recursive: boolean
scan: 'idle' | 'starting' | 'scanning'; progress: number; perFolder: {name, done, total}[]; log: {t, m}[] (last 200)
rootError, secondError: boolean
tab: 'duplicates' | 'similar'; query: string; hideReviewed: boolean; page: number
selectedGroupId; selectedCopyIndex; hoveredCopyIndex
compare: null | { mode: 'side' | 'copy' | 'original', confirm: null | 'copy' | 'original' }
confirm: null | { scope: 'all' | 'group' | 'copy', groupId?, copyId? }
busy: boolean
folderBrowser: null | { target: 'root' | 'second', path }
toasts: {id, kind: 'ok'|'info'|'error', msg}[]; errors: {id, time, msg}[]
menuOpen, errorsOpen: boolean
serviceDown: boolean
```
Data still comes from the existing API, `lib/` and SignalR progress. The "differs" list is computed client-side by comparing the copy to the original on name, format, pixel size, file size, folder and date, as the prototype's `differs()` does. Keep `Differences.tsx` if it already does this.

## Assets
- There are no image assets. Thumbnails and full images come from the existing endpoints. The striped tiles are placeholders.
- Fonts: DM Sans and DM Mono from Google Fonts.
- Icons are simple CSS shapes: the logo ring, the play triangle, the search circle, the menu bars and ×. Swap them for your icon set if you have one, at the same sizes.

## Screenshots
These were captured from the prototype at 1600×900 and scaled down. They are in `screenshots/`:
- `01-results-dark.png`: the main screen with a group selected
- `02-copy-hover-card.png`: the hover card on a copy tile, with "Differs from the original in"
- `03-compare-side-by-side.png`: the comparison window in Side by side mode
- `04-compare-flipped-to-original.png`: the comparison window flipped to Original
- `05-compare-inline-delete-confirm.png`: the two-step "Delete this copy" confirm
- `06-confirm-delete-all.png`: the confirmation dialog for Delete all duplicates
- `07-menu-theme-toggle.png`: the menu with the Dark/Light toggle
- `08-setup-first-run.png`: the Setup / first-run screen
- `09-folder-browser.png`: the folder browser dialog
- `10-folder-not-found-error-toast.png`: the inline error border and the error toast that stays until dismissed
- `11-errors-panel.png`: the persistent Errors panel
- `12-scanning.png`: the scan in progress (mosaic, counts and log tail)
- `13-results-light.png`, `14-compare-light.png`: the Light theme

## Files
- `PhotoSense Prototype.dc.html` is the clickable spec (it needs `support.js`). Markup with inline styles is at the top and the behavior logic is in `class Component`.
- `reference/PhotoSense Redesign (explorations).dc.html` has the earlier directions 1a, 1b and 1c, for context.
