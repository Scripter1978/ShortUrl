using Microsoft.AspNetCore.Identity;

namespace ShortUrl.Models;

public class UrlShort
{
    public int Id { get; set; }
    public required string Code { get; set; }
    public required string UserId { get; set; }
    public IdentityUser? User { get; set; }
    public DateTime CreatedAt { get; set; } 
    public DateTime? ExpirationDate { get; set; }
    public string? Password { get; set; } // Hashed with BCrypt
    public int CurrentDestinationIndex { get; set; }
    public int CurrentOgMetadataIndex { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public required List<DestinationUrl> DestinationUrls { get; set; } = new();
    public required List<OgMetadata> OgMetadataVariations { get; set; } = new();
    public required List<ClickStat> ClickStats { get; set; } = new();
}