using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.DTOs;
using static PhotoSense.Tests.TestPhotos;

namespace PhotoSense.Tests.Application;

public class PhotoMatcherTests
{
    [Test]
    public async Task Same_Instant_And_Near_Identical_Pixels_Is_The_Same_Picture()
    {
        var original = Make("IMG_4198.HEIC", taken: Shot);
        var converted = Make("IMG_4198.JPG", taken: Shot, signature: Signature(0.13), format: "JPEG");
        var verdict = PhotoMatcher.Compare(original, converted);
        await Assert.That(verdict!.Kind).IsEqualTo(MatchKind.SamePicture);
        await Assert.That(verdict.HashDistance).IsEqualTo(0);
        await Assert.That(verdict.Difference).IsGreaterThanOrEqualTo(0.12).And.IsLessThanOrEqualTo(0.14);
    }

    [Test]
    public async Task Burst_Frames_Are_Only_Similar_However_Alike_They_Look()
    {
        var first = Make("IMG_6527.JPG", taken: Shot);
        var next = Make("IMG_6528.JPG", taken: Shot.AddMilliseconds(54));
        await Assert.That(PhotoMatcher.Compare(first, next)!.Kind).IsEqualTo(MatchKind.Similar);
    }

    [Test]
    public async Task A_Millisecond_Of_Drift_In_The_Stamp_Is_Still_The_Same_Shot()
    {
        var a = Make("IMG_E8975.HEIC", taken: Shot);
        var b = Make("IMG_E8975.JPG", taken: Shot.AddMilliseconds(1));
        await Assert.That(PhotoMatcher.Compare(a, b)!.Kind).IsEqualTo(MatchKind.SamePicture);
    }

    [Test]
    public async Task A_Copy_That_Lost_Its_Sub_Seconds_Matches_Within_The_Second()
    {
        var original = Make("IMG_0001.HEIC", taken: Shot);
        var copy = Make("copy.jpg", taken: Shot.AddMilliseconds(-Shot.Millisecond));
        await Assert.That(PhotoMatcher.Compare(original, copy)!.Kind).IsEqualTo(MatchKind.SamePicture);
        await Assert.That(PhotoMatcher.Compare(copy, original)!.Kind).IsEqualTo(MatchKind.SamePicture);
        var laterShot = Make("later.jpg", taken: Shot.AddMilliseconds(-Shot.Millisecond).AddSeconds(3));
        await Assert.That(PhotoMatcher.Compare(original, laterShot)!.Kind).IsEqualTo(MatchKind.Similar);
    }

    [Test]
    public async Task An_Edited_Version_Is_Never_A_Duplicate_Of_Its_Original()
    {
        var original = Make("IMG_8975.HEIC", taken: Shot);
        var edited = Make("IMG_E8975.HEIC", taken: Shot);
        await Assert.That(PhotoMatcher.Compare(original, edited)!.Kind).IsEqualTo(MatchKind.Similar);
        await Assert.That(PhotoMatcher.Compare(edited, original)!.Kind).IsEqualTo(MatchKind.Similar);
    }

    [Test]
    public async Task Two_Saves_Of_The_Same_Edit_Are_The_Same_Picture()
    {
        var heic = Make("IMG_E8975.HEIC", taken: Shot);
        var jpeg = Make("IMG_E8975.JPG", taken: Shot, format: "JPEG");
        await Assert.That(PhotoMatcher.Compare(heic, jpeg)!.Kind).IsEqualTo(MatchKind.SamePicture);
    }

