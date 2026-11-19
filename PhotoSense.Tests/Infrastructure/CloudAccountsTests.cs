using System.Globalization;
using System.Net;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Locations;

namespace PhotoSense.Tests.Infrastructure;

/// <summary>
/// Signing in to Google Drive, OneDrive and Dropbox: each in the system's browser with PKCE and the narrowest
/// access, the answer tied to its sign-in, the tokens exchanged and renewed, and the account named.
/// </summary>
public class CloudAccountsTests
{
    private static readonly CloudAppOptions Registered = new()
    {
        GoogleClientId = "1234-abc.apps.googleusercontent.com", MicrosoftClientId = "ms-app", DropboxClientId = "db-key",
    };

    private static (CloudAccounts Accounts, FakeNetwork Network, TestClock Clock) Make(CloudAppOptions? apps = null)
    {
        var network = new FakeNetwork();
        var clock = new TestClock();
        return (new CloudAccounts(apps ?? Registered, new HttpClient(network), clock), network, clock);
    }

    private static Dictionary<string, string> QueryOf(Uri uri)
        => uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    [Test]
    public async Task Each_Service_Can_Be_Signed_In_To_Only_Once_PhotoSense_Is_Registered_With_It()
    {
        var (accounts, _, _) = Make();
        await Assert.That(LocationKinds.Cloud.All(accounts.Available)).IsTrue();
        await Assert.That(accounts.Available(LocationKind.AmazonS3)).IsFalse();
        var (none, _, _) = Make(new CloudAppOptions());
        await Assert.That(LocationKinds.Cloud.Any(none.Available)).IsFalse();
    }

