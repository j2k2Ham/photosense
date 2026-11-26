# Can Trovolve decode HEIC through the GPU's own HEVC decoder, and is that still not a decoder of its own?

Research for issue #50 (map #41), following #42. Gathered 2026-11-25. Facts are cited by number; where something is inferred or untested, it says so. Nothing here is legal advice: the pools' and vendors' terms are reported as they word them, for the lawyer to confirm (STEPS.md, Before selling).

## Short answer

- **Feasible, yes.** Windows exposes the GPU's HEVC decoder through Direct3D 11 (or 12) video decoding, which is part of Windows and the display driver, not the Store extension [1][2]. Trovolve would read the HEIF container, parse the HEVC parameter sets and slice headers of each tile, hand the GPU the compressed slices with DXVA's picture parameters, read back NV12, then assemble the grid, convert YUV to RGB and apply the colour profile itself. Everything is in C# over COM, with no native library to ship.
- **One correction to #42:** Chrome does **not** decode HEIC pictures. No version of Chrome, Edge or Firefox opens HEIC [3]. Chrome decodes HEVC *video* through D3D11, the Main Still Picture profile included since 128 [4][5]. The method carries over, but nothing on Windows does this for HEIC today.
- **How big a job:** about **5 to 8 weeks** for one person to build, test to 100% of branches and harden. After that, ongoing work on GPU drivers' quirks (Chromium keeps a switch to turn HEVC off per driver [5]). It cannot be tested on GitHub's ordinary Windows machines, which have no GPU (inferred).
- **Is it a decoder of Trovolve's own?** It is a judgement call, and the evidence cuts both ways. The GPU does all of the HEVC decoding process: "bitstream parsing, inverse quantization scaling, inverse transform processing, motion compensation, and deblocking" [2]. Trovolve would do none of the pixel work. But Microsoft's own specification calls the application's part "the host decoder or software decoder" [2], and Trovolve would ship HEVC syntax parsing (parameter sets, slice headers) and a HEIF reader of its own. **Jamie's call.**
- **Licence position (for the lawyer):**
  - *Copyright:* fine. Permissive sources exist for everything needed: Chromium's HEVC parser and D3D accelerator (BSD-3) [5][6], cros-codecs (BSD-3) [7], MetadataExtractor's HEIF boxes (Apache-2.0, already a dependency) [8], and Microsoft's own Windows metadata for the DXVA structures (MIT) [9]. libheif (LGPL), FFmpeg and GStreamer (LGPL), Nokia's HEIF library (non-commercial only) and ente's heic-decoder (AGPL) are out [10][11].
  - *HEVC patents:* both pools charge per device at first sale, and PC makers pay for the decoder in their machines. Dell, HP, Lenovo, Acer, Samsung, Microsoft and NVIDIA are HEVC Advance licensees; Intel, AMD and Qualcomm are not on that list [12][13]. The 2016 royalty-free software policy was read at the time as "if the software uses an existing hardware decoder ... there's no royalty, since a royalty on that hardware should have already been paid" [14]. But NVIDIA's driver licence makes any third-party codec licence the licensee's "sole responsibility" [15], and Google has said nothing public about Chrome [16].
  - *HEIF patents:* Nokia claims patents on HEIF itself and licenses its own HEIF software only for non-commercial use [10]. Today Microsoft's HEIF extension reads the container. With this route, Trovolve's own code would read it. That is a new question for the lawyer (inferred, not found discussed anywhere).
- **Coverage:**
  - *Works on:* most PCs with a working GPU driver since about 2015–2016. That means Intel Skylake (6th generation) and later [17], NVIDIA GTX 950/960 (GM206) and Pascal and later [18], AMD Carrizo/Fiji (UVD 6) and Polaris (RX 400) and later [19], and Qualcomm Adreno 618 and later. Chrome also lists older parts (Intel HD 4400, NVIDIA GT 635) [4], whose drivers decode partly in shaders (inferred).
  - *Fails on:* a virtual machine or remote session without the GPU. The Basic Display Adapter and WARP have no video device at all [20]. It also fails on PCs whose maker **disabled HEVC decoding in firmware or driver**: HP ProBook 460/465 G11 and EliteBook 665 G11, and Dell's base models with Intel Core Ultra 100V/200V, from 2024, to save royalties [21][22]. No program can turn it back on there.
