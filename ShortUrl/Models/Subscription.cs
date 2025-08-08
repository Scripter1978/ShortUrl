using Microsoft.AspNetCore.Identity;

namespace ShortUrl.Models;

public class MemberSubscription
{
    public int Id { get; set; }
    public string UserId { get; set; }
    public IdentityUser User { get; set; }
    public string PlanType { get; set; } // "Free", "Basic", "Professional", "Enterprise"
    public bool IsActive { get; set; } = true;
    public DateTime StartDate { get; set; } = DateTime.UtcNow;
    public DateTime? EndDate { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public bool CancelAtPeriodEnd { get; set; } = false;
}