using Android.Gms.Extensions;
using PhotoSense.Domain.Services;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.CodeScanner;

namespace PhotoSense.AndroidApp;

/// <summary>
/// Google's code scanner, as the phone's scanner: Google Play's own screen, which reads a QR code without PhotoSense
/// using the camera itself, and answers with its text, or nothing when the person puts it away.
/// </summary>
public sealed class GoogleCodeScanner : ICodeScanner
{
    /// <inheritdoc />
    public async Task<string?> ScanAsync(CancellationToken ct)
    {
        var options = new GmsBarcodeScannerOptions.Builder().SetBarcodeFormats(Barcode.FormatQrCode).Build();
        var scanner = GmsBarcodeScanning.GetClient((Android.Content.Context?)MainActivity.Shown ?? global::Android.App.Application.Context, options);
        try
        {
            var read = await scanner.StartScan().AsAsync<Barcode>().WaitAsync(ct);
            return read?.RawValue;
        }
        // Put away: nothing was read.
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return null; }
        catch (Java.Lang.Exception ex)
        {
            throw new IOException("The code scanner could not open on this phone. Choose Enter the code instead.", ex);
        }
    }
}
