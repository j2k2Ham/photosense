using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests.Domain;

/// <summary>The rules for a file's name where it arrives, so that nothing already there is replaced.</summary>
public sealed class NameRulesTests
{
    [Test]
    public async Task Names_With_A_Number_Are_Found_And_Recognised(CancellationToken ct)
    {
        var taken = new HashSet<string> { "IMG_1 (1).JPG", "IMG_1 (2).JPG" };
        await Assert.That(NameRules.NextFree("IMG_1.JPG", taken.Contains)).IsEqualTo("IMG_1 (3).JPG");
        await Assert.That(NameRules.NextFree("README", _ => false)).IsEqualTo("README (1)");

        var strict = NameRules.Family("IMG_1.JPG", ignoreCase: false);
        await Assert.That(new[] { "IMG_1.JPG", "IMG_1 (1).JPG", "IMG_1 (12).JPG" }.All(strict.IsMatch)).IsTrue();
        await Assert.That(new[] { "img_1.jpg", "IMG_1 (a).JPG", "IMG_11.JPG", "IMG_1.JPG.bak", "xIMG_1.JPG", "IMG_1  (1).JPG" }.Any(strict.IsMatch)).IsFalse();
        await Assert.That(NameRules.Family("IMG_1.JPG", ignoreCase: true).IsMatch("img_1 (1).jpg")).IsTrue();
        // Characters that mean something in a pattern are taken as they are.
        await Assert.That(NameRules.Family("a+b (x).JPG").IsMatch("a+b (x) (1).JPG")).IsTrue();
        await Assert.That(NameRules.Family("a+b (x).JPG").IsMatch("aab (x).JPG")).IsFalse();
    }

    [Test]
    public async Task A_Name_Asked_For_Must_Be_A_Plain_Name_With_The_Files_Own_Extension(CancellationToken ct)
    {
        foreach (var good in new[] { "IMG_6435 2023-06.JPG", "new.jpg", "a.b.JPG" }) await Assert.That(NameRules.Usable(good, "IMG_6435.JPG")).IsTrue();
        foreach (var bad in new[] { null, "", "  ", " new.JPG", "new.JPG ", "new.PNG", "new", ".JPG", "a/b.JPG", "a\\b.JPG", "what?.JPG", "a:b.JPG", "a*b.JPG", "a|b.JPG", "a<b.JPG", "a>b.JPG", "a\"b.JPG", "new.JPG." })
            await Assert.That(NameRules.Usable(bad, "IMG_6435.JPG")).IsFalse();
        await Assert.That(NameRules.Usable("notes", "README")).IsTrue();
    }
}
