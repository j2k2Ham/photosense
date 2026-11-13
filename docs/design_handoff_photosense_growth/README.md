# Handoff: PhotoSense growth features (Phases 1–4, first launch, settings and subscription, tablet, desktop)

## Overview

This package adds the features in `prototype/PhotoSense Product Plan.dc.html` to PhotoSense on iPhone, iPad, Android phone, Android tablet and the desktop app:

- **Phase 1:** Safe to delete, Swipe to clean, Automatic backup, and the Free plan with 50 removals a day.
- **Phase 2:** more Clean up categories, video compression, low-storage alerts, the share card.
- **Phase 3:** on-device search, trips and the travel map, fixing dates and places, sharing without location, the hidden vault, widgets and app shortcuts.
- **Phase 4:** the Family plan and Year in review.
- **Also:** first launch, Settings and the Subscription page, tablet layouts, and the desktop Phones and Family tabs.

It builds on `design_handoff_photosense_mobile/`, which is still the source for the shell, the four Native skins and the shared components. This README only covers what is new or changed. Where it says "as in the mobile handoff", follow that README.

## About the design files

The files in `prototype/` are **design references made in HTML**. They show the intended look and behaviour; they are not production code. Rebuild them in PhotoSense's existing stack:

- **Phone and tablet UI** goes in `PhotoSense.ReactUI` (Next.js, React, SWR, Tailwind), which both phone apps show in a web view, exactly as in the mobile handoff.
- **OS services** go in each app's thin native layer, behind the in-process API:
  - background backup
  - photo library writes (dates, places, deletes)
  - biometrics
  - purchases
  - notifications
  - widgets and app shortcuts
- **Desktop screens** go in the desktop layout of the same page, using the desktop tokens already in the repo.

## Fidelity

**High fidelity.** Colours, type, sizes, radii, spacing, copy and states are final. Numbers in the mockups (6,941 items, 19.8 GB and so on) are sample data. They must come from the API.

## How to view the prototype

1. Open `prototype/PhotoSense Growth Features.dc.html` in a browser, with every file in `prototype/` beside it.
2. The controls at the top switch Platform (iPhone / Android), Theme (Dark / Light) and Plan (Free / Trial / Pro).
3. Every phone, tablet and desktop frame is clickable and starts at the state named by its badge.

Behaviour lives in three files:

- `FeaturePhone.dc.html`, for phones. `seed()` lists every named start state; `renderVals()` derives each screen.
- `FeatureTablet.dc.html`, for tablets.
- `DesktopPhones.dc.html`, for the desktop.

`prototype/PhotoSense Product Plan.dc.html` holds the product reasons, pricing and trust rules. Read its Pricing and Trust rules sections before building anything with a limit or a paywall.

## Screenshots

There are 228 in all. Phones have one file per state, numbered the same in all four folders: `screenshots/iphone/{dark,light}/NN-iphone-{theme}.jpg` and `screenshots/android/{dark,light}/NN-android-{theme}.jpg`.

| # | State | # | State |
|---|---|---|---|
| 01 | Welcome | 26 | Library with search and collections |
| 02 | Why photo access is needed | 27 | Search results (free plan gate) |
| 03 | System photo-access prompt | 28 | Share without location |
| 04 | Pair a computer | 29 | Trips |
| 05 | First scan, in progress | 30 | Travel map |
| 06 | Scan result | 31 | Trip |
| 07 | Full-screen Pro offer | 32 | Fix dates and places |
| 08 | Library with backed-up badges (Phase 1) | 33 | Hidden vault, free plan |
| 09 | Safe to delete review | 34 | Hidden vault, unlocked |
| 10 | Over the free limit (Pro sheet) | 35 | Home screen widgets |
| 11 | Space freed (Pro) | 36 | App shortcuts menu |
| 12 | Clean up, Swipe to clean entry (Phase 1) | 37 | Family offer |
| 13 | Swiping | 38 | Family |
| 14 | Swipe summary | 39 | Family shared photos |
| 15 | Swipe summary over the free limit | 40 | Add family members |
| 16 | Backup, waiting | 41 | Year in review card in Library |
| 17 | Backup, running | 42 | Year in review, opening card |
| 18 | Clean up with all categories | 43 | Year in review, busiest month |
| 19 | Screenshots grid | 44 | Year in review, clean up |
| 20 | Bursts | 45 | Settings, Free |
| 21 | Large videos | 46 | Settings, Trial |
| 22 | Compress a video | 47 | Settings, Pro |
| 23 | Low-storage notification | 48 | Subscription, Free |
| 24 | Free up space | 49 | Subscription, Trial |
| 25 | Share card | 50 | Subscription, Pro |

