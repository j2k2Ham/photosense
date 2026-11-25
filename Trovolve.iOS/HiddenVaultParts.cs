using LocalAuthentication;
using Trovolve.Domain.Services;
using Security;

namespace Trovolve.iOS;

/// <summary>
/// The hidden vault's recovery key in the iPhone's Keychain, readable only while the iPhone is unlocked and never
/// carried to another device by a backup: a new iPhone opens the vault's backups with the key written down.
/// </summary>
internal sealed class KeychainVaultKeys : IVaultKeyStore
{
    private const string Service = "com.j2k2ham.trovolve.vault";
    private const string Account = "recovery-key";

    /// <inheritdoc />
    public Task<string?> RecoveryKeyAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var found = SecKeyChain.QueryAsRecord(Query(), out var status);
        return Task.FromResult(status == SecStatusCode.Success && found?.ValueData is { } data ? NSString.FromData(data, NSStringEncoding.UTF8)?.ToString() : null);
    }

    /// <inheritdoc />
    public Task SaveRecoveryKeyAsync(string recoveryKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Replaced whole: one recovery key at a time.
        SecKeyChain.Remove(Query());
        var record = Query();
        record.ValueData = NSData.FromString(recoveryKey, NSStringEncoding.UTF8);
        record.Accessible = SecAccessible.WhenUnlockedThisDeviceOnly;
        var status = SecKeyChain.Add(record);
        if (status != SecStatusCode.Success) throw new IOException($"The iPhone's Keychain would not keep the recovery key ({status}).");
        return Task.CompletedTask;
    }

    private static SecRecord Query() => new(SecKind.GenericPassword) { Service = Service, Account = Account };
}

/// <summary>Face ID or Touch ID, with the iPhone's passcode when they fail, before the hidden vault opens.</summary>
internal sealed class DeviceUnlocker : IUnlocker
{
    /// <inheritdoc />
    /// <remarks>An iPhone with no passcode cannot prove who holds it, so the vault stays locked there.</remarks>
    public async Task<bool> UnlockAsync(string reason, CancellationToken ct)
    {
        using var context = new LAContext();
        if (!context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthentication, out _)) return false;
        // The question goes away when whoever asked stops waiting.
        using var stop = ct.Register(context.Invalidate);
        var (passed, _) = await context.EvaluatePolicyAsync(LAPolicy.DeviceOwnerAuthentication, reason);
        return passed;
    }
}
