namespace PhotoSense.Server;

// Starts the service and waits for it to be stopped; everything it starts is built and tested in PhotoSenseServer.
/// <summary>Where the service starts.</summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal static class Program
{
    /// <summary>Runs the service until it is stopped.</summary>
    /// <param name="args">As given on the command line: <c>--urls</c>, <c>--Cors:Origins:0=</c> and any other setting.</param>
    public static void Main(string[] args) => PhotoSenseServer.Build(args).Run();
}
