using PhotoSense.Infrastructure.Hashing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace PhotoSense.Tests.Infrastructure;

public class PerceptualHashingServiceVariationTests
{
    [Fact]
    public async Task PerceptualHash_Differs_For_Different_Brightness()
    {
        var svc = new PerceptualHashingService();
        await using var img1 = new MemoryStream();
        await using var img2 = new MemoryStream();
        using (var image = new Image<Rgba32>(16,16))
        {
            for(int y=0;y<16;y++)
                for(int x=0;x<16;x++)
                    image[x,y]= ((x+y)%2==0) ? Color.Black : Color.White;
            await image.SaveAsPngAsync(img1);
        }
        using (var image = new Image<Rgba32>(16,16))
        {
            for(int y=0;y<16;y++)
                for(int x=0;x<16;x++)
                    image[x,y]= ((x+y)%2==0) ? Color.White : Color.Black;
            await image.SaveAsPngAsync(img2);
        }
        img1.Position = 0; img2.Position = 0;
        var h1 = await svc.ComputePerceptualHashAsync(img1);
        var h2 = await svc.ComputePerceptualHashAsync(img2);
        Assert.NotEqual(h1, h2);
    }
}
