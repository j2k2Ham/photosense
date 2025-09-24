namespace PhotoSense.Core.Configuration;

public class MessagingOptions
{
    public string ServiceBusConnection { get; set; } = string.Empty; // connection string
    public string TopicName { get; set; } = "photosense-events";
}
