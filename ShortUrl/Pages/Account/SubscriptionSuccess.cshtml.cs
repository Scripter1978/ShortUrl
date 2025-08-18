using Microsoft.AspNetCore.Mvc.RazorPages;
using Stripe.Checkout;

namespace ShortUrl.Pages.Account;

public class SubscriptionSuccessModel : PageModel
{
    public string SessionId { get; set; } = string.Empty;

    public async Task OnGetAsync(string sessionId)
    {
        SessionId = sessionId ?? string.Empty;
        if (!string.IsNullOrEmpty(sessionId))
        {
            var sessionService = new SessionService();
            var session = await sessionService.GetAsync(sessionId);
            // The webhook handles role updates, so no additional action needed here
        }
    }
}