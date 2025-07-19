namespace LegalDocumentAssistant.Api.DTOs;

public record LoginRequest(string Email, string Password);

public record RegisterRequest(string Email, string Password, string Name);

public record AuthResponse(string Token, UserDto User);

public record UserDto(Guid Id, string Email, string Name);