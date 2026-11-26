using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Trovolve.Desktop.Imaging;

// Windows Imaging Component's own interfaces, as wincodec.h declares them, with only the methods Trovolve calls; those
// before them in the table are kept as placeholders, in order, as COM finds a method by its place. Every method returns
// its HRESULT, which WicPictureDecoder checks.
#pragma warning disable CS1591, IDE1006 // the system's own names, documented by Microsoft

[SupportedOSPlatform("windows")]
[ComImport, Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICImagingFactory
{
    [PreserveSig] int CreateDecoderFromFilename();
    [PreserveSig] int CreateDecoderFromStream(IWICStream stream, IntPtr vendor, int metadataOptions, out IWICBitmapDecoder decoder);
    [PreserveSig] int CreateDecoderFromFileHandle();
    [PreserveSig] int CreateComponentInfo();
    [PreserveSig] int CreateDecoder();
    [PreserveSig] int CreateEncoder();
    [PreserveSig] int CreatePalette();
    [PreserveSig] int CreateFormatConverter(out IWICFormatConverter converter);
    [PreserveSig] int CreateBitmapScaler(out IWICBitmapScaler scaler);
    [PreserveSig] int CreateBitmapClipper();
    [PreserveSig] int CreateBitmapFlipRotator(out IWICBitmapFlipRotator rotator);
    [PreserveSig] int CreateStream(out IWICStream stream);
    [PreserveSig] int CreateColorContext(out IWICColorContext context);
    [PreserveSig] int CreateColorTransformer(out IWICColorTransform transform);
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("135ff860-22b7-4ddf-b0f6-218f4f299a43"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICStream
{
    // ISequentialStream and IStream.
    [PreserveSig] int Read();
    [PreserveSig] int Write();
    [PreserveSig] int Seek();
    [PreserveSig] int SetSize();
    [PreserveSig] int CopyTo();
    [PreserveSig] int Commit();
    [PreserveSig] int Revert();
    [PreserveSig] int LockRegion();
    [PreserveSig] int UnlockRegion();
    [PreserveSig] int Stat();
    [PreserveSig] int Clone();
    [PreserveSig] int InitializeFromIStream();
    [PreserveSig] int InitializeFromFilename();
    [PreserveSig] int InitializeFromMemory(IntPtr buffer, int size);
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("9edde9e7-8dee-47ea-99df-e6faf2ed44bf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICBitmapDecoder
{
    [PreserveSig] int QueryCapability();
    [PreserveSig] int Initialize();
    [PreserveSig] int GetContainerFormat();
    [PreserveSig] int GetDecoderInfo();
    [PreserveSig] int CopyPalette();
    [PreserveSig] int GetMetadataQueryReader();
    [PreserveSig] int GetPreview();
    [PreserveSig] int GetColorContexts();
    [PreserveSig] int GetThumbnail();
    [PreserveSig] int GetFrameCount(out int count);
    [PreserveSig] int GetFrame(int index, out IWICBitmapFrameDecode frame);
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("00000120-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICBitmapSource
{
    [PreserveSig] int GetSize(out int width, out int height);
    [PreserveSig] int GetPixelFormat(out Guid format);
    [PreserveSig] int GetResolution(out double dpiX, out double dpiY);
    [PreserveSig] int CopyPalette(IntPtr palette);
    [PreserveSig] int CopyPixels(IntPtr rectangle, int stride, int size, IntPtr buffer);
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("3b16811b-6a43-4ec9-a813-3d930c13b940"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICBitmapFrameDecode
{
    [PreserveSig] int GetSize(out int width, out int height);
    [PreserveSig] int GetPixelFormat(out Guid format);
    [PreserveSig] int GetResolution(out double dpiX, out double dpiY);
    [PreserveSig] int CopyPalette(IntPtr palette);
    [PreserveSig] int CopyPixels(IntPtr rectangle, int stride, int size, IntPtr buffer);
    [PreserveSig] int GetMetadataQueryReader(out IWICMetadataQueryReader reader);
    [PreserveSig] int GetColorContexts(int count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IWICColorContext[]? contexts, out int actual);
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICFormatConverter
{
    [PreserveSig] int GetSize(out int width, out int height);
    [PreserveSig] int GetPixelFormat(out Guid format);
    [PreserveSig] int GetResolution(out double dpiX, out double dpiY);
    [PreserveSig] int CopyPalette(IntPtr palette);
    [PreserveSig] int CopyPixels(IntPtr rectangle, int stride, int size, IntPtr buffer);
    [PreserveSig] int Initialize(IWICBitmapSource source, ref Guid format, int dither, IntPtr palette, double alphaThreshold, int paletteType);
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("00000302-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICBitmapScaler
{
    [PreserveSig] int GetSize(out int width, out int height);
    [PreserveSig] int GetPixelFormat(out Guid format);
    [PreserveSig] int GetResolution(out double dpiX, out double dpiY);
    [PreserveSig] int CopyPalette(IntPtr palette);
    [PreserveSig] int CopyPixels(IntPtr rectangle, int stride, int size, IntPtr buffer);
    [PreserveSig] int Initialize(IWICBitmapSource source, int width, int height, int interpolation);
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("5009834f-2d6a-41ce-9e1b-17c5aff7a782"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICBitmapFlipRotator
{
    [PreserveSig] int GetSize(out int width, out int height);
    [PreserveSig] int GetPixelFormat(out Guid format);
    [PreserveSig] int GetResolution(out double dpiX, out double dpiY);
    [PreserveSig] int CopyPalette(IntPtr palette);
    [PreserveSig] int CopyPixels(IntPtr rectangle, int stride, int size, IntPtr buffer);
    [PreserveSig] int Initialize(IWICBitmapSource source, int options);
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("3c613a02-34b2-44ea-9a7c-45aea9c6fd6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICColorContext
{
    [PreserveSig] int InitializeFromFilename();
    [PreserveSig] int InitializeFromMemory();
    [PreserveSig] int InitializeFromExifColorSpace(int value);
    [PreserveSig] int GetContextType(out int type);
    [PreserveSig] int GetProfileBytes();
    [PreserveSig] int GetExifColorSpace(out int value);
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("b66f034f-d0e2-40ab-b436-6de39e321a94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICColorTransform
{
    [PreserveSig] int GetSize(out int width, out int height);
    [PreserveSig] int GetPixelFormat(out Guid format);
    [PreserveSig] int GetResolution(out double dpiX, out double dpiY);
    [PreserveSig] int CopyPalette(IntPtr palette);
    [PreserveSig] int CopyPixels(IntPtr rectangle, int stride, int size, IntPtr buffer);
    [PreserveSig] int Initialize(IWICBitmapSource source, IWICColorContext from, IWICColorContext to, ref Guid format);
}

[SupportedOSPlatform("windows")]
[ComImport, Guid("30989668-e1c9-4597-b395-458eedb808df"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWICMetadataQueryReader
{
    [PreserveSig] int GetContainerFormat();
    [PreserveSig] int GetLocation();
    [PreserveSig] int GetMetadataByName([MarshalAs(UnmanagedType.LPWStr)] string name, ref PropVariant value);
}

/// <summary>A PROPVARIANT as WIC hands one back: only a two-byte number is ever read from it.</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PropVariant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public ushort Number;
}

#pragma warning restore CS1591, IDE1006
