using Trovolve.Api.Http;
using Trovolve.Application.Scanning.Services;
using Trovolve.Contracts.Duplicates;

namespace Trovolve.Api.Scanning;

/// <summary>What the scans so far have found to free, by kind, for the first scan and its result.</summary>
internal sealed class ScanFoundEndpoints
{
    private readonly LibraryFindings _findings;

    /// <summary>What <paramref name="findings"/> works out.</summary>
    public ScanFoundEndpoints(LibraryFindings findings) => _findings = findings;

    /// <summary>GET /api/scan/found: the copies, similar shots and large videos found so far, with how many and how much; it names no file.</summary>
    public async Task<ApiResponse> GetAsync(ApiRequest req, CancellationToken ct)
    {
        var found = await _findings.FindAsync(ct);
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new FindingsDto { Items = [.. found.Select(f => new FindingDto { Kind = f.Kind, Count = f.Count, Bytes = f.Bytes })] }, ct);
        return resp;
    }
}
