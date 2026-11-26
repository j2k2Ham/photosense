using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;

namespace Trovolve.Infrastructure.Imaging;

/// <summary>
/// Decodes pictures with ImageIO, Apple's own decoder, which reads HEIC, JPEG and the rest on every Mac, as the iPhone
/// app does: turned the right way up and reduced as it decodes, then drawn in standard colours. Reached through its C
/// functions, so that the computer's program needs no Apple framework of .NET's.
/// </summary>
[SupportedOSPlatform("macos")]
[ExcludeFromCodeCoverage(Justification = "Calls Apple's ImageIO, which only a Mac has: the picture decoders' tests run it on GitHub's Mac machines.")]
public sealed class ImageIOPictureDecoder : IPictureDecoder
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ImageIO = "/System/Library/Frameworks/ImageIO.framework/ImageIO";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const int IntNumber = 9, Utf8 = 0x08000100, NoneSkipLast = 5, HighQuality = 3;

    private static readonly Lazy<Keys> Names = new(() => new Keys());

    /// <inheritdoc />
    public Task<DecodedPicture> DecodeAsync(byte[] picture, PictureFormat format, int maxEdge, CancellationToken ct)
        => Task.Run(() => Decode(picture, format, maxEdge), ct);

    private static DecodedPicture Decode(byte[] picture, PictureFormat format, int maxEdge)
    {
        var keys = Names.Value;
        // Every object made, let go of in the end whatever happens.
        var made = new List<IntPtr>();
        IntPtr Keep(IntPtr thing)
        {
            if (thing != IntPtr.Zero) made.Add(thing);
            return thing;
        }
        try
        {
            var data = Keep(CFDataCreate(IntPtr.Zero, picture, picture.Length));
            // Told what the picture is, by its first bytes.
            var hint = Keep(CFStringCreateWithCString(IntPtr.Zero, TypeOf(format), Utf8));
            var source = Keep(CGImageSourceCreateWithData(data, Keep(Dictionary(keys, (keys.TypeHint, hint)))));
            if (source == IntPtr.Zero || CGImageSourceGetCount(source) < 1) throw new UnreadableImageException("The picture could not be read.");
            var properties = Keep(CGImageSourceCopyPropertiesAtIndex(source, 0, IntPtr.Zero));
            int width = Number(properties, keys.PixelWidth), height = Number(properties, keys.PixelHeight), orientation = Number(properties, keys.Orientation);
            if (width <= 0 || height <= 0) throw new UnreadableImageException("The picture does not say how large it is.");
            if (width > DecodedPicture.MaxEdge || height > DecodedPicture.MaxEdge)
                throw new UnreadableImageException($"The picture says it is {width:N0} × {height:N0} pixels, larger than any Trovolve reads.");
            var sideways = orientation >= 5;
            var (fullWidth, fullHeight) = sideways ? (height, width) : (width, height);
            var (across, down) = DecodedPicture.Fit(fullWidth, fullHeight, maxEdge);

            // Turned the right way up and decoded no larger than needed, which is what makes it quick.
            var size = Keep(Integer(Math.Max(across, down)));
            var options = Keep(Dictionary(keys, (keys.FromImageAlways, keys.True), (keys.WithTransform, keys.True), (keys.MaxPixelSize, size)));
            var image = CGImageSourceCreateThumbnailAtIndex(source, 0, options);
            if (image == IntPtr.Zero) throw new UnreadableImageException("The picture could not be decoded.");
            try
            {
                return new DecodedPicture(across, down, Drawn(image, across, down, keys), fullWidth, fullHeight);
            }
            finally
            {
                CGImageRelease(image);
            }
        }
        finally
        {
            for (var i = made.Count - 1; i >= 0; i--) CFRelease(made[i]);
        }
    }

    // The picture drawn at exactly the size asked, in standard colours, three bytes a pixel, row by row from the top.
    private static byte[] Drawn(IntPtr image, int width, int height, Keys keys)
    {
        var rgbx = new byte[width * height * 4];
        var pinned = GCHandle.Alloc(rgbx, GCHandleType.Pinned);
        var colours = CGColorSpaceCreateWithName(keys.Srgb);
        try
        {
            var context = CGBitmapContextCreate(pinned.AddrOfPinnedObject(), width, height, 8, width * 4, colours, NoneSkipLast);
            if (context == IntPtr.Zero) throw new UnreadableImageException("The picture could not be drawn.");
            CGContextSetInterpolationQuality(context, HighQuality);
            CGContextDrawImage(context, new Rect(0, 0, width, height), image);
            CGContextRelease(context);
        }
        finally
        {
            CGColorSpaceRelease(colours);
            pinned.Free();
        }
        var rgb = new byte[width * height * 3];
        for (int from = 0, to = 0; to < rgb.Length; from += 4, to += 3) (rgb[to], rgb[to + 1], rgb[to + 2]) = (rgbx[from], rgbx[from + 1], rgbx[from + 2]);
        return rgb;
    }

    private static string TypeOf(PictureFormat format) => format switch
    {
        PictureFormat.Jpeg => "public.jpeg",
        PictureFormat.Png => "public.png",
        PictureFormat.Gif => "com.compuserve.gif",
        PictureFormat.Bmp => "com.microsoft.bmp",
        PictureFormat.Tiff => "public.tiff",
        PictureFormat.WebP => "org.webmproject.webp",
        PictureFormat.Avif => "public.avif",
        _ => "public.heic",
    };

    private static IntPtr Dictionary(Keys keys, params (IntPtr Key, IntPtr Value)[] pairs)
        => CFDictionaryCreate(IntPtr.Zero, [.. pairs.Select(p => p.Key)], [.. pairs.Select(p => p.Value)], pairs.Length, keys.KeyCallbacks, keys.ValueCallbacks);

    private static IntPtr Integer(int value) => CFNumberCreate(IntPtr.Zero, IntNumber, ref value);

    // A number among a picture's properties; 0 when it has none of that name.
    private static int Number(IntPtr properties, IntPtr key)
    {
        var number = properties == IntPtr.Zero ? IntPtr.Zero : CFDictionaryGetValue(properties, key);
        return number != IntPtr.Zero && CFNumberGetValue(number, IntNumber, out var value) ? value : 0;
    }

    /// <summary>The names ImageIO, CoreGraphics and CoreFoundation export, read from the frameworks once.</summary>
    private sealed class Keys
    {
        public readonly IntPtr FromImageAlways, WithTransform, MaxPixelSize, TypeHint, PixelWidth, PixelHeight, Orientation, Srgb, True, KeyCallbacks, ValueCallbacks;

        public Keys()
        {
            var imageIO = NativeLibrary.Load(ImageIO);
            var coreGraphics = NativeLibrary.Load(CoreGraphics);
            var coreFoundation = NativeLibrary.Load(CoreFoundation);
            IntPtr Name(IntPtr library, string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));
            FromImageAlways = Name(imageIO, "kCGImageSourceCreateThumbnailFromImageAlways");
            WithTransform = Name(imageIO, "kCGImageSourceCreateThumbnailWithTransform");
            MaxPixelSize = Name(imageIO, "kCGImageSourceThumbnailMaxPixelSize");
            TypeHint = Name(imageIO, "kCGImageSourceTypeIdentifierHint");
            PixelWidth = Name(imageIO, "kCGImagePropertyPixelWidth");
            PixelHeight = Name(imageIO, "kCGImagePropertyPixelHeight");
            Orientation = Name(imageIO, "kCGImagePropertyOrientation");
            Srgb = Name(coreGraphics, "kCGColorSpaceSRGB");
            True = Name(coreFoundation, "kCFBooleanTrue");
            // The callbacks are structures, handed over where they are.
            KeyCallbacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks");
            ValueCallbacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Rect(double X, double Y, double Width, double Height);

    [DllImport(CoreFoundation)] private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, nint length);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int encoding);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallbacks, IntPtr valueCallbacks);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [DllImport(CoreFoundation)] private static extern IntPtr CFNumberCreate(IntPtr allocator, nint type, ref int value);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool CFNumberGetValue(IntPtr number, nint type, out int value);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr thing);
    [DllImport(ImageIO)] private static extern IntPtr CGImageSourceCreateWithData(IntPtr data, IntPtr options);
    [DllImport(ImageIO)] private static extern nint CGImageSourceGetCount(IntPtr source);
    [DllImport(ImageIO)] private static extern IntPtr CGImageSourceCopyPropertiesAtIndex(IntPtr source, nint index, IntPtr options);
    [DllImport(ImageIO)] private static extern IntPtr CGImageSourceCreateThumbnailAtIndex(IntPtr source, nint index, IntPtr options);
    [DllImport(CoreGraphics)] private static extern void CGImageRelease(IntPtr image);
    [DllImport(CoreGraphics)] private static extern IntPtr CGColorSpaceCreateWithName(IntPtr name);
    [DllImport(CoreGraphics)] private static extern void CGColorSpaceRelease(IntPtr space);
    [DllImport(CoreGraphics)] private static extern IntPtr CGBitmapContextCreate(IntPtr data, nint width, nint height, nint bitsPerComponent, nint bytesPerRow, IntPtr space, uint bitmapInfo);
    [DllImport(CoreGraphics)] private static extern void CGContextSetInterpolationQuality(IntPtr context, int quality);
    [DllImport(CoreGraphics)] private static extern void CGContextDrawImage(IntPtr context, Rect rect, IntPtr image);
    [DllImport(CoreGraphics)] private static extern void CGContextRelease(IntPtr context);
}
