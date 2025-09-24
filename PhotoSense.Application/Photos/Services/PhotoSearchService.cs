using PhotoSense.Application.Photos.Interfaces;
using PhotoSense.Core.Domain.Entities;
using PhotoSense.Core.Domain.Repositories;

namespace PhotoSense.Application.Photos.Services;

public class PhotoSearchService : IPhotoSearchService
{
    private readonly IPhotoRepository _repo;
    public PhotoSearchService(IPhotoRepository repo) => _repo = repo;

    public async Task<PagedResult<Photo>> SearchAsync(PhotoSearchQuery query, CancellationToken ct = default)
    {
        var all = await _repo.GetAllAsync(ct);
        IEnumerable<Photo> filtered = all;
        if (!string.IsNullOrWhiteSpace(query.Text))
            filtered = filtered.Where(p => p.FileName.Contains(query.Text, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Hash))
            filtered = filtered.Where(p => string.Equals(p.ContentHash, query.Hash, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.PerceptualHash))
            filtered = filtered.Where(p => string.Equals(p.PerceptualHash, query.PerceptualHash, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Set) && Enum.TryParse<PhotoSet>(query.Set, true, out var set))
            filtered = filtered.Where(p => p.Set == set);

        var total = filtered.Count();
        var skip = (query.Page - 1) * query.PageSize;
        var pageItems = filtered.Skip(skip).Take(query.PageSize).ToList();
        return new PagedResult<Photo>(pageItems, query.Page, query.PageSize, total);
    }
}