    [Test]
    [Arguments(0.1, MatchKind.SamePicture)]
    [Arguments(0.3, MatchKind.Similar)] // as close as burst frames get: not enough without a capture time
    public async Task Without_Capture_Times_Pixels_Must_Match_More_Closely(double difference, MatchKind expected)
    {
        var a = Make("a.jpg");
        var b = Make("b.jpg", signature: Signature(difference));
        await Assert.That(PhotoMatcher.Compare(a, b)!.Kind).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0.3, MatchKind.SamePicture)] // a resized copy that was stripped of its details when shared
    [Arguments(0.8, MatchKind.Similar)]
    public async Task A_Copy_Stripped_Of_Its_Capture_Time_Still_Matches_Its_Original(double difference, MatchKind expected)
    {
        var original = Make("IMG_2122.JPG", taken: Shot, width: 3396, height: 1927);
        var shared = Make("SKOZ2556.JPG", signature: Signature(difference), width: 1600, height: 908);
        await Assert.That(PhotoMatcher.Compare(original, shared)!.Kind).IsEqualTo(expected);
    }

    [Test]
    public async Task Visibly_Different_Pixels_Are_Unrelated()
    {
        var a = Make("a.jpg", taken: Shot);
        var b = Make("b.jpg", taken: Shot, signature: Signature(PhotoMatcher.SimilarDifference + 0.5));
        await Assert.That(PhotoMatcher.Compare(a, b)).IsNull();
    }

    [Test]
    public async Task Hashes_Are_Compared_Bit_By_Bit_Up_To_The_Candidate_Limit()
    {
        var a = Make("a.jpg", hash: 0);
        var atLimit = Make("b.jpg", hash: (1UL << PhotoMatcher.CandidateBits) - 1);
        var beyond = Make("c.jpg", hash: (1UL << (PhotoMatcher.CandidateBits + 1)) - 1);
        await Assert.That(PhotoMatcher.Compare(a, atLimit)!.HashDistance).IsEqualTo(PhotoMatcher.CandidateBits);
        await Assert.That(PhotoMatcher.Compare(a, beyond)).IsNull();
        // The old comparison counted differing hex characters: 16 for this pair, which differs in all 64 bits.
        await Assert.That(PhotoMatcher.HashDistance(0x7777777777777777, 0x8888888888888888)).IsEqualTo(64);
    }

    [Test]
    public async Task A_Different_Shape_Is_Not_A_Copy()
    {
        var full = Make("a.jpg", taken: Shot, width: 4032, height: 3024);
        var cropped = Make("b.jpg", taken: Shot, width: 3024, height: 3024);
        await Assert.That(PhotoMatcher.Compare(full, cropped)).IsNull();
        var resized = Make("c.jpg", taken: Shot, width: 1600, height: 1200);
        await Assert.That(PhotoMatcher.Compare(full, resized)).IsNotNull();
    }

    [Test]
    public async Task Photos_That_Were_Not_Decoded_Cannot_Be_Compared()
    {
        var good = Make("a.jpg");
        var noSignature = Make("b.jpg"); noSignature.Signature = null;
        var noHash = Make("c.jpg"); noHash.PerceptualHash = null;
        var oldHash = Make("d.jpg"); oldHash.PerceptualHash = "FFFF";
        var noSize = Make("e.jpg", width: 0, height: 0);
        var noHeight = Make("g.jpg", height: 0);
        var otherLength = Make("f.jpg", signature: new byte[12]);
        await Assert.That(PhotoMatcher.Compare(good, noSignature)).IsNull();
        await Assert.That(PhotoMatcher.Compare(noHeight, good)).IsNull();
        await Assert.That(PhotoMatcher.Compare(noSignature, good)).IsNull();
        await Assert.That(PhotoMatcher.Compare(good, noHash)).IsNull();
        await Assert.That(PhotoMatcher.Compare(oldHash, good)).IsNull();
        await Assert.That(PhotoMatcher.Compare(good, noSize)).IsNull();
        await Assert.That(PhotoMatcher.Compare(noSize, good)).IsNull();
        await Assert.That(PhotoMatcher.Compare(good, otherLength)).IsNull();
    }

    [Test]
    [Arguments("00000000000000FF", true)]
    [Arguments("ff00000000000000", true)]
    [Arguments("FFFF", false)]
    [Arguments("ZZZZZZZZZZZZZZZZ", false)]
    [Arguments(null, false)]
    public async Task Hash_Text_Must_Be_Sixteen_Hex_Characters(string? text, bool valid)
        => await Assert.That(PhotoMatcher.TryParseHash(text, out _)).IsEqualTo(valid);

    [Test]
    public async Task Signature_Difference_Is_The_Mean_Absolute_Difference()
        => await Assert.That(PhotoMatcher.SignatureDifference(new byte[] { 10, 20, 30, 40 }, new byte[] { 12, 17, 30, 45 })).IsEqualTo(2.5);
}
