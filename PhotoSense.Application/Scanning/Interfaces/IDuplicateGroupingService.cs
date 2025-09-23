using PhotoSense.Core.Domain.DTOs;

namespace PhotoSense.Application.Scanning.Interfaces;

public interface IDuplicateGroupingService
{
    Task<IReadOnlyList<DuplicateGroup>> GetDuplicateGroupsAsync(CancellationToken ct = default);
}
