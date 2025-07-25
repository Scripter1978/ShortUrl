using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;
using Stripe;
using ShortUrl.Data;
using ShortUrl.Models;

namespace ShortUrl.Pages.Account
{
    [Authorize(Roles = "Free,Basic,Professional")]
    public class UpgradeModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;

        public UpgradeModel(
            UserManager<IdentityUser> userManager,
            IConfiguration configuration,
            ApplicationDbContext dbContext)
        {
            _userManager = userManager;
            _configuration = configuration;
            _dbContext = dbContext;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            public string SubscriptionTier { get; set; } // Basic, Professional, Enterprise
        }

        public IActionResult OnGet()
        {
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToPage("/Account/Login");
            }

            // Check if user already has a Stripe customer ID
            var userStripeInfo = await _dbContext.UserStripeInfos
                .FirstOrDefaultAsync(u => u.UserId == user.Id);
            string customerId;

            if (userStripeInfo == null)
            {
                var customerService = new CustomerService();
                var customer = await customerService.CreateAsync(new CustomerCreateOptions
                {
                    Email = user.Email
                });
                customerId = customer.Id;

                _dbContext.UserStripeInfos.Add(new UserStripeInfo
                {
                    UserId = user.Id,
                    StripeCustomerId = customer.Id
                });
                await _dbContext.SaveChangesAsync();
            }
            else
            {
                customerId = userStripeInfo.StripeCustomerId;
            }

            // Map subscription tier to price ID
            var priceId = Input.SubscriptionTier switch
            {
                "Basic" => _configuration["Stripe:BasicPriceId"],
                "Professional" => _configuration["Stripe:ProfessionalPriceId"],
                "Enterprise" => _configuration["Stripe:EnterprisePriceId"],
                _ => null
            };

            if (priceId == null)
            {
                ModelState.AddModelError(string.Empty, "Invalid subscription tier selected.");
                return Page();
            }

            // Create Stripe Checkout session
            var sessionService = new SessionService();
            var sessionOptions = new SessionCreateOptions
            {
                Customer = customerId,
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Price = priceId,
                        Quantity = 1
                    }
                },
                Mode = "subscription",
                SuccessUrl = $"{_configuration["AppUrl"]}/Account/SubscriptionSuccess?sessionId={{CHECKOUT_SESSION_ID}}",
                CancelUrl = $"{_configuration["AppUrl"]}/Account/Upgrade"
            };
            var session = await sessionService.CreateAsync(sessionOptions);

            return Redirect(session.Url);
        }
    }
}