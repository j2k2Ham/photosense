using System.Net;
using PhotoSense.Api.Http;

namespace PhotoSense.Tests.Endpoints;

/// <summary>Each program answers its own half of sending photos, and only to PhotoSense's own page.</summary>
public sealed class TransferEndpointsTests
{
    // A program that has neither half, as no host registers one without the other's part.
    private static readonly TransferApi Neither = new(new TestApi());

    /// <summary>Each address of each half, asked of a program that has neither, with what it says.</summary>
    public static IEnumerable<Func<(Func<ApiRequest, CancellationToken, Task<ApiResponse>> Ask, ApiRequest Request, string Why)>> AskedOfTheWrongProgram()
    {
        yield return () => (Neither.StartSendingAsync, Http.Post("send/start", "{\"files\": [\"a\"]}").FromClient(), "Only a phone sends photos.");
        yield return () => (Neither.SendingAsync, Http.Get("send/state"), "Only a phone sends photos.");
        yield return () => (Neither.ConfirmSendingAsync, Http.Post("send/confirm").FromClient(), "Only a phone sends photos.");
        yield return () => (Neither.StopSendingAsync, Http.Post("send/stop").FromClient(), "Only a phone sends photos.");
        yield return () => (Neither.DeleteSentAsync, Http.Post("send/delete-sent").FromClient(), "Only a phone sends photos.");
        yield return () => (Neither.PhonesAsync, Http.Get("phones").FromClient(), "Only a computer receives photos.");
        yield return () => (Neither.StartReceivingAsync, Http.Post("receive/start").FromClient(), "Only a computer receives photos.");
        yield return () => (Neither.ReceivingAsync, Http.Get("receive/state"), "Only a computer receives photos.");
        yield return () => (Neither.ConfirmReceivingAsync, Http.Post("receive/confirm").FromClient(), "Only a computer receives photos.");
        yield return () => (Neither.StopReceivingAsync, Http.Post("receive/stop").FromClient(), "Only a computer receives photos.");
    }

    [Test]
    [MethodDataSource(nameof(AskedOfTheWrongProgram))]
    public async Task Asked_For_The_Other_Half_It_Says_It_Is_Not_The_One(Func<ApiRequest, CancellationToken, Task<ApiResponse>> ask, ApiRequest request, string why, CancellationToken ct)
    {
        var answer = await ask(request, ct);
        await Assert.That((answer.StatusCode, answer.Text())).IsEqualTo((HttpStatusCode.NotFound, why));
    }

    /// <summary>Each address of either half that changes something or names the phones about, with the request it takes.</summary>
    public static IEnumerable<Func<(Func<ApiRequest, CancellationToken, Task<ApiResponse>> Ask, ApiRequest Request)>> Guarded()
    {
        yield return () => (Neither.StartSendingAsync, Http.Post("send/start"));
        yield return () => (Neither.ConfirmSendingAsync, Http.Post("send/confirm"));
        yield return () => (Neither.StopSendingAsync, Http.Post("send/stop"));
        yield return () => (Neither.DeleteSentAsync, Http.Post("send/delete-sent"));
        yield return () => (Neither.PhonesAsync, Http.Get("phones"));
        yield return () => (Neither.StartReceivingAsync, Http.Post("receive/start"));
        yield return () => (Neither.ConfirmReceivingAsync, Http.Post("receive/confirm"));
        yield return () => (Neither.StopReceivingAsync, Http.Post("receive/stop"));
    }

    [Test]
    [MethodDataSource(nameof(Guarded))]
    public async Task Another_Page_Is_Refused_Before_Anything_Else(Func<ApiRequest, CancellationToken, Task<ApiResponse>> ask, ApiRequest request, CancellationToken ct)
        => await Assert.That((await ask(request, ct)).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
}
