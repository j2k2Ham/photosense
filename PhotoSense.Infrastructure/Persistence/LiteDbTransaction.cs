using LiteDB;

namespace PhotoSense.Infrastructure.Persistence;

/// <summary>Several writes to the database made as one.</summary>
internal static class LiteDbTransaction
{
    /// <summary>Makes the writes in <paramref name="work"/> together: all of them, or, should it fail part way, none.</summary>
    /// <remarks>
    /// LiteDB keeps a transaction for each thread, so the work must not await; the caller holds its own write lock
    /// meanwhile. Nothing calls this inside another of its transactions.
    /// </remarks>
    public static void Together(this LiteDatabase db, Action work)
    {
        db.BeginTrans();
        var done = false;
        try
        {
            work();
            done = db.Commit();
        }
        finally
        {
            // What failed part way is taken back whole, and the error goes on to the caller.
            if (!done) db.Rollback();
        }
    }
}
