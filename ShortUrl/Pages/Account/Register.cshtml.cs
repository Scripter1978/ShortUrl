using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Stripe.Checkout;
using Stripe;
using ShortUrl.Data;
using ShortUrl.Models;

namespace ShortUrl.Pages.Account
{
    public class RegisterModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ApplicationDbContext _dbContext;

        public RegisterModel(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            IConfiguration configuration,
            IHttpContextAccessor httpContextAccessor,
            ApplicationDbContext dbContext)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
            _dbContext = dbContext;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public class InputModel
        {
            public string Email { get; set; }
            public string Password { get; set; }
            public string SubscriptionTier { get; set; } // Free, Basic, Professional, Enterprise
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var user = new IdentityUser { UserName = Input.Email, Email = Input.Email };
            var result = await _userManager.CreateAsync(user, Input.Password);
            if (result.Succeeded)
            {
                // Assign Free role by default
                await _userManager.AddToRoleAsync(user, "Free");

                if (Input.SubscriptionTier != "Free")
                {
                    // Create Stripe customer
                    var customerService = new CustomerService();
                    var customer = await customerService.CreateAsync(new CustomerCreateOptions
                    {
                        Email = Input.Email
                    });

                    // Store Stripe customer ID
                    _dbContext.UserStripeInfos.Add(new UserStripeInfo
                    {
                        UserId = user.Id,
                        StripeCustomerId = customer.Id
                    });
                    await _dbContext.SaveChangesAsync();

                    // Map subscription tier to price ID
                    var priceId = Input.SubscriptionTier switch
                    {
                        "Basic" => _configuration["Stripe:BasicPriceId"],
                        "Professional" => _configuration["Stripe:ProfessionalPriceId"],
                        "Enterprise" => _configuration["Stripe:EnterprisePriceId"],
                        _ => null
                    };

                    if (priceId != null)
                    {
                        // Create Stripe Checkout session
                        var sessionService = new SessionService();
                        var sessionOptions = new SessionCreateOptions
                        {
                            Customer = customer.Id,
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
                            CancelUrl = $"{_configuration["AppUrl"]}/Account/Register"
                        };
                        var session = await sessionService.CreateAsync(sessionOptions);

                        // Sign in and redirect to Stripe Checkout
                        await _signInManager.SignInAsync(user, isPersistent: false);
                        return Redirect(session.Url);
                    }
                }

                // Sign in and redirect to homepage for Free users
                await _signInManager.SignInAsync(user, isPersistent: false);
                return RedirectToPage("/Index");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }
    }
}