- **Where it helps most:** retail, self-built and clean-installed PCs with a GPU but without the OEM's free HEVC extension. These are the PCs #48 is about.
- **A side benefit:** HEVC decoding is specified bit for bit [23], so the GPU gives the very YUV samples libde265 gives inside Magick.NET. Trovolve would then do the YUV-to-RGB step itself, so it could match libheif's conversion. This is the one Windows route where the colour shift #43 measured is under Trovolve's control (inferred, to be measured).

## 1. What Chrome actually does

- **HEIC:** no shipping browser but Safari decodes HEIF or HEIC pictures. Chrome is "Not supported" in every version listed, as are Edge and Firefox. caniuse notes "It is hard for browsers to support HEIC because it is complex and expensive to license." [3] So #42's "Chrome decodes HEIC through the GPU" overstates it. Chrome decodes HEVC *video* that way, still-picture profile included.
- **HEVC video on Windows:** "Chrome uses D3D11VideoDecoder to call D3D11VA (no need to install anything)", hardware only [4]. HEVC Main Still Picture is supported on Windows from Chrome 128.0.6607.0 (2024-07-19) [4].
- **The code** (`media/gpu/windows/d3d_h265_accelerator.cc`, BSD-style licence, 681 lines) [5]:
  - It fills `DXVA_PicParams_HEVC` from the parsed SPS, PPS and slice header, and sends the scaling lists as `DXVA_Qmatrix_HEVC` when present.
  - It sends each slice with start codes and a `DXVA_Slice_HEVC_Short` record.
  - It maps both `HEVCPROFILE_MAIN` and `HEVCPROFILE_MAIN_STILL_PICTURE` to `D3D11_DECODER_PROFILE_HEVC_VLD_MAIN`. The comment cites DXVA HEVC specification 7.4, under which that GUID "can be used for both main and main still picture profile".
  - The parsing is Chromium's own `media/parsers/h265_parser.cc` (2,396 lines) [6].
- **Limits Chrome applies** (`supported_profile_helpers.cc`) [5]:
  - The smallest picture it decodes on NVIDIA is 144 × 144, on Qualcomm 96 × 96, and elsewhere 64 × 64. An iPhone's 512 × 512 tiles are well above these.
  - It keeps a `disable_accelerated_hevc_decode` switch, set per GPU driver.

## 2. What it would take in Trovolve

### The pieces

1. **HEIF container reader**:
   - The boxes `ftyp`, `meta`, `hdlr`, `pitm`, `iinf`/`infe`, `iloc` (with `idat` and construction methods), `iref` (`dimg` for tiles, `thmb`, `auxl`), `iprp`/`ipco`/`ipma`, `hvcC`, `ispe`, `irot`, `imir`, `clap`, `colr` (ICC or nclx) and `pixi`.
   - Grid derivation (`grid`): an iPhone's 4032 × 3024 picture is 8 × 6 tiles of 512 × 512, that is 48 `hvc1` items, cropped to the output size [24][25].
   - Rotation comes from the container's `irot`, not EXIF, which Apple leaves at 1 [26].
   - MetadataExtractor (Apache-2.0) already parses nearly all of these boxes, `iloc`, `iref`, `ipma`, `hvcC`, `irot` and `colr` among them. Its classes are `internal` [8], so they would be copied with its notice or rewritten, not called.
2. **HEVC high-level syntax**:
   - The VPS (barely used), the SPS with its VUI and scaling lists, and the PPS with its tiles.
   - The slice segment header, as far as DXVA needs: IRAP and IDR flags, and the short-term RPS bit count, which is zero for an IDR still.
   - This is Annex B NAL unit handling, with emulation-prevention bytes taken out. Chromium's parser (BSD-3) [6] or cros-codecs' `src/codec/h265/parser.rs` (BSD-3) [7] can be ported. Only the parts a single intra picture uses are needed: no DPB, no POC, no reference lists.
