using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.Matching;

/// <summary>A burst: shots taken one after another, the one to keep first.</summary>
/// <param name="Keeper">The shot kept: the sharpest, then the largest, then the first.</param>
/// <param name="Shots">Every shot, in the order taken.</param>
public sealed record Burst(Photo Keeper, IReadOnlyList<Photo> Shots)
{
    /// <summary>The shots that would go: every one but the keeper.</summary>
    public IEnumerable<Photo> Extra => Shots.Where(s => s.Id != Keeper.Id);
}

/// <summary>
/// Finds bursts among pictures: those an iPhone marks as one burst, and elsewhere three or more taken by the same
/// camera with no more than <see cref="Gap"/> between one and the next.
/// </summary>
public static class Bursts
{
    /// <summary>The longest time between two shots of one burst, where the phone does not mark it.</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(1);

    /// <summary>The fewest shots that make a burst, where the phone does not mark it.</summary>
    public const int Fewest = 3;

    /// <summary>The bursts among the pictures given, latest first; videos and screenshots are no part of one.</summary>
    public static IReadOnlyList<Burst> Of(IEnumerable<Photo> photos)
    {
        var pictures = photos.Where(p => !p.IsVideo && !p.IsScreenshot).ToList();
        var bursts = pictures.Where(p => p.BurstId is not null).GroupBy(p => p.BurstId!)
            .Where(g => g.Count() >= 2).Select(g => g.OrderBy(p => p.TakenOn).ToList()).ToList();
        foreach (var camera in pictures.Where(p => p.BurstId is null && p.TakenOn is not null).GroupBy(p => p.CameraModel ?? string.Empty))
        {
            var run = new List<Photo>();
            foreach (var shot in camera.OrderBy(p => p.TakenOn))
            {
                if (run.Count > 0 && shot.TakenOn!.Value - run[^1].TakenOn!.Value > Gap)
                {
                    if (run.Count >= Fewest) bursts.Add(run);
                    run = [];
                }
                run.Add(shot);
            }
            if (run.Count >= Fewest) bursts.Add(run);
        }
        return [.. bursts.Select(shots => new Burst(KeeperOf(shots), shots)).OrderByDescending(b => b.Shots[0].TakenOn)];
    }

    /// <summary>The shot to keep: the sharpest, then the one with the most pixels, then the first taken.</summary>
    public static Photo KeeperOf(IReadOnlyList<Photo> shots)
        => shots.OrderByDescending(s => s.Sharpness ?? -1).ThenByDescending(s => s.PixelCount).ThenBy(s => s.TakenOn).First();
}
