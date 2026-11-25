using System.Text.Json;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Trovolve.Domain.Services;

namespace Trovolve.AndroidApp;

/// <summary>
/// The backup locations' keys, passwords and tokens, each location's sealed by a key the Android Keystore holds and
/// never gives out, in a file of the app's own where Android's backup of the app does not go.
/// </summary>
internal sealed class KeystoreLocationSecrets(Context context) : ILocationSecrets
{
    private const string Alias = "trovolve-locations";
    private const string Store = "AndroidKeyStore";
    private const int NonceSize = 12;

    private string Folder => Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "locations");

    // A location's id names its file; it is Trovolve's own, so nothing in it can climb out of the folder.
    private string Kept(string id) => Path.Combine(Folder, $"{string.Concat(id.Where(char.IsLetterOrDigit))}.sealed");

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> ReadAsync(string id, CancellationToken ct)
    {
        if (!File.Exists(Kept(id))) return new Dictionary<string, string>();
        var bytes = await File.ReadAllBytesAsync(Kept(id), ct);
        try
        {
            var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.DecryptMode, Key(), new GCMParameterSpec(128, bytes[..NonceSize]));
            return JsonSerializer.Deserialize<Dictionary<string, string>>(cipher.DoFinal(bytes[NonceSize..])!) ?? [];
        }
        catch (GeneralSecurityException ex) { throw new IOException($"The location's keys kept on this phone could not be opened: {ex.Message}"); }
    }

    /// <inheritdoc />
    public async Task SaveAsync(string id, IReadOnlyDictionary<string, string> secrets, CancellationToken ct)
    {
        byte[] sealedSecrets;
        try
        {
            var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
            cipher.Init(CipherMode.EncryptMode, Key());
            sealedSecrets = [.. cipher.GetIV()!, .. cipher.DoFinal(JsonSerializer.SerializeToUtf8Bytes(secrets))!];
        }
        catch (GeneralSecurityException ex) { throw new IOException($"The phone would not keep the location's keys: {ex.Message}"); }
        Directory.CreateDirectory(Folder);
        // Written whole under another name first, so keys half written never stand for a location's.
        var writing = Kept(id) + ".writing";
        await File.WriteAllBytesAsync(writing, sealedSecrets, ct);
        File.Move(writing, Kept(id), overwrite: true);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        File.Delete(Kept(id));
        return Task.CompletedTask;
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

/// <summary>
/// Signing in to a cloud service in a Custom Tab, the browser drawn over Trovolve: the person signs in on the
/// service's page, and the service sends them back to <see cref="SignInReturn"/>. Coming back to Trovolve any other
/// way closes the sign-in.
/// </summary>
internal sealed class WebSignIn : IWebSignIn
{
    private static TaskCompletionSource<Uri>? _waiting;

    /// <inheritdoc />
    public Task<Uri> SignInAsync(Uri start, string callbackScheme, CancellationToken ct)
    {
        var screen = MainActivity.Shown ?? throw new IOException("Trovolve has to be open on the screen to sign in.");
        var answer = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        // A sign-in begun before and never finished ends: no token of its caller's was cancelled.
        Interlocked.Exchange(ref _waiting, answer)?.TrySetCanceled(CancellationToken.None);
        ct.Register(() => answer.TrySetCanceled(ct));
        var open = new Intent(Intent.ActionView, Android.Net.Uri.Parse(start.AbsoluteUri));
        // Asks the browser for a Custom Tab, without the support library: an empty session says so.
        var extras = new Bundle();
        extras.PutBinder("android.support.customtabs.extra.SESSION", null);
        open.PutExtras(extras);
        screen.RunOnUiThread(() => screen.StartActivity(open));
        return answer.Task;
    }

    /// <summary>The service sent the person back with this address.</summary>
    public static void Returned(Uri back) => Interlocked.Exchange(ref _waiting, null)?.TrySetResult(back);

    /// <summary>
    /// Trovolve is on the screen again: when the service has not sent the person back by now, they closed the
    /// browser, and the sign-in ends.
    /// </summary>
    public static void Resumed()
    {
        if (Volatile.Read(ref _waiting) is not { } waiting) return;
        // The return arrives just before Trovolve shows again.
        new Handler(Looper.MainLooper!).PostDelayed(() =>
        {
            if (Interlocked.CompareExchange(ref _waiting, null, waiting) == waiting) waiting.TrySetCanceled();
        }, 1000);
    }
}

/// <summary>Where a cloud service sends the person back after signing in: hands the address over, and shows Trovolve again.</summary>
[Activity(Exported = true, NoHistory = true, Theme = "@android:style/Theme.Translucent.NoTitleBar")]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "com.j2k2ham.trovolve.auth")]
public sealed class SignInReturn : Activity
{
    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (Intent?.Data?.ToString() is { } back) WebSignIn.Returned(new Uri(back));
        StartActivity(new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop));
        Finish();
    }
}
