using System.ComponentModel.DataAnnotations;

namespace ShortUrl.Models;

public class OgMetadata
{
    public int Id { get; set; }
    public int UrlShortId { get; set; }
    [StringLength(100)]
    public required string Title { get; set; }
    public required string Description { get; set; }
    public string? Image { get; set; }
}