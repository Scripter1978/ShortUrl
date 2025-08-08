using Microsoft.AspNetCore.Identity;

namespace ShortUrl.Models;

public class AuditLog
{
    public int Id { get; set; }
    public string UserId { get; set; }
    public IdentityUser User { get; set; }
    public string Action { get; set; }
    public string EntityType { get; set; } 
    public string Details { get; set; }
    public DateTime Timestamp { get; set; }
}