using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.DTOs;

/// <summary>How sure the match between a photo and the one being kept is.</summary>
public enum MatchKind
{
    /// <summary>The two files have the same bytes.</summary>
    Identical,
    /// <summary>Confirmed to be the same shot: a converted, resized or re-saved copy.</summary>
    SamePicture,
    /// <summary>Looks alike but is not confirmed to be the same shot (a burst frame or an edited version). Review only.</summary>
    Similar
}

/// <param name="HashDistance">Bits by which the perceptual hashes differ (0-64).</param>
/// <param name="Difference">Average difference between the two signatures, in grey levels (0-255).</param>
public sealed record DuplicateMember(Photo Photo, MatchKind Match, int HashDistance, double Difference);

/// <summary>A photo to keep and the photos matched against it. Every member was compared with the keeper itself.</summary>
public sealed record DuplicateGroup(Photo Keeper, IReadOnlyList<DuplicateMember> Members)
{
    public string Key => Keeper.Id.ToString();

    /// <summary>Members that a bulk removal would take: everything the user has not marked to keep.</summary>
    public IEnumerable<Photo> Removable => Members.Where(m => !m.Photo.IsKept).Select(m => m.Photo);

    public long ReclaimableBytes => Removable.Sum(p => p.FileSizeBytes);
}

/// <param name="Duplicates">Groups whose members are identical to, or confirmed copies of, the keeper. Safe to remove in bulk.</param>
/// <param name="Similar">Groups of look-alike photos that are different shots or edits. For review only.</param>
public sealed record DuplicateAnalysis(IReadOnlyList<DuplicateGroup> Duplicates, IReadOnlyList<DuplicateGroup> Similar);
