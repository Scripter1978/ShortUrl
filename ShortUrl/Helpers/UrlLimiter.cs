using System.Security.Claims;

namespace ShortUrl.Helpers;

public class UrlLimiter
{
    public static int? GetShortUrlLimitForUser(ClaimsPrincipal user)
    {
        if (user.IsInRole("Enterprise")) return null; // Unlimited
        if (user.IsInRole("Professional")) return 10000;
        if (user.IsInRole("Basic")) return 200;
        if (user.IsInRole("Free")) return 20;
        return 0;
    }
}