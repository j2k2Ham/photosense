using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PhotoSense.Api;
using PhotoSense.Api.Http;
using PhotoSense.Tests.Server;

namespace PhotoSense.Tests.Endpoints;

public sealed class ApiRouterTests
{
    // A router whose every route answers with its own name and the values it was given.
    private static ApiRouter Router(params (string Method, string Template)[] routes)
    {
        var router = new ApiRouter();
        foreach (var (method, template) in routes)
        {
            Task<ApiResponse> Answer(ApiRequest request, IReadOnlyDictionary<string, string> values)
            {
                var response = request.CreateResponse();
                response.Headers.Add("X-Route", $"{method} {template}");
                response.Headers.Add("X-Values", string.Join(";", values.OrderBy(v => v.Key).Select(v => $"{v.Key}={v.Value}")) is { Length: > 0 } said ? said : "-");
                return Task.FromResult(response);
            }
            router = method switch { "GET" => router.Get(template, Answer), "POST" => router.Post(template, Answer), _ => router.Delete(template, Answer) };
        }
        return router;
    }

    private static async Task<string?> Route(ApiRouter router, string method, string path)
    {
        var answering = router.Answer(new ApiRequest(method, new Uri("http://localhost:7071" + path)));
        if (answering is null) return null;
        var answer = await answering;
        return $"{answer.Headers.GetValues("X-Route").Single()} | {answer.Headers.GetValues("X-Values").Single()}";
    }

    [Test]
    public async Task An_Address_Goes_To_The_First_Route_That_Fits_It()
    {
        var router = Router(("GET", "scan/logs"), ("GET", "scan/progress/latest"), ("GET", "scan/progress/{instanceId}"),
            ("GET", "photos/{id:guid}"), ("DELETE", "photos/{id:guid}"), ("POST", "organize/undo/{batchId}"));
        var id = Guid.NewGuid();

        await Assert.That(await Route(router, "GET", "/api/scan/logs")).IsEqualTo("GET scan/logs | -");
        await Assert.That(await Route(router, "GET", "/api/scan/logs/")).IsEqualTo("GET scan/logs | -");
        await Assert.That(await Route(router, "GET", "/api/scan/progress/latest")).IsEqualTo("GET scan/progress/latest | -");
        await Assert.That(await Route(router, "GET", "/api/Scan/Progress/abc")).IsEqualTo("GET scan/progress/{instanceId} | instanceId=abc");
        await Assert.That(await Route(router, "get", $"/api/photos/{id}")).IsEqualTo($"GET photos/{{id:guid}} | id={id}");
        await Assert.That(await Route(router, "DELETE", $"/api/photos/{id}")).IsEqualTo($"DELETE photos/{{id:guid}} | id={id}");
        // A value is read as it was meant, before it was written into the address.
        await Assert.That(await Route(router, "POST", "/api/organize/undo/b%201%2F2")).IsEqualTo("POST organize/undo/{batchId} | batchId=b 1/2");
    }

    [Test]
    [Arguments("GET", "/api/photos/not-a-guid")]          // not the kind of value the route takes
    [Arguments("POST", "/api/scan/logs")]                 // nothing answers that method there
    [Arguments("GET", "/api/organize/undo")]              // a value that may not be left out
    [Arguments("GET", "/api/organize/files//thumbnail")]  // nor be empty
    [Arguments("GET", "/api/scan/logs/abc")]              // more than the route there has
    [Arguments("GET", "/api/nothing")]
    [Arguments("GET", "/elsewhere/scan/logs")]            // not below /api at all
    [Arguments("GET", "/api")]
    public async Task An_Address_No_Route_Fits_Is_Answered_By_None(string method, string path)
    {
        var router = Router(("GET", "scan/logs"), ("GET", "photos/{id:guid}"), ("POST", "organize/undo/{batchId}"),
            ("GET", "organize/files/{id}/thumbnail"));
        await Assert.That(await Route(router, method, path)).IsNull();
    }

