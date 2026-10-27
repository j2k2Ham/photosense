namespace PhotoSense.Server;

// Starts the service and waits for it to be stopped; everything it starts is built and tested in PhotoSenseServer.
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static class Program
{
    public static void Main(string[] args) => PhotoSenseServer.Build(args).Run();
}
