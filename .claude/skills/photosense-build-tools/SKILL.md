---
name: photosense-build-tools
description: Use before changing or relying on PhotoSense's build tools and release files, meaning tools/ (version.mjs, notices.mjs, site.mjs), version.json, product.json, notices/, site/pages/ and branding/, or after adding or updating any NuGet or npm package.
---

# Build tools and release files

| File | What it does |
|---|---|
| `tools/version.mjs` | Each program's version from `version.json` and the git history: `1.2.0.57` for an ordinary build, `1.2.0` for a release (a tag `v1.2.0`, or `iphone-v1.2.0` for one program while they are not linked). `Directory.Build.targets` runs it for each project, named by `PhotoSensePart` (`server`, `iphone`, `android`). |
| `version.json` | The next kind of release (`major`, `minor`, `patch`) and whether the programs share one version (`linked`). A new program needs a `parts` entry. |
| `tools/notices.mjs` | Writes `notices/<program>.txt`, everything a program ships that others made, with each licence in full. `--check` fails when they are out of date, and CI runs it. Its `PROGRAMS` names each program's projects and platform, and where a phone app's own packages are listed: the iPhone's project, Android's `packages.lock.json`. |
| `tools/site.mjs` | Makes the web site from `site/pages/*.md`. It refuses without `supportEmail` and `governingLaw` in `product.json`. |
| `product.json` | Who makes PhotoSense and how to reach them, built into `PhotoSense.Api` and shown by About. |
| `site/pages/` | The privacy policy, terms and support page. About shows them; a `###` heading ending in `?` is a question in the accordion. |
| `branding/app-icon.png` | The one copy of the icon. Every program links it; nothing copies it. `branding/` also holds its SVG source, the Windows `.ico` (`PhotoSense.Server`'s `ApplicationIcon`) and the brand's masters; its `README.md` says what is used where. |

## After adding or updating a package

1. Restore it (`dotnet restore PhotoSense.sln`; `npm ci` in `PhotoSense.ReactUI`).
   - **A change to the Android app's packages, or to a package of a project it builds on** (Infrastructure, Api and theirs), changes `PhotoSense.Android/packages.lock.json`, which lists every package the app restores, theirs among them, and which its notices are written from. Restore the app here to write it again (`DOTNETSDK_WORKLOAD_PACK_ROOTS` set as `photosense-phone-apps` says; `dotnet restore PhotoSense.Android/PhotoSense.Android.csproj --force`, never with `-r`, which leaves one runtime out), and commit it. `android.yml` compares the list its restore writes with the one committed, leaving out the linker's tasks (`Microsoft.NET.ILLink.Tasks`, which the SDK adds at its own version, so NuGet's locked mode refuses a list made with another SDK), so a list out of date fails there, and a Dependabot update of the app's packages, which cannot restore it, fails there until it is.
   - The iPhone app's notices take only the packages its project names: its one package is Microsoft's own, whose packages are Microsoft's too, named under .NET. A package of anyone else's there needs a lock file as Android's has, made on a Mac, as one made on Windows names other runtimes.
2. Run `node tools/notices.mjs` and commit `notices/`.
3. A package naming a licence without its text needs `tools/licenses/<SPDX>.txt`, or a `NAMED_ONLY` entry in `notices.mjs` (as Guava's ListenableFuture binding has, whose licence is only a link).
4. A font added to `app/layout.tsx` needs its licence in `tools/fonts`.
5. The end-to-end suite's packages (`e2e/`) are not shipped and need no notices.
6. Next.js's sharp is never installed: `PhotoSense.ReactUI/stubs/sharp` stands in for it (`file:stubs/sharp` in `dependencies`, `"sharp": "$sharp"` in `overrides`), as its libvips is LGPL, which the dependency review refuses. Keep both lines when updating Next.js; an override naming the folder itself is resolved by npm relative to Next.js's folder and breaks the lock file.

## Testing the tools (100% of lines, branches and functions)

```sh
node --test --experimental-test-coverage --test-coverage-include="tools/*.mjs" --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 tools/tests/
```

These tests also check that `notices/` is current, so run the restore and `npm ci` first. A new program changes the counts that `tools/tests/notices.test.mjs` expects.
