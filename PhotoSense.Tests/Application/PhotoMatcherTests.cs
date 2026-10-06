using PhotoSense.Application.Scanning.Services;
using PhotoSense.Domain.DTOs;
using Xunit;
using static PhotoSense.Tests.TestPhotos;

namespace PhotoSense.Tests.Application;

public class PhotoMatcherTests
{
    [Fact]
    public void Same_Instant_And_Near_Identical_Pixels_Is_The_Same_Picture()
    {
        var original = Make("IMG_4198.HEIC", taken: Shot);
        var converted = Make("IMG_4198.JPG", taken: Shot, signature: Signature(0.13), format: "JPEG");
        var verdict = PhotoMatcher.Compare(original, converted);
        Assert.Equal(MatchKind.SamePicture, verdict!.Kind);
        Assert.Equal(0, verdict.HashDistance);
        Assert.InRange(verdict.Difference, 0.12, 0.14);
    }

    [Fact]
    public void Burst_Frames_Are_Only_Similar_However_Alike_They_Look()
    {
        var first = Make("IMG_6527.JPG", taken: Shot);
        var next = Make("IMG_6528.JPG", taken: Shot.AddMilliseconds(54));
        Assert.Equal(MatchKind.Similar, PhotoMatcher.Compare(first, next)!.Kind);
    }

    [Fact]
    public void A_Millisecond_Of_Drift_In_The_Stamp_Is_Still_The_Same_Shot()
    {
        var a = Make("IMG_E8975.HEIC", taken: Shot);
        var b = Make("IMG_E8975.JPG", taken: Shot.AddMilliseconds(1));
        Assert.Equal(MatchKind.SamePicture, PhotoMatcher.Compare(a, b)!.Kind);
    }

    [Fact]
    public void A_Copy_That_Lost_Its_Sub_Seconds_Matches_Within_The_Second()
    {
        var original = Make("IMG_0001.HEIC", taken: Shot);
        var copy = Make("copy.jpg", taken: Shot.AddMilliseconds(-Shot.Millisecond));
        Assert.Equal(MatchKind.SamePicture, PhotoMatcher.Compare(original, copy)!.Kind);
        var laterShot = Make("later.jpg", taken: Shot.AddMilliseconds(-Shot.Millisecond).AddSeconds(3));
        Assert.Equal(MatchKind.Similar, PhotoMatcher.Compare(original, laterShot)!.Kind);
    }

    [Fact]
    public void An_Edited_Version_Is_Never_A_Duplicate_Of_Its_Original()
    {
        var original = Make("IMG_8975.HEIC", taken: Shot);
        var edited = Make("IMG_E8975.HEIC", taken: Shot);
        Assert.Equal(MatchKind.Similar, PhotoMatcher.Compare(original, edited)!.Kind);
        Assert.Equal(MatchKind.Similar, PhotoMatcher.Compare(edited, original)!.Kind);
    }

    [Fact]
    public void Two_Saves_Of_The_Same_Edit_Are_The_Same_Picture()
    {
        var heic = Make("IMG_E8975.HEIC", taken: Shot);
        var jpeg = Make("IMG_E8975.JPG", taken: Shot, format: "JPEG");
        Assert.Equal(MatchKind.SamePicture, PhotoMatcher.Compare(heic, jpeg)!.Kind);
    }

    [Theory]
    [InlineData(0.1, MatchKind.SamePicture)]
    [InlineData(0.3, MatchKind.Similar)] // as close as burst frames get: not enough without a capture time
    public void Without_Capture_Times_Pixels_Must_Match_More_Closely(double difference, MatchKind expected)
    {
        var a = Make("a.jpg");
        var b = Make("b.jpg", signature: Signature(difference));
        Assert.Equal(expected, PhotoMatcher.Compare(a, b)!.Kind);
    }

