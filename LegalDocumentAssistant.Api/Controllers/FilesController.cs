using LegalDocumentAssistant.Api.DTOs;
using LegalDocumentAssistant.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LegalDocumentAssistant.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;

    public FilesController(IFileService fileService)
    {
        _fileService = fileService;
    }

    private Guid GetUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.Parse(userIdClaim ?? throw new UnauthorizedAccessException());
    }

    [HttpPost]
    public async Task<IActionResult> UploadDocument([FromForm] DocumentUploadRequest request)
    {
        var userId = GetUserId();
        var result = await _fileService.UploadDocumentAsync(request, userId);
        
        if (result == null)
        {
            return BadRequest(new { message = "Invalid file or file type not supported" });
        }

        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetDocuments()
    {
        var userId = GetUserId();
        var documents = await _fileService.GetUserDocumentsAsync(userId);
        return Ok(documents);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDocument(Guid id)
    {
        var userId = GetUserId();
        var document = await _fileService.GetDocumentAsync(id, userId);
        
        if (document == null)
        {
            return NotFound(new { message = "Document not found" });
        }

        return Ok(document);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDocument(Guid id)
    {
        var userId = GetUserId();
        var success = await _fileService.DeleteDocumentAsync(id, userId);
        
        if (!success)
        {
            return NotFound(new { message = "Document not found" });
        }

        return Ok(new { message = "Document deleted successfully" });
    }
}