namespace PhotoSense.Domain.Entities;

/// <summary>
/// How far a change has got. A change is written down before any file is touched, so one that a stop of
/// the service cut short is found at the next start and put back as it was before it began.
/// </summary>
public enum ChangeState
{
    /// <summary>Carried out in full. Records written before changes were written down first read as this, as they were only kept once done.</summary>
    Done,
    /// <summary>Being carried out.</summary>
    Started,
    /// <summary>Being undone.</summary>
    Undoing,
}

/// <summary>One file that a move, copy or removal put somewhere, with what is needed to put it back.</summary>
public sealed class OrganizeBatchItem
{
    /// <summary>Where the file was.</summary>
    public string From { get; set; } = string.Empty;
    /// <summary>Where the change put it.</summary>
    public string To { get; set; } = string.Empty;
    /// <summary>The file's size as it arrived; a copy that has changed since is not removed by an undo.</summary>
    public long SizeBytes { get; set; }
    /// <summary>When the file last changed, as it arrived, in UTC ticks.</summary>
    public long ModifiedUtcTicks { get; set; }
    /// <summary>A Live Photo video or edit file that came along with its picture.</summary>
    public bool Companion { get; set; }
    /// <summary>
    /// Where an undo under way is taking the file: for a file that was moved, its old place, numbered when
    /// another file has taken that name since; for a copy, a hidden name beside it until the copy is deleted.
    /// </summary>
    public string? Back { get; set; }
}

/// <summary>Everything one move, copy or removal did, kept so that it can be undone.</summary>
public sealed class OrganizeBatch
{
    /// <summary>Tells this change from every other.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>When the change was started, in UTC ticks; the newest change is listed first.</summary>
    /// <remarks>Ticks, because the database rounds a DateTime and hands it back as local time.</remarks>
    public required long UtcTicks { get; set; }
    /// <summary>What the folder is called where the move is listed.</summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>The files were copied, not moved, so undoing deletes the copies.</summary>
    public bool Copy { get; set; }
    /// <summary>The files were taken out of their folders into the folder that holds removed files.</summary>
    public bool Removed { get; set; }
    /// <summary>Files asked for, not counting what came along with them.</summary>
    public int Files { get; set; }
    /// <summary>The bytes of every file the change took, companions included.</summary>
    public long Bytes { get; set; }
    /// <summary>Every file the change put somewhere, companions included.</summary>
    public List<OrganizeBatchItem> Items { get; set; } = [];
    /// <summary>Folders that had to be made, deepest first; an undo removes those it leaves empty.</summary>
    public List<string> CreatedFolders { get; set; } = [];
    /// <summary>The change has been undone, so it is no longer offered.</summary>
    public bool Undone { get; set; }
    /// <summary>How far the change has got.</summary>
    public ChangeState State { get; set; }
    /// <summary>Folders an undo under way has to make again, because the folders the files came from have gone since; deepest first.</summary>
    public List<string> UndoFolders { get; set; } = [];
}
