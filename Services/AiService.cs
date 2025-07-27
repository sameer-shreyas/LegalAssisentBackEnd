using LegalDocumentAssistant.Api.DTOs;
using LegalDocumentAssistant.Api.Models;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
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
    private readonly string _huggingFaceApiKey;
    private readonly string _cerebrasApiUrl;
    private readonly string _huggingFaceApiUrl;
    private readonly int _requestTimeoutSeconds;
    private readonly int _maxRetryAttempts;

    public AiService(HttpClient httpClient, IConfiguration configuration, ILogger<AiService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        
        var aiSettings = _configuration.GetSection("AiSettings");
        _huggingFaceApiKey = aiSettings["HuggingFaceApiKey"] ?? throw new InvalidOperationException("HuggingFace API key is required");
        _huggingFaceApiUrl = aiSettings["HuggingFaceApiUrl"] ?? "https://api-inference.huggingface.co/models";
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
            return ParseAnalyzeCerebrasResponse(cerebrasResponse, request.AnalysisType);
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

    private ExplainSimpleResponse ParseExplainCerebrasResponse(CerebrasApiResponse response, string originalText)
    {
        if (response?.Choices == null || response.Choices.Length == 0)
            throw new InvalidOperationException("Invalid response from Cerebras API");

        var content = response.Choices[0].Message.Content;

        return new ExplainSimpleResponse(
            Original: originalText,
            Simplified: ExtractSimplifiedExplanation(content),
            KeyPoints: ExtractKeyPoints(content)
        );
    }
    private string ExtractSimplifiedExplanation(string content)
    {
        var sb = new StringBuilder();
        bool inSimplifiedSection = false;

        using (var reader = new StringReader(content))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var trimmedLine = line.Trim();

                // Detect start of Simplified Explanation
                if (trimmedLine.Equals("## Simplified Explanation", StringComparison.OrdinalIgnoreCase))
                {
                    inSimplifiedSection = true;
                    continue;
                }

                // End reading when we hit the next section
                if (inSimplifiedSection && trimmedLine.StartsWith("## "))
                    break;

                if (inSimplifiedSection)
                    sb.AppendLine(trimmedLine);
            }
        }

        var result = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(result)
            ? "Simplified explanation not available."
            : result;
    }

    private List<string> ExtractKeyPoints(string content)
    {
        var keyPoints = new List<string>();
        bool inKeyPointsSection = false;

        using (var reader = new StringReader(content))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var trimmedLine = line.Trim();

                // Start reading when we hit the "Key Points" heading
                if (trimmedLine.Equals("## Key Points", StringComparison.OrdinalIgnoreCase))
                {
                    inKeyPointsSection = true;
                    continue;
                }

                // Stop reading when we hit the next heading
                if (inKeyPointsSection && trimmedLine.StartsWith("## "))
                    break;

                // If we’re inside the section, extract bullet points
                if (inKeyPointsSection)
                {
                    if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* ") || trimmedLine.StartsWith("• "))
                    {
                        keyPoints.Add(trimmedLine.Substring(2).Trim());
                    }
                    else if (System.Text.RegularExpressions.Regex.IsMatch(trimmedLine, @"^\s*\d+[\.\)]\s+"))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"^\s*\d+[\.\)]\s+(.*)");
                        if (match.Success)
                            keyPoints.Add(match.Groups[1].Value.Trim());
                    }
                }
            }
        }

        // Fallback: get first 3 non-empty sentences
        if (keyPoints.Count == 0)
        {
            var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(s => s.Trim())
                                   .Where(s => s.Length > 0)
                                   .Take(3)
                                   .ToList();

            return sentences.Count > 0 ? sentences : new List<string> { "Key points not available" };
        }

        return keyPoints;
    }

    // Enhanced model fallback with retry
    private readonly string[] _modelPriorityList = new[]
    {
        //"qwen-3-32b",         // Best quality
        "llama-3.3-70b",      // High quality alternative
        "llama-3.1-8b"        // Lightweight fallback
    };

    public async Task<ExplainSimpleResponse> ExplainSimpleAsync(ExplainSimpleRequest request)
    {
        Exception lastError = null;

        foreach (var model in _modelPriorityList)
        {
            try
            {
                _logger.LogInformation($"Attempting legal explanation with model: {model}");
                return await ExplainWithModelAsync(request, model);
            }
            catch (Exception ex) when (IsRetryableError(ex))
            {
                _logger.LogWarning($"Model {model} failed, trying fallback. Error: {ex.Message}");
                lastError = ex;
            }
        }

        _logger.LogError(lastError, "All explanation models failed");
        throw new InvalidOperationException("All explanation models failed", lastError);
    }

    private async Task<ExplainSimpleResponse> ExplainWithModelAsync(
        ExplainSimpleRequest request,
        string model)
    {
        var cerebrasRequest = new
        {
            model,
            messages = new[]
            {
            new
            {
                role = "system",
                content = "You're a legal assistant explaining complex text in simple terms. " +
                          "Structure your response with:\n" +
                          "1. Simplified Explanation\n" +
                          "2. Key Points (as bullet points)\n" +
                          "3. Practical Implications\n" +
                          "Use clear headings for each section."
            },
            new
            {
                role = "user",
                content = $"Explain this legal text in simple terms:\n\n{request.Text}"
            }
        },
            max_tokens = 500,
            temperature = 0.3
        };

        var response = await ExecuteWithRetryAsync(async () =>
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _cerebrasApiUrl)
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", _cerebrasApiKey) },
                Content = new StringContent(JsonConvert.SerializeObject(cerebrasRequest),
                            Encoding.UTF8, "application/json")
            };

            var response = await _httpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        },
        maxRetries: _maxRetryAttempts,
        baseDelayMs: 1000);

        var cerebrasResponse = JsonConvert.DeserializeObject<CerebrasApiResponse>(response);
        return ParseExplainCerebrasResponse(cerebrasResponse, request.Text);
    }

    private bool IsRetryableError(Exception ex)
    {
        // Network-level errors
        if (ex is HttpRequestException httpEx)
        {
            return httpEx.StatusCode switch
            {
                HttpStatusCode.RequestTimeout => true,
                HttpStatusCode.TooManyRequests => true,
                HttpStatusCode.InternalServerError => true,
                HttpStatusCode.BadGateway => true,
                HttpStatusCode.ServiceUnavailable => true,
                HttpStatusCode.GatewayTimeout => true,
                _ => false
            };
        }

        // Timeout errors
        if (ex is TimeoutException)
            return true;

        // Cerebras API-specific errors
        if (ex.Message.Contains("model_overloaded", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("model_unavailable", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        int maxRetries = 3,
        int baseDelayMs = 1000)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (IsRetryableError(ex) && attempt < maxRetries)
            {
                attempt++;
                var delay = TimeSpan.FromMilliseconds(baseDelayMs * Math.Pow(2, attempt));
                _logger.LogWarning($"Retry attempt {attempt}/{maxRetries} in {delay.TotalSeconds}s");
                await Task.Delay(delay);
            }
        }
    }

    private List<string> ChunkDocument(string text, int maxChunkLength = 1500)
    {
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+");
        var chunks = new List<string>();
        var currentChunk = new StringBuilder();

        foreach (var sentence in sentences)
        {
            if (currentChunk.Length + sentence.Length > maxChunkLength && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString());
                currentChunk.Clear();
            }

            currentChunk.Append(sentence).Append(' ');
        }

        if (currentChunk.Length > 0)
            chunks.Add(currentChunk.ToString());

        return chunks;
    }

    private double CosineSimilarity(float[] vec1, float[] vec2)
    {
        float dot = 0, norm1 = 0, norm2 = 0;
        for (int i = 0; i < vec1.Length; i++)
        {
            dot += vec1[i] * vec2[i];
            norm1 += vec1[i] * vec1[i];
            norm2 += vec2[i] * vec2[i];
        }

        return dot / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request)
    {
        _logger.LogInformation("Starting RAG-based chat");
        var chunks = ChunkDocument(request.DocumentText);

        var questionEmbedding = await EmbeddingService.Instance.GetEmbeddingAsync(request.Question);

        var chunkScores = new ConcurrentBag<(string Chunk, double Score)>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount * 2
        };

        await Parallel.ForEachAsync(chunks, options, async (chunk, ct) =>
        {
            var embedding = await EmbeddingService.Instance.GetEmbeddingAsync(chunk);
            var similarity = CosineSimilarity(questionEmbedding, embedding);
            chunkScores.Add((chunk, similarity));
        });

        var topChunks = chunkScores
            .OrderByDescending(x => x.Score)
            .Take(3)
            .Select(x => x.Chunk)
            .ToList();

        var condensedContext = string.Join("\n\n---\n\n", topChunks);
        var model = condensedContext.Length > 5000 ? "llama-3.3-70b" : "llama-3.1-8b";

        var cerebrasRequest = new
        {
            model,
            messages = new[]
            {
            new
            {
                role = "system",
                content = "You are an expert legal assistant specializing in contract analysis. " +
                          "Reference specific document sections in responses. " +
                          "If a question requires external knowledge, state this explicitly."
            },
            new
            {
                role = "user",
                content = $"CONTEXT:\n{condensedContext}\n\nQUESTION: {request.Question}"
            }
        },
            max_tokens = 800,
            temperature = 0.3
        };

        try
        {
            var responseContent = await ExecuteWithRetryAsync(async () =>
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _cerebrasApiUrl);
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cerebrasApiKey);
                httpRequest.Headers.Add("cerebras-version", "2024-05-01");

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
            var chatContent = cerebrasResponse?.Choices?.FirstOrDefault()?.Message?.Content
                              ?? "Unable to generate response";
            return new ChatResponse(chatContent, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG chat error");
            return new ChatResponse("Legal analysis unavailable due to document size. Try a smaller section.", DateTime.UtcNow);
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

    private AnalysisResponseBase ParseAnalyzeCerebrasResponse(CerebrasApiResponse response, string analysisType)
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

    public record GenericAnalysisResponse(
        string Type,
        List<string> Analysis,
        int Confidence
    ) : AnalysisResponseBase(Type, Confidence);
}