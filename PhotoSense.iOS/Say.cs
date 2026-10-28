namespace PhotoSense.iOS;

/// <summary>A line on the app's console, which the simulator check keeps (simctl launch --stdout) to show how far it got.</summary>
public static class Say
{
    public static void Line(string text) => Console.WriteLine($"PhotoSense {DateTime.UtcNow:HH:mm:ss.fff} {text}");
}
