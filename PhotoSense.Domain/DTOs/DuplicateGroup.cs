using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.DTOs;

public sealed record DuplicateGroup(string Hash, IReadOnlyList<Photo> Photos)
{
    public bool IsTrueDuplicate => Photos.Count > 1;
};
