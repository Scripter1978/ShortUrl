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
using Microsoft.AspNetCore.SignalR;
using ShortUrl.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders()
.AddRoles<IdentityRole>();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("BasicClient", policy => policy.RequireRole("Basic", "Professional", "Enterprise"))
    .AddPolicy("ProfessionalClient", policy => policy.RequireRole("Professional", "Enterprise"))
    .AddPolicy("EnterpriseClient", policy => policy.RequireRole("Enterprise"));

builder.Services.AddRazorPages();
builder.Services.AddSignalR(); // Add SignalR
builder.Services.AddScoped<UrlShortenerService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IAuditService, AuditService>();

var app = builder.Build();

// Configure the Stripe API key
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
app.MapHub<ClickHub>("/clickHub"); // Map SignalR hub

app.MapGet("/{code}", async (string code, ApplicationDbContext db, HttpContext httpContext, IHttpClientFactory httpClientFactory, IConfiguration configuration, IHttpContextAccessor httpContextAccessor, IMemoryCache cache, IHubContext<ClickHub> hubContext) =>
{
    var cacheKey = $"UrlShort_{code}";
    var urlShort = await cache.GetOrCreateAsync(cacheKey, async entry =>
    {
        entry.SlidingExpiration = TimeSpan.FromMinutes(5);
        return await db.UrlShorts
            .Include(s => s.DestinationUrls)
            .Include(s => s.OgMetadataVariations)
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Code == code);
    });

    if (urlShort == null || urlShort.IsDeleted || urlShort.ExpirationDate.HasValue && urlShort.ExpirationDate.Value < DateTime.UtcNow)
    {
        return Results.Redirect("/InvalidUrl");
    }

    var userAgent = httpContext.Request.Headers.UserAgent.ToString().ToLower();
    var isSocialMediaCrawler = userAgent.Contains("facebookexternalhit") ||
                               userAgent.Contains("twitterbot") ||
                               userAgent.Contains("linkedinbot") ||
                               userAgent.Contains("slackbot") ||
                               userAgent.Contains("discordbot");

    if (isSocialMediaCrawler)
    {
        var variationIndex = httpContext.Request.Query["var"].FirstOrDefault();
        int varIndex = string.IsNullOrEmpty(variationIndex) || !int.TryParse(variationIndex, out varIndex) ? urlShort.CurrentOgMetadataIndex : varIndex;
        urlShort.CurrentOgMetadataIndex = (urlShort.OgMetadataVariations.Count > 0 ? (urlShort.CurrentOgMetadataIndex + 1) % urlShort.OgMetadataVariations.Count : 0);
        await db.SaveChangesAsync();
        return Results.Redirect($"/Preview/{code}?var={varIndex}");
    }

    if (!string.IsNullOrEmpty(urlShort.Password))
    {
        return Results.Redirect($"/Password/{code}");
    }

    if (urlShort.DestinationUrls.Count == 0)
    {
        return Results.Redirect("/InvalidUrl");
    }

    // Weighted A/B testing
    var destinationUrl = SelectWeightedDestinationUrl(urlShort);
    urlShort.CurrentDestinationIndex = (urlShort.CurrentDestinationIndex + 1) % urlShort.DestinationUrls.Count;

    string? country = null;
    string? city = null;
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
    #if DEBUG
    if (ipAddress == "::1") ipAddress = "24.48.0.1"; // Example IP for testing
    #endif
    if (!string.IsNullOrEmpty(ipAddress) && ipAddress != "::1" && ipAddress != "127.0.0.1")
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            var geoUrl = string.Format(configuration["GeoLocation:ApiUrl"], ipAddress);
            var response = await client.GetFromJsonAsync<GeoLocationResponse>(geoUrl);
            if (response is { Error: false })
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

    string? referrer = null;
    string? device = null;
    string? browser = null;
    string? language = null;
    string? operatingSystem = null;
    var isProfessionalOrHigher = false;
    if (urlShort.User != null)
    {
        var roles = await db.UserRoles
            .Where(ur => ur.UserId == urlShort.UserId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
            .ToListAsync();
        isProfessionalOrHigher = roles.Contains("Professional") || roles.Contains("Enterprise");
    }
 
        referrer = httpContext.Request.Headers.Referer.ToString();
        var uaParser = Parser.GetDefault();
        var ua = uaParser.Parse(httpContext.Request.Headers.UserAgent.ToString());
        device = ua.Device.Family;
        browser = $"{ua.Browser.Family} {ua.Browser.Major}.{ua.Browser.Minor}";
        operatingSystem = $"{ua.OS.Family} {ua.OS.Major}.{ua.OS.Minor}";
        language = httpContext.Request.Headers.AcceptLanguage.ToString().Split(',').FirstOrDefault()?.Split(';').FirstOrDefault();
   

    var clickStat = new ClickStat
    {
        UrlShortId = urlShort.Id,
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

    // Notify clients of a new click
    var totalClicks = await db.ClickStats.CountAsync(c => c.UrlShortId == urlShort.Id);
    var destinationClicks = await db.ClickStats
        .Where(c => c.UrlShortId == urlShort.Id)
        .GroupBy(c => c.DestinationUrlId)
        .ToDictionaryAsync(g => g.Key ?? 0, g => g.Count());
    var ogClicks = await db.ClickStats
        .Where(c => c.UrlShortId == urlShort.Id && c.OgMetadataId != null)
        .GroupBy(c => c.OgMetadataId)
        .ToDictionaryAsync(g => g.Key!.Value, g => g.Count());
    await hubContext.Clients.All.SendAsync("ReceiveClickUpdate", code, totalClicks, destinationClicks, ogClicks);

    await db.SaveChangesAsync();
    cache.Remove(cacheKey);
    return Results.Redirect(uriBuilder.ToString());
});
app.MapPost("/Account/Logout", async (SignInManager<IdentityUser> signInManager, HttpContext httpContext) =>
{
    await signInManager.SignOutAsync();
    return Results.Redirect("/");
});

app.MapGet("/api/check-slug/{slug}", async (string slug, ApplicationDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(slug) || !Regex.IsMatch(slug, @"^[a-zA-Z0-9\-_]+$"))
    {
        return Results.Json(new { isAvailable = false, message = "Invalid slug format." });
    }
    var isAvailable = !await db.UrlShorts.AnyAsync(s => s.Code.ToLower() == slug.ToLower());
    return Results.Json(new { isAvailable, message = isAvailable ? "Slug is available." : "Slug is already in use." });
});

