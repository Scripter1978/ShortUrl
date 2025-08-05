using Microsoft.EntityFrameworkCore;
using ShortUrl.Data;
using ShortUrl.Models;

namespace ShortUrl.Services;

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _dbContext;

    public AuditService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task LogAsync(string? userId, string action, string entityType, int? entityId, string details)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(action) || string.IsNullOrEmpty(entityType) || !entityId.HasValue)
        {
            throw new ArgumentException("Invalid parameters for audit log.");
        }
        var userForeign = await _dbContext.Users.FirstOrDefaultAsync(x=>x.UserName == userId);
        if (userForeign == null)
        {
            throw new ArgumentException("User not found for the provided userId.");
        }
        var auditLog = new AuditLog
        {
            UserId = userForeign.Id,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.Value,
            Details = details,
            Timestamp = DateTime.UtcNow
        };
        _dbContext.AuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync();
    }
}