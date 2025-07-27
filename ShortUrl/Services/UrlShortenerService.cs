using Microsoft.EntityFrameworkCore;
using ShortUrl.Data;
using ShortUrl.Models;
using System.Text.RegularExpressions;

namespace ShortUrl.Services
{
    public class UrlShortenerService
    {
        private readonly ApplicationDbContext _dbContext;
        private static readonly string Base62Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private readonly Random _random = new();

        public UrlShortenerService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<string> CreateShortUrlAsync(
            string userId,
            string? customSlug = null,
            List<DestinationUrl> destinationUrls = null,
            List<OgMetadata> ogMetadataVariations = null,
            DateTime? expirationDate = null,
            string? password = null)
        {
            if (destinationUrls == null || !destinationUrls.Any())
            {
                throw new ArgumentException("At least one destination URL is required.");
            }

            string code;
            if (!string.IsNullOrWhiteSpace(customSlug))
            {
                if (!Regex.IsMatch(customSlug, @"^[a-zA-Z0-9\-_]+$"))
                {
                    throw new ArgumentException("Custom slug can only contain alphanumeric characters, hyphens, and underscores.");
                }
                if (await _dbContext.ShortUrls.AnyAsync(s => s.Code.ToLower() == customSlug.ToLower()))
                {
                    throw new ArgumentException("Custom slug is already in use. Please choose a different slug.");
                }
                code = customSlug;
            }
            else
            {
                do
                {
                    code = GenerateCode(6);
                } while (await _dbContext.ShortUrls.AnyAsync(s => s.Code == code));
            }

            var shortUrl = new UrlShort
            {
                Code = code,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                ExpirationDate = expirationDate,
                Password = password,
                DestinationUrls = destinationUrls ?? new List<DestinationUrl>(),
                OgMetadataVariations = ogMetadataVariations ?? new List<OgMetadata>(),
                CurrentDestinationIndex = 0,
                CurrentOgMetadataIndex = 0
            };

            _dbContext.ShortUrls.Add(shortUrl);
            await _dbContext.SaveChangesAsync();

            return code;
        }

        public async Task DeleteShortUrlAsync(string code)
        {
            var shortUrl = await _dbContext.ShortUrls
                .FirstOrDefaultAsync(s => s.Code == code && !s.IsDeleted);
            if (shortUrl != null)
            {
                shortUrl.IsDeleted = true;
                await _dbContext.SaveChangesAsync();
            }
        }

        private string GenerateCode(int length)
        {
            var code = new char[length];
            for (int i = 0; i < length; i++)
            {
                code[i] = Base62Chars[_random.Next(Base62Chars.Length)];
            }
            return new string(code);
        }
    }
}