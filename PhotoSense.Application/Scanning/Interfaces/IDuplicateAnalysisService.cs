using PhotoSense.Domain.DTOs;

namespace PhotoSense.Application.Scanning.Interfaces;

public interface IDuplicateAnalysisService
{
    /// <summary>Groups of duplicate and similar photos for the photos currently recorded.</summary>
    Task<DuplicateAnalysis> GetAsync(CancellationToken ct = default);
}
