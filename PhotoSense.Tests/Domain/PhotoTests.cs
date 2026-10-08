using PhotoSense.Domain.Entities;

namespace PhotoSense.Tests.Domain;

public class PhotoTests
{
    [Test]
    public async Task AddCategory_ShouldAdd_WhenNew()
    {
        var photo = new Photo { SourcePath = "a", FileName = "b", FileSizeBytes = 1, Set = PhotoSet.Primary };
        photo.AddCategory("Nature");
        await Assert.That(photo.Categories).Contains("Nature");
    }

    [Test]
    public async Task AddCategory_ShouldIgnore_Duplicates_And_Whitespace()
    {
        var photo = new Photo { SourcePath = "a", FileName = "b", FileSizeBytes = 1, Set = PhotoSet.Primary };
        photo.AddCategory("Nature");
        photo.AddCategory("nature");
        photo.AddCategory(" ");
        await Assert.That(photo.Categories).HasSingleItem();
    }

    [Test]
    public async Task LoadCategories_ReplacesExisting()
    {
        var photo = new Photo { SourcePath = "a", FileName = "b", FileSizeBytes = 1, Set = PhotoSet.Primary };
        photo.AddCategory("A");
        photo.LoadCategories(new []{"B","C"});
        await Assert.That(photo.Categories).DoesNotContain("A");
        await Assert.That(photo.Categories.Count).IsEqualTo(2);
    }
}