app.MapPost("/api/update-screen-resolution", async (ApplicationDbContext db, [FromBody] ScreenResolutionRequest request) =>
{
    var clickStat = await db.ClickStats.FindAsync(request.ClickId);
    if (clickStat == null) return Results.NotFound();
    clickStat.ScreenResolution = request.ScreenResolution;
    await db.SaveChangesAsync();
    return Results.Ok();
});

app.MapGet("/qr/{code}", async (string code, ApplicationDbContext db, IConfiguration configuration) =>
{
    var urlShort = await db.UrlShorts.FirstOrDefaultAsync(s => s.Code == code);
    if (urlShort == null || urlShort.IsDeleted)
        return Results.NotFound();

    var urlShortFull = $"{configuration["AppUrl"]}/{code}";
    var qrGenerator = new QRCodeGenerator();
    var qrCodeData = qrGenerator.CreateQrCode(urlShortFull, QRCodeGenerator.ECCLevel.Q);
    var qrCode = new BitmapByteQRCode(qrCodeData);
    var qrCodeImage = qrCode.GetGraphic(20);

    return Results.File(qrCodeImage, "image/png", $"qr_{code}.png");
});

app.MapGet("/vcard-qr/{id:int}", async (int id, ApplicationDbContext db) =>
{
    var vcard = await db.VCards.FirstOrDefaultAsync(v => v.Id == id);
    if (vcard == null)
        return Results.NotFound();

    // Generate vCard format text
    var vcardText = $@"BEGIN:VCARD
VERSION:3.0
N:{vcard.LastName};{vcard.FirstName};;;
FN:{vcard.FirstName} {vcard.LastName}
ORG:{vcard.Organization}
TITLE:{vcard.JobTitle}
EMAIL:{vcard.Email}
TEL:{vcard.Mobile}
URL:{vcard.Website}
ADR:;;{vcard.Address};;;
NOTE:{vcard.Note}
END:VCARD";

    byte[] qrCodeImage;
    // Generate QR code
    using(var qrGenerator = new QRCodeGenerator())
    using(var qrCodeData = qrGenerator.CreateQrCode(vcardText, QRCodeGenerator.ECCLevel.Q))
    using (var qrCode = new PngByteQRCode(qrCodeData))
    {
        qrCodeImage = qrCode.GetGraphic(20);
    }
    

    return Results.File(qrCodeImage, "image/png", $"vcard_{vcard.FirstName}_{vcard.LastName}.png");
});

