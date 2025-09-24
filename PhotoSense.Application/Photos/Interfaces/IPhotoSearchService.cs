using PhotoSense.Core.Domain.Entities;

namespace PhotoSense.Application.Photos.Interfaces;

public interface IPhotoSearchService
{
    Task<PagedResult<Photo>> SearchAsync(PhotoSearchQuery query, CancellationToken ct = default);
}

public sealed record PhotoSearchQuery(int Page = 1, int PageSize = 50, string? Text = null, string? Hash = null, string? PerceptualHash = null, string? Set = null);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
