using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests.Domain;

/// <summary>What each quality makes of a video, about what its copy takes, and how an amount of bytes is written.</summary>
public class VideoTargetsTests
{
    [Test]
    public async Task Each_Quality_Keeps_Up_To_Its_Lines_At_The_Same_Shape_And_A_Bitrate_For_Its_Pixels(CancellationToken ct)
    {
        await Assert.That(VideoTargets.For(CompressionQuality.High, 3840, 2160)).IsEqualTo(new VideoTarget(3840, 2160, 17_418_240));
        await Assert.That(VideoTargets.For(CompressionQuality.Balanced, 3840, 2160)).IsEqualTo(new VideoTarget(1920, 1080, 4_354_560));
        // Upright, the shorter side is the width.
        await Assert.That(VideoTargets.For(CompressionQuality.Small, 1080, 1920)).IsEqualTo(new VideoTarget(720, 1280, 1_935_360));
        // Already within its lines, a video keeps its frame.
        await Assert.That(VideoTargets.For(CompressionQuality.Balanced, 1280, 720)).IsEqualTo(new VideoTarget(1280, 720, 1_935_360));
        // Encoders take even sizes.
        await Assert.That(VideoTargets.For(CompressionQuality.Small, 1366, 767) is { Width: 1282, Height: 720 }).IsTrue();
        await Assert.That((VideoTargets.ShortSideOf(CompressionQuality.High), VideoTargets.ShortSideOf(CompressionQuality.Balanced), VideoTargets.ShortSideOf(CompressionQuality.Small)))
            .IsEqualTo(((int?)null, (int?)1080, (int?)720));
    }

    [Test]
    public async Task The_Saving_Is_Worked_Out_From_The_Videos_Length_And_Frame_And_Only_When_It_Is_Worth_Having(CancellationToken ct)
    {
        // A minute of 4K at Balanced: (4,354,560 + 128,000) bits a second for 60 seconds.
        await Assert.That(VideoTargets.Estimate(CompressionQuality.Balanced, 3840, 2160, 60, 400_000_000)).IsEqualTo(33_619_200);
        // Its copy would be nearly as large: not offered.
        await Assert.That(VideoTargets.Estimate(CompressionQuality.Balanced, 3840, 2160, 60, 30_000_000)).IsNull();
        // Its frame or length not known.
        await Assert.That(VideoTargets.Estimate(CompressionQuality.Small, 0, 2160, 60, 400_000_000)).IsNull();
        await Assert.That(VideoTargets.Estimate(CompressionQuality.Small, 3840, 0, 60, 400_000_000)).IsNull();
        await Assert.That(VideoTargets.Estimate(CompressionQuality.Small, 3840, 2160, null, 400_000_000)).IsNull();
        await Assert.That(VideoTargets.Estimate(CompressionQuality.Small, 3840, 2160, 0, 400_000_000)).IsNull();
    }

    [Test]
    [Arguments(0L, "1 KB")]
    [Arguments(900L, "1 KB")]
    [Arguments(1536L, "2 KB")]
    [Arguments(5L * 1024 * 1024, "5.0 MB")]
    [Arguments(222_298_112L, "212.0 MB")]
    [Arguments(2_652_142_305L, "2.47 GB")]
    public async Task An_Amount_Is_Written_As_The_Page_Writes_It(long bytes, string written, CancellationToken ct)
        => await Assert.That(Sizes.Of(bytes)).IsEqualTo(written);
}