3. **DXVA submission** [2]:
   - Pick an adapter whose `ID3D11VideoDevice` lists `DXVA_ModeHEVC_VLD_Main` (`{5B11D51B-…}`), with NV12 output. For 10-bit HEIF (some cameras, not iPhone), use `DXVA_ModeHEVC_VLD_Main10` with P010.
   - Create a decoder per tile size, then for each tile: `DecoderBeginFrame`; submit the picture parameters, the optional quantisation matrix, the bitstream (padded to a multiple of 128 bytes) and the `DXVA_Slice_HEVC_Short` buffers; `DecoderEndFrame`.
   - Copy the result to a staging texture and read it back.
   - The DXVA specification defines only the short slice format (`ConfigBitstreamRaw` 1), with the accelerator parsing the slice data [2].
   - Interop: the structures and interfaces are in Microsoft's `win32metadata` (`dxva.h`, MIT) [9], so CsWin32 can generate them. Vortice.Direct3D11 (MIT) also wraps `ID3D11VideoDecoder` [27]. Either way there is no native DLL to bundle, and Trovolve's WIC interop is already hand-written COM.
4. **Assembling and colour**:
   - Place the tiles into the grid and crop.
   - Convert YUV to RGB with the matrix and range the stream signals. For iPhone HEIC this is reported as full-range BT.601 in the HEVC VUI, with a Display P3 ICC profile in the container and no `nclx` box [28].
   - Upsample chroma.
   - Apply `irot`, `imir` and `clap`.
   - Convert the ICC profile to sRGB. WIC's colour transform, which `WicPictureDecoder` already uses, can do this step, as it needs no HEVC.
   - For matching Magick.NET's numbers, the conversion should follow libheif's: its matrix, range, rounding and chroma upsampling. libheif's handling of `colr` against the VUI has been questioned [28], so #43's findings feed into this step.
5. **Where it fits:** a second decoder behind `IPictureDecoder` on Windows (in `Trovolve.Desktop`), tried after WIC fails with "HEVC missing", or chosen first for HEIC if #47 shows it scores better. JPEG, PNG and the rest stay with WIC.

### Effort (rough, one person, inferred)

