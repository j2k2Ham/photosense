using PhotoSense.Infrastructure.Places;

namespace PhotoSense.Tests.Infrastructure;

public class GeoNamesPlaceResolverTests
{
    private static readonly GeoNamesPlaceResolver Bundled = new();

    [Test]
    [Arguments(35.2677, -75.5424, "Buxton, North Carolina, US")]          // in the town
    [Arguments(48.8584, 2.2945, "Paris")]                                  // the list names its districts
    [Arguments(-33.8568, 151.2153, "New South Wales, AU")]                 // southern and eastern hemispheres
    public async Task Names_The_Town_A_Picture_Was_Taken_In(double latitude, double longitude, string expected)
        => await Assert.That(Bundled.Describe(latitude, longitude)).Contains(expected);

    [Test]
    public async Task Says_Near_When_The_Town_Is_Some_Way_Off()
    {
        // A trailhead in the hills above Anaconda, Montana.
        var place = Bundled.Describe(46.1994, -112.9836);
        await Assert.That(place).IsEqualTo("Near Anaconda, Montana, US");
    }

    [Test]
    [Arguments(30.0, -40.0)]     // mid-Atlantic
    [Arguments(-75.0, 0.0)]      // Antarctica
    [Arguments(91.0, 0.0)]       // not a position
    [Arguments(0.0, 181.0)]
    [Arguments(double.NaN, 0.0)]
    [Arguments(0.0, double.NaN)]
    public async Task Names_Nothing_Far_From_Anywhere_Or_For_Nonsense(double latitude, double longitude)
        => await Assert.That(Bundled.Describe(latitude, longitude)).IsNull();

    [Test]
    public async Task Picks_The_Nearest_Place_Across_Cell_Edges_And_The_Date_Line()
    {
        var list = string.Join('\n',
            "Westside\tRegion\tAA\t10.9900\t20.9900",
            "Eastside\tRegion\tAA\t11.0100\t21.0300",
            "Dateline East\t\tBB\t-17.0000\t179.9900",
            "not a row",
            "Bad\tRegion\tCC\tnorth\teast");
        var places = new GeoNamesPlaceResolver(() => new StringReader(list));

        // The nearest place lies in the neighbouring one-degree cell.
        await Assert.That(places.Describe(11.005, 21.02)).IsEqualTo("Eastside, Region, AA");
        await Assert.That(places.Describe(10.995, 20.995)).IsEqualTo("Westside, Region, AA");
        // Just across the 180th meridian; a place with no region is named without one.
        await Assert.That(places.Describe(-17.0, -179.995)).IsEqualTo("Dateline East, BB");
        await Assert.That(places.Describe(-17.3, -179.9)).IsEqualTo("Near Dateline East, BB");
        await Assert.That(places.Describe(-17.0, -178.0)).IsNull();
    }

    [Test]
    public async Task Distances_Shrink_Eastward_Towards_The_Poles()
    {
        // One degree of longitude is about 111 km at the equator but about 19 km at 80 degrees north.
        var places = new GeoNamesPlaceResolver(() => new StringReader("Equator Town\t\tAA\t0.0\t10.0\nArctic Town\t\tAA\t80.0\t10.0"));
        await Assert.That(places.Describe(0.0, 11.0)).IsNull();
        await Assert.That(places.Describe(80.0, 11.0)).IsEqualTo("Near Arctic Town, AA");
        await Assert.That(places.Describe(80.0, 13.5)).IsEqualTo("Near Arctic Town, AA");
    }

    [Test]
    public async Task A_Build_That_Left_The_Place_List_Out_Says_So()
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => GeoNamesPlaceResolver.OpenBundledList(typeof(GeoNamesPlaceResolverTests).Assembly));
        await Assert.That(error.Message).Contains("missing from the build");

        using var bundled = GeoNamesPlaceResolver.OpenBundledList(typeof(GeoNamesPlaceResolver).Assembly);
        await Assert.That(bundled.ReadLine()!.Split('\t').Length).IsEqualTo(5);
    }
}