    [Theory]
    [InlineData(0.3, MatchKind.SamePicture)] // a resized copy that was stripped of its details when shared
    [InlineData(0.8, MatchKind.Similar)]
    public void A_Copy_Stripped_Of_Its_Capture_Time_Still_Matches_Its_Original(double difference, MatchKind expected)
    {
        var original = Make("IMG_2122.JPG", taken: Shot, width: 3396, height: 1927);
        var shared = Make("SKOZ2556.JPG", signature: Signature(difference), width: 1600, height: 908);
        Assert.Equal(expected, PhotoMatcher.Compare(original, shared)!.Kind);
    }

    [Fact]
    public void Visibly_Different_Pixels_Are_Unrelated()
    {
        var a = Make("a.jpg", taken: Shot);
        var b = Make("b.jpg", taken: Shot, signature: Signature(PhotoMatcher.SimilarDifference + 0.5));
        Assert.Null(PhotoMatcher.Compare(a, b));
    }

    [Fact]
    public void Hashes_Are_Compared_Bit_By_Bit_Up_To_The_Candidate_Limit()
    {
        var a = Make("a.jpg", hash: 0);
        var atLimit = Make("b.jpg", hash: (1UL << PhotoMatcher.CandidateBits) - 1);
        var beyond = Make("c.jpg", hash: (1UL << (PhotoMatcher.CandidateBits + 1)) - 1);
        Assert.Equal(PhotoMatcher.CandidateBits, PhotoMatcher.Compare(a, atLimit)!.HashDistance);
        Assert.Null(PhotoMatcher.Compare(a, beyond));
        // The old comparison counted differing hex characters: 16 for this pair, which differs in all 64 bits.
        Assert.Equal(64, PhotoMatcher.HashDistance(0x7777777777777777, 0x8888888888888888));
    }

    [Fact]
    public void A_Different_Shape_Is_Not_A_Copy()
    {
        var full = Make("a.jpg", taken: Shot, width: 4032, height: 3024);
        var cropped = Make("b.jpg", taken: Shot, width: 3024, height: 3024);
        Assert.Null(PhotoMatcher.Compare(full, cropped));
        var resized = Make("c.jpg", taken: Shot, width: 1600, height: 1200);
        Assert.NotNull(PhotoMatcher.Compare(full, resized));
    }

    [Fact]
    public void Photos_That_Were_Not_Decoded_Cannot_Be_Compared()
    {
        var good = Make("a.jpg");
        var noSignature = Make("b.jpg"); noSignature.Signature = null;
        var noHash = Make("c.jpg"); noHash.PerceptualHash = null;
        var oldHash = Make("d.jpg"); oldHash.PerceptualHash = "FFFF";
        var noSize = Make("e.jpg", width: 0, height: 0);
        var otherLength = Make("f.jpg", signature: new byte[12]);
        Assert.Null(PhotoMatcher.Compare(good, noSignature));
        Assert.Null(PhotoMatcher.Compare(noSignature, good));
        Assert.Null(PhotoMatcher.Compare(good, noHash));
        Assert.Null(PhotoMatcher.Compare(oldHash, good));
        Assert.Null(PhotoMatcher.Compare(good, noSize));
        Assert.Null(PhotoMatcher.Compare(noSize, good));
        Assert.Null(PhotoMatcher.Compare(good, otherLength));
    }

    [Theory]
    [InlineData("00000000000000FF", true)]
    [InlineData("ff00000000000000", true)]
    [InlineData("FFFF", false)]
    [InlineData("ZZZZZZZZZZZZZZZZ", false)]
    [InlineData(null, false)]
    public void Hash_Text_Must_Be_Sixteen_Hex_Characters(string? text, bool valid)
        => Assert.Equal(valid, PhotoMatcher.TryParseHash(text, out _));

    [Fact]
    public void Signature_Difference_Is_The_Mean_Absolute_Difference()
        => Assert.Equal(2.5, PhotoMatcher.SignatureDifference(new byte[] { 10, 20, 30, 40 }, new byte[] { 12, 17, 30, 45 }));
}
