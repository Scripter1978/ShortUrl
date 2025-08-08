namespace ShortUrl.Services;

public interface IAuditService
{
    Task LogAsync(string? userId, string action, string entityType, string details);
}