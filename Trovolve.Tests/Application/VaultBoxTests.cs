using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Trovolve.Application.Vault;
using Trovolve.Infrastructure.Vault;

namespace Trovolve.Tests.Application;

/// <summary>
/// How an item of the hidden vault is sealed in its file, and read back a part at a time; the recovery key, as it is
/// written down and typed; and the vault's folder on a phone.
/// </summary>
public sealed class VaultBoxTests
{
    private static readonly RecoveryKey Key = RecoveryKey.New();

    /// <summary>A vault file holding these files, then a preview, sealed with <see cref="Key"/>.</summary>
    private static async Task<(byte[] File, VaultMeta Meta)> SealedAsync(CancellationToken ct, params byte[][] files)
    {
        using var output = new MemoryStream();
        var parts = new List<VaultPart>();
        using (var writer = new VaultBox.Writer(output, Key.VaultKey, Key.Id))
        {
            foreach (var (bytes, i) in files.Select((b, i) => (b, i)))
            {
                var at = writer.Position;
                var sha = await writer.CopyAsync(new MemoryStream(bytes), ct);
                parts.Add(new VaultPart(VaultBox.FilePart, $"IMG_{i}.JPG", at, writer.Position - at, sha));
            }
            var previewAt = writer.Position;
            await writer.WriteAsync("preview"u8.ToArray(), ct);
            parts.Add(new VaultPart(VaultBox.ThumbnailPart, string.Empty, previewAt, 7, null));
            await writer.CompleteAsync(new VaultMeta("IMG_0.JPG", "/Photo Library", new DateTime(2026, 3, 2, 14, 31, 7), 45.5, -110.2, false, new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc), parts), ct);
        }
        return (output.ToArray(), new VaultMeta("IMG_0.JPG", "/Photo Library", null, null, null, false, default, parts));
    }

    private static Task<VaultBox.Reader> OpenAsync(byte[] file, CancellationToken ct) => VaultBox.Reader.OpenAsync(new MemoryStream(file), Key.VaultKey, Key.Id, ct);

    [Test]
    public async Task What_Is_Sealed_Over_Many_Pieces_Reads_Back_Whole_Any_Part_On_Its_Own(CancellationToken ct)
    {
        var big = RandomNumberGenerator.GetBytes(VaultBox.PieceSize * 2 + 1234);
        var small = RandomNumberGenerator.GetBytes(10);
        var (file, written) = await SealedAsync(ct, big, small);
        // Nothing of what it holds is to be read in the file.
        await Assert.That(file.AsSpan().IndexOf(big.AsSpan(0, 64))).IsEqualTo(-1);
        await Assert.That(await VaultBox.KeyIdOfAsync(new MemoryStream(file), ct)).IsEqualTo(Convert.ToHexStringLower(Key.Id));

        using var reader = await OpenAsync(file, ct);
        var meta = await reader.MetaAsync(ct);
        await Assert.That((meta.Name, meta.Folder, meta.Taken, meta.Latitude, meta.Longitude, meta.Bytes)).IsEqualTo(("IMG_0.JPG", "/Photo Library", new DateTime(2026, 3, 2, 14, 31, 7), 45.5, -110.2, big.LongLength + 10));
        await Assert.That(meta.Files.Select(f => f.Name)).IsEquivalentTo(["IMG_0.JPG", "IMG_1.JPG"], CollectionOrdering.Matching);
        await Assert.That(meta.Preview(VaultBox.ViewPart)).IsNull();
        var preview = meta.Preview(VaultBox.ThumbnailPart)!;
        await Assert.That(await reader.ReadAsync(preview.Offset, (int)preview.Length, ct)).IsEquivalentTo("preview"u8.ToArray(), CollectionOrdering.Matching);
        // Across the end of one piece and into the next.
        await Assert.That(await reader.ReadAsync(VaultBox.PieceSize - 3, 6, ct)).IsEquivalentTo(big[(VaultBox.PieceSize - 3)..(VaultBox.PieceSize + 3)], CollectionOrdering.Matching);

        using var copy = new MemoryStream();
        await Assert.That(await reader.CopyToAsync(written.Parts[0], copy, ct)).IsEqualTo(written.Parts[0].Sha256);
        await Assert.That(copy.ToArray()).IsEquivalentTo(big, CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Part_Reads_As_A_Stream_From_Its_Start_Waiting_Or_Not(CancellationToken ct)
    {
        var big = RandomNumberGenerator.GetBytes(VaultBox.PieceSize + 500);
        var (file, written) = await SealedAsync(ct, [1, 2, 3], big);
        using var reader = await OpenAsync(file, ct);

        await using var part = reader.OpenPart(written.Parts[1]);
        await Assert.That((part.CanRead, part.CanSeek, part.CanWrite, part.Length, part.Position)).IsEqualTo((true, false, false, big.LongLength, 0L));
        using var copy = new MemoryStream();
        await part.CopyToAsync(copy, ct);
        await Assert.That(copy.ToArray()).IsEquivalentTo(big, CollectionOrdering.Matching);
        await Assert.That(await part.ReadAsync(new byte[10], ct)).IsEqualTo(0);

        // As Android copies a file: without waiting, a buffer at a time.
        using var plain = reader.OpenPart(written.Parts[1]);
        using var copied = new MemoryStream();
        plain.CopyTo(copied);
        await Assert.That(copied.ToArray()).IsEquivalentTo(big, CollectionOrdering.Matching);
        await Assert.That((plain.Read(new byte[10], 0, 10), plain.Read([], 0, 0))).IsEqualTo((0, 0));
        using var first = reader.OpenPart(written.Parts[0]);
        var three = new byte[3];
        await Assert.That(await first.ReadAsync(three, 0, 3, ct)).IsEqualTo(3);
        await Assert.That(three).IsEquivalentTo(new byte[] { 1, 2, 3 }, CollectionOrdering.Matching);

        first.Flush();
        await Assert.That(() => first.Position = 1).Throws<NotSupportedException>();
        await Assert.That(() => first.Seek(0, SeekOrigin.Begin)).Throws<NotSupportedException>();
        await Assert.That(() => first.SetLength(1)).Throws<NotSupportedException>();
        await Assert.That(() => first.Write([1], 0, 1)).Throws<NotSupportedException>();
    }

    [Test]
    public async Task A_File_Changed_Cut_Short_Sealed_With_Another_Key_Or_Not_Of_The_Vault_Is_Refused(CancellationToken ct)
    {
        var (file, _) = await SealedAsync(ct, RandomNumberGenerator.GetBytes(VaultBox.PieceSize + 10));

        var changed = file.ToArray();
        changed[VaultBox.HeaderSize + 3] ^= 1;
        using (var reader = await OpenAsync(changed, ct))
            await Assert.That(async () => await reader.ReadAsync(0, 1, ct)).Throws<InvalidDataException>().WithMessage("This file of the hidden vault is damaged.");
        // Cut after its first piece: that piece was not sealed as the last.
        using (var reader = await OpenAsync(file[..(VaultBox.HeaderSize + VaultBox.PieceSize + 16)], ct))
            await Assert.That(async () => await reader.MetaAsync(ct)).Throws<InvalidDataException>();
        using (var reader = await OpenAsync(file, ct))
            await Assert.That(async () => await reader.ReadAsync(-1, 1, ct)).Throws<InvalidDataException>();

        var other = RecoveryKey.New();
        await Assert.That(async () => await VaultBox.Reader.OpenAsync(new MemoryStream(file), other.VaultKey, other.Id, ct)).Throws<InvalidDataException>()
            .WithMessage("This file of the hidden vault was sealed with another recovery key.");
        foreach (var notVault in new[] { new byte[10], new byte[100] })
        {
            await Assert.That(async () => await OpenAsync(notVault, ct)).Throws<InvalidDataException>().WithMessage("This is not a file of the hidden vault.");
            await Assert.That(await VaultBox.KeyIdOfAsync(new MemoryStream(notVault), ct)).IsNull();
        }
        var noHeader = file.ToArray();
        noHeader[0] = (byte)'X';
        await Assert.That(async () => await OpenAsync(noHeader, ct)).Throws<InvalidDataException>();
    }

    /// <summary>A vault file sealed by hand, in one piece, holding exactly <paramref name="plain"/>.</summary>
    private static byte[] Raw(byte[] plain)
    {
        var header = new byte[VaultBox.HeaderSize];
        "PSV1"u8.CopyTo(header);
        Key.Id.CopyTo(header, 4);
        RandomNumberGenerator.Fill(header.AsSpan(20, 8));
        var nonce = new byte[12];
        header.AsSpan(20, 8).CopyTo(nonce);
        using var aes = new AesGcm(Key.VaultKey, 16);
        var sealedPiece = new byte[plain.Length + 16];
        aes.Encrypt(nonce, plain, sealedPiece.AsSpan(0, plain.Length), sealedPiece.AsSpan(plain.Length), [.. header, 1]);
        return [.. header, .. sealedPiece];
    }

    private static byte[] Length(int length)
    {
        var said = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(said, length);
        return said;
    }

    [Test]
    public async Task What_The_Item_Is_Must_Be_There_And_Fit_In_The_File(CancellationToken ct)
    {
        foreach (var plain in new[] { Length(0), Length(1_000_000), [.. "null"u8, .. Length(4)] })
        {
            using var reader = await OpenAsync(Raw(plain), ct);
            await Assert.That(async () => await reader.MetaAsync(ct)).Throws<InvalidDataException>().WithMessage("This file of the hidden vault is damaged.");
        }
    }

    [Test]
    public async Task A_Recovery_Key_Is_Written_In_Eight_Groups_And_Read_Back_However_It_Is_Typed(CancellationToken ct)
    {
        var key = RecoveryKey.New();
        var written = key.ToString();
        await Assert.That(written).Matches("^([0-9A-HJKMNP-TV-Z]{4}-){7}[0-9A-HJKMNP-TV-Z]{4}$");
        var typed = RecoveryKey.Parse(" " + written.Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant())!;
        await Assert.That((typed.ToString(), Convert.ToHexString(typed.VaultKey), Convert.ToHexString(typed.Id)))
            .IsEqualTo((written, Convert.ToHexString(key.VaultKey), Convert.ToHexString(key.Id)));
        await Assert.That(key.VaultKey.Length + key.Id.Length).IsEqualTo(48);
        await Assert.That(Convert.ToHexString(RecoveryKey.New().Id)).IsNotEqualTo(Convert.ToHexString(key.Id));

        // Letters that look alike are read as one another.
        await Assert.That(RecoveryKey.Parse(new string('O', 32))!.ToString()).IsEqualTo(RecoveryKey.Parse(new string('0', 32))!.ToString());
        await Assert.That(RecoveryKey.Parse(new string('I', 16) + new string('l', 16))!.ToString()).IsEqualTo(RecoveryKey.Parse(new string('1', 32))!.ToString());
        foreach (var wrong in new[] { null, "", new string('0', 31), new string('0', 33), new string('U', 32), written[..^1] + "!" })
            await Assert.That(RecoveryKey.Parse(wrong)).IsNull();
        await Task.CompletedTask.WaitAsync(ct);
    }

    [Test]
    public async Task The_Vaults_Folder_Shows_Only_Files_Written_Whole_And_Takes_Only_Plain_Names(CancellationToken ct)
    {
        var folder = Path.Combine(Path.GetTempPath(), "trovolve-vaultfiles-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vault = new FolderVaultFiles(folder);
            await Assert.That(vault.List()).IsEmpty();
            vault.Delete("a.psv");

            await vault.WriteAsync("a.psv", async writing =>
            {
                await writing.WriteAsync("sealed"u8.ToArray(), ct);
                // Not there until written whole.
                await Assert.That(vault.List()).IsEmpty();
            }, ct);
            await vault.WriteAsync("b.psv", writing => writing.WriteAsync("b"u8.ToArray(), ct).AsTask(), ct);
            // Cut short: nothing of it stays.
            await Assert.That(async () => await vault.WriteAsync("c.psv", async writing =>
            {
                await writing.WriteAsync("c"u8.ToArray(), ct);
                throw new IOException("The phone is full.");
            }, ct)).Throws<IOException>().WithMessage("The phone is full.");
            await Assert.That(vault.List().Select(f => (f.Name, f.Length))).IsEquivalentTo([("a.psv", 6L), ("b.psv", 1L)], CollectionOrdering.Matching);
            await using (var read = vault.OpenRead("a.psv"))
            {
                using var copy = new MemoryStream();
                await read.CopyToAsync(copy, ct);
                await Assert.That(copy.ToArray()).IsEquivalentTo("sealed"u8.ToArray(), CollectionOrdering.Matching);
            }
            await Assert.That(async () => await vault.WriteAsync("a.psv", _ => Task.CompletedTask, ct)).Throws<IOException>().WithMessage("The hidden vault has a file called a.psv already.");
            await Assert.That(Directory.GetFiles(folder).Select(f => Path.GetFileName(f)!)).IsEquivalentTo(["a.psv", "b.psv"], CollectionOrdering.Any);
            foreach (var bad in new[] { "", "..", ".", "../a.psv", "x.psv.writing" })
                await Assert.That(() => vault.OpenRead(bad)).Throws<ArgumentException>();

            vault.Delete("a.psv");
            await Assert.That(vault.List().Select(f => f.Name)).IsEquivalentTo(["b.psv"]);
            await Assert.That(() => vault.OpenRead("a.psv")).Throws<FileNotFoundException>();
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}
