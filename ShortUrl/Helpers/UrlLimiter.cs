using System.Security.Claims;

namespace ShortUrl.Helpers;

public class UrlLimiter
{
    public static int? GetShortUrlLimitForUser(ClaimsPrincipal user)
    {
        if (user.IsInRole("Basic")) return 200;
        return user.IsInRole("Free") ? 10 : 0;
    }
    
    public static int? GetVCardLimitForUser(ClaimsPrincipal user)
    {
        if (user.IsInRole("Basic")) return 50;
        return user.IsInRole("Free") ? 1 : 0;
    }
}