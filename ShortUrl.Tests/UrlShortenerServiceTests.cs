using Microsoft.EntityFrameworkCore;
using ShortUrl.Tests.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ShortUrl.Tests
{
    public class UrlShortenerServiceTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;

        public UrlShortenerServiceTests(TestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task CreateShortUrl_WithFreeUser_CreatesBasicUrl()
        {
            // Arrange
            var userId = "test-user-id"; // Free user
            var destinationUrls = new List<Models.DestinationUrl>
            {
                new Models.DestinationUrl
                {
                    Url = "https://example.com/test-page",
                    UtmSource = "test-source", // Will be ignored for free user
                    UtmMedium = "test-medium", // Will be ignored for free user
                    UtmCampaign = "test-campaign" // Will be ignored for free user
                }
            };

            // Act
            var code = await _fixture.UrlShortenerService.CreateShortUrlAsync(
                userId,
                null, // No custom slug for free user
                destinationUrls,
                new List<Models.OgMetadata>(), // No OG metadata for free user
                null, // No expiration date for free user
                null // No password for free user
            );

            // Assert
            Assert.NotNull(code);
            Assert.NotEmpty(code);
            
            var urlShort = await _fixture.DbContext.UrlShorts
                .Include(u => u.DestinationUrls)
                .Include(u => u.OgMetadataVariations)
                .FirstOrDefaultAsync(u => u.Code == code);
            
            Assert.NotNull(urlShort);
            Assert.Equal(userId, urlShort.UserId);
            Assert.Single(urlShort.DestinationUrls);
            Assert.Equal("https://example.com/test-page", urlShort.DestinationUrls.First().Url);
            
            // Free user limitations
            Assert.Null(urlShort.DestinationUrls.First().UtmSource);
            Assert.Null(urlShort.DestinationUrls.First().UtmMedium);
            Assert.Null(urlShort.DestinationUrls.First().UtmCampaign);
            Assert.Empty(urlShort.OgMetadataVariations);
            Assert.Null(urlShort.ExpirationDate);
            Assert.Null(urlShort.Password);
        }

        [Fact]
        public async Task CreateShortUrl_WithBasicUser_CreatesFullFeaturedUrl()
        {
            // Arrange
            var userId = "premium-user-id"; // Basic user
            var customSlug = "custom-slug-test";
            var destinationUrls = new List<Models.DestinationUrl>
            {
                new Models.DestinationUrl
                {
                    Url = "https://example.com/premium-page",
                    UtmSource = "premium-source",
                    UtmMedium = "premium-medium",
                    UtmCampaign = "premium-campaign",
                    Weight = 1
                },
                new Models.DestinationUrl
                {
                    Url = "https://example.com/variant-page",
                    Weight = 2
                }
            };
            var ogMetadata = new List<Models.OgMetadata>
            {
                new Models.OgMetadata
                {
                    Title = "Test Title",
                    Description = "Test Description",
                    Image = "/test-image.jpg"
                }
            };
            var expirationDate = DateTime.UtcNow.AddDays(30);
            var password = "testpassword";

            // Act
            var code = await _fixture.UrlShortenerService.CreateShortUrlAsync(
                userId,
                customSlug,
                destinationUrls,
                ogMetadata,
                expirationDate,
                password
            );

            // Assert
            Assert.Equal(customSlug, code); // Custom slug should be used
            
            var urlShort = await _fixture.DbContext.UrlShorts
                .Include(u => u.DestinationUrls)
                .Include(u => u.OgMetadataVariations)
                .FirstOrDefaultAsync(u => u.Code == code);
            
            Assert.NotNull(urlShort);
            Assert.Equal(userId, urlShort.UserId);
            Assert.Equal(2, urlShort.DestinationUrls.Count);
            
            // Basic user features
            Assert.NotNull(urlShort.DestinationUrls.First().UtmSource);
            Assert.NotNull(urlShort.DestinationUrls.First().UtmMedium);
            Assert.NotNull(urlShort.DestinationUrls.First().UtmCampaign);
            Assert.Single(urlShort.OgMetadataVariations);
            Assert.Equal("Test Title", urlShort.OgMetadataVariations.First().Title);
            Assert.Equal("Test Description", urlShort.OgMetadataVariations.First().Description);
            Assert.Equal("/test-image.jpg", urlShort.OgMetadataVariations.First().Image);
            Assert.NotNull(urlShort.ExpirationDate);
            Assert.NotNull(urlShort.Password); // Password should be hashed
            Assert.NotEqual(password, urlShort.Password);
        }

        [Fact]
        public async Task GetShortUrl_ReturnsCorrectUrl()
        {
            // Arrange
            var code = "test123"; // This code was seeded in the test data
            
            // Act
            var urlShort = await _fixture.UrlShortenerService.GetShortUrlAsync(code);
            
            // Assert
            Assert.NotNull(urlShort);
            Assert.Equal(code, urlShort.Code);
            Assert.Equal("test-user-id", urlShort.UserId);
            Assert.Single(urlShort.DestinationUrls);
            Assert.Equal("https://example.com", urlShort.DestinationUrls.First().Url);
        }

        [Fact]
        public async Task GetShortUrl_WithNonExistentCode_ReturnsNull()
        {
            // Arrange
            var code = "nonexistent";
            
            // Act
            var urlShort = await _fixture.UrlShortenerService.GetShortUrlAsync(code);
            
            // Assert
            Assert.Null(urlShort);
        }
    }
}
