using LiteDB;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>
/// LiteDB works out how a type maps to a document the first time the type is used, and files that mapping
/// in its shared table before it has finished filling it in. A store created on another thread at that very
/// moment finds the half-made mapping and fails with "Member ... not found on BsonMapper". Each store
/// therefore asks for its mapping here first, one at a time, and goes on only once it is complete.
/// </summary>
internal static class LiteDbMapping
{
    private static readonly object Gate = new();

    public static void Prepare<T>()
    {
        lock (Gate) BsonMapper.Global.Entity<T>();
    }
}