The tablet and desktop screenshots are named by screen:

- **Tablet:** `screenshots/ipad/{dark,light}/` and `screenshots/android-tablet/{dark,light}/` each hold T1 Library, T2 Clean up, T3 Swipe, T4 Trips and T5 Subscription.
- **Desktop:** `screenshots/desktop/{dark,light}/` holds D1 Phones, D2 Pair a phone, D3 Rename by date and D4 Family.

---

## Shared rules

### The free daily limit

- The free plan removes **50 items a day**. Clean up, Safe to delete, Swipe to clean, Free up space and deleting from the viewer all draw on the same count. Video compression is Pro only, so it never uses the count.
- The count goes up only when the system delete is confirmed, by the number actually deleted. It resets at local midnight.
- If someone selects more than is left, the **Pro sheet** opens with this line: "You chose N items. The free plan deletes 50 items a day, and you have L left today." It has two secondary buttons:
  - "Delete L now". For Safe to delete the button reads "Remove the L largest now", and it removes the largest backed-up items first.
  - "Not now".
- If none are left, the line reads "You have used today's 50 free removals. More are available tomorrow." The partial button is hidden.
- After a partial delete, the result screen shows a card: "N more are selected", "The free plan deletes 50 items a day. Come back tomorrow, or get Pro to remove the limit.", with **Get Pro**.
- Swipe to clean never shows a paywall during a session. The footer shows "Free plan: L removals left today." and turns amber, adding "The rest stay marked for tomorrow.", once the marked count passes L. The summary deletes the L largest and keeps the rest marked.

### Pro sheet

- A bottom sheet (`--sheet`, radius `--r-sheet` top corners, grabber 36 × 5 or 32 × 4).
- From the top:
  - an optional context box (`--amber-soft`, radius 16, 14.5 px)
  - the app icon at 48 px with "PhotoSense Pro" 24 px / 700 and "One purchase covers phone, tablet and computer"
  - a list of features, each with a check in `--acc`
  - the plans
  - the main button, the secondary buttons and the fine print
- **Plans** are radio cards: min-height 60, radius 16, `--sheet-cell`, a 2 px `--acc` border when selected.
  - Yearly · "7-day free trial, then $1.67 a month" · $19.99 · badge "Save 44%" (`--keep-soft` / `--keep`)
  - Monthly · "Billed every month" · $2.99
  - Lifetime · "Pay once" · $39
  - Family · "Pro for up to 6 people" · $29.99 (from Phase 4)
- **Main button:** "Start 7-day free trial", "Subscribe for $2.99 a month", "Buy for $39" or "Start Family for $29.99 a year".
- **Fine print:**
  - Yearly: "Free for 7 days, then $19.99 a year. We remind you 2 days before the trial ends. Cancel any time in Settings."
  - Monthly: "Renews every month. Cancel any time in Settings."
  - Lifetime: "One payment, no renewal."
- **Context lines** by entry point:
  - Automatic backup: "Automatic backup is part of Pro. Sending photos yourself stays free."
  - Compression: "Video compression is part of Pro."
  - Search: "Searching for what is in photos is part of Pro. Searching by date and place stays free."
  - Fix: "Fixing dates, places and file names is part of Pro."
  - Vault: "The hidden vault is part of Pro."
- Purchases go through StoreKit 2 and Google Play Billing. After a purchase, the action that opened the sheet carries on: the delete, turning on automatic backup, compressing, or applying the fix.

