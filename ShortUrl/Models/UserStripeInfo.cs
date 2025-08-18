namespace ShortUrl.Models;

public class UserStripeInfo
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string StripeCustomerId { get; set; }
}