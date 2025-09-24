using PhotoSense.Domain.Entities;
using Xunit;

namespace PhotoSense.Tests.Domain;

public class PhotoTests
{
    [Fact]
    public void AddCategory_ShouldAdd_WhenNew()
    {
        var photo = new Photo { SourcePath = "a", FileName = "b", FileSizeBytes = 1, Set = PhotoSet.Primary };
        photo.AddCategory("Nature");
        Assert.Contains("Nature", photo.Categories);
    }

    [Fact]
    public void AddCategory_ShouldIgnore_Duplicates_And_Whitespace()
    {
        var photo = new Photo { SourcePath = "a", FileName = "b", FileSizeBytes = 1, Set = PhotoSet.Primary };
        photo.AddCategory("Nature");
        photo.AddCategory("nature");
        photo.AddCategory(" ");
        Assert.Single(photo.Categories);
    }

    [Fact]
    public void LoadCategories_ReplacesExisting()
    {
        var photo = new Photo { SourcePath = "a", FileName = "b", FileSizeBytes = 1, Set = PhotoSet.Primary };
        photo.AddCategory("A");
        photo.LoadCategories(new []{"B","C"});
        Assert.DoesNotContain("A", photo.Categories);
        Assert.Equal(2, photo.Categories.Count);
    }
}
