using System.Text.Json.Serialization;

namespace PhotoSense.Contracts.Duplicates;

public sealed class PhotoItemDto
{
    [JsonPropertyName("id")] public Guid? Id { get; init; }
    [JsonPropertyName("fileName")] public string? FileName { get; init; }
    [JsonPropertyName("sourcePath")] public string? SourcePath { get; init; }
    [JsonPropertyName("fileSizeBytes")] public long FileSizeBytes { get; init; }
    [JsonPropertyName("contentHash")] public string? ContentHash { get; init; }
    [JsonPropertyName("perceptualHash")] public string? PerceptualHash { get; init; }
    [JsonPropertyName("takenOn")] public DateTime? TakenOn { get; init; }
    [JsonPropertyName("cameraModel")] public string? CameraModel { get; init; }
    [JsonPropertyName("set")] public string? Set { get; init; }
    [JsonPropertyName("kept")] public bool Kept { get; init; }
}

public interface IGroupItemDto
{
    string Key { get; }
    bool Perceptual { get; }
    IReadOnlyList<PhotoItemDto> Photos { get; }
}

public sealed class ExactGroupItemDto : IGroupItemDto
{
    public ExactGroupItemDto(string key, IReadOnlyList<PhotoItemDto> photos) { Key = key; Photos = photos; }
    public string Key { get; }
    public bool Perceptual => false;
    public IReadOnlyList<PhotoItemDto> Photos { get; }
}

public sealed class NearGroupItemDto : IGroupItemDto
{
    public NearGroupItemDto(string key, IReadOnlyList<PhotoItemDto> photos, int distance) { Key = key; Photos = photos; Distance = distance; }
    public string Key { get; }
    public bool Perceptual => true;
    [JsonPropertyName("distance")] public int Distance { get; }
    public IReadOnlyList<PhotoItemDto> Photos { get; }
}

public abstract class DuplicateGroupsPageDto
{
    protected DuplicateGroupsPageDto(string mode, int page, int pageSize, int unfilteredTotal, int total, int totalPages)
    { Mode = mode; Page = page; PageSize = pageSize; UnfilteredTotal = unfilteredTotal; Total = total; TotalPages = totalPages; }
    [JsonPropertyName("mode")] public string Mode { get; }
    [JsonPropertyName("page")] public int Page { get; }
    [JsonPropertyName("pageSize")] public int PageSize { get; }
    [JsonPropertyName("unfilteredTotal")] public int UnfilteredTotal { get; }
    [JsonPropertyName("total")] public int Total { get; }
    [JsonPropertyName("totalPages")] public int TotalPages { get; }
    [JsonPropertyName("items")] public abstract IReadOnlyList<IGroupItemDto> Items { get; }
}

public sealed class ExactDuplicateGroupsPageDto : DuplicateGroupsPageDto
{
    public ExactDuplicateGroupsPageDto(int page, int pageSize, int unfilteredTotal, int total, int totalPages, IReadOnlyList<ExactGroupItemDto> items)
        : base("exact", page, pageSize, unfilteredTotal, total, totalPages) { ItemsInternal = items; }
    public IReadOnlyList<ExactGroupItemDto> ItemsInternal { get; }
    public override IReadOnlyList<IGroupItemDto> Items => ItemsInternal;
}

public sealed class NearDuplicateGroupsPageDto : DuplicateGroupsPageDto
{
    public NearDuplicateGroupsPageDto(int threshold, int page, int pageSize, int unfilteredTotal, int total, int totalPages, IReadOnlyList<NearGroupItemDto> items)
        : base("near", page, pageSize, unfilteredTotal, total, totalPages) { Threshold = threshold; ItemsInternal = items; }
    [JsonPropertyName("threshold")] public int Threshold { get; }
    public IReadOnlyList<NearGroupItemDto> ItemsInternal { get; }
    public override IReadOnlyList<IGroupItemDto> Items => ItemsInternal;
}
