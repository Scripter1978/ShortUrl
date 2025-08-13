using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShortUrl.Data;
using ShortUrl.Models;

namespace ShortUrl.Pages;


    [Authorize]
    public class VCardManagerModel : PageModel
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly UserManager<IdentityUser> _userManager;

        public VCardManagerModel(
            ApplicationDbContext dbContext,
            UserManager<IdentityUser> userManager)
        {
            _dbContext = dbContext;
            _userManager = userManager;
        }

        [BindProperty]
        public VCard VCard { get; set; } = new VCard();
        
        public List<VCard> UserVCards { get; set; } = new List<VCard>();

        public async Task<IActionResult> OnGetAsync(int? id = null)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            
            UserVCards = await _dbContext.VCards
                .Where(v => v.UserId == userId)
                .Where(v => !v.IsDeleted)
                .ToListAsync();

            if (!id.HasValue) return Page();
            
            var vcard = await _dbContext.VCards
                .FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);
                
            if (vcard != null)
            {
                VCard = vcard;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            
        
            VCard.UserId = userId;
            VCard.User = await _userManager.FindByIdAsync(userId);
            
            ModelState.ClearValidationState("VCard.UserId");
            ModelState.ClearValidationState("VCard.User");


            ModelState.MarkFieldValid("VCard.UserId");
            ModelState.MarkFieldValid("VCard.User");
            
            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            

            if (VCard.Id == 0)
            {
                // New vCard
                _dbContext.VCards.Add(VCard);
            }
            else
            {
                // Editing existing vCard
                var existing = await _dbContext.VCards
                    .FirstOrDefaultAsync(v => v.Id == VCard.Id && v.UserId == userId);
                    
                if (existing == null)
                {
                    return NotFound();
                }
                
                _dbContext.Entry(existing).CurrentValues.SetValues(VCard);
            }
            
            await _dbContext.SaveChangesAsync();
            return RedirectToPage("/VCardManager");
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
             
            await _dbContext.VCards.Where(v => v.Id == id && v.UserId == userId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(v => v.IsDeleted, true)
                    .SetProperty(v => v.DeletedAt, DateTime.UtcNow));
          
            return RedirectToPage("/VCardManager");
        }
    }
