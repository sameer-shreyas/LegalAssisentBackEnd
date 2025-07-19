using LegalDocumentAssistant.Api.Data;
using LegalDocumentAssistant.Api.DTOs;
using LegalDocumentAssistant.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LegalDocumentAssistant.Tests.Services;

public class AuthServiceTests
{
    private ApplicationDbContext GetInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private IConfiguration GetConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "test-secret-key-that-is-at-least-32-characters-long",
                ["JwtSettings:Issuer"] = "TestIssuer",
                ["JwtSettings:Audience"] = "TestAudience",
                ["JwtSettings:ExpirationInMinutes"] = "60"
            })
            .Build();
        return configuration;
    }

    [Fact]
    public async Task RegisterAsync_WithValidData_ShouldCreateUser()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var configuration = GetConfiguration();
        var authService = new AuthService(context, configuration);
        var request = new RegisterRequest("test@example.com", "password123", "Test User");

        // Act
        var result = await authService.RegisterAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test@example.com", result.User.Email);
        Assert.Equal("Test User", result.User.Name);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task RegisterAsync_WithExistingEmail_ShouldReturnNull()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var configuration = GetConfiguration();
        var authService = new AuthService(context, configuration);
        var request = new RegisterRequest("test@example.com", "password123", "Test User");

        // Act
        await authService.RegisterAsync(request);
        var result = await authService.RegisterAsync(request);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldReturnAuthResponse()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var configuration = GetConfiguration();
        var authService = new AuthService(context, configuration);
        var registerRequest = new RegisterRequest("test@example.com", "password123", "Test User");
        var loginRequest = new LoginRequest("test@example.com", "password123");

        // Act
        await authService.RegisterAsync(registerRequest);
        var result = await authService.LoginAsync(loginRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test@example.com", result.User.Email);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task LoginAsync_WithInvalidCredentials_ShouldReturnNull()
    {
        // Arrange
        using var context = GetInMemoryContext();
        var configuration = GetConfiguration();
        var authService = new AuthService(context, configuration);
        var loginRequest = new LoginRequest("test@example.com", "wrongpassword");

        // Act
        var result = await authService.LoginAsync(loginRequest);

        // Assert
        Assert.Null(result);
    }
}