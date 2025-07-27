namespace LegalDocumentAssistant.Api.DTOs;

public record AnalyzeTextRequest(string Text, string AnalysisType);

// Add these outside the AiService class
public abstract record AnalysisResponseBase(string Type, int Confidence);

public record RiskAnalysisResponse(
    string Type,
    List<string> Risks,
    List<string> Mitigations,
    int Confidence
) : AnalysisResponseBase(Type, Confidence);

public record ReviewAnalysisResponse(
    string Type,
    List<string> Strengths,
    List<string> Weaknesses,
    List<string> Recommendations,
    int Confidence
) : AnalysisResponseBase(Type, Confidence);

public record AmbiguityAnalysisResponse(
    string Type,
    List<string> AmbiguousTerms,
    List<string> Clarifications,
    int Confidence
) : AnalysisResponseBase(Type, Confidence);

public record GenericAnalysisResponse(
    string Type,
    List<string> Analysis,
    int Confidence
) : AnalysisResponseBase(Type, Confidence);

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