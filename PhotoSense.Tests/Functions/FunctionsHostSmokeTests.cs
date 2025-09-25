using Xunit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Threading;
using PhotoSense.Functions;
using Microsoft.Extensions.Configuration;
using PhotoSense.Application.Scanning.Interfaces;

namespace PhotoSense.Tests.Functions;

public class FunctionsHostSmokeTests
{
    [Fact]
    public async Task CanResolveGroupingFacadeAndProgressStore()
    {
        using var host = new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection())
            .ConfigureServices((ctx,s)=> s.AddPhotoSenseCore(ctx.Configuration))
            .Build();
        var progress = host.Services.GetRequiredService<IScanProgressStore>();
        Assert.NotNull(progress.GetLatest());
        await host.StopAsync(CancellationToken.None);
    }
}