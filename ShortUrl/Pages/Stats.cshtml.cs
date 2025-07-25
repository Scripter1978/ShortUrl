using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore; 
using System.Security.Claims;
using ShortUrl.Data;
using ShortUrl.Models;

namespace ShortUrl.Pages
{
    [Authorize(Policy = "ProfessionalClient")]
    public class StatsModel : PageModel
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly UserManager<IdentityUser> _userManager;

        public StatsModel(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager)
        {
            _dbContext = dbContext;
            _userManager = userManager;
        }

        public UrlShort ShortUrl { get; set; }
        public int TotalClicks { get; set; }
        public int UniqueIps { get; set; }
        public List<DestinationUrlStat> DestinationUrlStats { get; set; }
        public List<OgMetadataStat> OgMetadataStats { get; set; }
        public List<ClickStat> ClickStats { get; set; }
        public List<AuditLog> AuditLogs { get; set; }
        public List<ClickTrend> ClickTrends { get; set; }
        public bool IsProfessionalOrHigher { get; set; }
        public int CurrentPage { get; set; }
        public int PageSize { get; set; } = 20;
        public int TotalPages { get; set; }

        public class DestinationUrlStat
        {
            public string Url { get; set; }
            public int? Weight { get; set; }
            public int Clicks { get; set; }
        }

        public class OgMetadataStat
        {
            public string Title { get; set; }
            public int Clicks { get; set; }
        }

        public class ClickTrend
        {
            public DateTime Date { get; set; }
            public int Clicks { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(int id, int? pageNumber)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            ShortUrl = await _dbContext.ShortUrls
                .Include(s => s.DestinationUrls)
                .Include(s => s.OgMetadataVariations)
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId && !s.IsDeleted);

            if (ShortUrl == null)
            {
                TempData["ErrorMessage"] = "URL not found or you don't have permission to view its statistics.";
                return Page();
            }

            if (ShortUrl.ExpirationDate.HasValue && ShortUrl.ExpirationDate.Value < DateTime.UtcNow)
            {
                TempData["ErrorMessage"] = "This URL has expired.";
                return Page();
            }

            // Calculate stats
            CurrentPage = pageNumber ?? 1;
            TotalClicks = await _dbContext.ClickStats.CountAsync(c => c.ShortUrlId == id);
            UniqueIps = await _dbContext.ClickStats
                .Where(c => c.ShortUrlId == id)
                .Select(c => c.IpAddress)
                .Distinct()
                .CountAsync();
            TotalPages = (int)Math.Ceiling((double)TotalClicks / PageSize);

            ClickStats = await _dbContext.ClickStats
                .Where(c => c.ShortUrlId == id)
                .OrderByDescending(c => c.ClickedAt)
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            DestinationUrlStats = await _dbContext.DestinationUrls
                .Where(d => d.ShortUrlId == id)
                .Select(d => new DestinationUrlStat
                {
                    Url = d.Url,
                    Weight = d.Weight,
                    Clicks = _dbContext.ClickStats.Count(c => c.DestinationUrlId == d.Id)
                })
                .ToListAsync();

            OgMetadataStats = await _dbContext.OgMetadataVariations
                .Where(o => o.ShortUrlId == id)
                .Select(o => new OgMetadataStat
                {
                    Title = o.Title,
                    Clicks = _dbContext.ClickStats.Count(c => c.OgMetadataId == o.Id)
                })
                .ToListAsync();

            ClickTrends = await _dbContext.ClickStats
                .Where(c => c.ShortUrlId == id)
                .GroupBy(c => c.ClickedAt.Date)
                .Select(g => new ClickTrend
                {
                    Date = g.Key,
                    Clicks = g.Count()
                })
                .OrderBy(t => t.Date)
                .Take(30) // Last 30 days for performance
                .ToListAsync();

            IsProfessionalOrHigher = ShortUrl.User != null && (
                await _userManager.IsInRoleAsync(ShortUrl.User, "Professional") ||
                await _userManager.IsInRoleAsync(ShortUrl.User, "Enterprise"));

            if (await _userManager.IsInRoleAsync(await _userManager.FindByIdAsync(userId), "Enterprise"))
            {
                AuditLogs = await _dbContext.AuditLogs
                    .Where(a => a.EntityType == "ShortUrl" && a.EntityId == id)
                    .OrderByDescending(a => a.Timestamp)
                    .Take(50)
                    .ToListAsync();
            }
            else
            {
                AuditLogs = new List<AuditLog>();
            }

            return Page();
        }
    }
}