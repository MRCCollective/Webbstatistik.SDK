namespace Webbstatistik.SDK;

public sealed class ServerPageviewTrackingOptions
{
    public const string SectionName = "Tracking:ServerPageviews";
    public string? WebbstatistikBaseUrl { get; set; }
    public string? SiteKey { get; set; }
    public string? WebsiteId { get; set; }
    public int QueueCapacity { get; set; } = 5000;
    public int MaxBatchSize { get; set; } = 100;
    public int FlushIntervalSeconds { get; set; } = 10;
    public string[] ExcludedPathPrefixes { get; set; } = ["/api"];
}
