using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShortUrl.Data;
using ShortUrl.Services;
using Stripe;
using Microsoft.AspNetCore.Mvc;
using System.Web;
using QRCoder;
using UAParser;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;
using ShortUrl.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders()
.AddRoles<IdentityRole>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("BasicClient", policy => policy.RequireRole("Basic", "Professional", "Enterprise"));
    options.AddPolicy("ProfessionalClient", policy => policy.RequireRole("Professional", "Enterprise"));
    options.AddPolicy("EnterpriseClient", policy => policy.RequireRole("Enterprise"));
});

builder.Services.AddRazorPages();
builder.Services.AddScoped<UrlShortenerService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IAuditService, AuditService>();

var app = builder.Build();

// Configure Stripe API key
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapGet("/{code}", async (string code, ApplicationDbContext db, HttpContext httpContext, IHttpClientFactory httpClientFactory, IConfiguration configuration, IHttpContextAccessor httpContextAccessor, IMemoryCache cache) =>
{
    var cacheKey = $"ShortUrl_{code}";
    var shortUrl = await cache.GetOrCreateAsync(cacheKey, async entry =>
    {
        entry.SlidingExpiration = TimeSpan.FromMinutes(5);
        return await db.ShortUrls
            .Include(s => s.DestinationUrls)
            .Include(s => s.OgMetadataVariations)
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Code == code);
    });

    if (shortUrl == null || shortUrl.IsDeleted)
    {
        return Results.Redirect("/InvalidUrl");
    }

    if (shortUrl.ExpirationDate.HasValue && shortUrl.ExpirationDate.Value < DateTime.UtcNow)
    {
        return Results.Redirect("/InvalidUrl");
    }

    var userAgent = httpContext.Request.Headers["User-Agent"].ToString().ToLower();
    var isSocialMediaCrawler = userAgent.Contains("facebookexternalhit") ||
                               userAgent.Contains("twitterbot") ||
                               userAgent.Contains("linkedinbot") ||
                               userAgent.Contains("slackbot") ||
                               userAgent.Contains("discordbot");

    if (isSocialMediaCrawler)
    {
        var variationIndex = httpContext.Request.Query["var"].FirstOrDefault();
        int varIndex = string.IsNullOrEmpty(variationIndex) || !int.TryParse(variationIndex, out varIndex) ? shortUrl.CurrentOgMetadataIndex : varIndex;
        shortUrl.CurrentOgMetadataIndex = (shortUrl.OgMetadataVariations.Count > 0 ? (shortUrl.CurrentOgMetadataIndex + 1) % shortUrl.OgMetadataVariations.Count : 0);
        await db.SaveChangesAsync();
        return Results.Redirect($"/Preview/{code}?var={varIndex}");
    }

    if (!string.IsNullOrEmpty(shortUrl.Password))
    {
        return Results.Redirect($"/Password/{code}");
    }

    if (!shortUrl.DestinationUrls.Any())
    {
        return Results.Redirect("/InvalidUrl");
    }

    // Weighted A/B testing
    var destinationUrl = SelectWeightedDestinationUrl(shortUrl);
    shortUrl.CurrentDestinationIndex = (shortUrl.CurrentDestinationIndex + 1) % shortUrl.DestinationUrls.Count;

    string? country = null;
    string? city = null;
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
    if (!string.IsNullOrEmpty(ipAddress) && ipAddress != "::1" && ipAddress != "127.0.0.1")
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            var geoUrl = string.Format(configuration["GeoLocation:ApiUrl"], ipAddress);
            var response = await client.GetFromJsonAsync<GeoLocationResponse>(geoUrl);
            if (response != null && response.Error == false)
            {
                country = response.CountryName;
                city = response.City;
            }
        }
        catch
        {
            // Log error if needed
        }
    }

    string? referrer = null;
    string? device = null;
    string? browser = null;
    string? language = null;
    string? operatingSystem = null;
    bool isProfessionalOrHigher = false;
    if (shortUrl.User != null)
    {
        var roles = await db.UserRoles
            .Where(ur => ur.UserId == shortUrl.UserId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
            .ToListAsync();
        isProfessionalOrHigher = roles.Contains("Professional") || roles.Contains("Enterprise");
    }

    if (isProfessionalOrHigher)
    {
        referrer = httpContext.Request.Headers["Referer"].ToString();
        var uaParser = Parser.GetDefault();
        var ua = uaParser.Parse(httpContext.Request.Headers["User-Agent"].ToString());
        device = ua.Device.Family;
        browser = $"{ua.Browser.Family} {ua.Browser.Major}.{ua.Browser.Minor}";
        operatingSystem = $"{ua.OS.Family} {ua.OS.Major}.{ua.OS.Minor}";
        language = httpContext.Request.Headers["Accept-Language"].ToString().Split(',').FirstOrDefault()?.Split(';').FirstOrDefault();
    }

    var clickStat = new ClickStat
    {
        ShortUrlId = shortUrl.Id,
        DestinationUrlId = destinationUrl.Id,
        OgMetadataId = null,
        ClickedAt = DateTime.UtcNow,
        IpAddress = ipAddress,
        Country = country,
        City = city,
        Referrer = isProfessionalOrHigher ? referrer : null,
        Device = isProfessionalOrHigher ? device : null,
        Browser = isProfessionalOrHigher ? browser : null,
        Language = isProfessionalOrHigher ? language : null,
        OperatingSystem = isProfessionalOrHigher ? operatingSystem : null,
        ScreenResolution = null
    };
    db.ClickStats.Add(clickStat);
    await db.SaveChangesAsync();

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

    await db.SaveChangesAsync();
    cache.Remove(cacheKey); // Invalidate cache after update
    return Results.Redirect(uriBuilder.ToString());
});

