using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using PhotoSense.Domain.Services;

namespace PhotoSense.Infrastructure.Locations;

/// <summary>
/// PhotoSense's registrations with the cloud services, which signing in needs. Each is empty until PhotoSense is
/// registered with that service; it then cannot be signed in to, and says so.
/// </summary>
public sealed class CloudAppOptions
{
    /// <summary>The client id Google gave PhotoSense's app: "1234-abc.apps.googleusercontent.com".</summary>
    public string GoogleClientId { get; init; } = "";

    /// <summary>The application id Microsoft gave PhotoSense.</summary>
    public string MicrosoftClientId { get; init; } = "";

    /// <summary>The app key Dropbox gave PhotoSense's app folder app.</summary>
    public string DropboxClientId { get; init; } = "";

    /// <summary>The client id Box gave PhotoSense.</summary>
    public string BoxClientId { get; init; } = "";

    /// <summary>The client secret Box gave PhotoSense, which Box asks for even with PKCE.</summary>
    public string BoxClientSecret { get; init; } = "";

    /// <summary>The scheme the services other than Google send the person back to the app by.</summary>
    public string Scheme { get; init; } = "com.j2k2ham.photosense.auth";
}

/// <summary>
/// Signing in to Google Drive, OneDrive, Dropbox and Box with OAuth 2 and PKCE, each asking only for the narrowest
/// access it offers: Google Drive the files PhotoSense makes, OneDrive and Dropbox their app folders, Box a folder of
/// its own. The person signs in in the system's browser; PhotoSense sees only the tokens the service gives it, which it
/// renews here as they run out.
/// </summary>
public sealed class CloudAccounts : ICloudAccounts
{
    /// <summary>The tokens' names among a cloud location's secrets.</summary>
    public const string Access = "access", Refresh = "refresh", Expires = "expires";

    private readonly CloudAppOptions _apps;
    private readonly HttpClient _http;
    private readonly TimeProvider _clock;

    /// <summary>Signs in with PhotoSense's registrations.</summary>
    /// <param name="apps">PhotoSense's registrations with the services.</param>
    /// <param name="http">Sends the requests.</param>
    /// <param name="clock">When tokens run out.</param>
    public CloudAccounts(CloudAppOptions apps, HttpClient http, TimeProvider clock)
    {
        _apps = apps;
        _http = http;
        _clock = clock;
    }

    /// <inheritdoc />
    public bool Available(LocationKind kind) => kind switch
    {
        LocationKind.GoogleDrive => _apps.GoogleClientId.Length > 0,
        LocationKind.OneDrive => _apps.MicrosoftClientId.Length > 0,
        LocationKind.Dropbox => _apps.DropboxClientId.Length > 0,
        LocationKind.Box => _apps.BoxClientId.Length > 0 && _apps.BoxClientSecret.Length > 0,
        _ => false,
    };

