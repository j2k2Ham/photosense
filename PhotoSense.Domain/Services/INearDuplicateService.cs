using PhotoSense.Domain.DTOs;

namespace PhotoSense.Domain.Services;

public interface INearDuplicateService
{
    Task<IReadOnlyList<NearDuplicateGroup>> GetNearDuplicatesAsync(int maxHammingDistance = 5, CancellationToken ct = default);
}
