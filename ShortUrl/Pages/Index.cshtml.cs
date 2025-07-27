using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore; 
using System.Security.Claims;
using ShortUrl.Data;
using ShortUrl.Helpers;
using ShortUrl.Models;
using ShortUrl.Services;

namespace ShortUrl.Pages
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly UrlShortenerService _urlShortenerService;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private readonly IWebHostEnvironment _environment;
        private readonly UserManager<IdentityUser> _userManager;

        public IndexModel(
            UrlShortenerService urlShortenerService,
            IConfiguration configuration,
            ApplicationDbContext dbContext,
            IWebHostEnvironment environment,
            UserManager<IdentityUser> userManager)
        {
            _urlShortenerService = urlShortenerService;
            _configuration = configuration;
            _dbContext = dbContext;
            _environment = environment;
            _userManager = userManager;
        }

        [BindProperty]
        public string? CustomSlug { get; set; }
        [BindProperty]
        public List<DestinationUrl> DestinationUrls { get; set; } = new List<DestinationUrl>();
        [BindProperty]
        public List<OgMetadata> OgMetadataVariations { get; set; } = new List<OgMetadata>();
        [BindProperty]
        public DateTime? ExpirationDate { get; set; }
        [BindProperty]
        public string? Password { get; set; }
        public string? ShortenedUrl { get; set; }
        public List<UrlShort> UserUrls { get; set; } = new List<UrlShort>();
        public string AppUrl => _configuration["AppUrl"];
        public List<IFormFile> OgImageFiles { get; set; } = new List<IFormFile>();

        public async Task OnGetAsync()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            UserUrls = await _dbContext.ShortUrls
                .Include(u => u.DestinationUrls)
                .Include(u => u.OgMetadataVariations)
                .Include(u => u.ClickStats)
                .Where(u => u.UserId == userId && !u.IsDeleted)
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostShortenAsync()
        {
            
            if (!ModelState.IsValid || !DestinationUrls.Any() || DestinationUrls.Any(d => string.IsNullOrWhiteSpace(d.Url)))
            {
                ModelState.AddModelError("", "At least one valid destination URL is required.");
                await OnGetAsync();
                return Page();
            }

            
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var currentCount = await _dbContext.ShortUrls.CountAsync(u => u.UserId == userId);
            var limit = UrlLimiter.GetShortUrlLimitForUser(User);

            if (limit.HasValue && currentCount >= limit.Value)
            {
                ModelState.AddModelError(string.Empty, $"You have reached your limit of {limit.Value} shortened URLs.");
                return Page(); // or return a suitable response
            }
            var isProfessionalOrHigher = await _userManager.IsInRoleAsync(await _userManager.FindByIdAsync(userId), "Professional") ||
                                        await _userManager.IsInRoleAsync(await _userManager.FindByIdAsync(userId), "Enterprise");
            var isEnterprise = await _userManager.IsInRoleAsync(await _userManager.FindByIdAsync(userId), "Enterprise");

            // Process OG metadata images
            var ogMetadataWithImages = new List<OgMetadata>();
            if (isProfessionalOrHigher)
            {
                for (int i = 0; i < OgMetadataVariations.Count; i++)
                {
                    var og = OgMetadataVariations[i];
                    if (Request.Form.Files.Any(f => f.Name == $"OgMetadataVariations[{i}].ImageFile"))
                    {
                        var file = Request.Form.Files[$"OgMetadataVariations[{i}].ImageFile"];
                        if (file != null && file.Length > 0)
                        {
                            var uploadsFolder = Path.Combine(_environment.WebRootPath, "og-images");
                            Directory.CreateDirectory(uploadsFolder);
                            var fileName = $"{Guid.NewGuid()}_{file.FileName}";
                            var filePath = Path.Combine(uploadsFolder, fileName);
                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                await file.CopyToAsync(stream);
                            }
                            og.Image = $"/og-images/{fileName}";
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(og.Title) || !string.IsNullOrWhiteSpace(og.Description) || !string.IsNullOrWhiteSpace(og.Image))
                    {
                        ogMetadataWithImages.Add(og);
                    }
                }
            }

            // Restrict features based on user role
            string? customSlug = isProfessionalOrHigher ? CustomSlug : null;
            List<DestinationUrl> destinationUrls = DestinationUrls;
            List<OgMetadata> ogMetadataVariations = isProfessionalOrHigher ? ogMetadataWithImages : new List<OgMetadata>();
            DateTime? expirationDate = isEnterprise ? ExpirationDate : null;
            string? password = isEnterprise ? Password : null;

            // For non-Professional users, limit to one destination URL and clear UTM parameters
            if (!isProfessionalOrHigher)
            {
                destinationUrls = new List<DestinationUrl> { destinationUrls.First() };
                destinationUrls[0].UtmSource = null;
                destinationUrls[0].UtmMedium = null;
                destinationUrls[0].UtmCampaign = null;
            }

            try
            {
                var code = await _urlShortenerService.CreateShortUrlAsync(
                    userId,
                    customSlug,
                    destinationUrls,
                    ogMetadataVariations,
                    expirationDate,
                    password);
                ShortenedUrl = $"{_configuration["AppUrl"]}/{code}";
            }
            catch (ArgumentException ex)
            {
                ModelState.AddModelError("CustomSlug", ex.Message);
                await OnGetAsync();
                return Page();
            }

            await OnGetAsync();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var shortUrl = await _dbContext.ShortUrls
                .FirstOrDefaultAsync(u => u.Id == id && u.UserId == userId && !u.IsDeleted);
            if (shortUrl == null)
            {
                return NotFound();
            }

            shortUrl.IsDeleted = true;
            await _dbContext.SaveChangesAsync();

            await OnGetAsync();
            return Page();
        }
    }
}