---
name: photosense-build-tools
description: Use before changing or relying on PhotoSense's build tools and release files, meaning tools/ (version.mjs, notices.mjs, site.mjs), version.json, product.json, notices/, site/pages/ and branding/, or after adding or updating any NuGet or npm package.
---

# Build tools and release files

| File | What it does |
|---|---|
| `tools/version.mjs` | Each program's version from `version.json` and the git history: `1.2.0.57` for an ordinary build, `1.2.0` for a release (a tag `v1.2.0`, or `iphone-v1.2.0` for one program while they are not linked). `Directory.Build.targets` runs it for each project, named by `PhotoSensePart` (`server`, `iphone`, `android`). |
| `version.json` | The next kind of release (`major`, `minor`, `patch`) and whether the programs share one version (`linked`). A new program needs a `parts` entry. |
| `tools/notices.mjs` | Writes `notices/<program>.txt`, everything a program ships that others made, with each licence in full. `--check` fails when they are out of date, and CI runs it. Its `PROGRAMS` names each program's projects and platform. |
| `tools/site.mjs` | Makes the web site from `site/pages/*.md`. It refuses without `supportEmail` and `governingLaw` in `product.json`. |
| `product.json` | Who makes PhotoSense and how to reach them, built into `PhotoSense.Api` and shown by About. |
| `site/pages/` | The privacy policy, terms and support page. About shows them; a `###` heading ending in `?` is a question in the accordion. |
| `branding/app-icon.png` | The one copy of the icon. Every program links it; nothing copies it. |

## After adding or updating a package

1. Restore it (`dotnet restore PhotoSense.sln`; `npm ci` in `PhotoSense.ReactUI`).
2. Run `node tools/notices.mjs` and commit `notices/`.
3. A package naming a licence without its text needs `tools/licenses/<SPDX>.txt`, or a `NAMED_ONLY` entry in `notices.mjs`.
4. A font added to `app/layout.tsx` needs its licence in `tools/fonts`.
5. The end-to-end suite's packages (`e2e/`) are not shipped and need no notices.

## Testing the tools (100% of lines, branches and functions)

```sh
node --test --experimental-test-coverage --test-coverage-include="tools/*.mjs" --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 tools/tests/
```

These tests also check that `notices/` is current, so run the restore and `npm ci` first. A new program changes the counts that `tools/tests/notices.test.mjs` expects.