    [Test]
    public async Task Signing_In_Asks_Each_Service_For_The_Narrowest_Access_With_PKCE_And_Sends_The_Person_Back_To_The_App()
    {
        var (accounts, _, _) = Make();
        var google = accounts.Begin(LocationKind.GoogleDrive);
        var asked = QueryOf(google.Start);
        await Assert.That(google.Start.GetLeftPart(UriPartial.Path)).IsEqualTo("https://accounts.google.com/o/oauth2/v2/auth");
        await Assert.That((asked["client_id"], asked["scope"], asked["code_challenge_method"], asked["state"])).IsEqualTo((Registered.GoogleClientId, "https://www.googleapis.com/auth/drive.file", "S256", google.State));
        await Assert.That(google.CallbackScheme).IsEqualTo("com.googleusercontent.apps.1234-abc");
        await Assert.That(asked["redirect_uri"]).IsEqualTo("com.googleusercontent.apps.1234-abc:/oauth2redirect");
        var challenge = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(google.Verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Assert.That(asked["code_challenge"]).IsEqualTo(challenge);

        var microsoft = QueryOf(accounts.Begin(LocationKind.OneDrive).Start);
        await Assert.That((microsoft["scope"], microsoft["redirect_uri"])).IsEqualTo(("Files.ReadWrite.AppFolder offline_access User.Read", "com.j2k2ham.photosense.auth:/onedrive"));
        var dropbox = QueryOf(accounts.Begin(LocationKind.Dropbox).Start);
        await Assert.That((dropbox.ContainsKey("scope"), dropbox["token_access_type"])).IsEqualTo((false, "offline"));
        await Assert.That(() => accounts.Begin(LocationKind.Sftp)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Finishing_Exchanges_The_Code_For_Tokens_And_Names_The_Account(CancellationToken ct)
    {
        var (accounts, network, clock) = Make();
        network.On("https://oauth2.googleapis.com/token", """{"access_token":"a1","refresh_token":"r1","expires_in":600}""")
            .On("https://www.googleapis.com/drive/v3/about", """{"user":{"emailAddress":"alex.m@gmail.com"}}""")
            .On("https://login.microsoftonline.com/common/oauth2/v2.0/token", """{"access_token":"a2"}""")
            .On("https://graph.microsoft.com/v1.0/me", """{"mail":null,"userPrincipalName":"alex.m@outlook.com"}""")
            .On("https://api.dropboxapi.com/oauth2/token", """{"access_token":"a3","refresh_token":"r3"}""")
            .On("https://api.dropboxapi.com/2/users/get_current_account", """{"email":"alex@dropbox.example"}""");
        async Task<CloudAccount> FinishAsync(LocationKind kind, string answer)
        {
            var begun = accounts.Begin(kind);
            return await accounts.FinishAsync(kind, begun, new Uri($"{begun.CallbackScheme}:/x?{answer.Replace("STATE", begun.State)}"), ct);
        }
        var google = await FinishAsync(LocationKind.GoogleDrive, "code=c%2B1&state=STATE");
        await Assert.That((google.Name, google.Secrets[CloudAccounts.Access], google.Secrets[CloudAccounts.Refresh])).IsEqualTo(("alex.m@gmail.com", "a1", "r1"));
        await Assert.That(google.Secrets[CloudAccounts.Expires]).IsEqualTo(clock.GetUtcNow().AddSeconds(600).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        var exchange = network.Asked.First(h => h.Url.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal));
        await Assert.That(exchange.Body).Contains("code=c%2B1").And.Contains("code_verifier=").And.Contains("grant_type=authorization_code").And.DoesNotContain("client_secret");
        await Assert.That(network.Asked.Last(h => h.Url.Contains("about")).Headers["Authorization"]).IsEqualTo("Bearer a1");

        // A service that keeps no refresh token gives none, and one that says nothing of when it runs out lasts an hour.
        var microsoft = await FinishAsync(LocationKind.OneDrive, "state=STATE&code=c");
        await Assert.That((microsoft.Name, microsoft.Secrets[CloudAccounts.Refresh])).IsEqualTo(("alex.m@outlook.com", ""));
        await Assert.That(microsoft.Secrets[CloudAccounts.Expires]).IsEqualTo(clock.GetUtcNow().AddHours(1).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        // The answer in the fragment, as some services send it.
        await Assert.That((await FinishAsync(LocationKind.Dropbox, "#code=c&state=STATE")).Name).IsEqualTo("alex@dropbox.example");
    }

    [Test]
    public async Task An_Account_That_Cannot_Be_Named_Is_Called_By_The_Service(CancellationToken ct)
    {
        var (accounts, network, _) = Make();
        network.On("https://oauth2.googleapis.com/token", """{"access_token":"a1"}""").On("https://www.googleapis.com/drive/v3/about", "{}", HttpStatusCode.Forbidden);
        var begun = accounts.Begin(LocationKind.GoogleDrive);
        await Assert.That((await accounts.FinishAsync(LocationKind.GoogleDrive, begun, new Uri($"x:/?code=c&state={begun.State}"), ct)).Name).IsEqualTo("Google Drive");
        network.On("https://www.googleapis.com/drive/v3/about", "{}");
        await Assert.That((await accounts.FinishAsync(LocationKind.GoogleDrive, begun, new Uri($"x:/?code=c&state={begun.State}"), ct)).Name).IsEqualTo("Google Drive");
        network.On(null, "https://www.googleapis.com/drive/v3/about", _ => throw new HttpRequestException("Down."));
        await Assert.That((await accounts.FinishAsync(LocationKind.GoogleDrive, begun, new Uri($"x:/?code=c&state={begun.State}"), ct)).Name).IsEqualTo("Google Drive");
    }

    [Test]
    public async Task A_Refusal_A_Stranger_s_Answer_Or_No_Tokens_Signs_In_To_Nothing_And_Says_Why(CancellationToken ct)
    {
        var (accounts, network, _) = Make();
        var begun = accounts.Begin(LocationKind.Dropbox);
        async Task<string> FailsAsync(string answer)
            => (await Assert.ThrowsAsync<IOException>(() => accounts.FinishAsync(LocationKind.Dropbox, begun, new Uri($"x:/?{answer}"), ct)))!.Message;
        await Assert.That(await FailsAsync("error=access_denied")).IsEqualTo("Dropbox was not connected: access was not allowed.");
        await Assert.That(await FailsAsync("error=server_error&error_description=Try+later")).IsEqualTo("Dropbox did not sign in: Try later");
        await Assert.That(await FailsAsync("error=server_error")).IsEqualTo("Dropbox did not sign in: server_error");
        await Assert.That(await FailsAsync("code=c&state=someone-else")).IsEqualTo("Dropbox sent back something that was not this sign-in. Try again.");
        await Assert.That(await FailsAsync($"state={begun.State}&=x&loose")).IsEqualTo("Dropbox sent back something that was not this sign-in. Try again.");
        network.On("https://api.dropboxapi.com/oauth2/token", """{"error":"invalid_grant"}""", HttpStatusCode.BadRequest);
        await Assert.That(await FailsAsync($"code=c&state={begun.State}")).StartsWith("Dropbox signed PhotoSense out.");
        network.On("https://api.dropboxapi.com/oauth2/token", "{}");
        await Assert.That(await FailsAsync($"code=c&state={begun.State}")).StartsWith("Dropbox signed PhotoSense out.");
        network.On("https://api.dropboxapi.com/oauth2/token", "[]");
        await Assert.That(await FailsAsync($"code=c&state={begun.State}")).StartsWith("Dropbox signed PhotoSense out.");
        // No answer in the address at all.
        await Assert.That((await Assert.ThrowsAsync<IOException>(() => accounts.FinishAsync(LocationKind.Dropbox, begun, new Uri("x:/plain"), ct)))!.Message).StartsWith("Dropbox sent back");
        network.Down = true;
        await Assert.That(await FailsAsync($"code=c&state={begun.State}")).StartsWith("PhotoSense could not reach Dropbox:");
    }

    [Test]
    public async Task Each_Service_That_Will_Not_Say_Who_Signed_In_Is_Named_Itself_And_OneDrive_Says_The_Mail_First(CancellationToken ct)
    {
        foreach (var (kind, token, me, name) in new[]
        {
            (LocationKind.OneDrive, "https://login.microsoftonline.com/common/oauth2/v2.0/token", "https://graph.microsoft.com/v1.0/me", "OneDrive"),
            (LocationKind.Dropbox, "https://api.dropboxapi.com/oauth2/token", "https://api.dropboxapi.com/2/users/get_current_account", "Dropbox"),
        })
        {
            var (accounts, network, _) = Make();
            network.On(token, """{"access_token":"a"}""").On(me, "{}", HttpStatusCode.Forbidden);
            var begun = accounts.Begin(kind);
            await Assert.That((await accounts.FinishAsync(kind, begun, new Uri($"x:/y?code=c&state={begun.State}"), ct)).Name).IsEqualTo(name);
        }
        var (graph, answers, _) = Make();
        answers.On("https://login.microsoftonline.com/common/oauth2/v2.0/token", """{"access_token":"a"}""").On("https://graph.microsoft.com/v1.0/me", """{"mail":"alex@outlook.com","userPrincipalName":"other"}""");
        var started = graph.Begin(LocationKind.OneDrive);
        await Assert.That((await graph.FinishAsync(LocationKind.OneDrive, started, new Uri($"x:/y?code=c&state={started.State}"), ct)).Name).IsEqualTo("alex@outlook.com");
    }

    [Test]
    public async Task Tokens_Are_Renewed_With_The_Refresh_Token_And_Run_Out_A_Minute_Early(CancellationToken ct)
    {
        var (accounts, network, clock) = Make();
        network.On("https://api.dropboxapi.com/oauth2/token", """{"access_token":"new","expires_in":60}""");
        var renewed = await accounts.RenewAsync(LocationKind.Dropbox, new Dictionary<string, string> { [CloudAccounts.Refresh] = "r" }, ct);
        await Assert.That((renewed[CloudAccounts.Access], renewed[CloudAccounts.Refresh])).IsEqualTo(("new", "r"));
        await Assert.That(network.Asked.Single().Body).Contains("grant_type=refresh_token").And.Contains("refresh_token=r").And.Contains("client_id=db-key");
        network.On("https://oauth2.googleapis.com/token", """{"access_token":"g"}""");
        await Assert.That((await accounts.RenewAsync(LocationKind.GoogleDrive, new Dictionary<string, string>(), ct))[CloudAccounts.Refresh]).IsEqualTo("");

        await Assert.That(accounts.RunningOut(new Dictionary<string, string>())).IsTrue();
        await Assert.That(accounts.RunningOut(renewed)).IsTrue();
        var later = new Dictionary<string, string> { [CloudAccounts.Expires] = clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) };
        await Assert.That(accounts.RunningOut(later)).IsFalse();
    }
}
