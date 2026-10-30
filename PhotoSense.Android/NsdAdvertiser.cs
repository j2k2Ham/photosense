using Android.Content;
using Android.Net.Nsd;
using Android.Util;
using PhotoSense.Domain.Services;

namespace PhotoSense.AndroidApp;

/// <summary>
/// Tells the computers on the Wi-Fi, with Android's own Bonjour (network service discovery), that this phone is
/// offering photos and where it waits. While it does, the screen stays on: a locked phone stops the app, and the
/// sending with it.
/// </summary>
public sealed class NsdAdvertiser(Context context) : ITransferAdvertiser
{
    private readonly object _lock = new();
    private Registered? _registered;

    public void Start(string id, string name, int port)
    {
        Stop();
        var service = new NsdServiceInfo { ServiceName = name, ServiceType = "_photosense._tcp", Port = port };
        service.SetAttribute("id", id);
        service.SetAttribute("name", name);
        var registered = new Registered();
        Manager.RegisterService(service, NsdProtocol.DnsSd, registered);
        lock (_lock) _registered = registered;
        MainActivity.KeepScreenOn(true);
    }

    public void Stop()
    {
        Registered? registered;
        lock (_lock) (registered, _registered) = (_registered, null);
        if (registered is null) return;
        try { Manager.UnregisterService(registered); }
        // Never registered after all: there is nothing to take back.
        catch (Java.Lang.IllegalArgumentException) { }
        MainActivity.KeepScreenOn(false);
    }

    private NsdManager Manager => (NsdManager)context.GetSystemService(Context.NsdService)!;

    private sealed class Registered : Java.Lang.Object, NsdManager.IRegistrationListener
    {
        public void OnServiceRegistered(NsdServiceInfo? serviceInfo) => Log.Info("PhotoSense", $"offering photos as {serviceInfo?.ServiceName}");
        public void OnRegistrationFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode) => Log.Warn("PhotoSense", $"could not offer photos on the network: {errorCode}");
        public void OnServiceUnregistered(NsdServiceInfo? serviceInfo) { }
        public void OnUnregistrationFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode) { }
    }
}
