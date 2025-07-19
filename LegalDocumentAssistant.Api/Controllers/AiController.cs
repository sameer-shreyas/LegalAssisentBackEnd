using LegalDocumentAssistant.Api.DTOs;
using LegalDocumentAssistant.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegalDocumentAssistant.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly IAiService _aiService;
    private readonly ILogger<AiController> _logger;

    public AiController(IAiService aiService, ILogger<AiController> logger)
    {
        _aiService = aiService;
        _logger = logger;
    }

    [HttpPost("analyze-text")]
    public async Task<IActionResult> AnalyzeText([FromBody] AnalyzeTextRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { message = "Text is required for analysis" });
        }

        try
        {
            _logger.LogInformation("Analyzing text of length {Length} for type {Type}", 
                request.Text.Length, request.AnalysisType);
            
            var result = await _aiService.AnalyzeTextAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing text");
            return StatusCode(500, new { message = "Error analyzing text", error = ex.Message });
        }
    }

    [HttpPost("extract-clauses")]
    public async Task<IActionResult> ExtractClauses([FromBody] ExtractClausesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { message = "Text is required for clause extraction" });
        }

        try
        {
            _logger.LogInformation("Extracting clauses from text of length {Length}", request.Text.Length);
            
            var result = await _aiService.ExtractClausesAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting clauses");
            return StatusCode(500, new { message = "Error extracting clauses", error = ex.Message });
        }
    }

    [HttpPost("explain-simple")]
    public async Task<IActionResult> ExplainSimple([FromBody] ExplainSimpleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { message = "Text is required for explanation" });
        }

        try
        {
            _logger.LogInformation("Explaining text of length {Length}", request.Text.Length);
            
            var result = await _aiService.ExplainSimpleAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error explaining text");
            return StatusCode(500, new { message = "Error explaining text", error = ex.Message });
        }
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { message = "Question is required for chat" });
        }

        try
        {
            _logger.LogInformation("Processing chat question: {Question}", request.Question);
            
            var result = await _aiService.ChatAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chat");
            return StatusCode(500, new { message = "Error processing chat", error = ex.Message });
        }
    }

    // Demo endpoint for testing AI integrations
    [HttpPost("ai/explain")]
    public async Task<IActionResult> DemoExplain([FromBody] DemoExplainRequest request)
    {
        try
        {
            _logger.LogInformation("Demo explain endpoint called with text: {Text}", request.Text);
            
            var explainRequest = new ExplainSimpleRequest(request.Text);
            var result = await _aiService.ExplainSimpleAsync(explainRequest);
            
            return Ok(new
            {
                success = true,
                original = result.Original,
                simplified = result.Simplified,
                keyPoints = result.KeyPoints,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in demo explain endpoint");
            return StatusCode(500, new { 
                success = false, 
                message = "Error explaining text", 
                error = ex.Message 
            });
        }
    }
}

public record DemoExplainRequest(string Text);
