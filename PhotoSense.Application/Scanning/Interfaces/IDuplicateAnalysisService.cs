using PhotoSense.Domain.DTOs;

namespace PhotoSense.Application.Scanning.Interfaces;

/// <summary>Finds the copies and look-alikes among the photos scans have recorded.</summary>
public interface IDuplicateAnalysisService
{
    /// <summary>Groups of duplicate and similar photos for the photos currently recorded.</summary>
    Task<DuplicateAnalysis> GetAsync(CancellationToken ct);
}
