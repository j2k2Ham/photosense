using System.Globalization;
using PhotoSense.Api.Hosting;
using WebKit;

namespace PhotoSense.iOS;

/// <summary>Hands out the page's own files from the app, and answers its requests below /api with the service's endpoints.</summary>
public sealed class PageSchemeHandler : NSObject, IWKUrlSchemeHandler
{
    private static readonly string PageFolder = Path.Combine(NSBundle.MainBundle.ResourcePath!, "wwwroot");

    // Requests the web view still waits for. Answering one it has given up on would end the app.
    private readonly Dictionary<IntPtr, CancellationTokenSource> _open = [];

    [Export("webView:startURLSchemeTask:")]
    public async void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        var request = urlSchemeTask.Request;
        var url = new Uri(request.Url.AbsoluteString!);
        var stop = new CancellationTokenSource();
        _open[urlSchemeTask.Handle] = stop;
        var started = DateTime.UtcNow;
        try
        {
            var answer = url.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal) ? await AskAsync(request, url, stop.Token) : PageFile(url);
            // Both this and the web view's giving up happen on the main thread, so it cannot give up in between.
            if (!stop.IsCancellationRequested) Send(urlSchemeTask, request.Url, answer);
            Say.Line($"{request.HttpMethod} {url.PathAndQuery} {answer.Status} in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms");
        }
        catch (OperationCanceledException) { Say.Line($"{request.HttpMethod} {url.PathAndQuery} given up by the web view"); }
        // An exception left to escape an async void method would end the app.
        catch (Exception e) { Say.Line($"{request.HttpMethod} {url.PathAndQuery} failed: {e}"); }
        finally { _open.Remove(urlSchemeTask.Handle); }
    }

    [Export("webView:stopURLSchemeTask:")]
    public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        if (_open.TryGetValue(urlSchemeTask.Handle, out var stop)) stop.Cancel();
    }

    private static async Task<InProcessAnswer> AskAsync(NSUrlRequest request, Uri url, CancellationToken ct)
    {
        var headers = new List<KeyValuePair<string, string>>();
        if (request.Headers is { } given)
            foreach (var (name, value) in given) headers.Add(new($"{name}", $"{value}"));
        var body = request.Body?.ToArray();
        await PhoneApp.Ready;
        // Off the main thread: the endpoints read photos and the database, which would hold up the screen.
        return await Task.Run(() => PhoneApp.Router.AnswerAsync(request.HttpMethod ?? "GET", url, headers, body, ct), ct);
    }

    private static InProcessAnswer PageFile(Uri url)
    {
        if (PageFiles.For(url.AbsolutePath, f => File.Exists(Path.Combine(PageFolder, f))) is not { } file)
            return new InProcessAnswer(404, new Dictionary<string, string> { ["Content-Type"] = "text/plain; charset=utf-8" }, "This is not part of the page."u8.ToArray());
        return new InProcessAnswer(200, new Dictionary<string, string> { ["Content-Type"] = PageFiles.ContentType(file) }, File.ReadAllBytes(Path.Combine(PageFolder, file)));
    }

    private static void Send(IWKUrlSchemeTask task, NSUrl url, InProcessAnswer answer)
    {
        using var headers = new NSMutableDictionary<NSString, NSString>();
        foreach (var (name, value) in answer.Headers) headers[(NSString)name] = (NSString)value;
        headers[(NSString)"Content-Length"] = (NSString)answer.Body.Length.ToString(CultureInfo.InvariantCulture);
        using var response = new NSHttpUrlResponse(url, answer.Status, "HTTP/1.1", headers);
        task.DidReceiveResponse(response);
        if (answer.Body.Length > 0) task.DidReceiveData(NSData.FromArray(answer.Body));
        task.DidFinish();
    }
}
