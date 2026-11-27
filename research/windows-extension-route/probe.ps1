# Probe for the decoder map's ticket "Check the Windows extension route on Arm, N editions, HDR photos and new installs".
# Says what Windows offers for HEIC on this machine, as Trovolve's WIC decoder would see it. Counts and messages only.
param([string]$Picture, [string]$Stage)
Add-Type -AssemblyName PresentationCore
Add-Type -TypeDefinition @'
using System; using System.IO; using System.Runtime.InteropServices;
public static class Hevc {
    [StructLayout(LayoutKind.Sequential)] struct TypeInfo { public Guid Major; public Guid Minor; }
    [DllImport("mfplat.dll")] static extern int MFTEnumEx(Guid category, int flags, ref TypeInfo input, IntPtr output, out IntPtr activates, out int count);
    // As WicPictureDecoder.AskForHevc asks: every HEVC video decoder Media Foundation has.
    public static string Decoders() {
        if (!File.Exists(Path.Combine(Environment.SystemDirectory, "mfplat.dll"))) return "no mfplat.dll";
        var wanted = new TypeInfo { Major = new Guid("73646976-0000-0010-8000-00aa00389b71"), Minor = new Guid("43564548-0000-0010-8000-00aa00389b71") };
        IntPtr found; int count;
        var hr = MFTEnumEx(new Guid("d6c02d4b-6833-45b4-971a-05a4b04bab91"), 0x3F, ref wanted, IntPtr.Zero, out found, out count);
        if (hr < 0) return "MFTEnumEx failed 0x" + hr.ToString("X8");
        for (var i = 0; i < count; i++) Marshal.Release(Marshal.ReadIntPtr(found, i * IntPtr.Size));
        Marshal.FreeCoTaskMem(found);
        return count.ToString();
    }
}
'@
$os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$packages = (Get-AppxPackage | Where-Object { $_.Name -match 'HEVC|HEIF' } | ForEach-Object { "$($_.Name) $($_.Version) $($_.Architecture)" }) -join '; '
$decoded = try {
    $fs = [IO.File]::OpenRead($Picture)
    try { $d = [Windows.Media.Imaging.BitmapDecoder]::Create($fs, 'None', 'OnLoad'); "decoded $($d.Frames[0].PixelWidth)x$($d.Frames[0].PixelHeight) by $($d.CodecInfo.FriendlyName)" } finally { $fs.Dispose() }
} catch { $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }; "failed 0x$($e.HResult.ToString('X8')): $($e.Message)" }
"[$Stage] $($os.ProductName) $($os.EditionID) build $($os.CurrentBuild) on $env:PROCESSOR_ARCHITECTURE"
"[$Stage] packages: $(if ($packages) { $packages } else { 'none' })"
"[$Stage] HEVC decoders in Media Foundation: $([Hevc]::Decoders())"
"[$Stage] the HEIC: $decoded"
