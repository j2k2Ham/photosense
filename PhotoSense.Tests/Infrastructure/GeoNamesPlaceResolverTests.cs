using PhotoSense.Infrastructure.Places;
using Xunit;

namespace PhotoSense.Tests.Infrastructure;

public class GeoNamesPlaceResolverTests
{
    private static readonly GeoNamesPlaceResolver Bundled = new();

    [Theory]
    [InlineData(35.2677, -75.5424, "Buxton, North Carolina, US")]          // in the town
    [InlineData(48.8584, 2.2945, "Paris")]                                  // the list names its districts
    [InlineData(-33.8568, 151.2153, "New South Wales, AU")]                 // southern and eastern hemispheres
    public void Names_The_Town_A_Picture_Was_Taken_In(double latitude, double longitude, string expected)
        => Assert.Contains(expected, Bundled.Describe(latitude, longitude));

    [Fact]
    public void Says_Near_When_The_Town_Is_Some_Way_Off()
    {
        // A trailhead in the hills above Anaconda, Montana.
        var place = Bundled.Describe(46.1994, -112.9836);
        Assert.Equal("Near Anaconda, Montana, US", place);
    }

    [Theory]
    [InlineData(30.0, -40.0)]     // mid-Atlantic
    [InlineData(-75.0, 0.0)]      // Antarctica
    [InlineData(91.0, 0.0)]       // not a position
    [InlineData(0.0, 181.0)]
    [InlineData(double.NaN, 0.0)]
    public void Names_Nothing_Far_From_Anywhere_Or_For_Nonsense(double latitude, double longitude)
        => Assert.Null(Bundled.Describe(latitude, longitude));

    [Fact]
    public void Picks_The_Nearest_Place_Across_Cell_Edges_And_The_Date_Line()
    {
        var list = string.Join('\n',
            "Westside\tRegion\tAA\t10.9900\t20.9900",
            "Eastside\tRegion\tAA\t11.0100\t21.0300",
            "Dateline East\t\tBB\t-17.0000\t179.9900",
            "not a row",
            "Bad\tRegion\tCC\tnorth\teast");
        var places = new GeoNamesPlaceResolver(() => new StringReader(list));

        // The nearest place lies in the neighbouring one-degree cell.
        Assert.Equal("Eastside, Region, AA", places.Describe(11.005, 21.02));
        Assert.Equal("Westside, Region, AA", places.Describe(10.995, 20.995));
        // Just across the 180th meridian; a place with no region is named without one.
        Assert.Equal("Dateline East, BB", places.Describe(-17.0, -179.995));
        Assert.Equal("Near Dateline East, BB", places.Describe(-17.3, -179.9));
        Assert.Null(places.Describe(-17.0, -178.0));
    }

    [Fact]
    public void Distances_Shrink_Eastward_Towards_The_Poles()
    {
        // One degree of longitude is about 111 km at the equator but about 19 km at 80 degrees north.
        var places = new GeoNamesPlaceResolver(() => new StringReader("Equator Town\t\tAA\t0.0\t10.0\nArctic Town\t\tAA\t80.0\t10.0"));
        Assert.Null(places.Describe(0.0, 11.0));
        Assert.Equal("Near Arctic Town, AA", places.Describe(80.0, 11.0));
        Assert.Equal("Near Arctic Town, AA", places.Describe(80.0, 13.5));
    }

    [Fact]
    public void A_Build_That_Left_The_Place_List_Out_Says_So()
    {
        var error = Assert.Throws<InvalidOperationException>(() => GeoNamesPlaceResolver.OpenBundledList(typeof(GeoNamesPlaceResolverTests).Assembly));
        Assert.Contains("missing from the build", error.Message);

        using var bundled = GeoNamesPlaceResolver.OpenBundledList(typeof(GeoNamesPlaceResolver).Assembly);
        Assert.Equal(5, bundled.ReadLine()!.Split('\t').Length);
    }
}
