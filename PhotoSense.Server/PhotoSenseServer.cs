using PhotoSense.Api;
using Microsoft.Extensions.FileProviders;

namespace PhotoSense.Server;

/// <summary>The service as one program: the addresses below /api and, when it has been built in, the page itself.</summary>
public static class PhotoSenseServer
{
    /// <param name="args">As given on the command line: <c>--urls</c>, <c>--Cors:Origins:0=</c> and any other setting.</param>
    /// <param name="contentRoot">Where its settings and the page (in wwwroot) are; beside the program when not given, wherever it is started from.</param>
    public static WebApplication Build(string[] args, string? contentRoot = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = contentRoot ?? AppContext.BaseDirectory });
        builder.Services.AddPhotoSenseComputer(builder.Configuration).AddPhotoSenseApi();
        // Any web page can make a browser send a plain request to this machine; only the page's own address
        // is let add the client header that every request touching someone's files must carry.
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

        var app = builder.Build();
        app.UseCors();
        // A program made with publish.ps1 has the page inside it; in development the page has a server of its own.
        if (app.Environment.WebRootFileProvider is not NullFileProvider)
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
        }
        app.MapWhen(context => context.Request.Path.StartsWithSegments("/api"), api => api.Run(ApiBridge.HandleAsync));
        return app;
    }
}
