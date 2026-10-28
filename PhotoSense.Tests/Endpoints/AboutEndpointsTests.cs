using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using PhotoSense.Api.About;
using PhotoSense.Api.Endpoints;
using PhotoSense.Server;

namespace PhotoSense.Tests.Endpoints;

public class AboutEndpointsTests
{
    private static readonly ProductInfo Product = new("PhotoSense", "Jamie Hamman", "© 2026 Jamie Hamman", "help@photosense.example", "https://photosense.example",
        "https://photosense.example/privacy", "https://photosense.example/terms", "https://photosense.example/support", "the State of Example");
    private static readonly ProductInfo NothingSetYet = Product with { SupportEmail = null, Website = null, PrivacyPolicyUrl = null, TermsUrl = null, SupportUrl = null, GoverningLaw = null };
    private static readonly ProgramInfo Program = new("iphone", "1.2.0.57", () => "LiteDB 5.0.21 (MIT)");

    private static ProductInfo Read(string json) => ProductInfo.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    [Test]
    public async Task Says_Who_Makes_PhotoSense_How_To_Reach_Them_And_Which_Program_And_Version_Is_Answering()
    {
        var json = (await new AboutEndpoints(Product, Program).GetAsync(Http.Get("about"))).Json();

        await Assert.That(json.GetProperty("name").GetString()).IsEqualTo("PhotoSense");
        await Assert.That(json.GetProperty("version").GetString()).IsEqualTo("1.2.0.57");
        await Assert.That(json.GetProperty("program").GetString()).IsEqualTo("iphone");
        await Assert.That(json.GetProperty("publisher").GetString()).IsEqualTo("Jamie Hamman");
        await Assert.That(json.GetProperty("copyright").GetString()).IsEqualTo("© 2026 Jamie Hamman");
        await Assert.That(json.GetProperty("supportEmail").GetString()).IsEqualTo("help@photosense.example");
        await Assert.That(json.GetProperty("website").GetString()).IsEqualTo("https://photosense.example");
        await Assert.That(json.GetProperty("privacyPolicyUrl").GetString()).IsEqualTo("https://photosense.example/privacy");
        await Assert.That(json.GetProperty("termsUrl").GetString()).IsEqualTo("https://photosense.example/terms");
        await Assert.That(json.GetProperty("supportUrl").GetString()).IsEqualTo("https://photosense.example/support");
    }

