using Microsoft.EntityFrameworkCore;
using ShortUrl.Data;
using ShortUrl.Models;

namespace ShortUrl.Services;

public class AuditService : IAuditService
{
    private readonly ApplicationDbContext _dbContext;

    public AuditService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task LogAsync(string? userId, string action, string entityType, string details)
    {
        if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(entityType))
        {
            throw new ArgumentException("Invalid parameters for audit log.");
        }
        
        // If userId is null or empty, we'll log without a user association
        if (string.IsNullOrEmpty(userId))
        {
            var anonymousAuditLog = new AuditLog
            {
                Action = action,
                EntityType = entityType,
                Details = details,
                Timestamp = DateTime.UtcNow
            };
            
            await _dbContext.AuditLogs.AddAsync(anonymousAuditLog);
            await _dbContext.SaveChangesAsync();
            return;
        }
        
        var userForeign = await _dbContext.Users.FirstOrDefaultAsync(x => x.UserName == userId);
        if (userForeign == null)
        {
            // Log with userId as string since user doesn't exist in database
            var unknownUserAuditLog = new AuditLog
            {
                UserIdString = userId, // Assuming you have this field, or a similar approach
                Action = action,
                EntityType = entityType,
                Details = details,
                Timestamp = DateTime.UtcNow
            };
            
            await _dbContext.AuditLogs.AddAsync(unknownUserAuditLog);
            await _dbContext.SaveChangesAsync();
            return;
        }
        
        var auditLog = new AuditLog
        {
            UserId = userForeign.Id,
            Action = action,
            EntityType = entityType, 
            Details = details,
            Timestamp = DateTime.UtcNow
        };
        _dbContext.AuditLogs.Add(auditLog);
        await _dbContext.SaveChangesAsync();
    }
}