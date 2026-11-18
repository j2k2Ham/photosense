using System.Net;
using System.Text.Json;
using PhotoSense.Api.Http;
using PhotoSense.Application.Organizing;
using PhotoSense.Contracts.Cleaning;
using PhotoSense.Domain.Services;

namespace PhotoSense.Api.Endpoints;

/// <summary>Year in review, and the phone's share sheet for the cards: only a phone has them.</summary>
internal sealed class YearEndpoints
{
    /// <summary>The largest picture handed to the share sheet: a card is far smaller.</summary>
    public const int LargestImage = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly byte[] PngStart = [0x89, (byte)'P', (byte)'N', (byte)'G'];
    private readonly YearInReview? _year;
    private readonly IShareSheet? _share;

    /// <summary>Year in review and sharing, where this program has them.</summary>
    /// <param name="year">Year in review; null on a computer.</param>
    /// <param name="share">The phone's share sheet; null on a computer.</param>
    public YearEndpoints(YearInReview? year, IShareSheet? share)
    {
        _year = year;
        _share = share;
    }

    /// <summary>GET /api/year: the year due now (null the rest of the year), and that year in photos.</summary>
    public async Task<ApiResponse> YearAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _year, "Only a phone has Year in review.", ct) is { } refused) return await refused;
        if (_year!.Due() is not { } due) return await Answers.JsonAsync(req, new YearReviewDto(), ct);
        var r = await _year.ReviewAsync(due, ct);
        return await Answers.JsonAsync(req, new YearReviewDto
        {
            Year = r.Year, Photos = r.Photos, Videos = r.Videos, Trips = r.Trips, TopTrips = [.. r.TopTrips.Select(t => new YearTripDto { Name = t.Name, Month = t.Month, Photos = t.Photos })], Months = r.Months, BusiestMonth = r.BusiestMonth,
            BusiestPlace = r.BusiestPlace is { } busiest ? Dto(busiest) : null, Freed = r.Freed, Removed = r.Removed, LongestStreak = r.LongestStreak, Sessions = r.Sessions,
        }, ct);
    }

    /// <summary>POST /api/share: hands a card, a PNG, to the phone's share sheet. 400 for anything but a PNG of a card's size.</summary>
    public async Task<ApiResponse> ShareAsync(ApiRequest req, CancellationToken ct)
    {
        if (Refused(req, _share, "Only a phone shares cards.", ct) is { } refused) return await refused;
        var asked = await req.ReadJsonAsync<ShareRequest>(Json, ct);
        if (Png(asked?.Image) is not { } png) return await Answers.RefuseAsync(req, HttpStatusCode.BadRequest, "Send the card as a PNG.", ct);
        try
        {
            await _share!.ShareImageAsync(png, string.IsNullOrWhiteSpace(asked!.Name) ? "PhotoSense.png" : Path.GetFileName(asked.Name), ct);
            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (IOException ex) { return await Answers.RefuseAsync(req, HttpStatusCode.Conflict, ex.Message, ct); }
    }

    // The picture, when it is a PNG no larger than a card.
    private static byte[]? Png(string? image)
    {
        if (string.IsNullOrEmpty(image) || image.Length > LargestImage / 3 * 4 + 4) return null;
        var bytes = new byte[image.Length];
        return Convert.TryFromBase64String(image, bytes, out var length) && bytes.AsSpan(0, length).StartsWith(PngStart) ? bytes[..length] : null;
    }

    private static YearPlaceDto Dto(YearPlace place) => new() { Town = place.Town, Photos = place.Photos };

    private static Task<ApiResponse>? Refused(ApiRequest req, object? feature, string onlyAPhone, CancellationToken ct)
    {
        if (!RequestGuard.Allows(req.Headers)) return Task.FromResult(req.CreateResponse(HttpStatusCode.Unauthorized));
        return feature is null ? Answers.RefuseAsync(req, HttpStatusCode.NotFound, onlyAPhone, ct) : null;
    }
}
