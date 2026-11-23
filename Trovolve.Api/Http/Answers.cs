using System.Net;

namespace Trovolve.Api.Http;

/// <summary>The two answers an endpoint gives: what was asked for, as JSON, or why not, in a sentence.</summary>
internal static class Answers
{
    /// <summary>200 with <paramref name="value"/> as JSON.</summary>
    public static async Task<ApiResponse> JsonAsync<T>(ApiRequest req, T value, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(value, ct);
        return resp;
    }

    /// <summary><paramref name="status"/>, saying why in a sentence the page shows as it is.</summary>
    public static async Task<ApiResponse> RefuseAsync(ApiRequest req, HttpStatusCode status, string why, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteStringAsync(why, ct);
        resp.StatusCode = status;
        return resp;
    }
}
