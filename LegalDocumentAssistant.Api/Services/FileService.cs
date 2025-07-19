using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using LegalDocumentAssistant.Api.Data;
using LegalDocumentAssistant.Api.DTOs;
using LegalDocumentAssistant.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Document = LegalDocumentAssistant.Api.Models.Document;

namespace LegalDocumentAssistant.Api.Services;

public class FileService : IFileService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly string _uploadPath;

    public FileService(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
        _uploadPath = _configuration["FileStorage:UploadPath"] ?? "wwwroot/uploads";
        
        if (!Directory.Exists(_uploadPath))
        {
            Directory.CreateDirectory(_uploadPath);
        }
    }

    public async Task<DocumentDto?> UploadDocumentAsync(DocumentUploadRequest request, Guid userId)
    {
        if (request.File == null || request.File.Length == 0)
        {
            return null;
        }

        var allowedTypes = new[] { "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "text/plain" };
        if (!allowedTypes.Contains(request.File.ContentType))
        {
            return null;
        }

        var fileName = $"{Guid.NewGuid()}_{request.File.FileName}";
        var filePath = System.IO.Path.Combine(_uploadPath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await request.File.CopyToAsync(stream);
        }

        var extractedText = await ExtractTextFromFileAsync(filePath, request.File.ContentType);

        var document = new Document
        {
            Title = request.Title ?? request.File.FileName,
            FileName = fileName,
            OriginalName = request.File.FileName,
            MimeType = request.File.ContentType,
            Size = request.File.Length,
            ExtractedText = extractedText,
            UserId = userId
        };

        _context.Documents.Add(document);
        await _context.SaveChangesAsync();

        return new DocumentDto(
            document.Id,
            document.Title,
            document.OriginalName,
            document.MimeType,
            document.Size,
            document.UploadedAt,
            document.ExtractedText
        );
    }

    public async Task<List<DocumentDto>> GetUserDocumentsAsync(Guid userId)
    {
        var documents = await _context.Documents
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync();

        return documents.Select(d => new DocumentDto(
            d.Id,
            d.Title,
            d.OriginalName,
            d.MimeType,
            d.Size,
            d.UploadedAt
        )).ToList();
    }

    public async Task<DocumentDto?> GetDocumentAsync(Guid documentId, Guid userId)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId);

        if (document == null)
        {
            return null;
        }

        return new DocumentDto(
            document.Id,
            document.Title,
            document.OriginalName,
            document.MimeType,
            document.Size,
            document.UploadedAt,
            document.ExtractedText
        );
    }

    public async Task<bool> DeleteDocumentAsync(Guid documentId, Guid userId)
    {
        var document = await _context.Documents
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId);

        if (document == null)
        {
            return false;
        }

        var filePath = System.IO.Path.Combine(_uploadPath, document.FileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<string> ExtractTextFromFileAsync(string filePath, string mimeType)
    {
        try
        {
            return mimeType switch
            {
                "application/pdf" => await ExtractTextFromPdfAsync(filePath),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => await ExtractTextFromDocxAsync(filePath),
                "text/plain" => await File.ReadAllTextAsync(filePath),
                _ => string.Empty
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<string> ExtractTextFromPdfAsync(string filePath)
    {
        var text = new StringBuilder();
        
        using var reader = new PdfReader(filePath);
        for (int page = 1; page <= reader.NumberOfPages; page++)
        {
            text.Append(PdfTextExtractor.GetTextFromPage(reader, page));
        }
        
        return text.ToString();
    }

    private async Task<string> ExtractTextFromDocxAsync(string filePath)
    {
        var text = new StringBuilder();
        
        using var document = WordprocessingDocument.Open(filePath, false);
        var body = document.MainDocumentPart?.Document.Body;
        
        if (body != null)
        {
            foreach (var paragraph in body.Elements<Paragraph>())
            {
                text.AppendLine(paragraph.InnerText);
            }
        }
        
        return text.ToString();
    }
}