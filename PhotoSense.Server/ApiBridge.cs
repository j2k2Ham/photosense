using Microsoft.AspNetCore.Http.Extensions;
using PhotoSense.Api;
using PhotoSense.Api.Http;

namespace PhotoSense.Server;

/// <summary>Hands a request below /api to the service's addresses, and sends back what they answer.</summary>
internal static class ApiBridge
{
    /// <summary>Answers one request below /api.</summary>
    /// <param name="context">The request, and where its answer goes.</param>
    /// <param name="ct">The request's own token: it stops the answering when whoever asked goes away.</param>
    public static async Task HandleAsync(HttpContext context, CancellationToken ct)
    {
        var request = context.Request;
        var asked = new ApiRequest(request.Method, new Uri(request.GetEncodedUrl()), request.Body, ct);
        foreach (var (name, values) in request.Headers) asked.Headers.TryAddWithoutValidation(name, (IEnumerable<string?>)values);

        if (context.RequestServices.GetRequiredService<PhotoSenseRoutes>().Router.Answer(asked) is not { } answering)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var answer = await answering;
        var response = context.Response;
        response.StatusCode = (int)answer.StatusCode;
        foreach (var (name, values) in answer.Headers) response.Headers[name] = values.ToArray();
        // An answer with nothing in it is sent with no length at all, as one that may carry none must be.
        if (answer.Body.Length == 0) return;
        response.ContentLength = answer.Body.Length;
        answer.Body.Position = 0;
        await answer.Body.CopyToAsync(response.Body, ct);
    }
}
