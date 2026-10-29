using System.Net;
using PhotoSense.Api.Endpoints;
using PhotoSense.Api.Http;

namespace PhotoSense.Tests.Endpoints;

/// <summary>Each program answers its own half of sending photos, and only to PhotoSense's own page.</summary>
public sealed class TransferEndpointsTests
{
    private static readonly TransferEndpoints Neither = new();

    public static IEnumerable<Func<(Func<ApiRequest, Task<ApiResponse>> Ask, ApiRequest Request, string Why)>> AskedOfTheWrongProgram()
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
    public async Task Asked_For_The_Other_Half_It_Says_It_Is_Not_The_One(Func<ApiRequest, Task<ApiResponse>> ask, ApiRequest request, string why)
    {
        var answer = await ask(request);
        await Assert.That((answer.StatusCode, answer.Text())).IsEqualTo((HttpStatusCode.NotFound, why));
    }

    public static IEnumerable<Func<(Func<ApiRequest, Task<ApiResponse>> Ask, string Path)>> Changes()
    {
        yield return () => (Neither.StartSendingAsync, "send/start");
        yield return () => (Neither.ConfirmSendingAsync, "send/confirm");
        yield return () => (Neither.StopSendingAsync, "send/stop");
        yield return () => (Neither.DeleteSentAsync, "send/delete-sent");
        yield return () => (Neither.PhonesAsync, "phones");
        yield return () => (Neither.StartReceivingAsync, "receive/start");
        yield return () => (Neither.ConfirmReceivingAsync, "receive/confirm");
        yield return () => (Neither.StopReceivingAsync, "receive/stop");
    }

    [Test]
    [MethodDataSource(nameof(Changes))]
    public async Task Another_Page_Is_Refused_Before_Anything_Else(Func<ApiRequest, Task<ApiResponse>> ask, string path)
        => await Assert.That((await ask(Http.Post(path))).StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
}
