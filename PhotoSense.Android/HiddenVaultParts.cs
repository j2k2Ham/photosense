using System.Text;
using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using PhotoSense.Domain.Services;

namespace PhotoSense.AndroidApp;

/// <summary>
/// The hidden vault's recovery key, sealed by a key the Android Keystore holds and never gives out, in a file of the
/// app's own. A backup of the app carries the file but not the Keystore's key, so a new phone opens the vault's
/// backups with the key written down.
/// </summary>
internal sealed class KeystoreVaultKeys(Context context) : IVaultKeyStore
{
    private const string Alias = "photosense-vault";
    private const string Store = "AndroidKeyStore";
    private const int NonceSize = 12;

    private string Kept => Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "vault-key.sealed");

    /// <inheritdoc />
    public async Task<string?> RecoveryKeyAsync(CancellationToken ct)
    {
        if (!File.Exists(Kept)) return null;
        var bytes = await File.ReadAllBytesAsync(Kept, ct);
        try
        {
            var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.DecryptMode, Key(), new GCMParameterSpec(128, bytes[..NonceSize]));
            return Encoding.UTF8.GetString(cipher.DoFinal(bytes[NonceSize..])!);
        }
        catch (GeneralSecurityException ex) { throw new IOException($"The recovery key kept on this phone could not be opened: {ex.Message}"); }
    }

    /// <inheritdoc />
    public async Task SaveRecoveryKeyAsync(string recoveryKey, CancellationToken ct)
    {
        byte[] sealedKey;
        try
        {
            var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.EncryptMode, Key());
            sealedKey = [.. cipher.GetIV()!, .. cipher.DoFinal(Encoding.UTF8.GetBytes(recoveryKey))!];
        }
        catch (GeneralSecurityException ex) { throw new IOException($"The phone would not keep the recovery key: {ex.Message}"); }
        // Written whole under another name first, so a key half written never stands for the vault's.
        var writing = Kept + ".writing";
        await File.WriteAllBytesAsync(writing, sealedKey, ct);
        File.Move(writing, Kept, overwrite: true);
    }

    // The Keystore's own key, made the first time.
    private static IKey Key()
    {
        var store = KeyStore.GetInstance(Store)!;
        store.Load(null);
        if (store.GetKey(Alias, null) is { } key) return key;
        var maker = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, Store)!;
        maker.Init(new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .Build());
        return maker.GenerateKey()!;
    }
}

/// <summary>Android's own fingerprint or face question, with the phone's PIN, pattern or password when they fail, before the hidden vault opens.</summary>
internal sealed class DeviceUnlocker : IUnlocker
{
    /// <inheritdoc />
    public Task<bool> UnlockAsync(string reason, CancellationToken ct)
    {
        var screen = MainActivity.Shown ?? throw new IOException("PhotoSense has to be open on the screen to unlock Hidden.");
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancel = new CancellationSignal();
        // The question goes away when whoever asked stops waiting.
        var stop = ct.Register(cancel.Cancel);
        _ = answer.Task.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
        screen.RunOnUiThread(() =>
        {
            var prompt = new BiometricPrompt.Builder(screen)
                .SetTitle(reason)
                .SetSubtitle("Use your fingerprint")
                .SetAllowedAuthenticators((int)(BiometricManagerAuthenticators.BiometricStrong | BiometricManagerAuthenticators.DeviceCredential))
                .Build();
            prompt.Authenticate(cancel, screen.MainExecutor!, new Answered(answer));
        });
        return answer.Task;
    }

    // Takes Android's answer: passed, or not; a failed try leaves the question up for another.
    private sealed class Answered(TaskCompletionSource<bool> answer) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result) => answer.TrySetResult(true);

        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString) => answer.TrySetResult(false);
    }
}
