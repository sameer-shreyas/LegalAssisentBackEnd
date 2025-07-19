namespace LegalDocumentAssistant.Api.DTOs;

public record AnalyzeTextRequest(string Text, string AnalysisType);

public record AnalysisResponse(
    string Type,
    List<string> Risks,
    List<string> Suggestions,
    int Confidence
);

public record ExtractClausesRequest(string Text);

public record ClauseDto(
    string Type,
    string Text,
    double Confidence,
    int StartIndex,
    int EndIndex
);

public record ExplainSimpleRequest(string Text);

public record ExplainSimpleResponse(
    string Original,
    string Simplified,
    List<string> KeyPoints
);

public record ChatRequest(string Question, string DocumentText);

public record ChatResponse(string Response, DateTime Timestamp);