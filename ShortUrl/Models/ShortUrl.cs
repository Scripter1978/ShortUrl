using Microsoft.AspNetCore.Identity;

namespace ShortUrl.Models;

public class UrlShort
{
    public int Id { get; set; }
    public string Code { get; set; }
    public string UserId { get; set; }
    public IdentityUser User { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public string? Password { get; set; } // Hashed with BCrypt
    public int CurrentDestinationIndex { get; set; }
    public int CurrentOgMetadataIndex { get; set; }
    public List<DestinationUrl> DestinationUrls { get; set; }
    public List<OgMetadata> OgMetadataVariations { get; set; }
    public List<ClickStat> ClickStats { get; set; }
}