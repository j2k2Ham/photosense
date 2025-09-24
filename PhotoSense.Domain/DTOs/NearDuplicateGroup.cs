using PhotoSense.Domain.Entities;

namespace PhotoSense.Domain.DTOs;

public sealed record NearDuplicateGroup(string RepresentativeHash, IReadOnlyList<Photo> Photos);
