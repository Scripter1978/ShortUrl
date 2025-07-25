namespace ShortUrl.Models;

public class UserStripeInfo
{
    public int Id { get; set; }
    public string UserId { get; set; }
    public string StripeCustomerId { get; set; }
}