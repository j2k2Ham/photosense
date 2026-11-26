using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trovolve.Api.About;
using Trovolve.Desktop.Browsing;
using Trovolve.Desktop.Imaging;
using Trovolve.Desktop.Removal;
using Trovolve.Desktop.Viewing;
using Trovolve.Domain.Configuration;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Services;
using Trovolve.Infrastructure;
using Trovolve.Infrastructure.Files;
using Trovolve.Infrastructure.Imaging;
using Trovolve.Infrastructure.Transfer;
using Trovolve.Application.Changes;
using Trovolve.Application.Plans;
using Trovolve.Application.Transfer;
using Trovolve.Domain.Repositories;

using Trovolve.Application;

namespace Trovolve.Server;

/// <summary>The services Trovolve runs on a computer.</summary>
internal static class TrovolveServices
{
    /// <summary>Trovolve as it runs on a computer, with its settings read from <paramref name="cfg"/>.</summary>
    /// <param name="s">The services to add to.</param>
    /// <param name="cfg">The program's settings.</param>
    public static IServiceCollection AddTrovolveComputer(this IServiceCollection s, IConfiguration cfg)
    {
        s.Configure<PhotoStorageOptions>(cfg.GetSection("PhotoStorage"));
        // Off unless the settings turn it on (UsageMetrics:Enabled), with the usage server's address.
        s.Configure<UsageMetricsOptions>(cfg.GetSection(UsageMetricsOptions.Section));
        s.AddSingleton<IValidateOptions<PhotoStorageOptions>, PhotoStorageOptionsValidator>();
        // The log goes to the console, as ASP.NET Core sends it, and to a file a day beside the database.
        s.AddTrovolveLogFile(sp => Path.Combine(sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.ResolveDatabaseFolder(), "logs"));
#if DEBUG
        // A development build (dev-start.ps1 makes one) offers plans for testing; a Release build, as made for others, never does.
        s.AddSingleton(new DevMode(true));
#endif
        // Where the photos are: this computer's disks. On a phone it is the phone's photo library.
        s.AddSingleton<IPhotoFileSystem, DiskFileSystem>();
        // Pictures are decoded by the system's own decoder, and measured alike whichever it is.
        s.AddSingleton(_ => ThisComputersDecoder());
        s.AddSingleton<IImageAnalyzer>(sp => new PictureAnalyzer(sp.GetRequiredService<IPictureDecoder>()));
        s.AddSingleton<ISystemViewer, ShellSystemViewer>();
        // The computer's Recycle Bin or Trash; turned off, as by the end-to-end tests, none at all.
        s.AddSingleton<IRecycleBin>(sp => sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.UseRecycleBin
            ? new ComputerRecycleBin() : new ComputerRecycleBin(_ => false, () => [], _ => { }));
        s.AddSingleton<IFolderBrowser, FileSystemFolderBrowser>();
        s.AddSingleton<IPhotoAccess, FolderAccess>();
        s.AddSingleton(ProgramInfo.From(typeof(TrovolveServices).Assembly, "server"));
        s.AddHostedService<PutBackUnfinishedChanges>();
        // Photos come from phones over the network, or through a cable by Apple's device service.
        s.AddSingleton<IPhoneFinder>(new PhoneFinders(new MdnsPhoneFinder(), new UsbmuxPhoneFinder()));
        s.AddSingleton(sp => new PhoneReceiving(sp.GetRequiredService<IPhoneFinder>(), sp.GetRequiredService<IPairingStore>(), sp.GetRequiredService<IPhotoFileSystem>(),
            sp.GetRequiredService<FileChanges>(), sp.GetRequiredService<Subscription>(), Environment.MachineName, sp.GetRequiredService<TimeProvider>()));
        // Phones back up to this computer: it waits for them on a port of its own while it runs, and says so on the network.
        s.AddSingleton(new PhoneDoor(new TcpTransferListener(TcpTransferListener.ComputerPort), new MdnsAdvertiser()));
        s.AddSingleton(sp => new ComputerBackup(sp.GetRequiredService<IBackupStore>(), sp.GetRequiredService<IPhotoFileSystem>(), sp.GetRequiredService<FileChanges>(),
            sp.GetRequiredService<Subscription>(), sp.GetRequiredService<IOptions<PhotoStorageOptions>>().Value.ResolveBackupPath(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IMediaDetailsReader>(),
            sp.GetRequiredService<IPlaceNameResolver>(), sp.GetRequiredService<TermsAcceptance>(), sp.GetRequiredService<IPairingStore>()));
        // Pair a phone: the computer shows a code, and a phone it does not know may pair with it.
        s.AddSingleton(sp => new ComputerPairing(sp.GetRequiredService<PhoneDoor>().Listener, sp.GetRequiredService<PhoneDoor>().Advertiser, sp.GetRequiredService<IPairingStore>(),
            Environment.MachineName, () => [.. MdnsPhoneFinder.Networks().Select(a => a.ToString())], sp.GetRequiredService<ComputerBackup>(), sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ComputerPairing>>()));
        s.AddHostedService<OpenToPhones>();
        s.AddHostedService<SendUsage>();
        // Sending uses is never worth stopping the program for: an error it did not expect is written to the log by the
        // host, which carries on, and sending waits for the next start.
        s.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);
        return s.AddTrovolveApplication().AddTrovolveCore();
    }

    // Windows Imaging Component on Windows, ImageIO on a Mac; anywhere else, where only the checks run, none.
    [ExcludeFromCodeCoverage(Justification = "Chooses the decoder of the system the program runs on; each choice is reached only on that system, where the picture decoders' tests run it.")]
    private static IPictureDecoder ThisComputersDecoder()
        => OperatingSystem.IsWindows() ? new WicPictureDecoder() : OperatingSystem.IsMacOS() ? new ImageIOPictureDecoder() : new UnavailablePictureDecoder();
}
