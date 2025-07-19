using LegalDocumentAssistant.Api.DTOs;
using Newtonsoft.Json;
using System.Text;
using System.Net.Http.Headers;

namespace LegalDocumentAssistant.Api.Services;

public class AiService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AiService> _logger;
    private readonly string _claudeApiKey;
    private readonly string _openAiApiKey;
    private readonly string _huggingFaceApiKey;
    private readonly string _claudeApiUrl;
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
        _claudeApiKey = aiSettings["ClaudeApiKey"] ?? throw new InvalidOperationException("Claude API key is required");
        _openAiApiKey = aiSettings["OpenAiApiKey"] ?? throw new InvalidOperationException("OpenAI API key is required");
        _huggingFaceApiKey = aiSettings["HuggingFaceApiKey"] ?? throw new InvalidOperationException("HuggingFace API key is required");
        _claudeApiUrl = aiSettings["ClaudeApiUrl"] ?? "https://api.anthropic.com/v1/messages";
        _openAiApiUrl = aiSettings["OpenAiApiUrl"] ?? "https://api.openai.com/v1/chat/completions";
        _huggingFaceApiUrl = aiSettings["HuggingFaceApiUrl"] ?? "https://api-inference.huggingface.co/models";
        _huggingFaceModel = aiSettings["HuggingFaceModel"] ?? "microsoft/DialoGPT-medium";
        _requestTimeoutSeconds = int.Parse(aiSettings["RequestTimeoutSeconds"] ?? "30");
        _maxRetryAttempts = int.Parse(aiSettings["MaxRetryAttempts"] ?? "3");

        _httpClient.Timeout = TimeSpan.FromSeconds(_requestTimeoutSeconds);
    }

    public async Task<AnalysisResponse> AnalyzeTextAsync(AnalyzeTextRequest request)
    {
        _logger.LogInformation("Starting Claude API analysis for type: {AnalysisType}", request.AnalysisType);

        var prompt = GenerateAnalysisPrompt(request.Text, request.AnalysisType);
        
        var claudeRequest = new
        {
            model = "claude-3-haiku-20240307", // Using Haiku for free tier
            max_tokens = 1000,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt
                }
            }
        };

        try
        {
            var response = await ExecuteWithRetryAsync(async () =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _claudeApiUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _claudeApiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
                request.Content = new StringContent(JsonConvert.SerializeObject(claudeRequest), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                return responseContent;
            });

            var claudeResponse = JsonConvert.DeserializeObject<ClaudeApiResponse>(response);
            return ParseClaudeAnalysisResponse(claudeResponse, request.AnalysisType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Claude API for analysis");
            throw new InvalidOperationException("Failed to analyze text with Claude API", ex);
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
            "risk" => $"Analyze this legal text for potential risks and liabilities. Identify specific clauses that could be problematic and suggest improvements:\n\n{text}",
            "review" => $"Provide a comprehensive review of this legal text. Identify strengths, weaknesses, and areas for improvement:\n\n{text}",
            "ambiguity" => $"Identify any ambiguous language or unclear terms in this legal text that could lead to disputes:\n\n{text}",
            _ => $"Analyze this legal text and provide insights about its content, structure, and potential issues:\n\n{text}"
        };
    }

    private AnalysisResponse ParseClaudeAnalysisResponse(ClaudeApiResponse claudeResponse, string analysisType)
    {
        var content = claudeResponse?.Content?.FirstOrDefault()?.Text ?? "";
        
        // Parse the response to extract risks and suggestions
        var risks = new List<string>();
        var suggestions = new List<string>();

        // Simple parsing - in production, you might want more sophisticated parsing
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        bool inRisksSection = false;
        bool inSuggestionsSection = false;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (trimmedLine.ToLower().Contains("risk") || trimmedLine.ToLower().Contains("problem"))
            {
                inRisksSection = true;
                inSuggestionsSection = false;
            }
            else if (trimmedLine.ToLower().Contains("suggest") || trimmedLine.ToLower().Contains("recommend"))
            {
                inRisksSection = false;
                inSuggestionsSection = true;
            }
            else if (trimmedLine.StartsWith("-") || trimmedLine.StartsWith("•"))
            {
                if (inRisksSection)
                    risks.Add(trimmedLine.Substring(1).Trim());
                else if (inSuggestionsSection)
                    suggestions.Add(trimmedLine.Substring(1).Trim());
            }
        }

        // Fallback if parsing doesn't work well
        if (!risks.Any())
        {
            risks.Add("Analysis completed - see full response for details");
        }
        if (!suggestions.Any())
        {
            suggestions.Add("Review the analysis for recommended improvements");
        }

        return new AnalysisResponse(analysisType, risks, suggestions, 85);
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

    // API Response Models
    private class ClaudeApiResponse
    {
        public ClaudeContent[]? Content { get; set; }
    }

    private class ClaudeContent
    {
        public string? Text { get; set; }
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
}