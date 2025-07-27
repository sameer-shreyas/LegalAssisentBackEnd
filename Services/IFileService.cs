using LegalDocumentAssistant.Api.DTOs;

namespace LegalDocumentAssistant.Api.Services;

public interface IFileService
{
    Task<DocumentDto?> UploadDocumentAsync(DocumentUploadRequest request, Guid userId);
    Task<List<DocumentDto>> GetUserDocumentsAsync(Guid userId);
    Task<DocumentDto?> GetDocumentAsync(Guid documentId, Guid userId);
    Task<bool> DeleteDocumentAsync(Guid documentId, Guid userId);
    Task<string> ExtractTextFromFileAsync(string filePath, string mimeType);
}