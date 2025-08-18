using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace ShortUrl.Models;

public class VCard
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    [StringLength(50)]
    public required string FirstName { get; set; }
    [StringLength(50)]
    public string? LastName { get; set; }
    public string? Organization { get; set; }
    public string? JobTitle { get; set; }
    [EmailAddress]
    public string? Email { get; set; }
    
    [Phone]
    public string? Phone { get; set; }
    
    [RegularExpression(@"^\+?(\d[\d-. ]+)?(\([\d-. ]+\))?[\d-. ]+\d$", 
        ErrorMessage = "Please enter a valid fax number")]
    public string? Fax { get; set; }
    
    [RegularExpression(@"^\+?(\d[\d-. ]+)?(\([\d-. ]+\))?[\d-. ]+\d$", 
        ErrorMessage = "Please enter a valid mobile number")]
    public string? Mobile { get; set; }
    
    [RegularExpression(@"^\+?(\d[\d-. ]+)?(\([\d-. ]+\))?[\d-. ]+\d$", 
        ErrorMessage = "Please enter a valid office number")]
    public string? OfficeNumber { get; set; }
    
    [Url]
    public string? Website { get; set; }
    public string? Address { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // Soft delete properties
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public IdentityUser? User { get; set; }
}