using Microsoft.EntityFrameworkCore;
using ShortUrl.Data;
using ShortUrl.Helpers;
using ShortUrl.Models;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;

namespace ShortUrl.Services
{
    public class UrlShortenerService
    {
        private readonly ApplicationDbContext _dbContext;
        private static readonly string Base62Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private readonly Random _random = new();
        private readonly List<string>? _allowedDomains;
        private readonly RateLimiter? _rateLimiter;
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public UrlShortenerService(
            ApplicationDbContext dbContext, 
            IConfiguration configuration, 
            RateLimiter? rateLimiter = null, 
            IHttpContextAccessor? httpContextAccessor = null)
        {
            _dbContext = dbContext;
            _rateLimiter = rateLimiter;
            _httpContextAccessor = httpContextAccessor;
            
            // Load allowed domains from configuration if available
            var allowedDomainsConfig = configuration["Security:AllowedRedirectDomains"];
            if (!string.IsNullOrEmpty(allowedDomainsConfig))
            {
                _allowedDomains = allowedDomainsConfig.Split(',').Select(d => d.Trim()).ToList();
            }
        }

        public async Task<string> CreateShortUrlAsync(
            string userId,
            string? customSlug = null,
            List<DestinationUrl>? destinationUrls = null,
            List<OgMetadata>? ogMetadataVariations = null,
            DateTime? expirationDate = null,
            string? password = null)
        {
            // Apply rate limiting if the rate limiter is available
            if (_rateLimiter != null && _httpContextAccessor != null)
            {
                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext != null)
                {
                    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    var isAuthenticated = httpContext.User.Identity?.IsAuthenticated ?? false;
                    
                    // Check if the user is allowed to create a URL based on rate limits
                    if (!_rateLimiter.AllowUrlCreation(ipAddress, isAuthenticated))
                    {
                        throw new InvalidOperationException("You have exceeded the rate limit for URL creation. Please try again later.");
                    }
                }
            }
            
            if (destinationUrls == null || !destinationUrls.Any())
            {
                throw new ArgumentException("At least one destination URL is required.");
            }
            
            // Validate all destination URLs
            foreach (var destinationUrl in destinationUrls)
            {
                if (string.IsNullOrWhiteSpace(destinationUrl.Url))
                {
                    throw new ArgumentException("Destination URL cannot be empty.");
                }
                
                // Check if URL is secure using the enhanced validation
                if (!InputValidation.IsSecureUrl(destinationUrl.Url, _allowedDomains))
                {
                    throw new ArgumentException($"The URL '{destinationUrl.Url}' is not allowed. Please provide a valid and secure URL.");
                }
            }

            // Check if user is a free user
            bool isFreeUser = await IsFreeUserAsync(userId);

            string code;
            if (!string.IsNullOrWhiteSpace(customSlug))
            {
                // Free users cannot use custom slugs
                if (isFreeUser)
                {
                    customSlug = null;
                    code = GenerateCode(6);
                }
                else
                {
                    if (!Regex.IsMatch(customSlug, @"^[a-zA-Z0-9\-_]+$"))
                    {
                        throw new ArgumentException("Custom slug can only contain alphanumeric characters, hyphens, and underscores.");
                    }
                    if (await _dbContext.UrlShorts.AnyAsync(s => s.Code.ToLower() == customSlug.ToLower()))
                    {
                        throw new ArgumentException("Custom slug is already in use. Please choose a different slug.");
                    }
                    code = customSlug;
                }
            }
            else
            {
                do
                {
                    code = GenerateCode(6);
                } while (await _dbContext.UrlShorts.AnyAsync(s => s.Code == code));
            }

            // Apply free user limitations
            if (isFreeUser)
            {
                // Limit to one destination URL
                if (destinationUrls.Count > 1)
                {
                    destinationUrls = new List<DestinationUrl> { destinationUrls.First() };
                }

                // Remove UTM parameters
                foreach (var url in destinationUrls)
                {
                    url.UtmSource = null;
                    url.UtmMedium = null;
                    url.UtmCampaign = null;
                }

                // No OG metadata for free users
                ogMetadataVariations = new List<OgMetadata>();

                // No expiration date for free users
                expirationDate = null;

                // No password for free users
                password = null;
            }

            var urlShort = new UrlShort
            {
                Code = code,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                ExpirationDate = expirationDate,
                Password = !string.IsNullOrEmpty(password) ? BCrypt.Net.BCrypt.HashPassword(password) : null,
                DestinationUrls = destinationUrls,
                OgMetadataVariations = ogMetadataVariations ?? new List<OgMetadata>(),
                ClickStats = new List<ClickStat>(),
                CurrentDestinationIndex = 0,
                CurrentOgMetadataIndex = 0
            };

            _dbContext.UrlShorts.Add(urlShort);
            await _dbContext.SaveChangesAsync();

            return code;
        }

        public async Task<UrlShort?> GetShortUrlAsync(string code)
        {
            return await _dbContext.UrlShorts
                .Include(u => u.DestinationUrls)
                .Include(u => u.OgMetadataVariations)
                .FirstOrDefaultAsync(s => s.Code == code && !s.IsDeleted);
        }

        public async Task DeleteShortUrlAsync(string code)
        {
            var urlShort = await _dbContext.UrlShorts
                .FirstOrDefaultAsync(s => s.Code == code && !s.IsDeleted);
            if (urlShort != null)
            {
                urlShort.IsDeleted = true;
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

        private async Task<bool> IsFreeUserAsync(string userId)
        {
            var userRoles = await _dbContext.UserRoles
                .Where(ur => ur.UserId == userId)
                .Join(_dbContext.Roles,
                    ur => ur.RoleId,
                    r => r.Id,
                    (ur, r) => r.Name)
                .ToListAsync();

            return userRoles.Contains("Free") && !userRoles.Contains("Basic");
        }
    }
}