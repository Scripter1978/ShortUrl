using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ShortUrl.Data;
using ShortUrl.Helpers;
using ShortUrl.Services;
using System;
using System.Threading.Tasks;
using Xunit;

namespace ShortUrl.Tests.Infrastructure
{
    public abstract class TestFixture : IAsyncLifetime, IDisposable
    {
        public ApplicationDbContext DbContext { get; private set; }
        public UrlShortenerService UrlShortenerService { get; private set; }
        private Mock<IConfiguration> MockConfiguration { get; set; }

        protected TestFixture()
        {
            DbContext = TestDbContext.GetInMemoryDbContext();
            
            // Setup mock IConfiguration
            MockConfiguration = new Mock<IConfiguration>();
            MockConfiguration.Setup(c => c["Security:AllowedRedirectDomains"]).Returns("example.com,test.com");
            
            // Create UrlShortenerService with our test dependencies
            UrlShortenerService = new UrlShortenerService(
                DbContext, 
                MockConfiguration.Object);
        }

        public async Task InitializeAsync()
        {
            await TestDbContext.SeedTestData(DbContext);
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            DbContext?.Dispose();
        }
    }
}
