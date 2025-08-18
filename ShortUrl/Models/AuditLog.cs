using Microsoft.AspNetCore.Identity;

namespace ShortUrl.Models;

public class AuditLog
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public string? UserIdString { get; set; } // For storing usernames when user isn't in database
    public IdentityUser? User { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; } 
    public required string Details { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}