    [Test]
    [Arguments("privacy", "# Privacy Policy")]
    [Arguments("terms", "# Terms of Use")]
    [Arguments("support", "# Support")]
    public async Task Gives_Each_Document_As_The_Web_Site_Has_It_With_The_Products_Details_Filled_In(string document, string title)
    {
        var response = await new AboutEndpoints(Product, Program).TextAsync(Http.Get($"about/{document}"), document);
        var text = response.Text();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Header("Content-Type")).IsEqualTo("text/markdown; charset=utf-8");
        await Assert.That(text).StartsWith(title);
        await Assert.That(text).Contains("help@photosense.example");
        await Assert.That(text).DoesNotContain("{{");
    }

    [Test]
    public async Task Names_The_Maker_And_The_Law_That_Applies_In_The_Terms_And_Says_So_Where_Something_Is_Not_Set_Yet()
    {
        var terms = AboutDocuments.Text("terms", Product)!;
        await Assert.That(terms).Contains("between you and Jamie Hamman");
        await Assert.That(terms).Contains("governed by the laws of the State of Example");

        var unset = AboutDocuments.Text("terms", NothingSetYet)!;
        await Assert.That(unset).Contains("(the support address is not set yet)");
        await Assert.That(unset).Contains("(the place whose law applies is not set yet)");
        await Assert.That(AboutDocuments.Text("cookies", Product)).IsNull();
    }

    [Test]
    public async Task Gives_The_Programs_Own_Third_Party_Notices_As_Plain_Text()
    {
        var response = await new AboutEndpoints(Product, Program).TextAsync(Http.Get("about/notices"), "notices");

        await Assert.That(response.Header("Content-Type")).IsEqualTo("text/plain; charset=utf-8");
        await Assert.That(response.Text()).IsEqualTo("LiteDB 5.0.21 (MIT)");
    }

    [Test]
    public async Task Gives_PhotoSenses_Icon_As_It_Is_Kept_In_Branding()
    {
        var response = await new AboutEndpoints(Product, Program).IconAsync(Http.Get("about/icon"));

        await Assert.That(response.Header("Content-Type")).IsEqualTo("image/png");
        await Assert.That(response.Bytes()).IsEquivalentTo(await File.ReadAllBytesAsync(Path.Combine(Repository.Root, "branding", "app-icon.png")), CollectionOrdering.Matching);
    }

    [Test]
    public async Task Says_Which_Documents_There_Are_When_Asked_For_One_That_Is_Not()
    {
        var response = await new AboutEndpoints(Product, Program).TextAsync(Http.Get("about/cookies"), "cookies");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(response.Text()).IsEqualTo("There is no document called cookies. There are privacy, terms, support and notices.");
    }

    [Test]
    public async Task Reads_Product_Json_Taking_A_Blank_Or_Missing_Value_As_Not_Set_Yet()
    {
        var product = Read("""{ "name": "PhotoSense", "publisher": " Jamie Hamman ", "copyright": "© 2026 Jamie Hamman", "supportEmail": "  ", "website": "https://photosense.example" }""");

        await Assert.That(product).IsEqualTo(new ProductInfo("PhotoSense", "Jamie Hamman", "© 2026 Jamie Hamman", null, "https://photosense.example", null, null, null, null));
        var error = Assert.Throws<InvalidDataException>(() => Read("""{ "name": "PhotoSense", "publisher": "", "copyright": "©" }"""));
        await Assert.That(error!.Message).IsEqualTo("product.json has no publisher.");
    }

    [Test]
    public async Task Builds_In_The_Product_Json_At_The_Root_Of_The_Repository()
    {
        await Assert.That(ProductInfo.Bundled.Name).IsEqualTo("PhotoSense");
        await Assert.That(ProductInfo.Bundled.Publisher).IsEqualTo("Jamie Hamman");
        var error = Assert.Throws<InvalidOperationException>(() => Bundle.Open("cookies.md"));
        await Assert.That(error!.Message).IsEqualTo("cookies.md was not built into PhotoSense.");
    }

    [Test]
    public async Task On_A_Computer_It_Is_The_Server_With_Its_Version_And_The_Notices_Built_Into_It()
    {
        var program = ProgramInfo.From(typeof(PhotoSenseServer).Assembly, "server");

        await Assert.That(program.Program).IsEqualTo("server");
        await Assert.That(program.Version).IsEqualTo(typeof(PhotoSenseServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        await Assert.That(program.Version).Matches(@"^\d+\.\d+\.\d+(\.\d+)?$");
        var notices = program.Notices();
        await Assert.That(notices).StartsWith("Third-party notices for PhotoSense for Windows and Mac");
        await Assert.That(notices).Contains("Magick.NET");
        await Assert.That(notices).Contains("GeoNames");
    }

    [Test]
    public async Task A_Program_Built_Without_A_Version_Or_Notices_Gives_Its_Assembly_Version_And_Says_What_Is_Missing()
    {
        var bare = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Bare") { Version = new Version(3, 4, 5, 6) }, AssemblyBuilderAccess.Run);
        await Assert.That(ProgramInfo.From(bare, "android").Version).IsEqualTo("3.4.5.6");

        var error = Assert.Throws<InvalidOperationException>(() => ProgramInfo.From(typeof(AboutEndpointsTests).Assembly, "android").Notices());
        await Assert.That(error!.Message).IsEqualTo("android was built without its third-party notices.");
    }
}
