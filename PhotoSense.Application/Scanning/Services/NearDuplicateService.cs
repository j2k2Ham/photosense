using PhotoSense.Domain.DTOs;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Domain.Entities;

namespace PhotoSense.Application.Scanning.Services;

public class NearDuplicateService : INearDuplicateService
{
    private readonly IPhotoRepository _repo;
    public NearDuplicateService(IPhotoRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<NearDuplicateGroup>> GetNearDuplicatesAsync(int maxHammingDistance = 5, CancellationToken ct = default)
    {
        var all = await _repo.GetAllAsync(ct);
        var withP = all.Where(p => !string.IsNullOrWhiteSpace(p.PerceptualHash)).ToList();
        var groups = new List<NearDuplicateGroup>();
        var visited = new HashSet<Guid>();
        foreach (var photo in withP)
        {
            if (!visited.Add(photo.Id.Value)) continue;
            var cluster = new List<Photo> { photo };
            foreach (var other in withP)
            {
                if (other.Id == photo.Id || visited.Contains(other.Id.Value)) continue;
                if (HammingDistance(photo.PerceptualHash!, other.PerceptualHash!) <= maxHammingDistance)
                {
                    cluster.Add(other);
                    visited.Add(other.Id.Value);
                }
            }
            if (cluster.Count > 1)
                groups.Add(new NearDuplicateGroup(photo.PerceptualHash!, cluster));
        }
        return groups;
    }

    private static int HammingDistance(string a, string b)
    {
        if (a.Length != b.Length) return int.MaxValue;
        int d = 0; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) d++; return d;
    }
}
