# The brand

The mark is direction 2a, the strata gem: five layers of a library forming one gem. The wordmark is lowercase Sora 600, outlined, so it needs no font. Its colours are the page's tokens in `Trovolve.ReactUI/app/globals.css`. `brand-kit.html` shows the whole kit in one offline page.

## In use

| File | Where it is used |
|---|---|
| `app-icon.png` | The icon, 1024 × 1024 and opaque: the only copy. `Trovolve.Api` builds it in for `/api/about/icon` (the page's tab, header, About, Welcome, Settings, the Pro sheet and the share cards); the iPhone app takes it as its single-size icon; Android links it as `mipmap/appicon.png`; `tools/site.mjs` copies it into the web site. |
| `app-icon.svg` | The icon's vector source. |
| `trovolve.ico` | The Windows program's icon, 16 to 256 pixels: `ApplicationIcon` in `Trovolve.Server.csproj`. |
| `png/app-icons/ios/AppIcon-dark-1024.png` | The iPhone's dark home-screen icon (iOS 18), linked into `AppIcon.appiconset`. |

Drawn in code from these masters, so they follow the theme:

- `Trovolve.ReactUI/components/FeatureIcons.tsx`: the twelve feature icons (`svg/feature-icons`), used by the phone and tablet layouts through `components/phone/Icons.tsx`.
- `Trovolve.ReactUI/components/TrovolveBrand.tsx`: the mark (`svg/mark`), the wordmark (`svg/lockups`), shown in the computer's bar and on a phone's Welcome, and the launch animation, played as the phone app opens (`components/phone/Launch.tsx`).
- `Trovolve.Android/Resources`: the adaptive icon (`mipmap-anydpi-v26/appicon.xml`, `drawable/appicon_foreground.xml`, `drawable/appicon_monochrome.xml`, `values/appicon_colors.xml`, from `svg/app-icon/android-*.svg`), the notification icon (`drawable/notice_icon.xml`, from `svg/mark/trovolve-mark-small-mono.svg`) and the icon menu's shortcuts (`drawable/shortcut_*.xml`, from `android/feature-icons`).

## Kept for later

- `svg/`: the masters of the mark, the app icons, the lockups, the feature icons (24 grid, `currentColor`) and the marketing pictures.
- `png/app-icons/`: every size for iOS (unused while Xcode makes them from the single icon), Android's legacy and round mipmaps and its adaptive layers (unused, as Android 11, the oldest supported, draws the adaptive icon), the Play Store's 512, macOS's `AppIcon.iconset` and menu-bar template for a Mac app bundle (rename `-2x` to `@2x`, then `iconutil -c icns AppIcon.iconset`), Windows' tiles and unplated icons for an installer, and the web's favicons, apple-touch icon and PWA icons.
- `png/mark/`, `png/lockups/`: the mark and the lockups on a transparent background, for documents and the web site.
- `png/marketing/`: the web site's hero, Google Play's feature graphic, the Open Graph picture, a square social post, the phone launch screen and the Android 12 splash icon.
- `png/feature-icons/`: every feature icon at 24, 48 and 72 pixels, for dark (#3ebfc6) and light (#007283).
- `android/feature-icons/`: the twelve feature icons as Android vector drawables. Android vectors draw no dashed lines, so Compress has a solid outline.