app.MapGet("/vcard-qr-print/{id:int}", async (int id, ApplicationDbContext db) =>
{
    var vcard = await db.VCards.FirstOrDefaultAsync(v => v.Id == id);
    if (vcard == null)
        return Results.NotFound();

    // Generate vCard format text
    var vcardText = $@"BEGIN:VCARD
VERSION:3.0
N:{vcard.LastName};{vcard.FirstName};;;
FN:{vcard.FirstName} {vcard.LastName}
ORG:{vcard.Organization}
TITLE:{vcard.JobTitle}
EMAIL:{vcard.Email}
TEL;CELL,VOICE;PREF:{vcard.Mobile}
TEL;HOME,VOICE:{vcard.Phone}
TEL;WORK;VOICE:{vcard.OfficeNumber}
URL:{vcard.Website}
ADR:;;{vcard.Address};;;
NOTE:{vcard.Note}
END:VCARD";

    // Generate QR code as a base64 string for embedding in HTML
    string base64QrCode;
    using(var qrGenerator = new QRCodeGenerator())
    using(var qrCodeData = qrGenerator.CreateQrCode(vcardText, QRCodeGenerator.ECCLevel.Q))
    using(var qrCode = new PngByteQRCode(qrCodeData))
    {
        var qrCodeImage = qrCode.GetGraphic(20);
        base64QrCode = Convert.ToBase64String(qrCodeImage);
    }

    // Create a simple HTML page with just the QR code and auto-print JavaScript
    var html = $@"<!DOCTYPE html>
<html>
<head>
    <title>VCard QR Code - {vcard.FirstName} {vcard.LastName}</title>
    <style>
        body {{ margin: 0; display: flex; justify-content: center; align-items: center; height: 100vh; }}
        .container {{ text-align: center; max-height: 20%; max-width: 20%; display: flex; flex-direction: column; justify-content: center; align-items: center; padding-top: 200px; }}
        img {{ max-width: 100%; height: auto; }}
        @media print {{
            .no-print {{ display: none; }}
        }}
    </style>
</head>
<body>
    <div class='container'>
        <img src='data:image/png;base64,{base64QrCode}' alt='VCard QR Code'>
        <h3>{vcard.FirstName} {vcard.LastName}</h3>
        <p class='no-print'><button onclick='window.print()'>Print QR Code</button></p>
    </div>
    <script>
        // Optional: Automatically open print dialog when page loads
        // window.onload = function() {{ window.print(); }};
    </script>
</body>
</html>";

    return Results.Content(html, "text/html");
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

        switch (stripeEvent.Type)
        {
            case EventTypes.CustomerSubscriptionCreated:
            {
                var subscription = stripeEvent.Data.Object as Subscription;
                var userStripeInfo = await db.UserStripeInfos
                    .FirstOrDefaultAsync(u => u.StripeCustomerId == subscription.CustomerId);
                if (userStripeInfo != null)
                {
                    var user = await userManager.FindByIdAsync(userStripeInfo.UserId);
                    if (user != null)
                    {
                        var newRole = subscription.Id switch
                        {
                            { } id when id == configuration["Stripe:BasicPriceId"] => "Basic",
                            { } id when id == configuration["Stripe:ProfessionalPriceId"] => "Professional",
                            { } id when id == configuration["Stripe:EnterprisePriceId"] => "Enterprise",
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

                break;
            }
            case EventTypes.CustomerSubscriptionDeleted:
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

                break;
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
return;

DestinationUrl SelectWeightedDestinationUrl(UrlShort urlShort)
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

internal class GeoLocationResponse
{
    public string Ip { get; init; }
    public string City { get; init; }
    public string Region { get; init; }
    public string Country { get; init; }
    public bool Error { get; init; }
}

internal class ScreenResolutionRequest
{
    public int ClickId { get; set; }
    public string ScreenResolution { get; set; }
}