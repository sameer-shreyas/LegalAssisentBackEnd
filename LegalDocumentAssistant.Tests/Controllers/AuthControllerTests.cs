using LegalDocumentAssistant.Api.DTOs;
using LegalDocumentAssistant.Api.Services;
using LegalDocumentAssistant.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace LegalDocumentAssistant.Tests.Controllers;

public class AuthControllerTests
{
    [Fact]
    public async Task Login_WithValidCredentials_ShouldReturnOk()
    {
        // Arrange
        var mockAuthService = new Mock<IAuthService>();
        var request = new LoginRequest("test@example.com", "password123");
        var expectedResponse = new AuthResponse("token", new UserDto(Guid.NewGuid(), "test@example.com", "Test User"));
        
        mockAuthService.Setup(x => x.LoginAsync(request))
                      .ReturnsAsync(expectedResponse);

        var controller = new AuthController(mockAuthService.Object);

        // Act
        var result = await controller.Login(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AuthResponse>(okResult.Value);
        Assert.Equal(expectedResponse.Token, response.Token);
        Assert.Equal(expectedResponse.User.Email, response.User.Email);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ShouldReturnUnauthorized()
    {
        // Arrange
        var mockAuthService = new Mock<IAuthService>();
        var request = new LoginRequest("test@example.com", "wrongpassword");
        
        mockAuthService.Setup(x => x.LoginAsync(request))
                      .ReturnsAsync((AuthResponse?)null);

        var controller = new AuthController(mockAuthService.Object);

        // Act
        var result = await controller.Login(request);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Register_WithValidData_ShouldReturnOk()
    {
        // Arrange
        var mockAuthService = new Mock<IAuthService>();
        var request = new RegisterRequest("test@example.com", "password123", "Test User");
        var expectedResponse = new AuthResponse("token", new UserDto(Guid.NewGuid(), "test@example.com", "Test User"));
        
        mockAuthService.Setup(x => x.RegisterAsync(request))
                      .ReturnsAsync(expectedResponse);

        var controller = new AuthController(mockAuthService.Object);

        // Act
        var result = await controller.Register(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AuthResponse>(okResult.Value);
        Assert.Equal(expectedResponse.Token, response.Token);
        Assert.Equal(expectedResponse.User.Email, response.User.Email);
    }

    [Fact]
    public async Task Register_WithExistingEmail_ShouldReturnBadRequest()
    {
        // Arrange
        var mockAuthService = new Mock<IAuthService>();
        var request = new RegisterRequest("test@example.com", "password123", "Test User");
        
        mockAuthService.Setup(x => x.RegisterAsync(request))
                      .ReturnsAsync((AuthResponse?)null);

        var controller = new AuthController(mockAuthService.Object);

        // Act
        var result = await controller.Register(request);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }
}