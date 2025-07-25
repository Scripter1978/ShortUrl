namespace ShortUrl.Models;

public class ClickStat
{
    public int Id { get; set; }
    public int ShortUrlId { get; set; }
    public int? DestinationUrlId { get; set; }
    public int? OgMetadataId { get; set; }
    public DateTime ClickedAt { get; set; }
    public string? IpAddress { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Referrer { get; set; }
    public string? Device { get; set; }
    public string? Browser { get; set; }
    public string? Language { get; set; }
    public string? OperatingSystem { get; set; }
    public string? ScreenResolution { get; set; }
}