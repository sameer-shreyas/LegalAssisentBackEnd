using LegalDocumentAssistant.Api.DTOs;

namespace LegalDocumentAssistant.Api.Services;

public interface IAiService
{
    Task<AnalysisResponse> AnalyzeTextAsync(AnalyzeTextRequest request);
    Task<List<ClauseDto>> ExtractClausesAsync(ExtractClausesRequest request);
    Task<ExplainSimpleResponse> ExplainSimpleAsync(ExplainSimpleRequest request);
    Task<ChatResponse> ChatAsync(ChatRequest request);
}