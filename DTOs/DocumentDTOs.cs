namespace LegalDocumentAssistant.Api.DTOs;

public record DocumentDto(
    Guid Id,
    string Title,
    string OriginalName,
    string MimeType,
    long Size,
    DateTime UploadedAt,
    string? ExtractedText = null
);

public record DocumentUploadRequest(IFormFile File, string? Title = null);