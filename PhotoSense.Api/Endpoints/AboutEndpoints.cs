using System.Net;
using PhotoSense.Api.About;
using PhotoSense.Api.Http;
using PhotoSense.Contracts.About;

namespace PhotoSense.Api.Endpoints;

/// <summary>What the About window shows. None of it names anyone's files, so it needs no client header.</summary>
internal sealed class AboutEndpoints
{
    private readonly ProductInfo _product;
    private readonly ProgramInfo _program;

    /// <summary>About the product and the program answering.</summary>
    public AboutEndpoints(ProductInfo product, ProgramInfo program)
    {
        _product = product;
        _program = program;
    }

    /// <summary>GET /api/about: who makes PhotoSense, how to reach them, and this program's version.</summary>
    public async Task<ApiResponse> GetAsync(ApiRequest req, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new AboutDto
        {
            Name = _product.Name, Version = _program.Version, Program = _program.Program, Publisher = _product.Publisher, Copyright = _product.Copyright,
            SupportEmail = _product.SupportEmail, Website = _product.Website, PrivacyPolicyUrl = _product.PrivacyPolicyUrl, TermsUrl = _product.TermsUrl, SupportUrl = _product.SupportUrl,
        }, ct);
        return resp;
    }

    /// <summary>GET /api/about/icon: PhotoSense's icon (branding/app-icon.png), for the page's own tab, header and About window.</summary>
    public async Task<ApiResponse> IconAsync(ApiRequest req, CancellationToken ct)
    {
        var resp = req.CreateResponse();
        resp.Headers.Add("Content-Type", "image/png");
        await using var icon = Bundle.Open("app-icon.png");
        await icon.CopyToAsync(resp.Body, ct);
        return resp;
    }

    /// <summary>GET /api/about/{document}: privacy, terms or support, in Markdown; notices, the third-party notices, as plain text.</summary>
    public async Task<ApiResponse> TextAsync(ApiRequest req, string document, CancellationToken ct)
    {
        var notices = document == "notices";
        if ((notices ? _program.Notices() : AboutDocuments.Text(document, _product)) is not { } text)
        {
            var missing = req.CreateResponse(HttpStatusCode.NotFound);
            await missing.WriteStringAsync($"There is no document called {document}. There are privacy, terms, support and notices.", ct);
            return missing;
        }
        var resp = req.CreateResponse();
        resp.Headers.Add("Content-Type", notices ? "text/plain; charset=utf-8" : "text/markdown; charset=utf-8");
        await resp.WriteStringAsync(text, ct);
        return resp;
    }
}
