using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Stripe;
using ShortUrl.Data; 

namespace ShortUrl.Pages.Account
{
    [Authorize(Roles = "Basic,Professional,Enterprise")]
    public class CancelSubscriptionModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ApplicationDbContext _dbContext;

        public CancelSubscriptionModel(
            UserManager<IdentityUser> userManager,
            ApplicationDbContext dbContext)
        {
            _userManager = userManager;
            _dbContext = dbContext;
            ErrorMessage = string.Empty;
        }

        public string ErrorMessage { get; set; }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToPage("/Account/Login");
            }

            var userStripeInfo = await _dbContext.UserStripeInfos
                .FirstOrDefaultAsync(u => u.UserId == user.Id);
            if (userStripeInfo == null)
            {
                ErrorMessage = "No subscription found for this account.";
                return Page();
            }

            try
            {
                var subscriptionService = new SubscriptionService();
                var subscriptions = await subscriptionService.ListAsync(new SubscriptionListOptions
                {
                    Customer = userStripeInfo.StripeCustomerId,
                    Status = "active"
                });

                var activeSubscription = subscriptions.FirstOrDefault();
                if (activeSubscription == null)
                {
                    ErrorMessage = "No active subscription found.";
                    return Page();
                }

                await subscriptionService.CancelAsync(activeSubscription.Id);
                return RedirectToPage("/Account/SubscriptionCancelled");
            }
            catch (StripeException ex)
            {
                ErrorMessage = $"Failed to cancel subscription: {ex.Message}";
                return Page();
            }
        }
    }
}