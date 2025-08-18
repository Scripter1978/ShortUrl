using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using ShortUrl.Helpers;
using ShortUrl.Tests.Infrastructure;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ShortUrl.Tests
{
    public class SubscriptionTierTests : IClassFixture<TestFixture>
    {
        private readonly TestFixture _fixture;
        private readonly Mock<UserManager<IdentityUser>> _mockUserManager;

        public SubscriptionTierTests(TestFixture fixture)
        {
            _fixture = fixture;
            
            // Setup mock UserManager with proper non-null parameters
            var userStoreMock = new Mock<IUserStore<IdentityUser>>();
            var optionsAccessorMock = new Mock<IOptions<IdentityOptions>>();
            var passwordHasherMock = new Mock<IPasswordHasher<IdentityUser>>();
            var userValidatorsMock = new[] { new Mock<IUserValidator<IdentityUser>>().Object };
            var passwordValidatorsMock = new[] { new Mock<IPasswordValidator<IdentityUser>>().Object };
            var keyNormalizerMock = new Mock<ILookupNormalizer>();
            var errorsMock = new Mock<IdentityErrorDescriber>();
            var servicesMock = new Mock<IServiceProvider>();
            var loggerMock = new Mock<ILogger<UserManager<IdentityUser>>>();

            _mockUserManager = new Mock<UserManager<IdentityUser>>(
                userStoreMock.Object,
                optionsAccessorMock.Object,
                passwordHasherMock.Object,
                userValidatorsMock,
                passwordValidatorsMock,
                keyNormalizerMock.Object,
                errorsMock.Object,
                servicesMock.Object,
                loggerMock.Object);
        }

        [Fact]
        public void UrlLimiter_ShouldReturnCorrectUrlLimits()
        {
            // Arrange
            var freeUserClaims = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
                new Claim(ClaimTypes.Role, "Free")
            }));

            var basicUserClaims = new ClaimsPrincipal(new ClaimsIdentity(new Claim[]
            {
                new Claim(ClaimTypes.NameIdentifier, "premium-user-id"),
                new Claim(ClaimTypes.Role, "Basic")
            }));

            // Act
            var freeUserLimit = UrlLimiter.GetShortUrlLimitForUser(freeUserClaims);
            var basicUserLimit = UrlLimiter.GetShortUrlLimitForUser(basicUserClaims);
            var freeUserVCardLimit = UrlLimiter.GetVCardLimitForUser(freeUserClaims);
            var basicUserVCardLimit = UrlLimiter.GetVCardLimitForUser(basicUserClaims);

            // Assert
            Assert.Equal(10, freeUserLimit);
            Assert.Equal(200, basicUserLimit);
            Assert.Equal(1, freeUserVCardLimit);
            Assert.Equal(50, basicUserVCardLimit);
        }

        [Fact]
        public async Task BasicUsers_ShouldHaveAccessToPremiumFeatures()
        {
            // Arrange - create a user with Basic role
            var basicUser = new IdentityUser
            {
                Id = "basic-test-user",
                UserName = "basic@test.com",
                NormalizedUserName = "BASIC@TEST.COM",
                Email = "basic@test.com",
                NormalizedEmail = "BASIC@TEST.COM"
            };

            // Set up mock IsInRoleAsync for Basic user check
            _mockUserManager
                .Setup(um => um.IsInRoleAsync(It.Is<IdentityUser>(u => u.Id == "basic-test-user"), "Basic"))
                .ReturnsAsync(true);
            _mockUserManager
                .Setup(um => um.FindByIdAsync("basic-test-user"))
                .ReturnsAsync(basicUser);

            // Create URLs with premium features for Basic user
            var destinationUrls = new List<Models.DestinationUrl>
            {
                new Models.DestinationUrl
                {
                    Url = "https://example.com/premium-feature",
                    UtmSource = "basic-source",
                    UtmMedium = "basic-medium",
                    UtmCampaign = "basic-campaign"
                },
                new Models.DestinationUrl
                {
                    Url = "https://example.com/alternative",
                    Weight = 2
                }
            };

            var ogMetadata = new List<Models.OgMetadata>
            {
                new Models.OgMetadata
                {
                    Title = "Basic User Test",
                    Description = "Testing premium features for Basic users",
                    Image = "/test-basic.jpg"
                }
            };

            // Act - Create a short URL with premium features
            var code = await _fixture.UrlShortenerService.CreateShortUrlAsync(
                "basic-test-user",
                "basic-custom-slug",
                destinationUrls,
                ogMetadata,
                System.DateTime.UtcNow.AddDays(30),
                "testpassword"
            );

            // Assert
            Assert.Equal("basic-custom-slug", code);

            var urlShort = await _fixture.DbContext.UrlShorts
                .Include(u => u.DestinationUrls)
                .Include(u => u.OgMetadataVariations)
                .FirstOrDefaultAsync(u => u.Code == code);

            Assert.NotNull(urlShort);
            Assert.Equal("basic-test-user", urlShort.UserId);
            Assert.Equal(2, urlShort.DestinationUrls.Count);
            Assert.NotNull(urlShort.DestinationUrls.First().UtmSource);
            Assert.Single(urlShort.OgMetadataVariations);
            Assert.NotNull(urlShort.Password);
            Assert.NotNull(urlShort.ExpirationDate);
        }

        [Fact]
        public async Task NoOtherTiersExist_BesidesFreeAndBasic()
        {
            // Act
            var roles = await _fixture.DbContext.Roles
                .Select(r => r.Name)
                .ToListAsync();

            // Assert
            Assert.Contains("Free", roles);
            Assert.Contains("Basic", roles);
            Assert.DoesNotContain("Professional", roles);
            Assert.DoesNotContain("Enterprise", roles);
            Assert.Equal(2, roles.Count); // Only Free and Basic should exist
        }
    }
}