### System dialogs (real, not built in HTML)

These come from the OS. The mockups show them so the flow reads end to end:

- **Photo access:** iOS full or limited access; Android "Allow all", "Select photos and videos" or "Don't allow". The iOS purpose string is: "PhotoSense checks photos on this iPhone to find duplicates and back them up to your computer."
- **Deleting:**
  - iOS: "Allow "PhotoSense" to delete N items?" When iCloud Photos is on, it adds the iCloud line.
  - Android: "Allow PhotoSense to move N items to trash?"
- **Biometrics:** Face ID on iOS (`LAContext`) and BiometricPrompt on Android: "Unlock Hidden", "Use your fingerprint".
- **Purchase and subscription management:** the App Store and Google Play sheets.

### Words that differ by platform

| | iOS | Android |
|---|---|---|
| Deleted items | Recently Deleted | Trash |
| Library | Photos library | gallery |
| Device | iPhone / iPad | phone / tablet |
| Store | App Store, Apple Account | Google Play, Google account |
| Unlock | Unlock with Face ID | Unlock with fingerprint |

### New tokens

These go on the existing skins:

- `--srch-h`: 40 px on iOS, 56 px on Android
- `--srch-bg`: `--fill` on iOS, `--cell` on Android
- `--wg-r` (widget radius): 24 px on iOS, 28 px on Android
- `--ai` (home-screen icon size): 62 px on iOS, 56 px on Android
- `--ai-r` (icon radius): 15 px on iOS, 50% on Android
- `--qa-r` (shortcut menu radius): 20 px on iOS, 16 px on Android

Fixed colours:

- Year in review cards: `oklch(0.28 0.05 205)`, `oklch(0.28 0.06 245)`, `oklch(0.27 0.06 285)`, `oklch(0.29 0.06 165)`, `oklch(0.25 0.04 205)`. Accent text `#A8EEF7`, white text.
- Share card: `#0F2E33` background, white text, `#A8EEF7` line, pills `rgba(255,255,255,.14)`. Font DM Sans.
- Category tiles on iOS: `oklch(0.62 0.13 H)` with a white icon. On Android there is no tile; the icon is `--t2`.
  - Hues: Large videos 28, Similar 262, Messaging 152, Duplicates 200, Bursts 330, Screenshots 232, Blurry 60, Memes 12, Receipts 180, Closed eyes 88.

---

## Screens: phone

Headers, lists, buttons, toasts, sheets and the tab bars are as in the mobile handoff. The tabs are now **Library, Clean up, Organize, Backup**: Send becomes Backup, with the same icon.

### First launch (01–07)

- **Layout:** full screen with 24 px side padding. At the top, progress dots: 6 px tall, the current one 22 px wide in `--acc`, done ones 6 px in `--acc`, the rest `--fill`. At the bottom, a sticky main button, plus an optional text button 42 px high in `--acc`.
- **Welcome (01):** app icon 92 × 92 (radius 22) and "PhotoSense" 32 px / 700. The line: "Free up space, back up to your own computer and keep your library tidy. Everything runs on your devices." Three rows with 36 px `--cell` tiles: "Find duplicates, screenshots and blurry shots", "Back up to your own computer over Wi-Fi", "Delete only what is already backed up". Button: **Get started**.
- **Photo access (02–03):** a 72 px tile in `--acc-soft` and "Let PhotoSense see your photos". The reason: "PhotoSense needs full access to find duplicates and show what is safe to delete. Photos are checked on this [iPhone] and never uploaded." A card: "With limited access, PhotoSense can only check the photos you pick, so most of Clean up and Safe to delete will not work." **Continue** opens the system prompt.
  - Allow goes on to pairing.
  - Limited goes on, with the toast "Limited access: PhotoSense checks only the photos you pick."
  - Don't allow stays, with the toast "PhotoSense needs photo access to work. You can allow it later in Settings."
