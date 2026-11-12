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

    /// <summary>Tells the network the phone offers photos on a port, under its id and name.</summary>
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

    /// <summary>Stops telling the network; nothing changes when it was not telling it.</summary>
    public void Stop()
    {
        Registered? registered;
        lock (_lock) (registered, _registered) = (_registered, null);
        if (registered is null) return;
        _ = TryUnregister(registered);
        MainActivity.KeepScreenOn(false);
    }

    /// <summary>Whether the network was told to forget the phone; false when it was never registered after all, and there is nothing to take back.</summary>
    private bool TryUnregister(Registered registered)
    {
        try
        {
            Manager.UnregisterService(registered);
            return true;
        }
        catch (Java.Lang.IllegalArgumentException)
        {
            return false;
        }
    }

    private NsdManager Manager => (NsdManager)context.GetSystemService(Context.NsdService)!;

    private sealed class Registered : Java.Lang.Object, NsdManager.IRegistrationListener
    {
        /// <summary>Notes that the network now knows of the phone.</summary>
        public void OnServiceRegistered(NsdServiceInfo? serviceInfo) => Log.Info("PhotoSense", $"offering photos as {serviceInfo?.ServiceName}");
        /// <summary>Notes that the network could not be told.</summary>
        public void OnRegistrationFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode) => Log.Warn("PhotoSense", $"could not offer photos on the network: {errorCode}");
        /// <summary>Notes that the network has forgotten the phone.</summary>
        public void OnServiceUnregistered(NsdServiceInfo? serviceInfo) { }
        /// <summary>Notes that the network could not be told to forget the phone.</summary>
        public void OnUnregistrationFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode) { }
    }
}