    /// <inheritdoc />
    public CloudSignInStart Begin(LocationKind kind)
    {
        var (authorize, _, scope) = Endpoints(kind);
        var verifier = Random();
        var state = Random();
        var challenge = Base64Url(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        var query = new Dictionary<string, string>
        {
            ["client_id"] = ClientId(kind), ["redirect_uri"] = Redirect(kind), ["response_type"] = "code", ["state"] = state,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256",
        };
        if (scope is not null) query["scope"] = scope;
        // Dropbox gives a token that lasts only with this.
        if (kind == LocationKind.Dropbox) query["token_access_type"] = "offline";
        var start = new Uri($"{authorize}?{string.Join('&', query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"))}");
        return new CloudSignInStart(start, Scheme(kind), state, verifier);
    }

    /// <inheritdoc />
    public async Task<CloudAccount> FinishAsync(LocationKind kind, CloudSignInStart begun, Uri callback, CancellationToken ct)
    {
        var said = Query(callback);
        var name = LocationKinds.NameOf(kind);
        if (said.TryGetValue("error", out var error))
            throw new IOException(error == "access_denied" ? $"{name} was not connected: access was not allowed." : $"{name} did not sign in: {said.GetValueOrDefault("error_description", error)}");
        if (said.GetValueOrDefault("state") != begun.State || !said.TryGetValue("code", out var code))
            throw new IOException($"{name} sent back something that was not this sign-in. Try again.");
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = Redirect(kind), ["client_id"] = ClientId(kind), ["code_verifier"] = begun.Verifier,
        };
        if (kind == LocationKind.Box) form["client_secret"] = _apps.BoxClientSecret;
        var secrets = await TokensAsync(kind, form, null, ct);
        return new CloudAccount(await AccountAsync(kind, secrets[Access], ct), secrets);
    }

    /// <summary>Renews a service's tokens with the refresh token it gave; the new ones are kept in place of the old.</summary>
    /// <exception cref="IOException">The service would not renew them: the person must sign in again.</exception>
    public Task<IReadOnlyDictionary<string, string>> RenewAsync(LocationKind kind, IReadOnlyDictionary<string, string> secrets, CancellationToken ct)
    {
        var form = new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = secrets.GetValueOrDefault(Refresh, ""), ["client_id"] = ClientId(kind) };
        if (kind == LocationKind.Box) form["client_secret"] = _apps.BoxClientSecret;
        return TokensAsync(kind, form, secrets.GetValueOrDefault(Refresh), ct);
    }

    /// <summary>Whether a service's access token has run out, or will within a minute.</summary>
    public bool RunningOut(IReadOnlyDictionary<string, string> secrets)
        => !long.TryParse(secrets.GetValueOrDefault(Expires), out var until) || DateTimeOffset.FromUnixTimeSeconds(until) <= _clock.GetUtcNow().AddMinutes(1);

    private async Task<IReadOnlyDictionary<string, string>> TokensAsync(LocationKind kind, Dictionary<string, string> form, string? refresh, CancellationToken ct)
    {
        var (_, token, _) = Endpoints(kind);
        HttpResponseMessage answer;
        try
        {
            answer = await _http.PostAsync(token, new FormUrlEncodedContent(form), ct);
        }
        catch (HttpRequestException ex)
        {
            throw new IOException($"PhotoSense could not reach {LocationKinds.NameOf(kind)}: {ex.Message}", ex);
        }
        using (answer)
        {
            var body = JsonNode.Parse(await answer.Content.ReadAsStringAsync(ct)) as JsonObject;
            if (!answer.IsSuccessStatusCode || body?["access_token"] is not JsonValue access)
                throw new IOException($"{LocationKinds.NameOf(kind)} signed PhotoSense out. In Settings, choose Locations and turn it on again to sign in.");
            var lasts = body["expires_in"] is JsonValue seconds ? seconds.GetValue<long>() : 3600;
            return new Dictionary<string, string>
            {
                [Access] = access.GetValue<string>(),
                // A service may keep the refresh token it gave before.
                [Refresh] = body["refresh_token"] is JsonValue renewed ? renewed.GetValue<string>() : refresh ?? "",
                [Expires] = _clock.GetUtcNow().AddSeconds(lasts).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            };
        }
    }

    // Who signed in, as the person would recognise it.
    private async Task<string> AccountAsync(LocationKind kind, string access, CancellationToken ct)
    {
        using var request = kind switch
        {
            LocationKind.GoogleDrive => new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/about?fields=user(emailAddress)"),
            LocationKind.OneDrive => new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName"),
            LocationKind.Dropbox => new HttpRequestMessage(HttpMethod.Post, "https://api.dropboxapi.com/2/users/get_current_account"),
            _ => new HttpRequestMessage(HttpMethod.Get, "https://api.box.com/2.0/users/me?fields=login"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        try
        {
            using var answer = await _http.SendAsync(request, ct);
            var body = answer.IsSuccessStatusCode ? JsonNode.Parse(await answer.Content.ReadAsStringAsync(ct)) : null;
            var said = kind switch
            {
                LocationKind.GoogleDrive => body?["user"]?["emailAddress"],
                LocationKind.OneDrive => body?["mail"] ?? body?["userPrincipalName"],
                LocationKind.Dropbox => body?["email"],
                _ => body?["login"],
            };
            return said is JsonValue name ? name.GetValue<string>() : LocationKinds.NameOf(kind);
        }
        catch (HttpRequestException)
        {
            // Signed in all the same: the account is named once the service answers.
            return LocationKinds.NameOf(kind);
        }
    }

    private string ClientId(LocationKind kind) => kind switch
    {
        LocationKind.GoogleDrive => _apps.GoogleClientId,
        LocationKind.OneDrive => _apps.MicrosoftClientId,
        LocationKind.Dropbox => _apps.DropboxClientId,
        _ => _apps.BoxClientId,
    };

    // Google sends an installed app back by its client id reversed; the others by PhotoSense's own scheme.
    private string Scheme(LocationKind kind) => kind == LocationKind.GoogleDrive
        ? string.Join('.', _apps.GoogleClientId.Split('.').Reverse())
        : _apps.Scheme;

    private string Redirect(LocationKind kind) => kind == LocationKind.GoogleDrive ? $"{Scheme(kind)}:/oauth2redirect" : $"{Scheme(kind)}:/{LocationKinds.IdOf(kind)}";

    private static (string Authorize, string Token, string? Scope) Endpoints(LocationKind kind) => kind switch
    {
        LocationKind.GoogleDrive => ("https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token", "https://www.googleapis.com/auth/drive.file"),
        LocationKind.OneDrive => ("https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token",
            "Files.ReadWrite.AppFolder offline_access User.Read"),
        // Dropbox's app folder, and Box's folder, are set when the app is registered.
        LocationKind.Dropbox => ("https://www.dropbox.com/oauth2/authorize", "https://api.dropboxapi.com/oauth2/token", null),
        LocationKind.Box => ("https://account.box.com/api/oauth2/authorize", "https://api.box.com/oauth2/token", null),
        _ => throw new ArgumentException($"{LocationKinds.NameOf(kind)} is not signed in to.", nameof(kind)),
    };

    private static string Random() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // A callback's query and fragment, by name, read from the address as it came: an app's own scheme may leave .NET
    // seeing no query in it.
    private static Dictionary<string, string> Query(Uri callback)
    {
        var said = new Dictionary<string, string>(StringComparer.Ordinal);
        var raw = callback.OriginalString;
        var at = raw.IndexOfAny(['?', '#']);
        foreach (var part in (at < 0 ? "" : raw[(at + 1)..]).Split('&', '#', '?').Where(p => p.Length > 0))
        {
            var equals = part.IndexOf('=');
            if (equals > 0) said[Uri.UnescapeDataString(part[..equals])] = Uri.UnescapeDataString(part[(equals + 1)..].Replace('+', ' '));
        }
        return said;
    }
}
