using AVFoundation;
using CoreFoundation;
using PhotoSense.Domain.Services;

namespace PhotoSense.iOS;

/// <summary>
/// The iPhone's camera, looking for a QR code: a screen of its own over the page, with Cancel, which answers with the
/// first code it reads. The camera is asked for first, and refused, nothing is read.
/// </summary>
public sealed class CameraCodeScanner : ICodeScanner
{
    /// <inheritdoc />
    public async Task<string?> ScanAsync(CancellationToken ct)
    {
        if (!await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video)) return null;
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ScannerController? shown = null;
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            var top = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>().SelectMany(s => s.Windows).FirstOrDefault(w => w.IsKeyWindow)?.RootViewController;
            if (top is null) { answer.TrySetResult(null); return; }
            while (top.PresentedViewController is { } above) top = above;
            shown = new ScannerController(text => answer.TrySetResult(text));
            top.PresentViewController(shown, true, null);
        });
        // A caller that has gone puts the camera away.
        using (ct.Register(() => answer.TrySetCanceled(ct)))
        {
            try { return await answer.Task; }
            finally { UIApplication.SharedApplication.InvokeOnMainThread(() => shown?.DismissViewController(true, null)); }
        }
    }

    // The camera's picture, a frame where to point it, and Cancel; the first QR code read ends it.
    private sealed class ScannerController(Action<string?> done) : UIViewController
    {
        private readonly AVCaptureSession _session = new();
        private readonly Reader _reader = new();
        private bool _answered;

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            View!.BackgroundColor = UIColor.Black;
            var camera = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
            if (camera is null || AVCaptureDeviceInput.FromDevice(camera, out _) is not { } input || !_session.CanAddInput(input)) { Answer(null); return; }
            _session.AddInput(input);
            var output = new AVCaptureMetadataOutput();
            _session.AddOutput(output);
            output.SetDelegate(_reader, DispatchQueue.MainQueue);
            output.MetadataObjectTypes = AVMetadataObjectType.QRCode;
            _reader.Read = Answer;

            var picture = new AVCaptureVideoPreviewLayer(_session) { Frame = View.Bounds, VideoGravity = AVLayerVideoGravity.ResizeAspectFill };
            View.Layer.AddSublayer(picture);
            var hint = new UILabel { Text = "Point at the code on your computer", TextColor = UIColor.White, TextAlignment = UITextAlignment.Center, TranslatesAutoresizingMaskIntoConstraints = false };
            var cancel = UIButton.FromType(UIButtonType.System);
            cancel.SetTitle("Cancel", UIControlState.Normal);
            cancel.SetTitleColor(UIColor.White, UIControlState.Normal);
            cancel.TranslatesAutoresizingMaskIntoConstraints = false;
            cancel.TouchUpInside += (_, _) => Answer(null);
            View.AddSubviews(hint, cancel);
            NSLayoutConstraint.ActivateConstraints([
                hint.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor), hint.BottomAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.BottomAnchor, -80),
                cancel.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor), cancel.BottomAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.BottomAnchor, -24),
            ]);
            // The session takes a moment to start; off the main thread, the screen comes up at once.
            DispatchQueue.GetGlobalQueue(DispatchQueuePriority.Default).DispatchAsync(_session.StartRunning);
        }

        public override void ViewDidDisappear(bool animated)
        {
            base.ViewDidDisappear(animated);
            _session.StopRunning();
            Answer(null);
        }

        private void Answer(string? text)
        {
            if (_answered) return;
            _answered = true;
            done(text);
        }
    }

    // Hands on the text of the first QR code the camera reads.
    private sealed class Reader : AVCaptureMetadataOutputObjectsDelegate
    {
        public Action<string?>? Read { get; set; }

        public override void DidOutputMetadataObjects(AVCaptureMetadataOutput captureOutput, AVMetadataObject[] metadataObjects, AVCaptureConnection connection)
        {
            if (metadataObjects.OfType<AVMetadataMachineReadableCodeObject>().FirstOrDefault()?.StringValue is { } text) Read?.Invoke(text);
        }
    }
}
