---
status: accepted
---

# HEIC on Windows through Microsoft's HEVC Video Extensions

On Windows, Trovolve reads HEIC photos only through Windows' own codecs (WIC, with Media Foundation's HEVC decoder). Where a PC lacks Microsoft's HEVC Video Extensions, the person is sent to the Microsoft Store to buy it for $0.99, unless the PC maker preinstalled the free edition. Trovolve ships no HEVC decoder of its own, so no GPL or LGPL code ships with it and the HEVC patent licences stay with Microsoft. That outweighed the cost of asking some people to pay for a Microsoft add-on (Jamie, 2026-11-26; issues 41 and 48).

## Considered options

- **Magick.NET** (libheif and libde265): it ships LGPL code and an HEVC decoder of Trovolve's own, carrying HEVC's patent licensing. It is kept in the tests only, as the yardstick.
- **The GPU's own HEVC decoder through Direct3D 11 video** (issue 50): it is free to the person on most PCs, but takes about 5–8 weeks. It also needs a HEIF reader and HEVC header parsing of Trovolve's own, which DXVA's specification calls a software decoder, with Nokia's HEIF patents a question for a lawyer. Ruled out of scope.
- **Measures worked out on the phone and sent with backups:** these cover only the phone app's backups, not folders on the computer. Left for later.
- **Commercial decoder SDKs:** ruled out on 2026-11-25 in favour of changing matching.

## Consequences

- A PC without the extension still finds byte-for-byte copies (by SHA-256) and an iPhone's HEIC and JPEG pairs (by Apple's `PhotoIdentifier` or the capture time to the sub-second). Both are included in Delete all duplicates. Other HEIC files wait, with a placeholder, until the extension is installed.
- Clean up says how many HEIC files wait, links to the Store and reads them once the extension is installed. On a Windows N edition, it says to add the Media Feature Pack first.
- The web site's Windows requirements and the FAQ name the extension and its price. The FAQ also gives the iPhone's own JPEG setting (*Transfer to Mac or PC: Automatic*) as the free way round.
- The same extension lets Media Foundation decode HEVC video, which the effort to find duplicate videos builds on.
- Checked (issue 56): the extensions and WIC's HEIC decoding work on Arm PCs; current Windows 11 media carries the free edition, so buying is mainly for older installs, Windows 10, Server and N editions; HDR HEIC decode as their standard picture. Not yet checked: an N edition, the paid edition, the Store link opened. A lawyer should confirm that this leaves Trovolve no HEVC licence to take.
