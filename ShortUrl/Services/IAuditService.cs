namespace ShortUrl.Services;

public interface IAuditService
{
    Task LogAsync(string? userId, string action, string entityType, int? entityId, string details);
}