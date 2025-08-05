using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShortUrl.Data;
using ShortUrl.Models;
using ShortUrl.Services;
using System.Security.Claims;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace ShortUrl.Pages
{
    [Authorize(Policy = "ProfessionalClient")]
    public class EditUrlModel : PageModel
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly IWebHostEnvironment _environment;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IAuditService _auditService;

        public EditUrlModel(
            ApplicationDbContext dbContext,
            IWebHostEnvironment environment,
            UserManager<IdentityUser> userManager,
            IAuditService auditService)
        {
            _dbContext = dbContext;
            _environment = environment;
            _userManager = userManager;
            _auditService = auditService;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            public int Id { get; set; }
            public string Code { get; set; }
            public List<DestinationUrl> DestinationUrls { get; set; } = new List<DestinationUrl>();
            public List<OgMetadata> OgMetadataVariations { get; set; } = new List<OgMetadata>();
            public DateTime? ExpirationDate { get; set; }
            public string? Password { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(int id)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var urlShort = await _dbContext.UrlShorts
                .Include(s => s.DestinationUrls)
                .Include(s => s.OgMetadataVariations)
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId && !s.IsDeleted);

            if (urlShort == null)
            {
                TempData["ErrorMessage"] = "URL not found or you don't have permission to edit it.";
                return RedirectToPage("/Index");
            }

            Input = new InputModel
            {
                Id = urlShort.Id,
                Code = urlShort.Code,
                DestinationUrls = urlShort.DestinationUrls,
                OgMetadataVariations = urlShort.OgMetadataVariations,
                ExpirationDate = urlShort.ExpirationDate,
                Password = null // Don't expose password
            };

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid || !Input.DestinationUrls.Any() || Input.DestinationUrls.Any(d => string.IsNullOrWhiteSpace(d.Url)))
            {
                TempData["ErrorMessage"] = "At least one valid destination URL is required.";
                return Page();
            }

            if (Input.DestinationUrls.Count > 5 || Input.OgMetadataVariations.Count > 5)
            {
                TempData["ErrorMessage"] = "You can have up to 5 destination URLs and 5 metadata variations.";
                return Page();
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var isEnterprise = await _userManager.IsInRoleAsync(await _userManager.FindByIdAsync(userId), "Enterprise");

            var urlShort = await _dbContext.UrlShorts
                .Include(s => s.DestinationUrls)
                .Include(s => s.OgMetadataVariations)
                .FirstOrDefaultAsync(s => s.Id == Input.Id && s.UserId == userId && !s.IsDeleted);

            if (urlShort == null)
            {
                TempData["ErrorMessage"] = "URL not found or you don't have permission to edit it.";
                return RedirectToPage("/Index");
            }

            // Validate custom slug
            if (!string.IsNullOrWhiteSpace(Input.Code) && Input.Code != urlShort.Code)
            {
                if (!Regex.IsMatch(Input.Code, @"^[a-zA-Z0-9\-_]+$"))
                {
                    ModelState.AddModelError("Input.Code", "Custom slug can only contain alphanumeric characters, hyphens, and underscores.");
                    TempData["ErrorMessage"] = "Invalid slug format.";
                    return Page();
                }
                if (await _dbContext.UrlShorts.AnyAsync(s => s.Code.ToLower() == Input.Code.ToLower() && s.Id != Input.Id))
                {
                    ModelState.AddModelError("Input.Code", "Custom slug is already in use. Please choose a different slug.");
                    TempData["ErrorMessage"] = "Slug is already in use.";
                    return Page();
                }
            }

            // Process OG metadata images
            var ogMetadataWithImages = new List<OgMetadata>();
            for (int i = 0; i < Input.OgMetadataVariations.Count; i++)
            {
                var og = Input.OgMetadataVariations[i];
                if (Request.Form.Files.Any(f => f.Name == $"Input.OgMetadataVariations[{i}].ImageFile"))
                {
                    var file = Request.Form.Files[$"Input.OgMetadataVariations[{i}].ImageFile"];
                    if (file != null && file.Length > 0)
                    {
                        if (!file.ContentType.StartsWith("image/"))
                        {
                            TempData["ErrorMessage"] = "Only image files are allowed for social media metadata.";
                            return Page();
                        }
                        if (file.Length > 2 * 1024 * 1024) // 2MB
                        {
                            TempData["ErrorMessage"] = "Image size must be less than 2MB.";
                            return Page();
                        }

                        var uploadsFolder = Path.Combine(_environment.WebRootPath, "og-images");
                        Directory.CreateDirectory(uploadsFolder);
                        var fileName = $"{Guid.NewGuid()}_{file.FileName}";
                        var filePath = Path.Combine(uploadsFolder, fileName);

                        // Compress image with SkiaSharp
                        using (var inputStream = file.OpenReadStream())
                        using (var bitmap = SKBitmap.Decode(inputStream))
                        {
                            int width = bitmap.Width;
                            int height = bitmap.Height;
                            float aspectRatio = (float)width / height;
                            int targetWidth = 1200;
                            int targetHeight = 630;

                            // Calculate new dimensions to maintain aspect ratio
                            if (aspectRatio > (float)targetWidth / targetHeight)
                            {
                                height = (int)(width / ((float)targetWidth / targetHeight));
                            }
                            else
                            {
                                width = (int)(height * ((float)targetWidth / targetHeight));
                            }

                            using (var resizedBitmap = bitmap.Resize(new SKImageInfo(width, height), SKFilterQuality.High))
                            using (var image = SKImage.FromBitmap(resizedBitmap))
                            using (var outputStream = new FileStream(filePath, FileMode.Create))
                            {
                                image.Encode(SKEncodedImageFormat.Jpeg, 80).SaveTo(outputStream);
                            }
                        }

                        // Remove old image if exists
                        if (!string.IsNullOrEmpty(og.Image) && System.IO.File.Exists(Path.Combine(_environment.WebRootPath, og.Image.TrimStart('/'))))
                        {
                            System.IO.File.Delete(Path.Combine(_environment.WebRootPath, og.Image.TrimStart('/')));
                        }
                        og.Image = $"/og-images/{fileName}";
                    }
                }
                if (!string.IsNullOrWhiteSpace(og.Title) || !string.IsNullOrWhiteSpace(og.Description) || !string.IsNullOrEmpty(og.Image))
                {
                    ogMetadataWithImages.Add(og);
                }
            }

            // Update UrlShort properties
            var oldCode = urlShort.Code;
            urlShort.Code = Input.Code ?? urlShort.Code;
            urlShort.ExpirationDate = isEnterprise ? Input.ExpirationDate : urlShort.ExpirationDate;
            if (isEnterprise && Input.Password != null)
            {
                urlShort.Password = string.IsNullOrEmpty(Input.Password) ? null : BCrypt.Net.BCrypt.HashPassword(Input.Password);
            }

            // Update DestinationUrls
            _dbContext.DestinationUrls.RemoveRange(urlShort.DestinationUrls);
            urlShort.DestinationUrls = Input.DestinationUrls.Select(d => new DestinationUrl
            {
                Url = d.Url,
                UtmSource = d.UtmSource,
                UtmMedium = d.UtmMedium,
                UtmCampaign = d.UtmCampaign,
                Weight = d.Weight > 0 ? d.Weight : 1 // Default weight
            }).ToList();
            urlShort.CurrentDestinationIndex = 0;

            // Update OgMetadataVariations
            _dbContext.OgMetadataVariations.RemoveRange(urlShort.OgMetadataVariations);
            urlShort.OgMetadataVariations = ogMetadataWithImages;
            urlShort.CurrentOgMetadataIndex = 0;

            await _dbContext.SaveChangesAsync();

            // Log audit
            if (isEnterprise)
            {
                await _auditService.LogAsync(userId, "Edit", "ShortUrl", urlShort.Id,
                    $"Updated slug from '{oldCode}' to '{urlShort.Code}', {urlShort.DestinationUrls.Count} URLs, {urlShort.OgMetadataVariations.Count} OG variations.");
            }

            TempData["SuccessMessage"] = "URL updated successfully.";
            return RedirectToPage("/Index");
        }
    }
}