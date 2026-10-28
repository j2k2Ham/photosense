using System.Net;
using PhotoSense.Api.About;
using PhotoSense.Api.Http;
using PhotoSense.Contracts.About;

namespace PhotoSense.Api.Endpoints;

/// <summary>What the About window shows. None of it names anyone's files, so it needs no client header.</summary>
public sealed class AboutEndpoints
{
    private readonly ProductInfo _product;
    private readonly ProgramInfo _program;

    public AboutEndpoints(ProductInfo product, ProgramInfo program)
    {
        _product = product;
        _program = program;
    }

    public async Task<ApiResponse> GetAsync(ApiRequest req)
    {
        var resp = req.CreateResponse();
        await resp.WriteAsJsonAsync(new AboutDto
        {
            Name = _product.Name, Version = _program.Version, Program = _program.Program, Publisher = _product.Publisher, Copyright = _product.Copyright,
            SupportEmail = _product.SupportEmail, Website = _product.Website, PrivacyPolicyUrl = _product.PrivacyPolicyUrl, TermsUrl = _product.TermsUrl, SupportUrl = _product.SupportUrl,
        });
        return resp;
    }

    /// <summary>PhotoSense's icon (branding/app-icon.png), for the page's own tab, header and About window.</summary>
    public async Task<ApiResponse> IconAsync(ApiRequest req)
    {
        var resp = req.CreateResponse();
        resp.Headers.Add("Content-Type", "image/png");
        await using var icon = Bundle.Open("app-icon.png");
        await icon.CopyToAsync(resp.Body);
        return resp;
    }

    /// <summary>privacy, terms or support, in Markdown; notices, the third-party notices, as plain text.</summary>
    public async Task<ApiResponse> TextAsync(ApiRequest req, string document)
    {
        var notices = document == "notices";
        if ((notices ? _program.Notices() : AboutDocuments.Text(document, _product)) is not { } text)
        {
            var missing = req.CreateResponse(HttpStatusCode.NotFound);
            await missing.WriteStringAsync($"There is no document called {document}. There are privacy, terms, support and notices.");
            return missing;
        }
        var resp = req.CreateResponse();
        resp.Headers.Add("Content-Type", notices ? "text/plain; charset=utf-8" : "text/markdown; charset=utf-8");
        await resp.WriteStringAsync(text);
        return resp;
    }
}
