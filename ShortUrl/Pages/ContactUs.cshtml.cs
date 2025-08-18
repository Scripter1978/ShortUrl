using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace ShortUrl.Pages;
    public class ContactUsModel : PageModel
    {
        [BindProperty]
        public ContactInputModel Contact { get; set; } = new();

        public void OnGet() { }

        public IActionResult OnPost()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Process the message here (e.g., send email, log, etc.)
            TempData["SuccessMessage"] = "Your message has been sent!";
            return RedirectToPage("/ContactUs");
        }
    }

    public class ContactInputModel
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;
    }
