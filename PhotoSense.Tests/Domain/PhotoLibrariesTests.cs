using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests.Domain;

public class PhotoLibrariesTests
{
    [Test]
    [Arguments("Photos Library.photoslibrary", true)]
    [Arguments("Family.PHOTOSLIBRARY", true)]
    [Arguments("iPhoto Library.photolibrary", true)]
    [Arguments("iPhoto Library.migratedphotolibrary", true)]
    [Arguments("Aperture Library.aplibrary", true)]
    [Arguments("iPhoto Library", true)]
    [Arguments("photo booth library", true)]
    [Arguments("Phone Pictures", false)]
    [Arguments("photoslibrary", false)]
    [Arguments("Photos Library", false)]
    public async Task A_Photo_Programs_Library_Is_Known_By_Its_Name(string folder, bool library)
        => await Assert.That(PhotoLibraries.IsLibrary(folder)).IsEqualTo(library);

    [Test]
    [Arguments("/Users/jamie/Pictures/Photos Library.photoslibrary", true)]
    [Arguments("/Users/jamie/Pictures/Photos Library.photoslibrary/originals/0", true)]
    [Arguments("C:\\Users\\jamie\\Pictures\\iPhoto Library\\Masters", true)]
    [Arguments("/Users/jamie/Pictures/Trips", false)]
    [Arguments("C:\\Users\\jamie\\Phone Pictures", false)]
    public async Task A_Path_Is_In_A_Library_When_Any_Folder_On_The_Way_Is_One(string path, bool inside)
        => await Assert.That(PhotoLibraries.IsInLibrary(path)).IsEqualTo(inside);
}
