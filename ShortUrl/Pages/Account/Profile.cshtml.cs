using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShortUrl.Data;
using System.Security.Claims;

namespace ShortUrl.Pages.Account
{
    [Authorize]
    public class ProfileModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ApplicationDbContext _dbContext;

        public ProfileModel(
            UserManager<IdentityUser> userManager,
            ApplicationDbContext dbContext)
        {
            _userManager = userManager;
            _dbContext = dbContext;
        }

        public string Username { get; set; }
        public DateTime MemberSince { get; set; }
        public DateTime? SubscriptionRenewalDate { get; set; }
        public int ShortUrlCount { get; set; }
        public int VCardCount { get; set; }
        public int TotalClicks { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.FindByIdAsync(userId);
            
            if (user == null)
            {
                return NotFound();
            }

            Username = user.UserName;
            
            // Get the user creation date - fallback to current time if not available
            var userClaims = await _userManager.GetClaimsAsync(user);
            var creationDateClaim = userClaims.FirstOrDefault(c => c.Type == "CreatedAt");
            if (creationDateClaim != null && DateTime.TryParse(creationDateClaim.Value, out var createdDate))
            {
                MemberSince = createdDate;
            }
            else
            {
                MemberSince = DateTime.UtcNow;
            }

            // Get subscription details from your Subscriptions table
            var subscription = await _dbContext.MemberSubscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.IsActive);
                
            if (subscription != null)
            {
                SubscriptionRenewalDate = subscription.NextBillingDate;
            }

            // Get usage statistics from your models
            ShortUrlCount = await _dbContext.UrlShorts
                .CountAsync(u => u.UserId == userId && !u.IsDeleted);
                
            VCardCount = await _dbContext.VCards
                .CountAsync(v => v.UserId == userId);
                
            // Sum all clicks associated with the user's shortened URLs
            TotalClicks = await _dbContext.ClickStats
                .Join(_dbContext.UrlShorts,
                      c => c.UrlShortId,
                      u => u.Id,
                      (c, u) => new { Click = c, Url = u })
                .Where(x => x.Url.UserId == userId && !x.Url.IsDeleted)
                .CountAsync();

            return Page();
        }

        public async Task<IActionResult> OnPostCancelSubscriptionAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            var subscription = await _dbContext.MemberSubscriptions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.IsActive);
                
            if (subscription != null)
            {
                // Mark subscription for cancellation at the end of the billing period
                subscription.CancelAtPeriodEnd = true;
                
                // Log the cancellation request in audit logs
                _dbContext.AuditLogs.Add(new Models.AuditLog
                {
                    UserId = userId,
                    Action = "CancelSubscription",
                    EntityType = "Subscription", 
                    Details = "User canceled subscription",
                    Timestamp = DateTime.UtcNow
                });
                
                await _dbContext.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "Your subscription has been canceled and will end on your next billing date.";
            }
            
            return RedirectToPage();
        }
    }
}