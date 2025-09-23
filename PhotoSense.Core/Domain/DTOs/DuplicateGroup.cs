using PhotoSense.Core.Domain.Entities;

namespace PhotoSense.Core.Domain.DTOs;

public sealed record DuplicateGroup(string Hash, IReadOnlyList<Photo> Photos)
{
    public bool IsTrueDuplicate => Photos.Count > 1;
};
