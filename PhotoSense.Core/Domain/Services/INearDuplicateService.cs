using PhotoSense.Core.Domain.DTOs;

namespace PhotoSense.Core.Domain.Services;

public interface INearDuplicateService
{
    Task<IReadOnlyList<NearDuplicateGroup>> GetNearDuplicatesAsync(int maxHammingDistance = 5, CancellationToken ct = default);
}
