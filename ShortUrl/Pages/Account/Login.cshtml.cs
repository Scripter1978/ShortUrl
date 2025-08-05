using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using ShortUrl.Services;
using System.ComponentModel.DataAnnotations;

namespace ShortUrl.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IAuditService _auditService;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            IAuditService auditService,
            ILogger<LoginModel> logger)
        {
            _signInManager = signInManager ?? throw new ArgumentNullException(nameof(signInManager));
            _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ErrorMessage { get; set; }

        public class InputModel
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            public bool RememberMe { get; set; }
        }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    ErrorMessage = "Please correct the errors in the form.";
                    await _auditService.LogAsync(null, "Invalid login attempt", "User", 0, "Invalid form data");
                    return Page();
                }

                var user = await _userManager.FindByEmailAsync(Input.Email);
                if (user == null)
                {
                    ErrorMessage = "Invalid email or password.";
                    await _auditService.LogAsync(null, "Login failed: User not found", "User", 0, $"Email: {Input.Email}");
                    return Page();
                }

                var result = await _signInManager.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);
                if (result.Succeeded)
                {
                    await _auditService.LogAsync(user.Id, "Successful login", "User", 0, $"Email: {Input.Email}");
                    return LocalRedirect(returnUrl ?? "/Index");
                }

                if (result.IsLockedOut)
                {
                    ErrorMessage = "This account is locked out. Please try again later.";
                    await _auditService.LogAsync(user.Id, "Login failed: Account locked out", "User", 0, $"Email: {Input.Email}");
                    return Page();
                }

                ErrorMessage = "Invalid email or password.";
                await _auditService.LogAsync(user.Id, "Login failed: Invalid password", "User", 0, $"Email: {Input.Email}");
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing login for email: {Email}", Input.Email);
                ErrorMessage = "An error occurred during login. Please try again later.";
                await _auditService.LogAsync(Input.Email, "Login error", "User", 0, ex.Message);
                return Page();
            }
        }
    }
}
