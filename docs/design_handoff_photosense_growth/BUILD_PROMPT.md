# Prompt: build the growth features

Unzip this folder into the repo as `docs/design_handoff_photosense_growth/`, next to `docs/design_handoff_photosense_mobile/`. Build the mobile handoff first. This builds on it. Then paste everything below the line into Claude in your IDE, from the repo root.

---

Load the `photosense-page` and `photosense-phone-apps` skills. Read `docs/design_handoff_photosense_growth/README.md` in full, and `docs/design_handoff_photosense_mobile/README.md` for the shell, skins and shared components it refers to. Look at the screenshots listed in the README's table:

- phones in `screenshots/iphone` and `screenshots/android`
- tablets in `screenshots/ipad` and `screenshots/android-tablet`
- the computer in `screenshots/desktop`

Each comes in dark and light.

To see the behaviour, open `docs/design_handoff_photosense_growth/prototype/PhotoSense Growth Features.dc.html` in a browser. The specs for state and transitions are the logic classes in `prototype/FeaturePhone.dc.html` (`seed`, `reqDel`, `sysOk`, `buyPro`, `renderVals`), `prototype/FeatureTablet.dc.html` and `prototype/DesktopPhones.dc.html`. Do not copy the prototype code. Rebuild it in our stack. `prototype/PhotoSense Product Plan.dc.html` holds the pricing and trust rules; follow them exactly.

**Where it goes.**

- Phone and tablet screens go in `PhotoSense.ReactUI`, in the phone and tablet layouts from the mobile handoff.
- Desktop screens go in the desktop layout: two new tabs, Phones and Family.
- OS work goes in `PhotoSense.iOS` and `PhotoSense.Android`, behind the in-process API, and nowhere else:
  - background backup
  - photo library writes and deletes
  - biometrics
  - StoreKit 2 and Play Billing
  - notifications
  - widgets
  - App Intents and app shortcuts
- Ask me before changing any existing API contract. New endpoints are fine; list them in your report.

**Build it in this order, one reviewable step at a time:**

1. **Entitlements and the daily limit.**
   - The Free, Trial, Pro and Family states, purchase, restore, and sharing with the desktop.
   - The 50-a-day removal count, raised only after a confirmed system delete.
   - The Pro sheet with its context lines, its partial "Delete L now", and "Remove the L largest now".
   - Settings (45–47) and Subscription (48–50).
2. **First launch (01–07).** Welcome, photo access, pairing (QR and 6-digit code), the first scan, the result, and the one-time offer.
3. **Backup and verification.**
   - Phone: the Backup tab (16–17). Automatic backup is Pro; Back up now stays free.
   - Computer: the Phones tab (D1–D2) and SHA-256 checks.
   - Then Safe to delete (08–11), with keep rules and the iCloud warning.
4. **Swipe to clean (12–15).** Collected deletions, one confirmation, Undo, the streak, and the over-limit summary.
5. **Clean up categories (18–21, 24).** On-device detection, the category grid, bursts, and Free up space.
6. **Video compression (22) and low-storage alerts (23).**
7. **Share card (25) and Year in review (41–44).** The cards never contain photos.
8. **Search (26–27), viewer and share without location (28).**
9. **Trips and the map (29–31), and trip folders on the computer.**
10. **Fix dates and places (32), and Rename by date on the computer (D3).**
11. **Hidden vault (33–34).** Encryption, biometric unlock, and including vault items in backups.
12. **Widgets and app shortcuts (35–36)** on both platforms.
13. **Family (37–40, D4).** Family Sharing on iOS and invite links on Android. Duplicates across members are found by checksum.
14. **Tablet layouts (T1–T5).**

**Rules that apply here** (from `CLAUDE.md`, plus this design):

- 100% branch coverage of the page with Vitest. Report the measured figures.
- TSDoc on everything exported.
- Every deletion goes through the system dialog and stays recoverable for 30 days. Nothing is erased by PhotoSense itself.
- Every number shown before payment is real. The free daily limit is shown next to it.
- No paywall during a swipe session. No ads. Photos and search queries never leave the person's devices.
- Keep the copy in the README exact, with the platform words from its table.
- Keep `STEPS.md` current: add these steps and tick each one once it is done and verified.
- Commit or push only when I ask.

After each step:

1. Run the page's tests and build.
2. Open the page at 402 × 874 and 412 × 892, at 1180 × 820 and 1280 × 800 for tablets, and in the desktop window.
3. Compare each screen with the matching screenshots in both themes.
4. Tell me what changed, what was verified and how, what was not verified, and any decision I have not confirmed.