    [Test]
    public async Task An_Answer_Says_What_It_Holds_Unless_Told_Otherwise()
    {
        var text = new ApiResponse();
        await text.WriteStringAsync("Folder not found");
        var json = new ApiResponse();
        await json.WriteAsJsonAsync(new List<object> { new { Page = 1 }, new { instanceId = "a" } });
        var stream = new ApiResponse();
        stream.Headers.Add("Content-Type", "text/event-stream");
        await stream.WriteStringAsync("event: end\n");
        await stream.WriteAsJsonAsync(1);

        await Assert.That((text.Headers.GetValues("Content-Type").Single(), Encoding.UTF8.GetString(((MemoryStream)text.Body).ToArray()))).IsEqualTo(("text/plain; charset=utf-8", "Folder not found"));
        // Each object keeps its own fields, under the names they were given.
        await Assert.That((json.Headers.GetValues("Content-Type").Single(), Encoding.UTF8.GetString(((MemoryStream)json.Body).ToArray()))).IsEqualTo(("application/json; charset=utf-8", """[{"Page":1},{"instanceId":"a"}]"""));
        await Assert.That(stream.Headers.GetValues("Content-Type").Single()).IsEqualTo("text/event-stream");
        // A request made with no body has an empty one.
        await Assert.That(await new StreamReader(new ApiRequest("GET", new Uri("http://localhost/api/health")).Body).ReadToEndAsync()).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Every_Address_Of_The_Service_Reaches_Its_Endpoint()
    {
        using var f = new ServerFixture();
        var router = f.Services.GetRequiredService<PhotoSenseRoutes>().Router;
        var id = Guid.NewGuid();
        // Asked as another web page would ask: what shows or changes someone's files is refused, the rest finds nothing.
        (string Method, string Path, HttpStatusCode Expected)[] addresses =
        [
            ("GET", "health", HttpStatusCode.OK), ("GET", "folders", HttpStatusCode.Unauthorized), ("GET", $"photos/{id}/thumbnail", HttpStatusCode.NotFound),
            ("GET", $"photos/{id}/image", HttpStatusCode.NotFound), ("GET", $"photos/{id}/video", HttpStatusCode.NotFound), ("POST", $"photos/{id}/open", HttpStatusCode.Unauthorized),
            ("DELETE", $"photos/{id}", HttpStatusCode.Unauthorized), ("POST", $"photos/{id}/keep", HttpStatusCode.Unauthorized),
            ("POST", "photos/bulk/remove-duplicates", HttpStatusCode.Unauthorized), ("GET", "scan/status", HttpStatusCode.OK), ("GET", "scan/groups", HttpStatusCode.Unauthorized),
            ("POST", "scan/start", HttpStatusCode.Unauthorized), ("POST", "scan/reset", HttpStatusCode.Unauthorized), ("GET", "scan/progress/scan-1", HttpStatusCode.NotFound),
            ("GET", "scan/logs", HttpStatusCode.Unauthorized),
            ("GET", "organize/files", HttpStatusCode.Unauthorized), ("GET", "organize/progress", HttpStatusCode.Unauthorized), ("POST", "organize/plan", HttpStatusCode.Unauthorized),
            ("POST", "organize/apply", HttpStatusCode.Unauthorized), ("POST", "organize/remove", HttpStatusCode.Unauthorized), ("POST", "organize/undo/b1", HttpStatusCode.Unauthorized),
            ("GET", "organize/batches", HttpStatusCode.Unauthorized), ("GET", "organize/files/f1/thumbnail", HttpStatusCode.NotFound), ("GET", "organize/files/f1/image", HttpStatusCode.NotFound),
            ("POST", "organize/files/f1/open", HttpStatusCode.Unauthorized), ("GET", "removed", HttpStatusCode.Unauthorized), ("POST", "removed/erase", HttpStatusCode.Unauthorized),
        ];

        var answered = new List<string>();
        foreach (var (method, path, expected) in addresses)
        {
            var answer = await router.Answer(new ApiRequest(method, new Uri($"http://localhost:7071/api/{path}")))!;
            if (answer.StatusCode != expected) answered.Add($"{method} {path}: {answer.StatusCode}");
        }
        await Assert.That(answered).IsEmpty();
    }
}