- **Pair (04):** "Connect your computer", then numbered steps:
  1. "Install PhotoSense on your Mac or PC."
  2. "On the computer, open Phones and choose Pair a phone."
  3. "Point this [iPhone] at the code on the screen."

  A 230 × 230 camera viewfinder (radius 28, white corner brackets). **Scan the code**, and **Skip for now**. Once paired: a green tick, "Paired with Studio iMac", "Backups go to Pictures/PhotoSense on Studio iMac. You can change the folder on the computer."
- **Scan (05):** "Scanning your library", the count 44 px / 700 "N of 6,941", an 8 px bar, and live counts for Duplicates, Screenshots, Blurry and Large videos. A green dot and "Everything stays on this [iPhone]." The button is disabled at 50% opacity.
- **Result (06):** "Scan complete" in `--keep`, the total found 52 px / 700, "can be freed from this [iPhone]", and the 5 largest categories with item count and size. A card: "The free plan removes 50 items a day, and Swipe to clean is included. Nothing is deleted until you confirm." **Continue**.
- **Offer (07):** a close button at the top right (it means Not now), "Free all [total] with Pro", four features, the Yearly, Monthly and Lifetime plans, the main button, **Continue with Free**, and the fine print. Both choices land on Clean up. Show this once, after the first scan only.

### Phase 1 (08–17)

- **Library (08):**
  - Each backed-up tile has an 18 px badge at the bottom left: `rgba(0,0,0,.52)` with a white tick.
  - Banner: "6,561 items are on Studio iMac" / "Remove them from this [iPhone] to free up to 31.1 GB." with **Review** and a hide button.
  - Chips: All, On Studio iMac, Waiting.
  - Footer line: "On Studio iMac, checked today at 9:41".
- **Safe to delete review (09):**
  - The amount 52 px / 700 with "can be freed from this [iPhone]". A storage bar: to remove in `--acc`, everything else in `--t3`, free in `--fill2`.
  - "Checked on Studio iMac": a tick row "N photos and videos" / "Each one matches the copy on the computer", then Last checked and Folder.
  - "Keep on this [iPhone]": Favorites and Last 30 days switches, both on by default.
  - iOS only, an amber card: "iCloud Photos is on. Removing items here also removes them from iCloud and your other Apple devices. The copies on Studio iMac are not affected."
  - Sticky button "Remove N items". Footer: "Free plan: L removals left today. Removed items stay in [Recently Deleted] for 30 days."
- **Space freed (11):** a 64 px tick, "28.1 GB freed", the sub-line, Before and Now bars, the share card row, then **Share** and **Done**.
- **Clean up and Swipe (12–15):**
  - The swipe card on Clean up: a fan of 3 tilted cards, "Swipe to clean", a 7-day streak row of 24 px circles, and Start or Continue.
  - Swiping: a card with a 28 px radius filling the space; drag past 100 px to decide.
    - It rotates by dx/18 degrees and shows DELETE (`#FF453A`) or KEEP (`#30D158`) stamps.
    - Flags: On Studio iMac, Blurry, Screenshot.
    - 68 px Delete and Keep buttons, and Undo at the top right.
  - Summary:
    - the amount marked, the streak card, and Reviewed / Kept / To delete
    - the marked grid: 6 columns; tap to keep
    - an amber over-limit notice, and "Delete L now" plus "Delete all N with Pro"
- **Backup (16–17):**
  - Computer card: Studio iMac, "Connected on Home Wi-Fi". Status: "380 new items waiting", "Backing up N of 380", "Paused at N of 380" or "Up to date".
  - A 6 px bar and 6 thumbnails that tick off as they arrive. The button is Back up now, Pause or Resume.
  - An "Automatic backup" section with a PRO badge: Back up automatically (Pro), Wi-Fi network, Only while charging, Include videos, Folder.
  - Note on iOS: "iOS decides when background backups run. They usually happen while iPhone is charging on Home Wi-Fi. A backup also runs whenever PhotoSense is open."
  - Then "Send now" › Choose photos to send.

### Phase 2 (18–25)

