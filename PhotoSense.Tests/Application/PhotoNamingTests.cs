using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.ValueObjects;
using Xunit;

namespace PhotoSense.Tests.Application;

public class PhotoNamingTests
{
    [Theory]
    [InlineData("IMG_1234.HEIC", "IMG_1234")]
    [InlineData("img_1234.jpg", "IMG_1234")]
    [InlineData("IMG_1234.MOV", "IMG_1234")]       // its Live Photo video
    [InlineData("IMG_E1234.HEIC", "IMG_1234")]     // its edited version
    [InlineData("IMG_1234.AAE", "IMG_1234")]       // the record of its edits ...
    [InlineData("IMG_O1234.AAE", "IMG_1234")]      // ... in both forms iOS writes
    [InlineData("AROP4792.JPG", "AROP4792")]
    [InlineData("AROPE4792.JPG", "AROP4792")]
    [InlineData("IMG_1234 (1).HEIC", "IMG_1234 (1)")]   // a copy made in the same folder is its own item
    [InlineData("IMG_E1234 (1).HEIC", "IMG_1234 (1)")]
    [InlineData("DSC_0132.JPG", "DSC_0132")]
    [InlineData("IMG_12345.JPG", "IMG_12345")]
    [InlineData("holiday.png", "HOLIDAY")]
    public void Every_File_Of_One_Item_Shares_Its_Name(string fileName, string item)
        => Assert.Equal(item, PhotoNaming.ItemOf(Path.Combine("some", "folder", fileName)));

    [Theory]
    [InlineData("IMG_E8975.HEIC", true)]
    [InlineData("img_e8975.jpg", true)]
    [InlineData("AROPE4792.JPG", true)]
    [InlineData("IMG_E8975 (1).HEIC", true)]
    [InlineData("IMG_8975.HEIC", false)]
    [InlineData("IMG_O8975.AAE", false)]
    [InlineData("AROP4792.JPG", false)]
    [InlineData("DSC_0132.JPG", false)]
    [InlineData("IMG_E89751.HEIC", false)]
    public void Recognises_The_Name_iOS_Gives_An_Edited_Picture(string fileName, bool edited)
        => Assert.Equal(edited, PhotoNaming.IsEditedRender(fileName));

    [Theory]
    [InlineData("IMG_1234.AAE", true)]
    [InlineData("IMG_O1234.aae", true)]
    [InlineData("IMG_1234.HEIC", false)]
    public void Knows_A_Sidecar(string fileName, bool sidecar)
        => Assert.Equal(sidecar, PhotoNaming.IsSidecar(fileName));

    [Theory]
    [InlineData("a.HEIC", true, false)]
    [InlineData("a.jpg", true, false)]
    [InlineData("a.MOV", false, true)]
    [InlineData("a.mp4", false, true)]
    [InlineData("a.aae", false, false)]
    [InlineData("a", false, false)]
    public void Tells_Pictures_From_Videos_By_Extension(string fileName, bool image, bool video)
    {
        Assert.Equal((image, video, image || video), (MediaFiles.IsImage(fileName), MediaFiles.IsVideo(fileName), MediaFiles.IsScanned(fileName)));
    }
}
