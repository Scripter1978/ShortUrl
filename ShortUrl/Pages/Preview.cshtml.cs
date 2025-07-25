using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShortUrl.Data;
using ShortUrl.Models;

namespace ShortUrl.Pages
{
    [Authorize]
    public class PreviewModel : PageModel
    {
        private readonly ApplicationDbContext _dbContext;

        public PreviewModel(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public UrlShort ShortUrl { get; set; }
        public List<ClickStat> ClickStats { get; set; } = new List<ClickStat>();
        public bool IsProfessionalOrHigher { get; set; }

        public async Task<IActionResult> OnGetAsync(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return NotFound();
            }

            ShortUrl = await _dbContext.ShortUrls
                .FirstOrDefaultAsync(s => s.Code == code);

            if (ShortUrl == null)
            {
                return NotFound();
            }

            // Check if user has the "Paid" role
            IsProfessionalOrHigher = User.IsInRole("Paid");

            if (IsProfessionalOrHigher)
            {
                // Fetch click stats for Paid users
                ClickStats = await _dbContext.ClickStats
                    .Where(c => c.ShortUrlId == ShortUrl.Id)
                    .OrderByDescending(c => c.ClickedAt)
                    .ToListAsync();
            }

            return Page();
        }
    }
}