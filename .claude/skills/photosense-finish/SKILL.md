---
name: photosense-finish
description: Use before telling Jamie a piece of PhotoSense work is done, committing it, or pushing it. Covers the checks to run and measure, the documents to bring up to date, STEPS.md, how to report, and the git rules.
---

# Finishing a piece of work

## 1. Check and measure

Run what the change touched, and write down the figures it gives.

- [ ] **Service:** every test, with coverage at 100% of lines and branches (`photosense-service`), and `dotnet build PhotoSense.sln -c Release -nodeReuse:false -warnaserror` clean.
- [ ] **Page:** `npm run test:coverage` at 100%, `npx tsc --noEmit` clean, and `npm run lint` with 0 errors and no new warnings. Build the page in a copy when anything server-rendered or imported by `app/layout.tsx` changed.
- [ ] **Build tools:** their tests at 100%, with `notices/` current after any package change (`photosense-build-tools`).
- [ ] **End to end:** for anything a person sees, `npm run app` then the affected specs, and a test of its own for something new (`photosense-e2e`).
- [ ] **Phone apps:** when an app or its shared code changed, the app's workflow run after a push Jamie agreed to (`photosense-phone-apps`).
- [ ] **In the program:** tried as Jamie would use it, on a scratch copy and the largest real case that applies (`photosense-verify`).

## 2. Bring the documents up to date

- [ ] `PhotoSense.ReactUI/lib/guide.ts` when anything on screen was renamed, added or removed.
- [ ] `README.md` for behaviour and the API table.
- [ ] CLAUDE.md, or the skill concerned, when a pattern, command or trap changed. Each fact lives in one place.
- [ ] `STEPS.md`:
  - tick what is done and verified, and add what was found;
  - add new questions for Jamie, and decisions Jamie has not confirmed;
  - move a question Jamie answered into its step;
  - update "Where it stands" and the date.

## 3. Report to Jamie

Say, plainly and with they/them for Jamie:

1. What changed.
2. What was verified, how, and the measured figures.
3. What was not verified.
4. Every decision Jamie has not confirmed.
5. What waits on Jamie.

Keep it short; the detail lives in STEPS.md and the commits.

## 4. Git

- Commit or push only when Jamie asks, and ask each time, unless Jamie has said pushes for checks are fine for the work in hand. No pull request unless asked.
- Stage the files by name. Never `git stash`: Jamie keeps uncommitted work in the tree. Never `git rm`: a partial commit once captured only the deletions.
- Leave out build output, coverage reports, `e2e/.cache`, `test-results` and `docs/design_handoff_photosense_redesign`.
- End each commit message with the attribution line the session gives.
