using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Net;

namespace ShortUrl.Helpers;

/// <summary>
/// Rate limiter to prevent DoS attacks while not impacting legitimate users
/// </summary>
public class RateLimiter
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<RateLimiter> _logger;
    
    // Constants for rate limiting
    private const int REDIRECTION_LIMIT_ANONYMOUS = 60; // 60 requests per minute for anonymous users
    private const int REDIRECTION_LIMIT_AUTHENTICATED = 200; // 200 requests per minute for authenticated users
    private const int URL_CREATION_LIMIT_ANONYMOUS = 5; // 5 requests per minute for anonymous users
    private const int URL_CREATION_LIMIT_AUTHENTICATED = 20; // 20 requests per minute for authenticated users
    private const int API_LIMIT_ANONYMOUS = 30; // 30 requests per minute for anonymous users
    private const int API_LIMIT_AUTHENTICATED = 100; // 100 requests per minute for authenticated users
    private const int QR_LIMIT_ANONYMOUS = 15; // 15 requests per minute for anonymous users
    private const int QR_LIMIT_AUTHENTICATED = 50; // 50 requests per minute for authenticated users
    
    public RateLimiter(IMemoryCache cache, ILogger<RateLimiter> logger)
    {
        _cache = cache;
        _logger = logger;
    }
    
    /// <summary>
    /// Check if the user is allowed to be redirected based on rate limits
    /// </summary>
    public bool AllowRedirection(string ipAddress, bool isAuthenticated)
    {
        var cacheKey = $"redirection_limit_{ipAddress}";
        var limit = isAuthenticated ? REDIRECTION_LIMIT_AUTHENTICATED : REDIRECTION_LIMIT_ANONYMOUS;
        
        return IsRequestAllowed(cacheKey, limit);
    }
    
    /// <summary>
    /// Check if the user is allowed to create a URL based on rate limits
    /// </summary>
    public bool AllowUrlCreation(string ipAddress, bool isAuthenticated)
    {
        var cacheKey = $"url_creation_limit_{ipAddress}";
        var limit = isAuthenticated ? URL_CREATION_LIMIT_AUTHENTICATED : URL_CREATION_LIMIT_ANONYMOUS;
        
        return IsRequestAllowed(cacheKey, limit);
    }
    
    /// <summary>
    /// Check if the user is allowed to access an API endpoint based on rate limits
    /// </summary>
    public bool AllowApiAccess(string ipAddress, bool isAuthenticated)
    {
        var cacheKey = $"api_limit_{ipAddress}";
        var limit = isAuthenticated ? API_LIMIT_AUTHENTICATED : API_LIMIT_ANONYMOUS;
        
        return IsRequestAllowed(cacheKey, limit);
    }
    
    /// <summary>
    /// Check if the user is allowed to generate QR codes based on rate limits
    /// </summary>
    public bool AllowQrGeneration(string ipAddress, bool isAuthenticated)
    {
        var cacheKey = $"qr_limit_{ipAddress}";
        var limit = isAuthenticated ? QR_LIMIT_AUTHENTICATED : QR_LIMIT_ANONYMOUS;
        
        return IsRequestAllowed(cacheKey, limit);
    }
    
    private bool IsRequestAllowed(string cacheKey, int limit)
    {
        // Get the current request count for this IP
        var requestInfo = _cache.GetOrCreate(cacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return new RequestInfo { Count = 0, FirstRequestTime = DateTime.UtcNow };
        });
        
        // Increment the request count
        requestInfo.Count++;
        
        // If we've exceeded the limit, deny the request
        if (requestInfo.Count > limit)
        {
            _logger.LogWarning($"Rate limit exceeded for {cacheKey}, count: {requestInfo.Count}");
            return false;
        }
        
        return true;
    }
    
    private class RequestInfo
    {
        public int Count { get; set; }
        public DateTime FirstRequestTime { get; set; }
    }
}
