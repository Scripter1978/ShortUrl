using Microsoft.AspNetCore.Identity;

namespace ShortUrl.Models;

public class VCard
{
    public int Id { get; set; }
    public string UserId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Organization { get; set; }
    public string JobTitle { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Website { get; set; }
    public string Address { get; set; }
    public string Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public IdentityUser User { get; set; }
}