using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests.Application;

public class PhotoNamingTests
{
    [Test]
    [Arguments("IMG_1234.HEIC", "IMG_1234")]
    [Arguments("img_1234.jpg", "IMG_1234")]
    [Arguments("IMG_1234.MOV", "IMG_1234")]       // its Live Photo video
    [Arguments("IMG_E1234.HEIC", "IMG_1234")]     // its edited version
    [Arguments("IMG_1234.AAE", "IMG_1234")]       // the record of its edits ...
    [Arguments("IMG_O1234.AAE", "IMG_1234")]      // ... in both forms iOS writes
    [Arguments("AROP4792.JPG", "AROP4792")]
    [Arguments("AROPE4792.JPG", "AROP4792")]
    [Arguments("IMG_1234 (1).HEIC", "IMG_1234 (1)")]   // a copy made in the same folder is its own item
    [Arguments("IMG_E1234 (1).HEIC", "IMG_1234 (1)")]
    [Arguments("DSC_0132.JPG", "DSC_0132")]
    [Arguments("IMG_12345.JPG", "IMG_12345")]
    [Arguments("holiday.png", "HOLIDAY")]
    public async Task Every_File_Of_One_Item_Shares_Its_Name(string fileName, string item)
        => await Assert.That(PhotoNaming.ItemOf(Path.Combine("some", "folder", fileName))).IsEqualTo(item);

    [Test]
    [Arguments("IMG_E8975.HEIC", true)]
    [Arguments("img_e8975.jpg", true)]
    [Arguments("AROPE4792.JPG", true)]
    [Arguments("IMG_E8975 (1).HEIC", true)]
    [Arguments("IMG_8975.HEIC", false)]
    [Arguments("IMG_O8975.AAE", false)]
    [Arguments("AROP4792.JPG", false)]
    [Arguments("DSC_0132.JPG", false)]
    [Arguments("IMG_E89751.HEIC", false)]
    public async Task Recognises_The_Name_iOS_Gives_An_Edited_Picture(string fileName, bool edited)
        => await Assert.That(PhotoNaming.IsEditedRender(fileName)).IsEqualTo(edited);

    [Test]
    [Arguments("IMG_1234.AAE", true)]
    [Arguments("IMG_O1234.aae", true)]
    [Arguments("IMG_1234.HEIC", false)]
    public async Task Knows_A_Sidecar(string fileName, bool sidecar)
        => await Assert.That(PhotoNaming.IsSidecar(fileName)).IsEqualTo(sidecar);

    [Test]
    [Arguments("a.HEIC", true, false)]
    [Arguments("a.jpg", true, false)]
    [Arguments("a.MOV", false, true)]
    [Arguments("a.mp4", false, true)]
    [Arguments("a.aae", false, false)]
    [Arguments("a", false, false)]
    public async Task Tells_Pictures_From_Videos_By_Extension(string fileName, bool image, bool video)
    {
        await Assert.That((MediaFiles.IsImage(fileName), MediaFiles.IsVideo(fileName), MediaFiles.IsScanned(fileName))).IsEqualTo((image, video, image || video));
    }
}
