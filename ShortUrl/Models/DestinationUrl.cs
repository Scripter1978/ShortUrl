namespace ShortUrl.Models;

public class DestinationUrl
{
    public int Id { get; set; }
    public int ShortUrlId { get; set; }
    public string Url { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public int? Weight { get; set; }
}