using LegalDocumentAssistant.Api.DTOs;

namespace LegalDocumentAssistant.Api.Services;

public interface IAuthService
{
    Task<AuthResponse?> LoginAsync(LoginRequest request);
    Task<AuthResponse?> RegisterAsync(RegisterRequest request);
    Task<UserDto?> GetUserByIdAsync(Guid userId);
}