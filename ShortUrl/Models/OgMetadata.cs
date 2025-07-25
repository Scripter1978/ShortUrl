namespace ShortUrl.Models;

public class OgMetadata
{
    public int Id { get; set; }
    public int ShortUrlId { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string? Image { get; set; }
}