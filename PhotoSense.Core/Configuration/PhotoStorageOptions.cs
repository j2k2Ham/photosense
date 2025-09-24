namespace PhotoSense.Core.Configuration;

public class PhotoStorageOptions
{
    public string PrimaryPath { get; set; } = string.Empty;
    public string SecondaryPath { get; set; } = string.Empty;
    public string DatabasePath { get; set; } = "photosense.db";
}