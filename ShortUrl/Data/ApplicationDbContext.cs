using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShortUrl.Models;

namespace ShortUrl.Data
{
    public class ApplicationDbContext : IdentityDbContext<IdentityUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<UrlShort> UrlShorts { get; set; }
        public DbSet<ClickStat> ClickStats { get; set; }
        public DbSet<UserStripeInfo> UserStripeInfos { get; set; }
        public DbSet<DestinationUrl> DestinationUrls { get; set; }
        public DbSet<OgMetadata> OgMetadataVariations { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<VCard> VCards { get; set; }
        public DbSet<MemberSubscription> MemberSubscriptions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<UrlShort>()
                .HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}