namespace PhotoSense.Domain.Entities;

/// <summary>One file that a move or copy put somewhere, with what is needed to put it back.</summary>
public sealed class OrganizeBatchItem
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    // The file as it arrived; a copy that has changed since is not removed by an undo.
    public long SizeBytes { get; set; }
    public long ModifiedUtcTicks { get; set; }
    /// <summary>A Live Photo video or edit file that came along with its picture.</summary>
    public bool Companion { get; set; }
}

/// <summary>Everything one Organize move or copy did, kept so that it can be undone.</summary>
public sealed class OrganizeBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    // Ticks, because the database rounds a DateTime and hands it back as local time.
    public long UtcTicks { get; set; } = DateTime.UtcNow.Ticks;
    /// <summary>What the folder is called where the move is listed.</summary>
    public string Label { get; set; } = string.Empty;
    public bool Copy { get; set; }
    /// <summary>The files were taken out of their folders into the folder that holds removed files.</summary>
    public bool Removed { get; set; }
    /// <summary>Files asked for, not counting what came along with them.</summary>
    public int Files { get; set; }
    public long Bytes { get; set; }
    public List<OrganizeBatchItem> Items { get; set; } = [];
    /// <summary>Folders that had to be made, deepest first; an undo removes those it leaves empty.</summary>
    public List<string> CreatedFolders { get; set; } = [];
    public bool Undone { get; set; }
}
