# Prompt: build the Organize feature

Paste this into Claude in your IDE, from the repo root, after unzipping this folder to `docs/design_handoff_photosense_redesign/`.

---

Read `docs/design_handoff_photosense_redesign/README.md`. Focus on **"6. Organize (new feature)"**, the new rows in **"Mapping to existing components"**, and screenshots `15` to `25` in `screenshots/`. Open `PhotoSense Prototype.dc.html` in a browser (set `startAt` to "Organize") and use its `class Component` logic (`mkOrg`, `suggestions`, `pvCalc`, `clash`, `doMove`, `undoMove`) as the behavior spec. Do not copy the prototype code. Rebuild it in our stack.

Build the **Organize** feature in `PhotoSense.ReactUI` (Next.js 14, React 18, Tailwind, SWR) with back-end support in the .NET solution. Keep the existing Clean up screens, API and SignalR wiring unchanged.

**Front end**
1. Add the Clean up / Organize pill switch to the header. Organize is a second top-level view.
2. Build the Organize view exactly as specified: the toolbar (root chip with "Choose root folder", search, hint), the file gallery (60 per page, checkbox, Shift-click range, double-click opens in the default viewer, drag to folders, "For …" / "In …" / "Copied to …" tags, filter pills All / Not organized / No location) and the floating selection bar with the inline new-folder input.
3. Build the Suggested folders panel:
   - Group by Place or Date. For Place: combine nearby places (1, 5, 15, 25 or 50 km, default 5) or keep them separate. Folder names come from Landmark or area (default, falling back to the town name), Town, state, or State \ landmark. Structure is Place \ Year (default) or Flat. For Date: one folder per Year (`2022`) or Month (`2022-07`).
   - The cluster map toggle, the suggestion cards and the No location card.
   - Your folders (create, add selected, drop, remove).
   - The drop tray. Suggestion cards also accept drops.
   - Recently moved with Undo.
   - Files in your folders are left out of suggestions. Files already in their destination are skipped.
4. Put clustering and naming in `lib/organize.ts` as pure functions with unit tests. Use haversine distance and single-linkage clustering at the chosen radius. Name each cluster after the place with the most files.
5. Build the Move Preview window:
   - A file grid, 100 per page. Clicking a file leaves it out. Clashing files get a NAME TAKEN badge.
   - Editable folder name, Move / Copy switch, and Goes to with **Change**. Change opens the existing folder browser with "Put the files here" and "Make the new folder here". "Use the default" resets the destination.
   - The Files / Size / Taken / From summary and the Live Photo / edit-files checkbox.
   - The Name already taken summary with **Review each name**.
   - The Move button, a toast with Undo, and Recently moved.
6. Build the Name already taken window, reusing the `PhotoWindow` shell:
   - Side by side / Moving file / Already there. Space flips between the two files.
   - Show every same-named file already in the destination, including "(1)" copies.
   - A diff table with differing rows highlighted, and an identical-file note.
   - The choices Add a number (default), Rename it (extension fixed; a taken or invalid name falls back to a number with a warning) and Leave it where it is.
   - Previous / Next and ← → step through clashes, plus "Add a number to all the remaining names". Esc returns to the preview.
7. Match the README's colors, type, radii and copy, in both Dark and Light themes. All buttons are pills.

**Back end** (Functions API, reusing the existing services)
- `GET /api/organize/files?root=…`: list media under any folder with taken date, GPS, resolved place (city, state, and a landmark/area when known), size, kind and current folder, with no duplicate scan. Reuse `PhotoFileEnumerator`, `BasicExifMetadataExtractor` and `GeoNamesPlaceResolver`, and add the landmark/area level to the resolver with a town fallback. Cache results per folder.
- `POST /api/organize/plan`: given files and a destination (base path, folder name, direct or new, year split), return the destination per file and name clashes. For each clash, return the existing files of that name (with their details and whether they are byte-identical, using `Sha256ImageHashingService`) and the next free " (n)" name.
- `POST /api/organize/apply`: move or copy files with per-file final names, bring companion files along (`CompanionFileFinder`, `LivePhotoLink`), never overwrite, and record an `AuditEntry` batch. Return a batch id.
- `POST /api/organize/undo/{batchId}`: reverse the batch. Delete copies, or move files back under their original names.
- Errors go through the existing toast + Errors panel flow. Honor `RequestGuard`.

Work in small, reviewable steps:
1. `lib/organize.ts` with tests.
2. The API endpoints with tests.
3. The Organize view and panel.
4. The preview.
5. The name-clash window.
6. The header switch and polish.

After each step, run the build and tests and compare against the matching screenshots. Ask me before changing any existing API contract.
