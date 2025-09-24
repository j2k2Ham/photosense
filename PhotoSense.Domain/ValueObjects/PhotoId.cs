namespace PhotoSense.Domain.ValueObjects;

public readonly record struct PhotoId(Guid Value)
{
    public static PhotoId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
