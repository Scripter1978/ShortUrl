using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ShortUrl.Data;
using ShortUrl.Models;
using ShortUrl.Services;

namespace ShortUrl.Pages
{
    [Authorize(Policy = "BasicClient")]
    public class StatsModel : PageModel
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IAuditService _auditService;
        private readonly ILogger<StatsModel> _logger;
        private readonly IMemoryCache _cache;
        private readonly UserManager<IdentityUser> _userManager;

        public StatsModel(
            ApplicationDbContext dbContext,
            IAuditService auditService,
            ILogger<StatsModel> logger,
            IMemoryCache cache,
            UserManager<IdentityUser> userManager)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        }

        public UrlShort? UrlShort { get; set; }
        public string ShortenedUrl { get; set; }
        public List<ClickStat> ClickStats { get; set; } = [];
        public Dictionary<int, int> ClickCountsByDestination { get; set; } = new();
        public Dictionary<int, int> ClickCountsByOgMetadata { get; set; } = new();
        public string ErrorMessage { get; set; }
        public List<string> Dates { get; set; } = [];
        public List<int> Clicks { get; set; } = [];
        public string CsvData { get; set; }
        public bool IsProfessionalOrHigher { get; set; }
        public List<UrlShort> UserUrls { get; set; } = [];
        public int TotalClicks { get; set; } = 0;

        public async Task<IActionResult> OnGetAsync(int? id, DateTime? startDate, DateTime? endDate)
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                // Show all URLs if no specific ID is provided
        if (id is null or <= 0)
        {
            // Get all user's URLs
            var userUrlsQuery = _dbContext.UrlShorts 
                .Include(s => s.User)
                .Include(s => s.DestinationUrls)
                .Include(s => s.ClickStats)
                .Where(s => s.UserId == userId && !s.IsDeleted);
            
            UserUrls = await userUrlsQuery.ToListAsync();
            
            // Collect all clicks data with date filtering
            var allClicks = new List<ClickStat>();
            foreach (var url in UserUrls)
            {
                var clicksQuery = _dbContext.ClickStats.Where(c => c.UrlShortId == url.Id);
                
                if (startDate.HasValue)
                    clicksQuery = clicksQuery.Where(c => c.ClickedAt >= 
                        DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc));
                if (endDate.HasValue)
                    clicksQuery = clicksQuery.Where(c => c.ClickedAt <= 
                        DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc));
                
                allClicks.AddRange(await clicksQuery.ToListAsync());
            }
            
            // Calculate total clicks and prepare chart data
            TotalClicks = allClicks.Count;
            
            var clickGroupsList = allClicks
                .GroupBy(c => c.ClickedAt.Date)
                .OrderBy(g => g.Key)
                .ToList();
                
            Dates = clickGroupsList.Select(g => g.Key.ToString("yyyy-MM-dd")).ToList();
            Clicks = clickGroupsList.Select(g => g.Count()).ToList();
            
            // Get user role information
            var user = await _userManager.FindByIdAsync(userId);
            IsProfessionalOrHigher = await _userManager.IsInRoleAsync(user, "Professional") || 
                                   await _userManager.IsInRoleAsync(user, "Enterprise");
            
            await _auditService.LogAsync(User.Identity?.Name, "Viewed all URL stats", "UrlShort", "Stats viewed");
            return Page();
        }

                var cacheKey = $"UrlShort_{id}";
                UrlShort = await _cache.GetOrCreateAsync(cacheKey, async entry =>
                {
                    entry.SlidingExpiration = TimeSpan.FromMinutes(5);
                    return await _dbContext.UrlShorts
                        .Include(s => s.User)
                        .Include(s => s.DestinationUrls)
                        .Include(s => s.OgMetadataVariations)
                        .Include(s => s.ClickStats)
                        .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);
                });

                if (UrlShort == null)
                {
                    ErrorMessage = "No URL found for the provided ID.";

                    await _auditService.LogAsync(User.Identity?.Name, $"Failed to find URL for ID: {id}",
                        "UrlShort", "URL not found");
                    return Page();
                }

                if (UrlShort.UserId != User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value &&
                    !User.IsInRole("Admin"))
                {
                    ErrorMessage = "You are not authorized to view statistics for this URL.";

                    await _auditService.LogAsync(User.Identity?.Name, $"Unauthorized stats access for ID: {id}",
                        "UrlShort",  "Unauthorized");
                    return Page();
                }

                if (UrlShort.ExpirationDate.HasValue && UrlShort.ExpirationDate.Value < DateTime.UtcNow)
                {
                    ErrorMessage = "This URL has expired.";

                    await _auditService.LogAsync(User.Identity?.Name,
                        $"Attempted to view stats for expired URL: {id}", "UrlShort", "Expired URL");
                    return Page();
                }

                var clickStatsQuery = _dbContext.ClickStats.Where(c => c.UrlShortId == UrlShort.Id);

                if (startDate.HasValue)
                    clickStatsQuery = clickStatsQuery.Where(c =>
                        c.ClickedAt >= DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc));
                if (endDate.HasValue)
                    clickStatsQuery = clickStatsQuery.Where(c =>
                        c.ClickedAt <=
                        DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc));

                ClickStats = await clickStatsQuery
                    .OrderByDescending(c => c.ClickedAt)
                    .Take(50)
                    .ToListAsync();

                ClickCountsByDestination = ClickStats
                    ?.GroupBy(c => c.DestinationUrlId)
                    .ToDictionary(g => g.Key ?? 0, g => g.Count()) ?? new Dictionary<int, int>();

                ClickCountsByOgMetadata = UrlShort.ClickStats
                    ?.GroupBy(c => c.OgMetadataId)
                    .ToDictionary(g => g.Key ?? 0, g => g.Count()) ?? new Dictionary<int, int>();
                IsProfessionalOrHigher = (
                    await _userManager.IsInRoleAsync(UrlShort.User, "Professional") ||
                    await _userManager.IsInRoleAsync(UrlShort.User, "Enterprise"));
                // Prepare chart data
                var clickGroups = ClickStats
                    .GroupBy(c => c.ClickedAt.Date)
                    .OrderBy(g => g.Key)
                    .ToList();
                Dates = clickGroups.Select(g => g.Key.ToString("yyyy-MM-dd")).ToList();
                Clicks = clickGroups.Select(g => g.Count()).ToList();

                // Prepare CSV data
                var headers = new List<string>
                {
                    "Time", "IP Address", "Country", "City", "Referrer", "Device", "Browser", "OS", "Language",
                    "Screen Resolution"
                };
                var csvHeader = string.Join(",", headers);
                var csvRows = ClickStats.Select(stat => string.Join(",", new[]
                {
                    $"\"{stat.ClickedAt:g}\"",
                    $"\"{stat.IpAddress ?? ""}\"",
                    $"\"{stat.Country ?? ""}\"",
                    $"\"{stat.City ?? ""}\"",
                    $"\"{stat.Referrer ?? ""}\"",
                    $"\"{stat.Device ?? ""}\"",
                    $"\"{stat.Browser ?? ""}\"",
                    $"\"{stat.OperatingSystem ?? ""}\"",
                    $"\"{stat.Language ?? ""}\"",
                    $"\"{stat.ScreenResolution ?? ""}\""
                }));
                CsvData = string.Join("\n", new[] { csvHeader }.Concat(csvRows));

                ShortenedUrl = $"{Request.Scheme}://{Request.Host}/{UrlShort.Code}";
                await _auditService.LogAsync(User.Identity?.Name, $"Viewed stats for URL ID: {id}", "UrlShort", 
                    "Stats viewed");
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving stats for URL ID: {Id}", id);
                ErrorMessage = "An error occurred while retrieving statistics. Please try again later.";
                await _auditService.LogAsync(User.Identity?.Name, $"Error viewing stats", "UrlShort", 
                     ex.Message);
                return Page();
            }
        }
    }
}