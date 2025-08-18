using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShortUrl.Data;
using ShortUrl.Models;
using System.Web;
using UAParser;

namespace ShortUrl.Pages
{
    public class PasswordModel : PageModel
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public PasswordModel(
            ApplicationDbContext dbContext,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _dbContext = dbContext;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            Input = new InputModel
            {
                Code = null,
                Password = null
            };
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            public required string? Code { get; set; }
            public required string? Password { get; set; }
        }

        public IActionResult OnGet(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return RedirectToPage("/InvalidUrl");
            }

            Input = new InputModel
            {
                Code = code,
                Password = null
            };
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(Input.Password))
            {
                return Page();
            }

            var urlShort = await _dbContext.UrlShorts.Include(urlShort => urlShort.DestinationUrls)
                .Include(s => s.User)
                .Include(s => s.DestinationUrls)
                .FirstOrDefaultAsync(s => s.Code == Input.Code && !s.IsDeleted);
            if (urlShort == null || (urlShort.ExpirationDate.HasValue && urlShort.ExpirationDate.Value < DateTime.UtcNow))
            {
                return RedirectToPage("/InvalidUrl");
            }

            if (string.IsNullOrEmpty(urlShort.Password) || !BCrypt.Net.BCrypt.Verify(Input.Password, urlShort.Password))
            {
                ModelState.AddModelError("Input.Password", "Incorrect password.");
                return Page();
            }

            if (urlShort.DestinationUrls.Count == 0)
            {
                return RedirectToPage("/InvalidUrl");
            }
            var destinationUrl = SelectWeightedDestinationUrl(urlShort);
            urlShort.CurrentDestinationIndex = (urlShort.CurrentDestinationIndex + 1) % urlShort.DestinationUrls.Count;

            string? country = null;
            string? city = null;
            string? ipAddress = null;
            
            // Check for privacy consent cookie before collecting IP
            var consentCookie = Request.Cookies["privacy-consent"];
            var hasConsent = consentCookie == "accepted";
            
            // Only collect IP if user has consented
            if (hasConsent)
            {
                ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                if (!string.IsNullOrEmpty(ipAddress) && ipAddress != "::1" && ipAddress != "127.0.0.1")
                {
                    try
                    {
                        var client = _httpClientFactory.CreateClient();
                        var geoUrl = string.Format(_configuration["GeoLocation:ApiUrl"] ?? string.Empty, ipAddress);
                        var response = await client.GetFromJsonAsync<GeoLocationResponse>(geoUrl);
                        if (response != null && response.Error == false)
                        {
                            country = response.Country;
                            city = response.City;
                        }
                    }
                    catch
                    {
                        // Log error if needed
                    }
                }
            }

            bool isBasicOrHigher = false;
            if (urlShort.User != null)
            {
                var roles = await _dbContext.UserRoles
                    .Where(ur => ur.UserId == urlShort.UserId)
                    .Join(_dbContext.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                    .ToListAsync();
                isBasicOrHigher = roles.Contains("Basic");
            }

            string? referrer = null;
            string? device = null;
            string? browser = null;
            string? language = null;
            string? operatingSystem = null;
            if (isBasicOrHigher)
            {
                referrer = HttpContext.Request.Headers.Referer.ToString();
                var uaParser = await Parser.GetDefaultAsync();
                var ua = await uaParser.ParseAsync(HttpContext.Request.Headers["User-Agent"].ToString());
                device = ua.Device.Family;
                browser = $"{ua.Browser.Family} {ua.Browser.Major}.{ua.Browser.Minor}";
                operatingSystem = $"{ua.OS.Family} {ua.OS.Major}.{ua.OS.Minor}";
                language = HttpContext.Request.Headers.AcceptLanguage.ToString().Split(',').FirstOrDefault()?.Split(';').FirstOrDefault();
            }

            var clickStat = new ClickStat
            {
                UrlShortId = urlShort.Id,
                DestinationUrlId = destinationUrl.Id,
                OgMetadataId = null,
                ClickedAt = DateTime.UtcNow,
                IpAddress = ipAddress,
                Country = country,
                City = city,
                Referrer = isBasicOrHigher ? referrer : null,
                Device = isBasicOrHigher ? device : null,
                Browser = isBasicOrHigher ? browser : null,
                Language = isBasicOrHigher ? language : null,
                OperatingSystem = isBasicOrHigher ? operatingSystem : null,
                ScreenResolution = null
            };
            _dbContext.ClickStats.Add(clickStat);
            await _dbContext.SaveChangesAsync();

            var uriBuilder = new UriBuilder(destinationUrl.Url);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            if (!string.IsNullOrEmpty(destinationUrl.UtmSource))
                query["utm_source"] = destinationUrl.UtmSource;
            if (!string.IsNullOrEmpty(destinationUrl.UtmMedium))
                query["utm_medium"] = destinationUrl.UtmMedium;
            if (!string.IsNullOrEmpty(destinationUrl.UtmCampaign))
                query["utm_campaign"] = destinationUrl.UtmCampaign;
            query["clickId"] = clickStat.Id.ToString();
            uriBuilder.Query = query.ToString();

            await _dbContext.SaveChangesAsync();
            return Redirect(uriBuilder.ToString());
        }

        private DestinationUrl SelectWeightedDestinationUrl(UrlShort urlShort)
        {
            var totalWeight = urlShort.DestinationUrls.Sum(d => d.Weight ?? 1);
            var random = new Random().NextDouble() * totalWeight;
            double currentWeight = 0;
            foreach (var url in urlShort.DestinationUrls)
            {
                currentWeight += url.Weight ?? 1;
                if (random <= currentWeight)
                {
                    return url;
                }
            }
            return urlShort.DestinationUrls[urlShort.CurrentDestinationIndex];
        }
    }
}