- **Clean up (18):**
  - The subtitle is the total found ("19.8 GB can be freed"), with a storage bar.
  - The free plan adds a row "L of 50 free removals left today" with **Get Pro**.
  - The swipe card, then **Find**: each category as a row with its tile, name and "N items · size", sorted by size.
    - Large videos, Similar shots, Messaging apps (Android only, "WhatsApp and Telegram"), Duplicates, Bursts ("N extra shots"), Screenshots, Blurry, Memes, Receipts and documents, Closed eyes.
  - **Alerts:** Low-storage alerts, "When less than 10% is free, at most once a week".
- **Category grid (19):**
  - Header: the name, with Deselect all / Select all.
  - The selected size 40 px / 700, "selected from N [unit]", and the note "Found on this [iPhone]. Nothing was uploaded. Tap a photo to keep it."
  - 4-column grid, all selected at first. The selection circle sits bottom right on iOS and top left on Android.
  - Sticky "Delete N items" and the limit line.
- **Bursts (20):** a card for each burst: date, "Place · N shots", 6 columns of 3:4 tiles (radius 8). The kept shot has a 2.5 px `--keep` border and a "Keep" pill; the others are at 50% opacity. Tap a shot to keep it instead. "Delete N extra shots".
- **Large videos (21):** total and "in 24 videos over 200 MB".
  - A Compress all card (`--acc-soft`, PRO badge on free): "Saves about N GB at Balanced quality".
  - Rows: a 56 px thumbnail with its duration, place, "date · 4K / 1080p", size, "→ compressed size". Done rows show "Compressed" in `--keep`.
- **Compress (22):**
  - Two 3:4 frames, Original and Compressed, with sizes under them. "Saves about N", "1.8 GB → 450 MB · same frame at 0:12".
  - A segmented control: High (×0.45, "Original resolution, HEVC"), Balanced (×0.25, "1080p, HEVC") and Small (×0.12, "720p, HEVC").
  - Rows: name and date, "Date and location · Kept", "Original · Moves to [Recently Deleted]".
  - Footer: Compress, then "Delete this video instead". On free: "Video compression is part of Pro."
  - While running there is a progress bar and Cancel. When done, the system delete dialog for the original appears.
    - Allow: the toast "Saved N. The original is in [Recently Deleted] for 30 days." with Share.
    - Deny: "The original and the smaller copy are both kept."
- **Low-storage alert (23):**
  - Lock screen notification. Title "[iPhone] storage is 92% full", body "PhotoSense found 4.1 GB you can free."
  - Android adds **Review** and **Not now**.
  - Only when less than 10% is free, at most once a week. The number must equal what Free up space shows.
- **Free up space (24):**
  - An amber kicker with the % full, the selected size 46 px, a storage bar, and checkboxes for Duplicates, Screenshots, Blurry and Closed eyes (iOS circles, Android rounded squares).
  - Note: "These four are the quickest to check. Large videos and similar shots are in Clean up. Low-storage alerts come at most once a week."
- **Share card (25):**
  - A sheet: "Share your result" / "None of your photos are on the card."
  - Square (272 × 272) or Story (204 × 362).
  - The card: PhotoSense logo, the size 60 / 50 px, "freed on my [iPhone]", pills for the streak and the item count.
  - A switch "Show streak and item count", then **Share** (the system share sheet with the image) and Not now. It is offered only on result screens.

### Phase 3 (26–36)

- **Library (26):**
  - A search field (`--srch-h`, `--srch-bg`, radius 999).
  - Three collection tiles (radius 16): Trips · "6 trips"; Needs fixing · "374 items"; Hidden, with a lock on `--fill2` and "24 items" or "Pro".
  - Tapping a photo opens the viewer.
- **Search (27):**
  - Before searching: Recent and Try lists.
  - The query is split into chips: In photo, Place, Month, Year.
  - Free plan with In photo words: an `--acc-soft` card, "Searching for what is in photos, like "dog" and "beach", is part of Pro. Showing photos that match 2023.", with Get Pro. Results match only the date and place parts.
  - Pro: full results in a 4-column grid.
  - Footer: "Search runs on this [iPhone]. Your photos and searches are not uploaded."
  - It uses an on-device image-text embedding index (Core ML / LiteRT), built while charging.
