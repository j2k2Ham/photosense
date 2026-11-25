namespace Trovolve.Domain.Services;

/// <summary>
/// The computer's own Recycle Bin, or the Trash on a Mac: where Move to Recycle Bin sends what waits in
/// _Trovolve_Removed, to be got back until it is emptied (Jamie, 2026-11-20). A phone has none of these: it sends
/// files to its own bin through <see cref="IPhotoChangesTogether.EraseAll"/>.
/// </summary>
public interface IRecycleBin
{
    /// <summary>What the computer calls it: "Recycle Bin" on Windows, "Trash" on a Mac.</summary>
    string Name { get; }

    /// <summary>Whether files in this folder can go to it: not on a network share, nor on a drive that has none.</summary>
    bool Holds(string folder);

    /// <summary>Sends the files to it, as the computer's own Delete key would, asking nothing.</summary>
    /// <exception cref="IOException">The system would not take them; some may have gone all the same.</exception>
    void Recycle(IReadOnlyList<string> paths);
}
