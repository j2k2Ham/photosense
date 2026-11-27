# The Windows extension route: Arm, N editions, HDR photos, new installs

For the decoder map's ticket *Check the Windows extension route on Arm, N editions, HDR photos and new installs* (2026-11-27). Counts and behaviour only; no photo of Jamie's leaves the PC. Never merged.

## How it was checked

- **Jamie's PC** (Windows 11 Home, x64, a Dell, build 26300): what Media Foundation and WIC offer, the Photos app's package manifest, and the ground-truth set's 1,286 HEIC files (read only, through the measures already taken for the steady-measures ticket, and WIC's frame count on four).
- **GitHub's machines** (`probe.ps1`, run by `.github/workflows/research-windows-extension-route.yml`, run 45943579285): Windows Server 2025 x64 and Windows 11 Enterprise Arm64, each decoding libheif's example HEIC (a public test picture, downloaded in the run) as it comes, then after asking winget for HEIF Image Extension and the free HEVC edition.
- **The Store's catalogue** (`displaycatalog.mp.microsoft.com`) for the three products.

## Found

| Question | Answer | How sure |
|---|---|---|
| Arm | The paid HEVC Video Extensions (`9NMZLZ57R3T7`, $0.99), the free OEM edition (`9N4WGH0Z6VHQ`) and HEIF Image Extension (`9PMMSR1CGPWG`) all have Arm64 packages. On GitHub's Windows 11 Arm64 machine, WIC decoded the sample HEIC through the free edition's Arm64 package. | Checked (free edition); paid edition on Arm by catalogue only |
| New installs | GitHub's Windows 11 Enterprise Arm64 image (build 26200) comes with `Microsoft.HEVCVideoExtension` 2.4.109 and `Microsoft.HEIFImageExtension` 1.2.48, older than the Store's (2.5.48, 1.2.65 on Jamie's PC), so from the install media, not the Store. Microsoft's IT Pro blog (*Inbox Microsoft Store apps update in Windows media*, 2025-07-15) lists "HEVC Video Extension" among the 36 apps in Windows 11 24H2 media refreshed in or after June 2025. The free edition is the singular family `Microsoft.HEVCVideoExtension`. So current media carries the free edition: the map's earlier finding ("clean installs provision HEIF, WebP and VP9 but not HEVC") is out of date. | Arm Enterprise image checked; x64 consumer media by Microsoft's post only |
| Windows Server | Server 2025 has neither extension. HEIF Image Extension installs through winget's `msstore` source; the free HEVC edition is "No package found", as the earlier research said (no Purchase action without the PC's licence). | Checked |
| What Trovolve's check sees | Media Foundation's HEVC decoders (`MFTEnumEx`, as `WicPictureDecoder.AskForHevc` asks): **1** with the free edition (Jamie's PC, the Arm machine), **0** with neither. No GPU driver registers one: Jamie's PC (Intel and NVIDIA graphics) shows only the extension's, none in hardware, so a GPU cannot make the check say yes while HEIC still fails. The paid edition not checked (nobody here has it). | Checked, but for the paid edition |
| Errors WIC gives | Neither extension: `0x88982F8B` (component initialisation failed), not `0x88982F50` (component not found) as Trovolve's `readerFound` expects, through WPF's decoder. HEIF without HEVC: `0xC00D5212` (no suitable transform). So Trovolve should tell which extension is missing by package family (`Microsoft.HEIFImageExtension`, `Microsoft.HEVCVideoExtension`, `Microsoft.HEVCVideoExtensions`) and Media Foundation's list, not by WIC's error code. | Seen through WPF, not through Trovolve's own decoder |
| HDR HEIC | 1,260 of the set's 1,286 HEIC carry Apple's gain map (121 also the ISO `tmap` gain map of iOS 18). WIC decoded every one at the primary image's size, as one 8-bit frame (`Bgr32`), the SDR base: corrected WIC against Magick.NET is median 0.35 apart on gain-map HEIC with an ICC profile and 0.40 on those without a gain map. Nothing HDR reaches Trovolve, and nothing needs doing. | Checked |
| The new Photos app | Its package registers no media codec and no WIC decoder; its `.heic` entry is an Explorer context-menu verb (Edit). Whatever Photos does with HEIC is its own and open to no other app. | Checked |
| Windows N | Not tried: Jamie's PC is Windows Home, with no Hyper-V for a VM. By Microsoft's documents an N edition without the Media Feature Pack has no Media Foundation (`mfplat.dll`); the extensions' decoders are Media Foundation transforms, so HEIC cannot decode. Trovolve's `AskForHevc` imports `mfplat.dll` and would throw `DllNotFoundException`, escaping as something other than an unreadable picture. N is told apart by `EditionID` ending in N (`CoreN`, `ProfessionalN`…) and the pack by `mfplat.dll` in System32. | Documents and code reading only |
| The Store link | `ms-windows-store://pdp/?ProductId=9NMZLZ57R3T7` names HEVC Video Extensions at $0.99 (x86, x64, Arm64; family `Microsoft.HEVCVideoExtensions`); the `ms-windows-store` protocol is registered on Jamie's PC. Not opened. | Catalogue only |

## Not checked, for Jamie or the build

1. The Store link opened on a PC (one click: `start ms-windows-store://pdp/?ProductId=9NMZLZ57R3T7`).
2. A Windows N edition, before and after the Media Feature Pack (needs a PC with Hyper-V, Pro or better): `probe.ps1` says "no mfplat.dll" without the pack.
3. x64 consumer media from today (`Get-AppxProvisionedPackage -Online` in an elevated PowerShell on a fresh install).
4. The paid edition's decoder counted by Media Foundation (needs a PC without the free edition, and $0.99).
5. Trovolve's own decoder's error codes on a machine with neither extension (Windows Server on GitHub would do once the spec is built).
