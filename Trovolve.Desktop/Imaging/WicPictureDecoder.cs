using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;

namespace Trovolve.Desktop.Imaging;

/// <summary>
/// Decodes pictures with Windows Imaging Component, which Windows has built in for JPEG, PNG, GIF, BMP and TIFF, and
/// which reads HEIC, WebP and AVIF once their free extensions are installed from the Microsoft Store. Where one is
/// missing, the picture is unreadable, and the message says which to install (<see cref="WindowsExtensions"/>).
/// </summary>
[SupportedOSPlatform("windows")]
[ExcludeFromCodeCoverage(Justification = "Calls Windows Imaging Component, which only Windows has: the picture decoders' tests run it on Windows, here and on GitHub's Windows machines, and its rule for missing extensions is tested everywhere.")]
public sealed class WicPictureDecoder : IPictureDecoder
{
    private static readonly Guid FactoryClass = new("317d06e8-5f24-433d-bdf7-79ce68d8abc2");
    private static readonly Guid Bgr24 = new("6fddc324-4e03-4bfe-b185-3d77768dc90c");
    private static readonly Guid Rgba64 = new("6fddc324-4e03-4bfe-b185-3d77768dc916");
    private static readonly Guid VideoDecoders = new("d6c02d4b-6833-45b4-971a-05a4b04bab91");
    private static readonly Guid Video = new("73646976-0000-0010-8000-00aa00389b71");
    private static readonly Guid Hevc = new("43564548-0000-0010-8000-00aa00389b71");
    private const int ComponentNotFound = unchecked((int)0x88982F50);
    private const int ExifColorSpace = 2, Srgb = 1, AdobeRgb = 2;
    private const short UnsignedShort = 18;

    // Whether HEVC Video Extensions are installed, asked of Media Foundation once, when a HEIC first fails.
    private static readonly Lazy<bool> HevcInstalled = new(AskForHevc);

    /// <inheritdoc />
    public Task<DecodedPicture> DecodeAsync(byte[] picture, PictureFormat format, CancellationToken ct)
        => Task.Run(() => Decode(picture, format), ct);

    private static DecodedPicture Decode(byte[] picture, PictureFormat format)
    {
        // Every object Windows made, let go of in the end whatever happens.
        var made = new List<object>();
        T Keep<T>(T thing) where T : class
        {
            made.Add(thing);
            return thing;
        }
        // A step that failed: what to install, where that explains it, or Windows' own words.
        void Check(int result, bool readerFound = true)
        {
            if (result >= 0) return;
            // Windows' own error, its words and number, kept with the one Trovolve knows a picture it cannot read by.
            var windows = Marshal.GetExceptionForHR(result) ?? new COMException($"Windows could not read the picture (0x{result:X8}).", result);
            throw new UnreadableImageException(WindowsExtensions.Missing(format, readerFound, format != PictureFormat.Heic || HevcInstalled.Value) ?? windows.Message, windows);
        }

        var pinned = GCHandle.Alloc(picture, GCHandleType.Pinned);
        try
        {
            var factory = Keep((IWICImagingFactory)Activator.CreateInstance(Type.GetTypeFromCLSID(FactoryClass, throwOnError: true)!)!);
            Check(factory.CreateStream(out var stream));
            Keep(stream);
            Check(stream.InitializeFromMemory(pinned.AddrOfPinnedObject(), picture.Length));
            var found = factory.CreateDecoderFromStream(stream, IntPtr.Zero, 0, out var decoder);
            Check(found, readerFound: found != ComponentNotFound);
            Keep(decoder);
            Check(decoder.GetFrame(0, out var frame));
            Keep(frame);
            Check(frame.GetSize(out var width, out var height));
            // A picture claiming to be enormous is never drawn: it can only be unreadable.
            if (width > DecodedPicture.MaxEdge || height > DecodedPicture.MaxEdge)
                throw new UnreadableImageException($"The picture says it is {width:N0} × {height:N0} pixels, larger than any Trovolve reads.");

            var orientation = Orientation(frame, Keep);
            var sideways = orientation >= 5;
            var (across, down) = sideways ? (height, width) : (width, height);

            IWICBitmapSource Converted(IWICBitmapSource from, Guid format)
            {
                Check(factory.CreateFormatConverter(out var converter));
                Keep(converter);
                Check(converter.Initialize(from, ref format, 0, IntPtr.Zero, 0, 0));
                return (IWICBitmapSource)converter;
            }

            var source = (IWICBitmapSource)frame;
            // Decoded whole and never reduced here: WIC's scaler, and a JPEG decoded at a fraction of its size, would give
            // the picture pixels of their own (PictureAnalyzer reduces every picture one way). A picture in colours of its
            // own is turned to standard ones at sixteen bits a channel (the one deep format the transform takes), so that
            // rounding twice does not set it apart from a standard copy of itself.
            if (Profile(factory, frame, Keep) is var (carried, standard))
            {
                source = Converted(source, Rgba64);
                Check(factory.CreateColorTransformer(out var transform));
                Keep(transform);
                var rgba64 = Rgba64;
                // A profile that cannot be used leaves the picture's colours as they are.
                if (transform.Initialize(source, carried, standard, ref rgba64) >= 0) source = (IWICBitmapSource)transform;
            }
            source = Converted(source, Bgr24);
            if (orientation != 1)
            {
                Check(factory.CreateBitmapFlipRotator(out var rotator));
                Keep(rotator);
                Check(rotator.Initialize(source, Transform(orientation)));
                source = (IWICBitmapSource)rotator;
            }

            var rgb = new byte[across * down * 3];
            var pixels = GCHandle.Alloc(rgb, GCHandleType.Pinned);
            try { Check(source.CopyPixels(IntPtr.Zero, across * 3, rgb.Length, pixels.AddrOfPinnedObject())); }
            finally { pixels.Free(); }
            // Windows hands back blue, green, red.
            for (var i = 0; i < rgb.Length; i += 3) (rgb[i], rgb[i + 2]) = (rgb[i + 2], rgb[i]);
            return new DecodedPicture(across, down, rgb);
        }
        finally
        {
            pinned.Free();
            for (var i = made.Count - 1; i >= 0; i--) Marshal.ReleaseComObject(made[i]);
        }
    }

