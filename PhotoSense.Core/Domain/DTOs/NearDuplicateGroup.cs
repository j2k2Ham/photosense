using PhotoSense.Core.Domain.Entities;

namespace PhotoSense.Core.Domain.DTOs;

public sealed record NearDuplicateGroup(string RepresentativeHash, IReadOnlyList<Photo> Photos);
