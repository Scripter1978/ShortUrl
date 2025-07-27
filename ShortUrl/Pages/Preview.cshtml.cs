using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ShortUrl.Data;
using ShortUrl.Models;
using ShortUrl.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ShortUrl.Pages
{
    [Authorize]
    public class PreviewModel : PageModel
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IUrlShortenerService _urlService;
        private readonly IAuditService _auditService;
        private readonly ILogger<PreviewModel> _logger;
        private readonly IMemoryCache _cache;
        private readonly UserManager<IdentityUser> _userManager;

        public PreviewModel(
            ApplicationDbContext dbContext,
            IUrlShortenerService urlService,
            IAuditService auditService,
            ILogger<PreviewModel> logger,
            IMemoryCache cache,
            UserManager<IdentityUser> userManager)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _urlService = urlService ?? throw new ArgumentNullException(nameof(urlService));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        }

        public UrlShort ShortUrl { get; set; }
        public string ShortenedUrl { get; set; }
        public List<ClickStat> ClickStats { get; set; } = new List<ClickStat>();
        public Dictionary<int, int> ClickCountsByDestination { get; set; } = new Dictionary<int, int>();
        public Dictionary<int, int> ClickCountsByOgMetadata { get; set; } = new Dictionary<int, int>();
        public bool IsBasicOrHigher { get; set; }
        public string ErrorMessage { get; set; }
        public int ClicksCount { get; set; }

        public async Task<IActionResult> OnGetAsync(string code)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    ErrorMessage = "Slug cannot be empty.";
                    await _auditService.LogAsync(User.Identity?.Name, "Invalid slug provided for preview", "ShortUrl", 0, "Invalid slug");
                    return Page();
                }

                var cacheKey = $"ShortUrl_{code}";
                ShortUrl = await _cache.GetOrCreateAsync(cacheKey, async entry =>
                {
                    entry.SlidingExpiration = TimeSpan.FromMinutes(5);
                    return await _dbContext.ShortUrls
                        .Include(s => s.User)
                        .Include(s => s.DestinationUrls)
                        .Include(s => s.OgMetadataVariations)
                        .Include(s => s.ClickStats)
                        .FirstOrDefaultAsync(s => s.Code == code && !s.IsDeleted);
                });

                if (ShortUrl == null)
                {
                    ErrorMessage = "No URL found for the provided slug.";
                    await _auditService.LogAsync(User.Identity?.Name, $"Failed to find URL for slug: {code}", "ShortUrl", 0, "URL not found");
                    return Page();
                }

                if (ShortUrl.ExpirationDate.HasValue && ShortUrl.ExpirationDate.Value < DateTime.UtcNow)
                {
                    ErrorMessage = "This URL has expired.";
                    await _auditService.LogAsync(User.Identity?.Name, $"Attempted to view expired URL: {code}", "ShortUrl", ShortUrl.Id, "Expired URL");
                    return Page();
                }

                var user = await _userManager.GetUserAsync(User);
                IsBasicOrHigher = user != null && (
                    await _userManager.IsInRoleAsync(user, "Basic") ||
                    await _userManager.IsInRoleAsync(user, "Professional") ||
                    await _userManager.IsInRoleAsync(user, "Enterprise"));

                if (IsBasicOrHigher)
                {
                    ClickStats = await _dbContext.ClickStats
                        .Where(c => c.ShortUrlId == ShortUrl.Id)
                        .OrderByDescending(c => c.ClickedAt)
                        .Take(50)
                        .ToListAsync();
                }

                ClicksCount = ShortUrl.ClickStats?.Count ?? 0;
                ClickCountsByDestination = ShortUrl.ClickStats
                    ?.GroupBy(c => c.DestinationUrlId)
                    .ToDictionary(g => g.Key ?? 0, g => g.Count()) ?? new Dictionary<int, int>();

                ClickCountsByOgMetadata = ShortUrl.ClickStats
                    ?.GroupBy(c => c.OgMetadataId)
                    .ToDictionary(g => g.Key ?? 0, g => g.Count()) ?? new Dictionary<int, int>();

                ShortenedUrl = $"{Request.Scheme}://{Request.Host}/{ShortUrl.Code}";
                await _auditService.LogAsync(User.Identity?.Name, $"Viewed preview for slug: {code}", "ShortUrl", ShortUrl.Id, "Preview viewed");
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving URL data for slug: {Code}", code);
                ErrorMessage = "An error occurred while retrieving URL data. Please try again later.";
                await _auditService.LogAsync(User.Identity?.Name, $"Error viewing preview for slug: {code}", "ShortUrl", 0, ex.Message);
                return Page();
            }
        }

        [Authorize(Policy = "BasicClient")]
        public async Task<IActionResult> OnPostDeleteAsync(string code)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(code))
                {
                    ErrorMessage = "Slug cannot be empty.";
                    await _auditService.LogAsync(User.Identity?.Name, "Invalid slug provided for deletion", "ShortUrl", 0, "Invalid slug");
                    return Page();
                }

                var shortUrl = await _dbContext.ShortUrls
                    .Include(s => s.User)
                    .FirstOrDefaultAsync(s => s.Code == code && !s.IsDeleted);

                if (shortUrl == null)
                {
                    ErrorMessage = "No URL found for the provided slug.";
                    await _auditService.LogAsync(User.Identity?.Name, $"Failed to find URL for deletion: {code}", "ShortUrl", 0, "URL not found");
                    return Page();
                }

                if (shortUrl.UserId != User.FindFirst(ClaimTypes.NameIdentifier)?.Value && !User.IsInRole("Admin"))
                {
                    ErrorMessage = "You are not authorized to delete this URL.";
                    await _auditService.LogAsync(User.Identity?.Name, $"Unauthorized deletion attempt for slug: {code}", "ShortUrl", shortUrl.Id, "Unauthorized");
                    return Page();
                }

                await _urlService.DeleteShortUrlAsync(code);
                await _auditService.LogAsync(User.Identity?.Name, $"Deleted URL with slug: {code}", "ShortUrl", shortUrl.Id, "URL deleted");
                _cache.Remove($"ShortUrl_{code}");
                return RedirectToPage("/Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting URL for slug: {Code}", code);
                ErrorMessage = "An error occurred while deleting the URL. Please try again later.";
                await _auditService.LogAsync(User.Identity?.Name, $"Error deleting URL for slug: {code}", "ShortUrl", 0, ex.Message);
                return Page();
            }
        }
    }
}