    // How to turn the picture the right way up, from the photo's orientation (1 to 8, as EXIF numbers it); 1 when it says none.
    private static int Orientation(IWICBitmapFrameDecode frame, Func<IWICMetadataQueryReader, IWICMetadataQueryReader> keep)
    {
        if (frame.GetMetadataQueryReader(out var reader) < 0) return 1;
        keep(reader);
        var value = new PropVariant();
        try
        {
            return reader.GetMetadataByName("System.Photo.Orientation", ref value) >= 0 && value.Type == UnsignedShort && value.Number is >= 1 and <= 8 ? value.Number : 1;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    // Windows' rotation and flip for each orientation. Windows flips first, then turns clockwise, so a picture to be
    // turned a quarter and mirrored (5 and 7) is mirrored and turned the other way.
    private static int Transform(int orientation) => orientation switch
    {
        2 => 8,
        3 => 2,
        4 => 16,
        5 => 3 | 8,
        6 => 1,
        7 => 1 | 8,
        8 => 3,
        _ => 0,
    };

    // The colours the picture says it is in, and standard ones to turn them to; null when it says none, or standard ones already.
    private static (IWICColorContext Carried, IWICColorContext Standard)? Profile(IWICImagingFactory factory, IWICBitmapFrameDecode frame, Func<object, object> keep)
    {
        if (frame.GetColorContexts(0, null, out var count) < 0 || count == 0) return null;
        var contexts = new IWICColorContext[count];
        for (var i = 0; i < count; i++)
        {
            if (factory.CreateColorContext(out var context) < 0) return null;
            contexts[i] = (IWICColorContext)keep(context);
        }
        if (frame.GetColorContexts(count, contexts, out _) < 0) return null;
        // A profile of its own, if it has one; otherwise what the camera said of its colours. Of those only Adobe RGB is
        // a profile: "uncalibrated" (65535, which an iPhone's panorama says) names no colours, and is taken as standard.
        var carried = contexts.FirstOrDefault(c => c.GetContextType(out var type) >= 0 && type != ExifColorSpace) ?? contexts[0];
        if (carried.GetContextType(out var kind) >= 0 && kind == ExifColorSpace && (carried.GetExifColorSpace(out var space) < 0 || space != AdobeRgb)) return null;
        if (factory.CreateColorContext(out var standard) < 0 || standard.InitializeFromExifColorSpace(Srgb) < 0) return null;
        keep(standard);
        return (carried, standard);
    }

    // Whether Media Foundation has an HEVC decoder, which HEVC Video Extensions bring: Windows reads HEIC with it.
    private static bool AskForHevc()
    {
        var wanted = new TypeInfo { Major = Video, Minor = Hevc };
        if (MFTEnumEx(VideoDecoders, 0x3F, ref wanted, IntPtr.Zero, out var found, out var count) < 0) return false;
        for (var i = 0; i < count; i++) Marshal.Release(Marshal.ReadIntPtr(found, i * IntPtr.Size));
        Marshal.FreeCoTaskMem(found);
        return count > 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TypeInfo
    {
        public Guid Major;
        public Guid Minor;
    }

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFTEnumEx(Guid category, int flags, ref TypeInfo input, IntPtr output, out IntPtr activates, out int count);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int PropVariantClear(ref PropVariant value);
}
