using PhotoSense.Application.Scanning.Interfaces;
using PhotoSense.Domain.DTOs;
using PhotoSense.Domain.Repositories;

namespace PhotoSense.Application.Scanning.Services;

public class DuplicateGroupingService : IDuplicateGroupingService
{
    private readonly IPhotoRepository _repo;

    public DuplicateGroupingService(IPhotoRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<DuplicateGroup>> GetDuplicateGroupsAsync(CancellationToken ct = default)
    {
        var all = await _repo.GetAllAsync(ct);
        var groups = all
            .Where(p => !string.IsNullOrWhiteSpace(p.ContentHash))
            .GroupBy(p => p.ContentHash!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DuplicateGroup(g.Key, g.ToList()))
            .Where(g => g.IsTrueDuplicate)
            .OrderByDescending(g => g.Photos.Count)
            .ToList();
        return groups;
    }
}
