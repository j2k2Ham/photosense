using System.Globalization;
using System.Web;

namespace PhotoSense.Application.Transfer;

/// <summary>
/// What a computer's QR code says, for a phone to pair with it: which computer, where to reach it, and the secret that
/// proves the phone scanned it. Written as <c>photosense:pair?v=1&amp;id=…&amp;name=…&amp;hosts=…&amp;port=…&amp;key=…</c>.
/// </summary>
/// <param name="ComputerId">The computer's own id.</param>
/// <param name="ComputerName">What the computer is called.</param>
/// <param name="Hosts">The computer's addresses on its networks, tried in turn.</param>
/// <param name="Port">The port the computer waits for phones on.</param>
/// <param name="Secret">The secret only the QR code carries.</param>
public sealed record PairingLink(string ComputerId, string ComputerName, IReadOnlyList<string> Hosts, int Port, byte[] Secret)
{
    private const string Start = "photosense:pair?";

    /// <summary>The text of the QR code.</summary>
    public string Write()
        => $"{Start}v=1&id={Uri.EscapeDataString(ComputerId)}&name={Uri.EscapeDataString(ComputerName)}&hosts={Uri.EscapeDataString(string.Join(',', Hosts))}"
           + $"&port={Port.ToString(CultureInfo.InvariantCulture)}&key={Convert.ToBase64String(Secret).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

    /// <summary>What a QR code says, or null when it is not a PhotoSense pairing code, or one that cannot be read.</summary>
    public static PairingLink? Read(string? text)
    {
        if (text is null || !text.StartsWith(Start, StringComparison.Ordinal)) return null;
        var q = HttpUtility.ParseQueryString(text[Start.Length..]);
        if (q["v"] != "1" || q["id"] is not { Length: > 0 } id || q["name"] is not { } name || q["hosts"] is not { Length: > 0 } hosts
            || !int.TryParse(q["port"], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535 || q["key"] is not { } key)
            return null;
        var padded = key.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        var secret = new byte[padded.Length];
        if (!Convert.TryFromBase64String(padded, secret, out var length) || length < 16) return null;
        return new PairingLink(id, name, hosts.Split(','), port, secret[..length]);
    }
}
