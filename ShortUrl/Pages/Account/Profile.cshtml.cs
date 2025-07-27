using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

[Authorize]
public class ProfileModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;

    public ProfileModel(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
    }

    [Display(Name = "Email")]
    public string Email { get; set; }

    public IList<string> Roles { get; set; } = new List<string>();

    [BindProperty]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters.")]
    public string NewPassword { get; set; }

    [BindProperty]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; }

    public string StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();

        Email = user.Email;
        Roles = (await _userManager.GetRolesAsync(user)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return NotFound();

        Email = user.Email;
        Roles = (await _userManager.GetRolesAsync(user)).ToList();

        if (!ModelState.IsValid)
            return Page();

        if (!string.IsNullOrEmpty(NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, NewPassword);
            if (result.Succeeded)
            {
                StatusMessage = "Password updated successfully.";
                await _signInManager.RefreshSignInAsync(user);
            }
            else
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);
                return Page();
            }
        }
        else
        {
            StatusMessage = "No changes made.";
        }

        return Page();
    }
}