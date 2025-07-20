using LegalDocumentAssistant.Api.DTOs;
using LegalDocumentAssistant.Api.Models;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace LegalDocumentAssistant.Api.Services;

public class AiService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiService> _logger;
    private readonly string _cerebrasApiKey;
    private readonly string _openAiApiKey;
    private readonly string _huggingFaceApiKey;
    private readonly string _cerebrasApiUrl;
    private readonly string _openAiApiUrl;
    private readonly string _huggingFaceApiUrl;
    private readonly string _huggingFaceModel;
    private readonly int _requestTimeoutSeconds;
    private readonly int _maxRetryAttempts;

    public AiService(HttpClient httpClient, IConfiguration configuration, ILogger<AiService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        
        var aiSettings = _configuration.GetSection("AiSettings");
        _openAiApiKey = aiSettings["OpenAiApiKey"] ?? throw new InvalidOperationException("OpenAI API key is required");
        _huggingFaceApiKey = aiSettings["HuggingFaceApiKey"] ?? throw new InvalidOperationException("HuggingFace API key is required");
        _openAiApiUrl = aiSettings["OpenAiApiUrl"] ?? "https://api.openai.com/v1/chat/completions";
        _huggingFaceApiUrl = aiSettings["HuggingFaceApiUrl"] ?? "https://api-inference.huggingface.co/models";
        _huggingFaceModel = aiSettings["HuggingFaceModel"] ?? "microsoft/DialoGPT-medium";
        _requestTimeoutSeconds = int.Parse(aiSettings["RequestTimeoutSeconds"] ?? "30");
        _maxRetryAttempts = int.Parse(aiSettings["MaxRetryAttempts"] ?? "3");
        _cerebrasApiKey = aiSettings["CerebrasApiKey"] ?? throw new InvalidOperationException("Cerebras key required");
        _cerebrasApiUrl = aiSettings["CerebrasApiUrl"] ?? "https://api.cerebras.ai/v1/chat/completions";
        _httpClient.Timeout = TimeSpan.FromSeconds(_requestTimeoutSeconds);
    }

    public async Task<AnalysisResponseBase> AnalyzeTextAsync(AnalyzeTextRequest request)
    {
        _logger.LogInformation("Starting Cerebras API analysis for type: {AnalysisType}", request.AnalysisType);

        var prompt = GenerateAnalysisPrompt(request.Text, request.AnalysisType);
        var cerebrasRequest = new
        {
            model = "llama3.1-8b",
                                               // model = "cerebras-llama-4-scout-17b-16e-instruct", // For longer documents (16K context)
            max_tokens = 1000,
            messages = new[] { new { role = "user", content = prompt } }
        };

        try
        {
            var responseContent = await ExecuteWithRetryAsync(async () =>
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _cerebrasApiUrl);
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cerebrasApiKey);

                // Cerebras requires version header
                httpRequest.Headers.Add("cerebras-version", "2024-05-01"); // Add this line

                httpRequest.Content = new StringContent(
                    JsonConvert.SerializeObject(cerebrasRequest),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.SendAsync(httpRequest);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            });

            var cerebrasResponse = JsonConvert.DeserializeObject<CerebrasApiResponse>(responseContent);
            return ParseCerebrasResponse(cerebrasResponse, request.AnalysisType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cerebras API error");
            throw new InvalidOperationException("Cerebras analysis failed", ex);
        }
    }

    public async Task<List<ClauseDto>> ExtractClausesAsync(ExtractClausesRequest request)
    {
        _logger.LogInformation("Starting Hugging Face clause extraction");

        // For clause extraction, we'll use a more specific approach
        // You can replace this with a specialized legal NER model
        var huggingFaceRequest = new
        {
            inputs = request.Text,
            parameters = new
            {
                return_full_text = false,
                max_length = 100,
                num_return_sequences = 1
            }
        };

        try
        {
            var response = await ExecuteWithRetryAsync(async () =>
            {
                var modelUrl = $"{_huggingFaceApiUrl}/nlptown/bert-base-multilingual-uncased-sentiment";
                using var request = new HttpRequestMessage(HttpMethod.Post, modelUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _huggingFaceApiKey);
                request.Content = new StringContent(JsonConvert.SerializeObject(huggingFaceRequest), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsStringAsync();
            });

            // For now, we'll use a rule-based approach combined with the text
            // In production, you'd want to use a specialized legal NER model
            return ExtractClausesFromText(request.Text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Hugging Face API for clause extraction");
            // Fallback to rule-based extraction
            return ExtractClausesFromText(request.Text);
        }
    }

    public async Task<ExplainSimpleResponse> ExplainSimpleAsync(ExplainSimpleRequest request)
    {
        _logger.LogInformation("Starting GPT-4o explanation");

        var openAiRequest = new
        {
            model = "gpt-4o-mini", // Using mini for cost efficiency
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are a helpful legal assistant that explains complex legal text in simple, everyday language. Your goal is to make legal concepts accessible to non-lawyers. Always provide clear, concise explanations and highlight the key practical implications."
                },
                new
                {
                    role = "user",
                    content = $"Please explain this legal text in simple terms that anyone can understand:\n\n{request.Text}\n\nProvide:\n1. A simplified explanation\n2. Key points in bullet format\n3. What this means in practical terms"
                }
            },
            max_tokens = 500,
            temperature = 0.3
        };

        try
        {
            var response = await ExecuteWithRetryAsync(async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _openAiApiUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);
                request.Content = new StringContent(JsonConvert.SerializeObject(openAiRequest), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsStringAsync();
            });

            var openAiResponse = JsonConvert.DeserializeObject<OpenAiApiResponse>(response);
            return ParseOpenAiExplanationResponse(openAiResponse, request.Text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI API for explanation");
            throw new InvalidOperationException("Failed to explain text with OpenAI API", ex);
        }
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request)
    {
        _logger.LogInformation("Starting chat interaction");

        var openAiRequest = new
        {
            model = "gpt-4o-mini",
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are an expert legal assistant specializing in contract analysis. You help users understand legal documents, identify risks, and provide practical advice. Always be precise, helpful, and reference specific parts of the document when relevant."
                },
                new
                {
                    role = "user",
                    content = $"Based on this legal document:\n\n{request.DocumentText}\n\nUser question: {request.Question}\n\nPlease provide a helpful, specific answer."
                }
            },
            max_tokens = 800,
            temperature = 0.4
        };

        try
        {
            var response = await ExecuteWithRetryAsync(async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _openAiApiUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);
                request.Content = new StringContent(JsonConvert.SerializeObject(openAiRequest), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsStringAsync();
            });

            var openAiResponse = JsonConvert.DeserializeObject<OpenAiApiResponse>(response);
            var chatResponse = openAiResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "I'm sorry, I couldn't process your question.";

            return new ChatResponse(chatResponse, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in chat interaction");
            throw new InvalidOperationException("Failed to process chat request", ex);
        }
    }

    private async Task<string> ExecuteWithRetryAsync(Func<Task<string>> operation)
    {
        Exception lastException = null;

        for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (HttpRequestException ex) when (attempt < _maxRetryAttempts)
            {
                lastException = ex;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                _logger.LogWarning("API call failed on attempt {Attempt}, retrying in {Delay}ms", attempt, delay.TotalMilliseconds);
                await Task.Delay(delay);
            }
            catch (TaskCanceledException ex) when (attempt < _maxRetryAttempts)
            {
                lastException = ex;
                _logger.LogWarning("API call timed out on attempt {Attempt}, retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw lastException ?? new InvalidOperationException("All retry attempts failed");
    }

    private string GenerateAnalysisPrompt(string text, string analysisType)
    {
        return analysisType.ToLower() switch
        {
            "risk" =>
                $@"Analyze this legal text for risks and liabilities. Return JSON with:
            {{
                ""risks"": [""risk description""],
                ""mitigations"": [""mitigation strategy""],
                ""confidence"": 0-100
            }}
            Text: {text}",

            "review" =>
                $@"Review this legal text. Return JSON with:
            {{
                ""strengths"": [""strength description""],
                ""weaknesses"": [""weakness description""],
                ""recommendations"": [""improvement suggestion""],
                ""confidence"": 0-100
            }}
            Text: {text}",

            "ambiguity" =>
                $@"Identify ambiguous terms. Return JSON with:
            {{
                ""ambiguousTerms"": [""term/phrase""],
                ""clarifications"": [""clear alternative""],
                ""confidence"": 0-100
            }}
            Text: {text}",

            _ =>
                $@"Analyze this legal text. Return JSON with:
            {{
                ""analysis"": [""key insight""],
                ""confidence"": 0-100
            }}
            Text: {text}"
        };
    }

    private AnalysisResponseBase ParseCerebrasResponse(CerebrasApiResponse response, string analysisType)
    {
        if (response?.Choices == null || response.Choices.Length == 0)
            throw new InvalidOperationException("Invalid Cerebras response");

        var content = response.Choices[0].Message.Content;

        try
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
            {
                throw new InvalidDataException("No JSON found in response");
            }
            var jsonContent = content.Substring(jsonStart, jsonEnd - jsonStart + 1);

            return analysisType.ToLower() switch
            {
                "risk" => ParseRiskResponse(jsonContent),
                "review" => ParseReviewResponse(jsonContent),
                "ambiguity" => ParseAmbiguityResponse(jsonContent),
                _ => ParseGenericResponse(jsonContent)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSON parsing failed. Attempting fallback parsing");
            return ParseFallbackResponse(content, analysisType);
        }
    }
    private RiskAnalysisResponse ParseRiskResponse(string json)
    {
        var result = JsonConvert.DeserializeObject<RiskResult>(json);
        return new RiskAnalysisResponse(
            "risk",
            result.Risks ?? new List<string>(),
            result.Mitigations ?? new List<string>(),
            result.Confidence
        );
    }

    private ReviewAnalysisResponse ParseReviewResponse(string json)
    {
        var result = JsonConvert.DeserializeObject<ReviewResult>(json);
        return new ReviewAnalysisResponse(
            "review",
            result.Strengths ?? new List<string>(),
            result.Weaknesses ?? new List<string>(),
            result.Recommendations ?? new List<string>(),
            result.Confidence
        );
    }

    private AnalysisResponseBase ParseFallbackResponse(string content, string analysisType)
    {
        return analysisType.ToLower() switch
        {
            "review" => ParseReviewFromText(content),
            "risk" => ParseRiskFromText(content),
            "ambiguity" => ParseAmbiguityFromText(content),
            _ => new GenericAnalysisResponse("general", new List<string> { content }, 50)
        };
    }
    private ReviewAnalysisResponse ParseReviewFromText(string content)
    {
        var strengths = new List<string>();
        var weaknesses = new List<string>();
        var recommendations = new List<string>();

        // Extract strengths
        var strengthSection = ExtractSection(content, "Strengths:");
        if (strengthSection != null)
        {
            strengths = ExtractNumberedItems(strengthSection);
        }

        // Extract weaknesses
        var weaknessSection = ExtractSection(content, "Weaknesses:");
        if (weaknessSection != null)
        {
            weaknesses = ExtractNumberedItems(weaknessSection);
        }

        // Extract recommendations
        var recSection = ExtractSection(content, "Suggestions for improvement:");
        if (recSection != null)
        {
            recommendations = ExtractNumberedItems(recSection);
        }

        return new ReviewAnalysisResponse(
            "review",
            strengths,
            weaknesses,
            recommendations,
            strengths.Any() || weaknesses.Any() || recommendations.Any() ? 80 : 50
        );
    }

    private string ExtractSection(string content, string header)
    {
        var start = content.IndexOf(header);
        if (start < 0) return null;

        var end = content.IndexOf("\n\n", start);
        if (end < 0) end = content.Length;

        return content.Substring(start, end - start);
    }

    private List<string> ExtractNumberedItems(string text)
    {
        return Regex.Matches(text, @"\d+\.\s+(.+?)(?=\n\d+\.|\n\n|$)", RegexOptions.Singleline)
                    .Select(m => m.Groups[1].Value.Trim())
                    .ToList();
    }
    private AmbiguityAnalysisResponse ParseAmbiguityResponse(string json)
    {
        var result = JsonConvert.DeserializeObject<AmbiguityResult>(json);
        return new AmbiguityAnalysisResponse(
            "ambiguity",
            result.AmbiguousTerms ?? new List<string>(),
            result.Clarifications ?? new List<string>(),
            result.Confidence
        );
    }

    private GenericAnalysisResponse ParseGenericResponse(string json)
    {
        var result = JsonConvert.DeserializeObject<GenericResult>(json);
        return new GenericAnalysisResponse(
            "general",
            result.Analysis ?? new List<string>(),
            result.Confidence
        );
    }

    private RiskAnalysisResponse ParseRiskFromText(string content)
    {
        var risks = new List<string>();
        var mitigations = new List<string>();

        // Extract risks
        var riskSection = ExtractSection(content, "Risks:");
        if (riskSection != null)
        {
            risks = ExtractNumberedItems(riskSection);
        }

        // Extract mitigations
        var mitigationSection = ExtractSection(content, "Mitigations:");
        if (mitigationSection == null)
        {
            mitigationSection = ExtractSection(content, "Suggestions:"); // Fallback
        }
        if (mitigationSection != null)
        {
            mitigations = ExtractNumberedItems(mitigationSection);
        }

        return new RiskAnalysisResponse(
            "risk",
            risks,
            mitigations,
            risks.Any() || mitigations.Any() ? 80 : 50
        );
    }

    private AmbiguityAnalysisResponse ParseAmbiguityFromText(string content)
    {
        var ambiguousTerms = new List<string>();
        var clarifications = new List<string>();

        // Extract ambiguous terms
        var termsSection = ExtractSection(content, "Ambiguous Terms:");
        if (termsSection != null)
        {
            ambiguousTerms = ExtractNumberedItems(termsSection);
        }

        // Extract clarifications
        var clarificationsSection = ExtractSection(content, "Clarifications:");
        if (clarificationsSection == null)
        {
            clarificationsSection = ExtractSection(content, "Suggestions:"); // Fallback
        }
        if (clarificationsSection != null)
        {
            clarifications = ExtractNumberedItems(clarificationsSection);
        }

        return new AmbiguityAnalysisResponse(
            "ambiguity",
            ambiguousTerms,
            clarifications,
            ambiguousTerms.Any() || clarifications.Any() ? 80 : 50
        );
    }
    private ExplainSimpleResponse ParseOpenAiExplanationResponse(OpenAiApiResponse openAiResponse, string originalText)
    {
        var explanation = openAiResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? "Unable to generate explanation";
        
        // Extract key points from the explanation
        var keyPoints = new List<string>();
        var lines = explanation.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            if (line.Trim().StartsWith("-") || line.Trim().StartsWith("•") || line.Trim().StartsWith("*"))
            {
                keyPoints.Add(line.Trim().Substring(1).Trim());
            }
        }

        if (!keyPoints.Any())
        {
            keyPoints.Add("This text contains important legal terms and conditions");
            keyPoints.Add("It establishes rights and obligations for the parties involved");
            keyPoints.Add("Consider consulting with a legal professional for specific advice");
        }

        return new ExplainSimpleResponse(originalText, explanation, keyPoints);
    }

    private List<ClauseDto> ExtractClausesFromText(string text)
    {
        var clauses = new List<ClauseDto>();
        var lowerText = text.ToLower();

        // Rule-based clause extraction - in production, use a proper NER model
        var clausePatterns = new Dictionary<string, string[]>
        {
            ["termination"] = new[] { "terminate", "termination", "end this agreement", "cancel", "cancellation" },
            ["indemnity"] = new[] { "indemnify", "indemnification", "hold harmless", "defend", "liability" },
            ["confidentiality"] = new[] { "confidential", "confidentiality", "non-disclosure", "proprietary", "trade secret" },
            ["jurisdiction"] = new[] { "jurisdiction", "governing law", "courts of", "legal proceedings", "dispute resolution" },
            ["payment"] = new[] { "payment", "pay", "invoice", "fee", "compensation", "remuneration" },
            ["intellectual_property"] = new[] { "intellectual property", "copyright", "trademark", "patent", "proprietary rights" }
        };

        foreach (var clauseType in clausePatterns)
        {
            foreach (var keyword in clauseType.Value)
            {
                var index = lowerText.IndexOf(keyword);
                if (index >= 0)
                {
                    // Extract surrounding context (simplified approach)
                    var start = Math.Max(0, index - 50);
                    var end = Math.Min(text.Length, index + keyword.Length + 100);
                    var clauseText = text.Substring(start, end - start).Trim();
                    
                    // Clean up the extracted text
                    var sentences = clauseText.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    var relevantSentence = sentences.FirstOrDefault(s => s.ToLower().Contains(keyword))?.Trim();
                    
                    if (!string.IsNullOrEmpty(relevantSentence))
                    {
                        clauses.Add(new ClauseDto(
                            clauseType.Key,
                            relevantSentence + ".",
                            0.75 + (new Random().NextDouble() * 0.2), // Simulated confidence
                            start,
                            end
                        ));
                        break; // Only add one clause per type
                    }
                }
            }
        }

        return clauses.Take(6).ToList(); // Limit to 6 clauses
    }

    
    private class OpenAiApiResponse
    {
        public OpenAiChoice[]? Choices { get; set; }
    }

    private class OpenAiChoice
    {
        public OpenAiMessage? Message { get; set; }
    }

    private class OpenAiMessage
    {
        public string? Content { get; set; }
    }
    // Add these inside the AiService class
    private class AmbiguityResult
    {
        public List<string> AmbiguousTerms { get; set; }
        public List<string> Clarifications { get; set; }
        public int Confidence { get; set; }
    }

    private class GenericResult
    {
        public List<string> Analysis { get; set; }
        public int Confidence { get; set; }
    }

    // Add this outside the AiService class
    public record GenericAnalysisResponse(
        string Type,
        List<string> Analysis,
        int Confidence
    ) : AnalysisResponseBase(Type, Confidence);
}