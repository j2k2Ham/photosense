using System.Security.Cryptography;
using System.Text;

namespace Trovolve.Application.Vault;

/// <summary>
/// The hidden vault's recovery key: 160 random bits, written as 32 letters and digits in eight groups of four, which
/// the person writes down. The vault's key and its id are made from it, so that with it alone another phone can open
/// the vault's backups. Letters that look alike are read as one another (O as 0, I and L as 1), and case and dashes
/// do not matter.
/// </summary>
public sealed class RecoveryKey
{
    // Crockford's letters: no I, L, O or U, which look like others or spell words.
    private const string Letters = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Bits = 160;

    private readonly byte[] _secret;

    private RecoveryKey(byte[] secret) => _secret = secret;

    /// <summary>A new recovery key, at random.</summary>
    public static RecoveryKey New() => new(RandomNumberGenerator.GetBytes(Bits / 8));

    /// <summary>The recovery key a person typed; null for anything that is not one.</summary>
    public static RecoveryKey? Parse(string? typed)
    {
        var plain = new StringBuilder();
        foreach (var c in (typed ?? string.Empty).ToUpperInvariant())
        {
            if (c is '-' or ' ') continue;
            var letter = c switch { 'O' => '0', 'I' or 'L' => '1', _ => c };
            if (!Letters.Contains(letter)) return null;
            plain.Append(letter);
        }
        if (plain.Length != Bits / 5) return null;
        var secret = new byte[Bits / 8];
        var bit = 0;
        foreach (var c in plain.ToString())
        {
            var value = Letters.IndexOf(c);
            for (var i = 4; i >= 0; i--, bit++)
                if ((value >> i & 1) == 1) secret[bit / 8] |= (byte)(0x80 >> bit % 8);
        }
        return new RecoveryKey(secret);
    }

    /// <summary>The key that seals the vault's files.</summary>
    public byte[] VaultKey => HKDF.DeriveKey(HashAlgorithmName.SHA256, _secret, 32, info: "Trovolve hidden vault key"u8.ToArray());

    /// <summary>What the vault's files say they were sealed with, so that the computer can find a phone's own without the key.</summary>
    public byte[] Id => HKDF.DeriveKey(HashAlgorithmName.SHA256, _secret, 16, info: "Trovolve hidden vault id"u8.ToArray());

    /// <summary>The key as the person writes it down: "K7QP-2M9X-…".</summary>
    public override string ToString()
    {
        var written = new StringBuilder();
        for (var bit = 0; bit < Bits; bit += 5)
        {
            var value = 0;
            for (var i = 0; i < 5; i++) value = value << 1 | (_secret[(bit + i) / 8] >> 7 - (bit + i) % 8 & 1);
            if (bit > 0 && bit % 20 == 0) written.Append('-');
            written.Append(Letters[value]);
        }
        return written.ToString();
    }
}