- **Viewer and Share without location (28):**
  - The viewer has the date and place in the header, the photo, and a toolbar: Share, Favorite, Info, Delete. It is a glass capsule on iOS and a bottom bar with labels on Android. Info shows Date, Place, Camera and Size.
  - Share opens "Share photo" with two switches: "Remove location" (on, "Glacier National Park is left out of the copy.") and "Remove camera details" ("Camera model, lens and exposure").
  - Note: "The photo in your library keeps its location and details. Only the shared copy changes."
- **Trips (29–31):**
  - A List / Map segmented control. The note: "Grouped by place and date from photos taken away from home in Bozeman, Montana."
  - List: 16:9 cards with name, dates and count.
  - Map: pins with the count (`--acc`, 2 px `--bg` ring), a Home pin, and labels on `--bg`. Use MapKit or Google Maps; the grid is a placeholder.
  - A trip: a 16:10 cover, the name 28 px / 700, a grid for each day, and a sticky "Make a folder on Studio iMac". Once done it shows "Folder created" and the toast "Created Pictures/PhotoSense/2026-07 Glacier National Park on Studio iMac."
  - Trips use the 5 km clusters from Organize plus gaps between dates.
- **Fix dates and places (32):**
  - The total "374 items", "need a date or place".
  - Cards, each with an icon tile, title, sub-line, 6 thumbnails, a change box ("from" struck through → "to"), a note, and the action button (PRO badge on free):
    - Camera clock not set (32): Jan 1, 2000 → Mar 2, 2026
    - Missing location (342): No location → Place of the nearest photo
    - File names on Studio iMac: IMG_4821.HEIC → 2026-03-02 14.31.07.HEIC
  - When done: "✓ 32 dates fixed" with Undo.
- **Hidden vault (33–34):**
  - Free plan: a lock, "Hidden vault", the explanation, and Get Pro.
  - Pro: "Hidden is locked", "24 items", "Unlock with Face ID / fingerprint", then the biometric prompt.
  - Unlocked: a 4-column grid, Lock at the top right, an info card, and an amber warning: "If you delete PhotoSense, hidden items that are not backed up are deleted too. 3 items are not backed up yet."
  - Then **Add photos**.
- **Widgets (35):**
  - Storage widget, medium (166 high): "92% full" in amber, the amount found, "can be freed", and a backup line with a **Back up now** button (an interactive widget through App Intents on iOS 17+ and Glance on Android).
  - Swipe widget, small (166 × 166): "Swipe to clean", "50 photos ready today", the streak dots and "6-day streak". Tapping it opens Swipe to clean.
- **App shortcuts (36):** press and hold the app icon. "Search photos", "Start swipe", "Back up now". The same three are App Intents for Shortcuts and Siri on iOS, and static app shortcuts on Android.

### Phase 4 (37–44)

- **Family (37–40):**
  - Opened from Backup › Family.
  - Offer: stacked avatars, "PhotoSense Family", "Pro for up to 6 people for $29.99 a year." Three perks:
    - "Everyone gets Pro on their own phone, tablet and computer"
    - "Everyone can back up to one family computer"
    - "Photos that several people have are kept once in a shared Family folder"

    The button is "Start Family for $29.99 a year".
  - Family: a plan card, "Members · 4 of 6" with avatar rows (36 px, `oklch(0.6 0.12 H)`, white initial) and their status, then Add members / Invite people. Family computer: Studio iMac. A shared photos card with Review.
  - Shared photos: "3.8 GB", "can be saved on Studio iMac", a 3-column grid with avatar chips, and "Keep one copy of 1,204 photos". Footer: "Extra copies on Studio iMac go to its Trash. Nothing changes on anyone's phone."
  - Add members:
    - iOS: Family Sharing. "Open Family Sharing" opens Settings.
    - Android: an invite link with Copy, and "Share invite link". The six seats show members' initials, with dashed circles for empty seats.
