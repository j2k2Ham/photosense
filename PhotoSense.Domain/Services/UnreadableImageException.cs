namespace PhotoSense.Domain.Services;

/// <summary>A picture that its decoder could not make sense of: damaged, cut short, or in a form it does not know.</summary>
/// <remarks>
/// Each <see cref="IImageAnalyzer"/> turns its own decoder's errors into this one, so that the scan and the
/// previews catch a damaged picture without catching every error there is.
/// </remarks>
public sealed class UnreadableImageException : Exception
{
    /// <summary>A picture that could not be decoded, for the reason given.</summary>
    public UnreadableImageException(string message) : base(message) { }

    /// <summary>A picture that could not be decoded, for the reason the decoder gave.</summary>
    /// <param name="message">What went wrong, in words the person can read.</param>
    /// <param name="inner">The decoder's own error.</param>
    public UnreadableImageException(string message, Exception inner) : base(message, inner) { }
}
