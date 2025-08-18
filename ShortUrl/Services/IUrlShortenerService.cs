using ShortUrl.Models;

namespace ShortUrl.Services;

public interface IUrlShortenerService
{
    Task<string> CreateShortUrlAsync(
        string userId,
        string? customSlug = null,
        List<DestinationUrl>? destinationUrls = null,
        List<OgMetadata>? ogMetadataVariations = null,
        DateTime? expirationDate = null,
        string? password = null);

    Task<UrlShort?> GetShortUrlAsync(string code);

    Task DeleteShortUrlAsync(string code);
}