- **Year in review (41–44):**
  - A Library card in December.
  - Five full-screen story cards with progress bars at the top. Tap the right 70% to go on and the left 30% to go back.
    1. "2026", 7,512 photos and 316 videos
    2. Places, "3 trips"
    3. Busiest month: "July", with a 12-bar chart, July in `#A8EEF7`
    4. Clean up: "41.6 GB", items removed, longest streak, sessions
    5. That was 2026
  - Each card has **Share** and "No photos are on these cards." Built on the device from the Trips data.

### Settings and Subscription (45–50)

- **Settings** opens from the gear on any tab.
  - The plan card at the top opens Subscription. It reads:
    - Free: "L of 50 free removals left today" with Get Pro
    - Trial: "5 days left · then $19.99 a year"
    - Pro: "Yearly plan · renews Nov 17, 2027"
  - Sections:
    - Computer: Studio iMac · Connected; Backup folder
    - Appearance: Theme Auto / Dark / Light
    - Clean up: Keep favorites, Keep the last 30 days, Low-storage alerts
    - Privacy: Remove location when sharing
    - About: Help, Privacy policy, Version
  - Footer: "Your photos stay on your own devices. PhotoSense has no ads and does not track what is in your photos."
- **Subscription, Free:** "L of 50", "free removals left today", a bar, a Free / Pro comparison table (8 rows), the four plans, the main button and the fine print.
- **Subscription, Trial:** "5 days left", "Then $19.99 a year, unless you cancel".
  - A timeline: Nov 10 Trial started, Nov 15 We remind you, Nov 17 Renews at $19.99 a year.
  - The switch "Remind me 2 days before it ends" is on by default.
  - "Your trial includes", then "Manage in [App Store]". Footer: "To cancel, use Manage. You keep Pro until Nov 17."
- **Subscription, Pro:**
  - "PhotoSense Pro" with an Active pill. Rows: Renews, Price, Billed through, Covers.
  - Change plan, with the current one marked "Current". Choosing another shows "Switch to Monthly for $2.99".
- All three states show Restore purchases, Terms and Privacy policy. The note: "One purchase covers PhotoSense on your phone, tablet and computer when you sign in with the same [Apple Account]."

---

## Screens: tablet

These use the iPad sidebar (280 px glass panel) and the Android rail (96 px) from the mobile handoff. The sidebar adds a **Collections** group (Trips, Needs fixing, Hidden) and **Settings** at the bottom. Swipe hides the navigation.

- **T1 Library:**
  - Search field, 340 px wide, at the top right of the header.
  - A row of three cards: Safe to delete (1.4fr), Trips (cover), Year in review.
  - An 8-column grid (radius 14 on the group).
- **T2 Clean up:**
  - A 380 px list on the left: total, storage bar, swipe card, categories. The selected one is filled `--sel`.
  - The selected category's 6-column grid on the right, with a footer bar holding the limit line and "Delete N items".
- **T3 Swipe to clean:**
  - A 480 px card column with 72 px buttons.
  - A 320 px side panel: amount marked, the 15 newest marked thumbnails, the streak, and "Free plan: 50 removals left today. Deletions here count toward it."
- **T4 Trips:** a 380 px list, the map filling the rest, and a 360 px floating card for the selected trip with Open trip and Make a folder on Studio iMac.
- **T5 Subscription:** a 380 px Settings list on the left (plan card selected). On the right, the Free subscription page with the comparison table and the plans side by side.

## Screens: desktop

New tabs in the desktop header's segmented control: **Phones** and **Family**, each with a 6 px `--brand` dot until first opened. The header also gets a plan pill. Desktop tokens are unchanged (`--bg`, `--s1–3`, `--brand`, `--keep`, `--line`, DM Sans 14 px).

- **D1 Phones:**
  - A 320 px `--s1` list of paired devices: a 38 px icon tile, name, and status dot plus text. **Pair a phone** at the bottom.
  - The detail pane:
    - The name 26 px / 700, and Pause backups.
    - A status card: "Backing up N of 380", "1.2 GB left · about 3 min · each file is checked after it arrives", a 6 px `--brand` bar, and 10 thumbnails that get a `--keep` tick once verified.
    - Three stat cards: On this computer; Checked ("All match", "SHA-256 checksum of every file"); Safe to delete on the phone.
    - "Files from this phone": Folder · Change…, File names · Rename by date…, Trip folders.
    - Activity.
