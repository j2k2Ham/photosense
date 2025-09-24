namespace PhotoSense.Domain.Configuration;

public class MessagingOptions
{
    public string ServiceBusConnection { get; set; } = string.Empty;
    public string TopicName { get; set; } = "photosense-events";
}