app.MapGet("/api/check-slug/{slug}", async (string slug, ApplicationDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(slug) || !Regex.IsMatch(slug, @"^[a-zA-Z0-9\-_]+$"))
    {
        return Results.Json(new { isAvailable = false, message = "Invalid slug format." });
    }
    bool isAvailable = !await db.ShortUrls.AnyAsync(s => s.Code.ToLower() == slug.ToLower());
    return Results.Json(new { isAvailable, message = isAvailable ? "Slug is available." : "Slug is already in use." });
});

app.MapPost("/api/update-screen-resolution", async (ApplicationDbContext db, [FromBody] ScreenResolutionRequest request) =>
{
    var clickStat = await db.ClickStats.FindAsync(request.ClickId);
    if (clickStat != null)
    {
        clickStat.ScreenResolution = request.ScreenResolution;
        await db.SaveChangesAsync();
        return Results.Ok();
    }
    return Results.NotFound();
});

app.MapGet("/qr/{code}", async (string code, ApplicationDbContext db, IConfiguration configuration) =>
{
    var shortUrl = await db.ShortUrls.FirstOrDefaultAsync(s => s.Code == code);
    if (shortUrl == null || shortUrl.IsDeleted)
        return Results.NotFound();

    var shortUrlFull = $"{configuration["AppUrl"]}/{code}";
    var qrGenerator = new QRCodeGenerator();
    var qrCodeData = qrGenerator.CreateQrCode(shortUrlFull, QRCodeGenerator.ECCLevel.Q);
    var qrCode = new BitmapByteQRCode(qrCodeData);
    var qrCodeImage = qrCode.GetGraphic(20);

    return Results.File(qrCodeImage, "image/png", $"qr_{code}.png");
});

app.MapPost("/webhook/stripe", async (
    HttpContext httpContext,
    ApplicationDbContext db,
    UserManager<IdentityUser> userManager,
    IConfiguration configuration) =>
{
    var json = await new StreamReader(httpContext.Request.Body).ReadToEndAsync();
    try
    {
        var stripeEvent = EventUtility.ConstructEvent(
            json,
            httpContext.Request.Headers["Stripe-Signature"],
            configuration["Stripe:WebhookSecret"]
        );

        if (stripeEvent.Type == EventTypes.CustomerSubscriptionCreated)
        {
            var subscription = stripeEvent.Data.Object as Subscription;
            var userStripeInfo = await db.UserStripeInfos
                .FirstOrDefaultAsync(u => u.StripeCustomerId == subscription.CustomerId);
            if (userStripeInfo != null)
            {
                var user = await userManager.FindByIdAsync(userStripeInfo.UserId);
                if (user != null)
                {
                    string newRole = subscription.Id switch
                    {
                        string id when id == configuration["Stripe:BasicPriceId"] => "Basic",
                        string id when id == configuration["Stripe:ProfessionalPriceId"] => "Professional",
                        string id when id == configuration["Stripe:EnterprisePriceId"] => "Enterprise",
                        _ => null
                    };
                    if (newRole != null)
                    {
                        var currentRoles = await userManager.GetRolesAsync(user);
                        foreach (var role in currentRoles)
                        {
                            await userManager.RemoveFromRoleAsync(user, role);
                        }
                        await userManager.AddToRoleAsync(user, newRole);
                        await db.SaveChangesAsync();
                    }
                }
            }
        }
        else if (stripeEvent.Type == EventTypes.CustomerSubscriptionDeleted)
        {
            var subscription = stripeEvent.Data.Object as Subscription;
            var userStripeInfo = await db.UserStripeInfos
                .FirstOrDefaultAsync(u => u.StripeCustomerId == subscription.CustomerId);
            if (userStripeInfo != null)
            {
                var user = await userManager.FindByIdAsync(userStripeInfo.UserId);
                if (user != null)
                {
                    var currentRoles = await userManager.GetRolesAsync(user);
                    foreach (var role in currentRoles)
                    {
                        await userManager.RemoveFromRoleAsync(user, role);
                    }
                    await userManager.AddToRoleAsync(user, "Free");
                    await db.SaveChangesAsync();
                }
            }
        }

        return Results.Ok();
    }
    catch (StripeException)
    {
        return Results.BadRequest();
    }
});

// Seed roles
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    string[] roles = ["Free", "Basic", "Professional", "Enterprise"];
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }
}

app.Run();

DestinationUrl SelectWeightedDestinationUrl(UrlShort shortUrl)
{
    var totalWeight = shortUrl.DestinationUrls.Sum(d => d.Weight ?? 1);
    var random = new Random().NextDouble() * totalWeight;
    double currentWeight = 0;
    foreach (var url in shortUrl.DestinationUrls)
    {
        currentWeight += url.Weight ?? 1;
        if (random <= currentWeight)
        {
            return url;
        }
    }
    return shortUrl.DestinationUrls[shortUrl.CurrentDestinationIndex];
}

public class GeoLocationResponse
{
    public string Ip { get; set; }
    public string City { get; set; }
    public string Region { get; set; }
    public string CountryName { get; set; }
    public bool Error { get; set; }
}

public class ScreenResolutionRequest
{
    public int ClickId { get; set; }
    public string ScreenResolution { get; set; }
}