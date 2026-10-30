using System.Collections.Concurrent;
using System.Text;
using Android.Content;
using Android.Content.Res;
using Android.Util;
using Android.Webkit;
using Java.Interop;
using PhotoSense.Api.Hosting;

namespace PhotoSense.AndroidApp;

/// <summary>
/// Hands out the page's own files from the app, and answers its requests below /api with the service's endpoints.
/// The page is shown at <see cref="Origin"/>, an address Android keeps for an app's own files, which no web site has.
/// </summary>
public sealed class PageClient(Context context) : WebViewClient
{
    public const string Host = "appassets.androidplatform.net";
    public const string Origin = "https://" + Host + "/";

    private readonly Lazy<HashSet<string>> _pageFiles = new(() => Listed(context.Assets!, "wwwroot"));

    // Started with the self-check, the app makes it once the page is in.
    public override void OnPageFinished(WebView? view, string? url)
    {
        base.OnPageFinished(view, url);
        if (SelfCheck.Starting()) view?.EvaluateJavascript(SelfCheck.Script, null);
    }

    public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
    {
        if (request?.Url is not { } address || address.Host != Host) return null;
        var url = new Uri(address.ToString()!);
        var started = DateTime.UtcNow;
        InProcessAnswer answer;
        try
        {
            // Asked off the main thread, which waits for the answer here as a web server's caller would.
            answer = url.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal) ? Ask(request, url) : PageFile(url);
        }
        // The page is answered all the same, with what went wrong, or it would wait for an answer for ever.
        catch (Exception e)
        {
            Log.Warn("PhotoSense", $"{request.Method} {url.PathAndQuery} failed: {e}");
            answer = new InProcessAnswer(500, new Dictionary<string, string> { ["Content-Type"] = "text/plain; charset=utf-8" }, Encoding.UTF8.GetBytes(e.Message));
        }
        Log.Info("PhotoSense", $"{request.Method} {url.PathAndQuery} {answer.Status} in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms");
        return Response(answer);
    }

    // A link to anywhere else opens in the phone's browser or mail app.
    public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
    {
        if (request?.Url is not { } address || address.Host == Host) return false;
        context.StartActivity(new Intent(Intent.ActionView, address).AddFlags(ActivityFlags.NewTask));
        return true;
    }

    private static InProcessAnswer Ask(IWebResourceRequest request, Uri url)
    {
        var headers = request.RequestHeaders?.Select(h => new KeyValuePair<string, string>(h.Key, h.Value)).ToList() ?? [];
        var body = BodyBridge.Take(headers.FirstOrDefault(h => h.Key.Equals(BodyBridge.Header, StringComparison.OrdinalIgnoreCase)).Value);
        PhoneApp.Ready.GetAwaiter().GetResult();
        return PhoneApp.Router.AnswerAsync(request.Method ?? "GET", url, headers, body).GetAwaiter().GetResult();
    }

    private InProcessAnswer PageFile(Uri url)
    {
        if (PageFiles.For(url.AbsolutePath, _pageFiles.Value.Contains) is not { } file)
            return new InProcessAnswer(404, new Dictionary<string, string> { ["Content-Type"] = "text/plain; charset=utf-8" }, "This is not part of the page."u8.ToArray());
        using var asset = context.Assets!.Open("wwwroot/" + file);
        var bytes = new MemoryStream();
        asset.CopyTo(bytes);
        return new InProcessAnswer(200, new Dictionary<string, string> { ["Content-Type"] = PageFiles.ContentType(file) }, bytes.ToArray());
    }

    private static WebResourceResponse Response(InProcessAnswer answer)
    {
        var type = answer.Headers.GetValueOrDefault("Content-Type", "application/octet-stream");
        var parts = type.Split(';', 2, StringSplitOptions.TrimEntries);
        var charset = parts.Length > 1 && parts[1].StartsWith("charset=", StringComparison.OrdinalIgnoreCase) ? parts[1]["charset=".Length..] : null;
        var headers = answer.Headers.Where(h => !h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)).ToDictionary(h => h.Key, h => h.Value);
        // Android takes only answers that say they are fine or that something went wrong; a redirect is none of PhotoSense's.
        var status = answer.Status is >= 200 and < 300 or >= 400 and < 600 ? answer.Status : 500;
        return new WebResourceResponse(parts[0], charset, status, status < 400 ? "OK" : "Error", headers, new MemoryStream(answer.Body));
    }

    // Every file of the page, by its path inside the page's folder.
    private static HashSet<string> Listed(AssetManager assets, string folder)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in assets.List(folder) ?? [])
        {
            var path = $"{folder}/{name}";
            var inside = Listed(assets, path);
            if (inside.Count == 0) files.Add(path["wwwroot/".Length..]);
            else files.UnionWith(inside);
        }
        return files;
    }
}

/// <summary>
/// Android's web view hands the app no request's body, so the page hands each one over here first, and the request
/// carries the number it was given (<see cref="Header"/>).
/// </summary>
public sealed class BodyBridge : Java.Lang.Object
{
    public const string Name = "PhotoSenseAndroid";
    public const string Header = "x-photosense-body";
    private static readonly ConcurrentDictionary<string, string> Bodies = new();

    [JavascriptInterface]
    [Export("keep")]
    public string Keep(string body)
    {
        var id = Guid.NewGuid().ToString("N");
        Bodies[id] = body;
        return id;
    }

    /// <summary>The self-check's report, written after every step.</summary>
    [JavascriptInterface]
    [Export("report")]
    public void Report(string report) => SelfCheck.Write(report);

    public static byte[]? Take(string? id) => id is not null && Bodies.TryRemove(id, out var body) ? Encoding.UTF8.GetBytes(body) : null;
}
