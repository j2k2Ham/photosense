# The Android app

- **The project:**
  - Android 11 (API 30) or later. Application id `com.j2k2ham.photosense`.
  - Namespace `PhotoSense.AndroidApp`: a namespace ending in `Android` would capture every `Android.…` name inside it.
  - The activity is named `com.j2k2ham.photosense.MainActivity` so that adb can start it.
- **The web view:**
  - It shows the page at `https://appassets.androidplatform.net/`, which no web site can have. `PageClient.ShouldInterceptRequest` answers on a worker thread.
  - Android's web view drops request bodies. The page hands each one to `BodyBridge.keep` (`[JavascriptInterface]`, `[Export]`), and the request carries the number in `x-photosense-body`.
- **`AndroidMediaStore`:**
  - A photo another app made is moved only after `MediaStore.CreateWriteRequest`, and put in the bin only after `CreateTrashRequest`. Both are asked once per batch through `MainActivity.Ask`, which waits for the answer.
  - A photo PhotoSense made itself (`OWNER_PACKAGE_NAME`) is changed without asking. The self-check relies on this.
  - A batch of moves that fails partway puts back those already made.
  - Opening uses `MediaStore.SetRequireOriginal`, which needs `ACCESS_MEDIA_LOCATION` to keep the place a photo was taken.
- **The folder rules** live in shared code (`MediaFileSystem`): photos go only inside DCIM, Pictures or Movies, and what is removed from the whole phone goes into `Pictures/PhotoSense Removed`. Whether Android also refuses moves into or out of Download or `Android/media` (WhatsApp) has not been tried on a phone.
- **The rest of the thin layer:**
  - `AndroidImageAnalyzer` (`ImageDecoder`, sRGB, software allocator).
  - `NsdAdvertiser` (`NsdManager`), which keeps the screen on while offering.
  - `AndroidAccess`: Android 14's choose-some permission, and the app's settings page.
  - Back sends Escape to the page, and leaves the app when no window is open.
- **The icon** is linked from `branding/app-icon.png` with `LogicalName="mipmap\appicon.png"`, because Android takes no dash in a resource's name.

## Building on Jamie's PC

- **What is missing:** the android workload, a JDK (`C:\Program Files\Android\openjdk\jdk-21.0.8`) and the SDK (`C:\Program Files (x86)\Android\android-sdk`) came with Visual Studio. The Mono pack the build needs did not, and installing it into Program Files needs admin.
- **The workaround:** give it from a folder of its own.
  1. Unzip `Microsoft.NET.Runtime.MonoTargets.Sdk` 10.0.8 from nuget.org into `<scratch>/packs/Microsoft.NET.Runtime.MonoTargets.Sdk/10.0.8`, and `Microsoft.NET.Runtime.MonoAOTCompiler.Task` beside it.
  2. Set `DOTNETSDK_WORKLOAD_PACK_ROOTS=<scratch>`.
  3. Build:

  ```powershell
  dotnet build PhotoSense.Android -c Release -nodeReuse:false "-p:PhotoSensePage=<page out>" `
    "-p:JavaSdkDirectory=C:\Program Files\Android\openjdk\jdk-21.0.8" "-p:AndroidSdkDirectory=C:\Program Files (x86)\Android\android-sdk" `
    -p:RunAOTCompilation=false -p:AndroidEnableProfiledAot=false
  ```

  Ahead-of-time compilation would also want four large cross-compiler packs, so locally it is turned off. GitHub's build has it.
- **No emulator here:** the PC has no hypervisor, and turning one on needs admin and a restart.

## `android.yml`

- Builds for `android-x64` with three PNG samples built in (`-p:PhotoSenseSamples`).
- Installs the app with `adb install -g`, which grants its permissions, and starts it with `--ez selfcheck true`.
- The app adds the samples to `DCIM/Camera` as its own, then runs the check.
- Waits for `/sdcard/Android/data/com.j2k2ham.photosense/files/self-check.json` to say `"finished":true`.
- The emulator step runs one line at a time, so what it does is written out as `self-check.sh` first.
