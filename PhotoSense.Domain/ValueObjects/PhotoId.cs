namespace PhotoSense.Domain.ValueObjects;

/// <summary>Names one scanned photo's record; it stays the same when the file is moved.</summary>
/// <param name="Value">The id itself.</param>
public readonly record struct PhotoId(Guid Value)
{
    /// <summary>An id no other record has.</summary>
    public static PhotoId New() => new(Guid.NewGuid());

    /// <summary>The id as the page and the database write it.</summary>
    public override string ToString() => Value.ToString();
}
