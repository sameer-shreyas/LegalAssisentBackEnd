namespace LegalDocumentAssistant.Api.Models;

public class RiskResult
{
    public List<string> Risks { get; set; }
    public List<string> Mitigations { get; set; }
    public int Confidence { get; set; }
}

public class ReviewResult
{
    public List<string> Strengths { get; set; }
    public List<string> Weaknesses { get; set; }
    public List<string> Recommendations { get; set; }
    public int Confidence { get; set; }
}