| Piece | Size | Time |
|---|---|---|
| HEIF reader (boxes, grid, transforms, thumbnails, auxiliary images skipped) | 600–900 lines | 3–5 days |
| HEVC parameter sets and slice header, ported from Chromium | 1,200–1,800 lines | 5–8 days |
| D3D11 video device, adapter choice, DXVA buffers, readback, errors | 700–1,000 lines | 5–8 days |
| Grid assembly, YUV to RGB matching libheif, ICC via WIC | 300–500 lines | 2–4 days |
| Tests at 100% of branches, sample files under a free licence (Trovolve's Magick.NET cannot write HEIC; CC0 samples exist [25]), the GPU calls left out of coverage as `NativeRecycleBin`'s are | — | 5–8 days |
| Hardening: a parser of untrusted files whose output goes into a kernel-mode driver (a malformed stream can hang or reset the GPU; inferred), fuzzing, limits | — | 3–5 days |
| Measuring on the ground truth (#46, #47) on two or three GPU makers | — | 2–4 days |
| **Total** | **3,000–4,500 lines** | **about 5–8 weeks** |

Ongoing: driver bugs per GPU maker and generation, as Chromium's list shows [5]; new HEIF variants (Apple's gain maps and auxiliary images, already skipped); and GPUs Trovolve never sees in testing. GitHub's standard Windows runners have no GPU, so CI would run the parser and colour code on fixtures and never the GPU path (inferred). The GPU path would be checked by hand on the development PC (Intel and NVIDIA) only.

### Speed (inferred, not measured)

A tile of 512 × 512 is a tiny job for a hardware decoder, so 48 tiles per photo should take tens of milliseconds plus the copy back. On this PC WIC takes 0.7–1.2 s per iPhone photo (STEPS.md). The parsing in C# is small beside it. To be measured.

### A smaller variant

Decode only the HEIC's own thumbnail item. It is one HEVC picture with no grid (its size was not checked here; it must be at least 144 × 144 for NVIDIA [5]). This halves the HEIF work but changes what the measures see, so it would need #45's steady signatures. Not recommended as the main route.

## 3. Which GPUs, and where it fails

| Maker | HEVC Main (8-bit) in hardware from | Notes |
|---|---|---|
| Intel | Skylake (6th gen, 2015): "full fixed-function H.265/HEVC 8-bit 4:2:0 decoding"; 10-bit fixed function from Kaby Lake [17] | Chrome lists HD 4400 (Haswell) too [4], presumably the driver's hybrid decoder (inferred) |
| NVIDIA | GM206 (GTX 950/960) 8- and 10-bit; every Pascal (GP106/GP107) and later [18] | Kepler, GM107, GM204 (GTX 970/980) and GM200 have no fixed-function HEVC [18]; Chrome lists GT 635/GTX 645 [4] (driver hybrid, inferred); MX150 (GP108) has no decoder [18] |
| AMD | UVD 6.0 (Carrizo, Fiji) 8-bit; UVD 6.3 (Polaris, RX 400) 10-bit [19] | Chrome lists RX 460, R7 and Vega M [4] |
| Qualcomm | Adreno 618 and later [4] | Windows on Arm |

**Main Still Picture:** DXVA has no profile of its own for it. `DXVA_ModeHEVC_VLD_Main` "supports the features necessary for a decoder that conforms to the HEVC Main profile and Main Still Picture profile" [2], so any GPU with HEVC Main decodes it. HEIC tiles use Main Still Picture [24][29].

**Where it fails:**

- **No GPU driver, a virtual machine, WARP:** "If you query an ID3D11Device for ID3D11VideoDevice and the Direct3D 11 device created is using the reference rasterizer or WARP, or is a hardware device and you are using the Microsoft Basic Display Adapter, E_NOINTERFACE is returned." [20]
  - A Hyper-V, VMware or VirtualBox guest without GPU passthrough or partitioning has no HEVC decoder this way (inferred from [20]).
  - Hyper-V's GPU partitioning shows encode and decode shares per partition [30], but only on Windows Server 2025 with data-centre GPUs.
- **Remote Desktop:**
  - By default, Windows 10 and Server 2016 do not use the GPU for rendering in an RDP session; a Group Policy setting turns it on [31].
  - With that setting on, the session host reportedly blocks D3D and DXGI, DXVA and D3D11VA decoding included, for other uses [32].
  - A program run inside an RDP session therefore probably gets no video device (inferred, untested). Trovolve normally runs in the person's own session.
- **Makers that disabled it:**
  - HP documents for ProBook 460 G11, ProBook 465 G11 and EliteBook 665 G11: "Hardware acceleration for CODEC H.265/HEVC (High Efficiency Video Coding) is disabled on this platform." It did this in 2024 and recommends "licensed third-party software solutions" [21][22].
  - Dell keeps hardware HEVC only on systems with a 4K panel, a separate GPU, Dolby Vision or CyberLink's player [21].
  - Reported as mostly Intel Core Ultra 100V/200V machines. Browsers fail on them while players with their own decoders work [21][22].
  - Ars Technica ties it to royalties rising from 20 to 24 cents a unit after 2025-09-30 [22].
  - These are exactly the cheaper PCs least likely to have the OEM's free extension (inferred).
- **Old or basic adapters:** PCs from before about 2015, and NVIDIA's GM204/GM200 generation, unless their drivers' hybrid decoders answer (inferred).
- **Laptops with two GPUs:** Trovolve should list every adapter (DXGI) and use whichever has the profile.

## 4. Who holds the patent licence

As the pools and vendors word it. For the lawyer.

- **HEVC Advance (Access Advance), July 2025 overview** [12]:
  - It is "primarily focused on licensing HEVC Decoders and/or HEVC Encoders installed in devices or software at the time of First Sale". It seeks "one royalty per device/software copy on First Sale". Multiple decoders in one device "incur ONLY a single royalty fee", with exceptions including "HEVC Software Sold separately from a device".
  - Licences go to "Parties that Sell substantially completed devices or software ... which items incorporate one or more HEVC Decoders".
  - It "reserves the right to seek licenses from any party infringing", but its "primary focus" is branded products and OEMs.
  - Footnote: "Excludes certain types of HEVC Software." These are not listed.
- **Licensees** (Access Advance's list, updated 2026-11-25) [13]: Microsoft, NVIDIA, Google, Dell, HP, Lenovo PC, Acer and Samsung are listed. Intel, AMD, Qualcomm and Apple are not. They may license through VCL Advance (formerly Via LA), whose list was not checked.
- **The 2016 royalty-free software policy** [14][33][34]:
  - It waived royalties on "application layer software downloaded to mobile devices or personal computers after the initial sale of the device, where the HEVC encoding or decoding is fully executed in software on a general purpose CPU" [33].
  - Contemporary reading: it excludes "operating systems updates altogether, as well as drivers, libraries, and firmware updates that unlock HEVC decode hardware already on the system"; and "If the software uses an existing hardware decoder, of course, there's no royalty, since a royalty on that hardware should have already been paid." [14]
  - On that reading, a program driving a GPU decoder the driver already exposes unlocks nothing and relies on the device's royalty. Whether the policy still stands is not stated in the 2025 overview (as #42 found).
- **What the makers' behaviour suggests:** HP and Dell switch the hardware decoder off on cheap models, and every program loses it [21][22]. That fits a royalty paid per device for an *enabled* hardware decoder, not per program that uses it (inferred).
- **NVIDIA's driver licence** (section 6): "it is your sole responsibility to obtain any additional third-party licenses required ... including, without limitation, audio and/or video encoders and decoders and implementations of technical standards." [15] The person installing the driver accepts this. Whether it reaches a program using the driver is for the lawyer.
- **Google and Chrome:**
  - Google has given no public reasoning. Streaming Media "weren't able to get any input from Google" [16].
  - Commentators read Chrome's hardware-only HEVC as leaving the licence with the device's maker [16][35].
  - Google is itself an HEVC Advance licensee and licensor [13], so Chrome's position is not one Trovolve could borrow.
- **Mozilla:** Firefox decodes HEVC on Windows through Media Foundation and the Store extension, not D3D11 directly. Its own bug says affected people must "either update their Windows version or purchase" the extension [36]. (That bug claims the extension is preinstalled from Windows 11 22H2, yet reports failures on 25H2 and 26H2. This conflicts with #42's finding, and #42's is better sourced.)
- **HEIF itself:** Nokia's HEIF licence speaks of patents "Necessarily Infringed" by the HEIF specification. It grants its software only for "non-commercial purposes of evaluation, testing and academic research" [10]. Today Trovolve reads the container through Microsoft's HEIF extension. A reader of Trovolve's own would raise whether Nokia's HEIF patents reach it. This is not answered by any source found.
- **Outside the pools:** holders in neither pool are covered by no pool term, as #42 noted.

**Reading, not advice:** the plain reading of the pool's summary is that the GPU's decoder is licensed with the device, and that a program using it as the driver offers it adds no "HEVC Decoder" of its own. But Trovolve would ship HEVC syntax parsing, and Microsoft's specification calls that a "software decoder" [2], so the lawyer should be asked this exact question.

## 5. Against the alternatives

| Route | Cost to the person | Trovolve's code | Licence | Coverage |
|---|---|---|---|---|
| WIC with the OEM's free HEVC extension (today's branch) | none, where preinstalled | none extra | Microsoft's and the PC maker's | unknown share of PCs (#42) |
| WIC with the paid extension | $0.99, against the map | none extra | Microsoft's | any PC |
| **D3D11 video (this ticket)** | none | 3,000–4,500 lines, 5–8 weeks, driver upkeep | device's licence likely; host parsing and HEIF reader for the lawyer | PCs with a working HEVC GPU since about 2015, not VMs, RDP or makers' disabled models |
| Vendor SDKs (NVIDIA NVDEC with its own parser, Intel VPL, AMD AMF) | none | three code paths, less parsing of Trovolve's own | NVIDIA's terms put codec licences on the licensee [15] | per maker only; no Qualcomm |
| Software decoder (libde265, heic-rs) | none | small | outside the map: a decoder of Trovolve's own, LGPL or not [11] | any PC |
| Matching HEIC by metadata only (#44, #48) | none | small | none | any PC, weaker matching |

## What this means for the map

- **#48:** the GPU route is a real answer for PCs without the free extension, if Jamie accepts host-side parsing as "not a decoder of Trovolve's own" and the lawyer agrees. Even then a share of PCs (VMs, RDP, HP's and Dell's disabled models, old GPUs) still needs #48's fallback, so #48 stays needed.
- **#43:** with this route Trovolve does the YUV-to-RGB step itself from bit-exact samples, so its pixels could be made to match libheif's, and so Magick.NET's. Worth stating in #43 as the alternative if WIC cannot be made to match.
- **#47:** if this route is built, it is a third decoder to score.
- **Suggested new ticket:** a spike of a few days. Decode the tiles of one CC0 HEIC through D3D11 on the development PC (Intel and NVIDIA), and compare the YUV with libde265's and the RGB with Magick.NET's. This would settle feasibility and the colour question before 5–8 weeks are spent.
- **Suggested question for the lawyer** (to add to STEPS.md's list): does a program that parses HEVC parameter sets and slice headers and has the GPU's licensed decoder do the rest incorporate an "HEVC Decoder"? And does reading HEIF in its own code engage Nokia's HEIF patents?

## Sources

1. Microsoft Learn, `ID3D11VideoDevice::GetVideoDecoderProfile`: https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11videodevice-getvideodecoderprofile ; Direct3D 12 video GUIDs: https://learn.microsoft.com/en-us/windows/win32/medfound/direct3d-12-video-guids
2. Microsoft, DirectX Video Acceleration Specification for HEVC, version 1.0 (published 2025-03-27), `DXVA_HEVC_Spec.docx`: https://www.microsoft.com/en-us/download/details.aspx?id=39947 (sections 1.3, 5, 7.1; "host decoder or software decoder" in the introduction)
3. caniuse, HEIF/HEIC image format: https://caniuse.com/heif
4. StaZhu, enable-chromium-hevc-hardware-decoding (third-party guide, GPU list and Main Still Picture from Chrome 128): https://github.com/StaZhu/enable-chromium-hevc-hardware-decoding
5. Chromium source, `media/gpu/windows/d3d_h265_accelerator.cc`, `d3d_decoder_configurator.cc`, `supported_profile_helpers.cc`: https://github.com/chromium/chromium/tree/main/media/gpu/windows
6. Chromium source, `media/parsers/h265_parser.cc` and `.h`: https://github.com/chromium/chromium/tree/main/media/parsers
7. cros-codecs (BSD-3-Clause), `src/codec/h265/parser.rs`: https://github.com/chromeos/cros-codecs
8. MetadataExtractor for .NET (Apache-2.0), `MetadataExtractor/Formats/Iso14496/Boxes`: https://github.com/drewnoakes/metadata-extractor-dotnet/tree/main/MetadataExtractor/Formats/Iso14496/Boxes
9. Microsoft win32metadata (MIT), recompiled `dxva.h`: https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/dxva.h ; CsWin32: https://github.com/microsoft/CsWin32
10. Nokia HEIF licence v2.1: https://github.com/nokiatech/heif/blob/master/LICENSE.TXT
11. libheif (LGPL for the library): https://github.com/strukturag/libheif/blob/master/COPYING ; ente heic-decoder (AGPL-3.0): https://github.com/ente/heic-decoder ; heic-rs (Apache-2.0, a full software decoder): https://github.com/ArintonAkos/heic-rs ; libavif (BSD-2, a HEIF container reader for AV1): https://github.com/AOMediaCodec/libavif/blob/main/LICENSE
12. Access Advance, HEVC Advance Program Overview, July 2025: https://accessadvance.com/wp-content/uploads/2021/06/HEVC-Advance-Program-Overview-July-2025.pdf
13. Access Advance, HEVC Advance licensees (page dated 2026-11-25): https://accessadvance.com/hevc-advance-patent-pool-licensees/
14. Jan Ozer, Streaming Learning Center, "HEVC Advance Makes Some Software Royalty Free" (2016-11-22): https://streaminglearningcenter.com/blogs/hevc-advance-makes-software-royalty-free.html
15. NVIDIA Driver License Agreement, section 6: https://www.nvidia.com/en-us/drivers/nvidia-license/
16. Streaming Learning Center, "Google Chrome Plays HEVC: What Does it Mean?": https://streaminglearningcenter.com/codecs/google-chrome-plays-hevc-what-does-it-mean.html
17. Wikipedia, Intel Quick Sync Video: https://en.wikipedia.org/wiki/Intel_Quick_Sync_Video
18. Wikipedia, Nvidia NVDEC: https://en.wikipedia.org/wiki/Nvidia_NVDEC
19. Wikipedia, Unified Video Decoder: https://en.wikipedia.org/wiki/Unified_Video_Decoder
20. Microsoft Learn, `ID3D11VideoDevice` interface (remarks): https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nn-d3d11-id3d11videodevice
21. Engadget, "HP and Dell cut HEVC support in some laptops" (2025-11-21): https://www.engadget.com/big-tech/hp-and-dell-cut-hevc-support-in-some-laptops-130000940.html ; VideoCardz: https://videocardz.com/newz/hp-and-dell-disable-hevc-hardware-decoding-on-select-laptops-as-royalties-rise
22. Ars Technica, "HP and Dell disable HEVC support built into their laptops' CPUs" (2025-11): https://arstechnica.com/gadgets/2025/11/hp-and-dell-disable-hevc-support-built-into-their-laptops-cpus/ (read through [21] and search summaries; not fetched directly)
23. ITU-T H.265 (conformance: decoded output must equal the decoding process exactly): https://www.itu.int/rec/T-REC-H.265 ; JCT-VC conformance draft: https://www.itu.int/wftp3/av-arch/jctvc-site/bitstream_exchange/backup/JCTVC-Lxxxxx_conformance_d3.doc
24. heic.fyi, HEIC sample files (CC0; 512 × 512 grid, Main Still Picture): https://heic.fyi/heic-sample-files
25. Cheeky4n6Monkey, "Monkey takes a .heic" (48 `hvc1` tile items): http://cheeky4n6monkey.blogspot.com/2017/10/monkey-takes-heic.html
26. photrix pull request #26 (Apple's `irot`, EXIF orientation 1): https://github.com/scottdrichards/photrix/pull/26
27. Vortice.Windows (MIT), `ID3D11VideoDecoder`: https://github.com/amerkoleci/Vortice.Windows/blob/main/src/Vortice.Direct3D11/ID3D11VideoDecoder.cs
28. heif-oxide pull request #9 (iPhone HEIC: full range, BT.601, Display P3 in the VUI; no `nclx`): https://github.com/dan335/heif-oxide/pull/9 ; libheif issue #240: https://github.com/strukturag/libheif/issues/240 ; PNGCrushCS pull request #317: https://github.com/Hawkynt/PNGCrushCS/pull/317
29. Library of Congress, HEIF HEIC/HEIX brands: https://www.loc.gov/preservation/digital/formats/fdd/fdd000526.shtml
30. Microsoft Learn, Partition and assign GPUs to a virtual machine: https://learn.microsoft.com/en-us/windows-server/virtualization/hyper-v/partition-assign-vm-gpu
31. Esri Community, enabling GPU rendering on Windows Server 2016 / Windows 10 RDP: https://community.esri.com/t5/implementing-arcgis-questions/enabling-gpu-rendering-on-windows-server-2016/td-p/658522
32. Emby, "HWA fails with RDP": https://emby.media/support/articles/Hwa-Fails-with-RDP.html
33. HEVC Advance, "HEVC Advance Announces 'Royalty Free' HEVC Software" (2016-11-22): https://www.prnewswire.com/news-releases/hevc-advance-announces-royalty-free-hevc-software-300367212.html
34. Beamr, "Patent Pool HEVC Advance Responds": https://blog.beamr.com/2016/11/30/patent-pool-hevc-advance-responds-announces-royalty-free-hevc-software/
35. Neowin, "Chrome 107 is landing today with HEVC decoding support, despite pushback from Mozilla" (quoted from search results; the page refused automated reading): https://www.neowin.net/news/chrome-107-is-landing-today-with-hevc-decoding-support-despite-pushback-from-mozilla/
36. Mozilla bug 1963910 (hevc-windows-support): https://bugzilla.mozilla.org/show_bug.cgi?id=1963910
