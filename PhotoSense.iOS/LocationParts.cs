using System.Text.Json;
using AuthenticationServices;
using PhotoSense.Domain.Services;
using Security;

namespace PhotoSense.iOS;

/// <summary>
/// The backup locations' keys, passwords and tokens in the iPhone's Keychain, one record each, readable once the iPhone
/// has been unlocked after it started, so that a backup in the background can reach them, and never carried to another
/// device by a backup.
/// </summary>
internal sealed class KeychainLocationSecrets : ILocationSecrets
{
    private const string Service = "com.j2k2ham.photosense.locations";

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ReadAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var found = SecKeyChain.QueryAsRecord(Query(id), out var status);
        IReadOnlyDictionary<string, string> secrets = status == SecStatusCode.Success && found?.ValueData is { } data
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(data.ToArray()) ?? []
            : new Dictionary<string, string>();
        return Task.FromResult(secrets);
    }

    /// <inheritdoc />
    public Task SaveAsync(string id, IReadOnlyDictionary<string, string> secrets, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Replaced whole: a location's secrets go together.
        SecKeyChain.Remove(Query(id));
        var record = Query(id);
        record.ValueData = NSData.FromArray(JsonSerializer.SerializeToUtf8Bytes(secrets));
        record.Accessible = SecAccessible.AfterFirstUnlockThisDeviceOnly;
        var status = SecKeyChain.Add(record);
        if (status != SecStatusCode.Success) throw new IOException($"The iPhone's Keychain would not keep the location's keys ({status}).");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        SecKeyChain.Remove(Query(id));
        return Task.CompletedTask;
    }

    private static SecRecord Query(string id) => new(SecKind.GenericPassword) { Service = Service, Account = id };
}

/// <summary>
/// The system's own sheet for signing in to a cloud service (ASWebAuthenticationSession): the person signs in on the
/// service's page, and PhotoSense sees only where the service sends them back.
/// </summary>
internal sealed class WebSignIn : IWebSignIn
{
    /// <inheritdoc />
    public Task<Uri> SignInAsync(Uri start, string callbackScheme, CancellationToken ct)
    {
        var answer = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            var session = new ASWebAuthenticationSession(new NSUrl(start.AbsoluteUri), callbackScheme, (back, error) =>
            {
                if (back is not null) answer.TrySetResult(new Uri(back.AbsoluteString!));
                // The person closed the sheet: no token of the caller's was cancelled.
                else if (error is { Code: (long)ASWebAuthenticationSessionErrorCode.CanceledLogin }) answer.TrySetCanceled(CancellationToken.None);
                else answer.TrySetException(new IOException($"The sign-in sheet could not be shown: {error?.LocalizedDescription}"));
            })
            {
                PresentationContextProvider = new Anchor(),
            };
            // The sheet goes away when whoever asked stops waiting.
            ct.Register(() => UIApplication.SharedApplication.InvokeOnMainThread(session.Cancel));
            if (!session.Start()) answer.TrySetException(new IOException("The sign-in sheet could not be shown."));
        });
        return answer.Task;
    }

    /// <summary>Shows the sheet over PhotoSense's own window.</summary>
    private sealed class Anchor : NSObject, IASWebAuthenticationPresentationContextProviding
    {
        [Export("presentationAnchorForWebAuthenticationSession:")]
        public UIWindow GetPresentationAnchor(ASWebAuthenticationSession session)
            => UIApplication.SharedApplication.ConnectedScenes.ToArray().OfType<UIWindowScene>().SelectMany(s => s.Windows).FirstOrDefault(w => w.IsKeyWindow)
               ?? new UIWindow();
    }
}
