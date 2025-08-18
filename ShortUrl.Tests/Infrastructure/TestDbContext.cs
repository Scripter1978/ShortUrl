using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortUrl.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace ShortUrl.Tests.Infrastructure
{
    public class TestDbContext
    {
        public static ApplicationDbContext GetInMemoryDbContext(string? dbName = null)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
                .Options;

            var context = new ApplicationDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        public static async Task SeedTestData(ApplicationDbContext dbContext)
        {
            // First, clear any existing roles
            dbContext.Roles.RemoveRange(dbContext.Roles);
            await dbContext.SaveChangesAsync();
            
            // Seed only Free and Basic roles
            var roleStore = new RoleStore<IdentityRole>(dbContext);
            
            if (!dbContext.Roles.Any(r => r.Name == "Free"))
            {
                await roleStore.CreateAsync(new IdentityRole("Free") { NormalizedName = "FREE" });
            }
            
            if (!dbContext.Roles.Any(r => r.Name == "Basic"))
            {
                await roleStore.CreateAsync(new IdentityRole("Basic") { NormalizedName = "BASIC" });
            }

            // Seed a test user
            var userStore = new UserStore<IdentityUser>(dbContext);
            var testUser = new IdentityUser
            {
                Id = "test-user-id",
                UserName = "testuser@example.com",
                NormalizedUserName = "TESTUSER@EXAMPLE.COM",
                Email = "testuser@example.com",
                NormalizedEmail = "TESTUSER@EXAMPLE.COM",
                EmailConfirmed = true
            };

            if (!dbContext.Users.Any(u => u.Id == testUser.Id))
            {
                await userStore.CreateAsync(testUser);
                await dbContext.UserRoles.AddAsync(new IdentityUserRole<string>
                {
                    UserId = testUser.Id,
                    RoleId = dbContext.Roles.First(r => r.Name == "Free").Id
                });
            }

            // Seed a premium user
            var premiumUser = new IdentityUser
            {
                Id = "premium-user-id",
                UserName = "premium@example.com",
                NormalizedUserName = "PREMIUM@EXAMPLE.COM",
                Email = "premium@example.com",
                NormalizedEmail = "PREMIUM@EXAMPLE.COM",
                EmailConfirmed = true
            };

            if (!dbContext.Users.Any(u => u.Id == premiumUser.Id))
            {
                await userStore.CreateAsync(premiumUser);
                await dbContext.UserRoles.AddAsync(new IdentityUserRole<string>
                {
                    UserId = premiumUser.Id,
                    RoleId = dbContext.Roles.First(r => r.Name == "Basic").Id
                });
            }

            // Seed some example URLs
            if (!dbContext.UrlShorts.Any())
            {
                var freeUserUrl = new Models.UrlShort
                {
                    Id = 1,
                    UserId = testUser.Id,
                    Code = "test123",
                    CreatedAt = DateTime.UtcNow.AddDays(-7),
                    IsDeleted = false,
                    CurrentDestinationIndex = 0,
                    CurrentOgMetadataIndex = 0,
                    DestinationUrls = new List<Models.DestinationUrl>
                    {
                        new Models.DestinationUrl
                        {
                            Id = 1,
                            Url = "https://example.com",
                            Weight = 1
                        }
                    },
                    OgMetadataVariations = new List<Models.OgMetadata>(),
                    ClickStats = new List<Models.ClickStat>()
                };

                var premiumUserUrl = new Models.UrlShort
                {
                    Id = 2,
                    UserId = premiumUser.Id,
                    Code = "premium123",
                    CreatedAt = DateTime.UtcNow.AddDays(-3),
                    IsDeleted = false,
                    CurrentDestinationIndex = 0,
                    CurrentOgMetadataIndex = 0,
                    DestinationUrls = new List<Models.DestinationUrl>
                    {
                        new Models.DestinationUrl
                        {
                            Id = 2,
                            Url = "https://example.org",
                            UtmSource = "test",
                            UtmMedium = "email",
                            UtmCampaign = "summer_promo",
                            Weight = 1
                        },
                        new Models.DestinationUrl
                        {
                            Id = 3,
                            Url = "https://example.net",
                            Weight = 2
                        }
                    },
                    OgMetadataVariations = new List<Models.OgMetadata>
                    {
                        new Models.OgMetadata
                        {
                            Id = 1,
                            Title = "Premium Test",
                            Description = "This is a test for premium users",
                            Image = "/og-images/test.jpg"
                        }
                    },
                    ClickStats = new List<Models.ClickStat>()
                };

                dbContext.UrlShorts.Add(freeUserUrl);
                dbContext.UrlShorts.Add(premiumUserUrl);
            }

            await dbContext.SaveChangesAsync();
        }
    }
}
