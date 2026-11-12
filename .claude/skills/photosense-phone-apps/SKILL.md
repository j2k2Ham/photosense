---
name: photosense-phone-apps
description: Use before changing, building or checking PhotoSense's phone apps, the iPhone app (PhotoSense.iOS) or the Android app (PhotoSense.Android), or their GitHub workflows (iphone.yml, android.yml), or when reading the result of one of those runs.
---

# The phone apps

Both apps are the same page in a web view, with its `/api` requests answered inside the app by the same endpoints the computer serves (`InProcessApi`, `PageFiles`).

- **Only the thin layer over Apple's or Google's frameworks lives in an app project:** the photo library, the picture decoder, Bonjour, permissions and the web view.
- **Anything that decides goes in shared code, tested to 100%:** `PhotoLibraryFileSystem` for the iPhone, `MediaFileSystem` for Android, `PhoneSending`. How each phone shows photos as folders is in `photosense-service`'s `reference/patterns.md`.
- **On real devices** both are checked by hand, as Jamie agreed; nothing has been run on one yet.

## Building and checking

| | iPhone (`PhotoSense.iOS`) | Android (`PhotoSense.Android`) |
|---|---|---|
| In `PhotoSense.sln` | No | No |
| Builds on Jamie's PC | No: it needs a Mac | Yes, with the workaround in `reference/android.md` |
| Checked by | `iphone.yml` on GitHub's macOS: the simulator | `android.yml` on GitHub's Ubuntu: an Android 14 emulator |
| Its self-check | `SelfCheck` (script in `SelfCheck.cs`) | `SelfCheck` (started with the extra `selfcheck`) |

- **Both apps need the page built first:** `NEXT_PUBLIC_API_BASE=/api npm run build` in a copy of `PhotoSense.ReactUI` without `.env.local`, then `-p:PhotoSensePage=<its out folder>`.
- **The workflows run on a push.** Ask Jamie before pushing unless they have said pushes for checks are fine for the work in hand.
- **Reading a run:** every step of the self-check is an annotation on it:

  ```sh
  gh run list --branch feature/mobile-and-mac --limit 4
  gh run view <id> --log | grep -E "##\[(notice|warning|error)\]" | grep -v "36;1m" | sed 's/^.*Z //'
  gh run download <id> -n iphone-simulator      # or android-emulator: self-check.json and the app's log
  ```

- **A self-check that stops without finishing** is usually the phone waiting on a question of its own, as when iOS asks before an app deletes an album. Look in the app's log for an alert, and change the code so that nothing waits on the person in the check.

Before working on one platform, read only its reference: `reference/ios.md` or `reference/android.md`.