- **D2 Pair a phone:** a modal 640 px wide: a QR code (220 px), three steps, "Or enter this code on the phone" with a 6-digit code in DM Mono 20 px, and "The phone and this computer need to be on the same Wi-Fi. The code changes every 5 minutes."
- **D3 Rename by date:**
  - A modal 680 px wide with a Date and time / Date, time and place segmented control and a Now → After table in DM Mono.
  - The note: "Only the copies on this computer are renamed. Files on the phone keep their names. Files with the same date and time get -2, -3 at the end."
  - Then Rename 6,941 files.
- **D4 Family:** members with their devices and storage, PhotoSense's disk use, and a "Shared photos" pane (Pictures/PhotoSense/Family) with an 8-column grid of avatar-tagged tiles and **Keep one copy**.

---

## State and data

New services the in-process API needs:

| Need | Notes |
|---|---|
| Backup sessions | Pairing (code or QR), a transfer queue, progress, pause and resume. Files keep their names, dates and location. |
| Verification | The computer returns a SHA-256 checksum for each file it holds. The phone marks an item safe only when the checksum matches. |
| Safe to delete | Backed-up ids minus keep rules (favorites, last 30 days). A "largest first" ordering for partial deletes. |
| Daily removal count | Stored locally and keyed by local date. Raised only after the system delete succeeds. |
| Entitlements | StoreKit 2 / Play Billing. Free, Trial (with end date), Pro (monthly, yearly, lifetime), Family. Restore purchases. Shared with the desktop through the same store account. |
| Categories | On-device detection. Vision on iOS: classification, face landmarks for closed eyes, image aesthetics scores for utility images. ML Kit and a LiteRT blur model on Android. Messaging folders on Android only. |
| Compression | AVFoundation export / Media3 Transformer to HEVC, saved as a new item with the original's metadata, then a system delete of the original. |
| Alerts | Free-space checks. WorkManager on Android; background refresh on iOS, best effort. |
| Search index | Image-text embeddings on the device, built while charging. |
| Trips | Location clusters (5 km) plus gaps between dates, away from home. |
| Metadata fixes | Date shift and copying a location from the nearest photo (PHAssetChangeRequest / MediaStore write request). Renaming runs on the computer. |
| Vault | Encrypted storage inside the app, removed from the library after a verified encrypted copy exists. Biometric unlock. |
| Family | Members, seats, the shared folder on the family computer, and finding duplicates across members by checksum. |

UI state added on phones:

- the onboarding step
- the open Pro sheet and what opened it
- the share card and its format
- the selected category and its deselections
- the burst keepers
- the compression quality and job
- the search query and its parsed parts
- the viewer item and the share-privacy switches
- the trips view (list or map) and the selected trip
- applied fixes
- vault lock state
- the year-in-review card index
- the theme choice (Auto, Dark, Light)

## Assets

- `prototype/app-icon.png` is `branding/app-icon.png` in the repo.
- Icons are 24 px line icons at 1.9 px stroke. The paths are in `IC` in `FeaturePhone.dc.html`; use the repo's icon set where it already has a match.
- The striped tiles are placeholders. Use real thumbnails. The map grid and the QR pattern are placeholders too: use the platform map and a real QR code.

## Files

- `prototype/PhotoSense Growth Features.dc.html`: the clickable overview of every state.
- `prototype/FeaturePhone.dc.html`: phone behaviour and markup.
- `prototype/FeatureTablet.dc.html`: tablet layouts.
- `prototype/DesktopPhones.dc.html`: desktop Phones and Family.
- `prototype/PhotoSense Product Plan.dc.html`: features, pricing, trust rules, metrics, risks.
- `prototype/ios-frame.jsx`, `android-frame.jsx`, `doc-page.js`, `support.js`: prototype support only.
