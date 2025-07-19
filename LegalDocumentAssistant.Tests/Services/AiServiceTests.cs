using LegalDocumentAssistant.Api.DTOs;
using LegalDocumentAssistant.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;

namespace LegalDocumentAssistant.Tests.Services;

public class AiServiceTests
{
    private readonly Mock<ILogger<AiService>> _mockLogger;
    private readonly IConfiguration _configuration;

    public AiServiceTests()
    {
        _mockLogger = new Mock<ILogger<AiService>>();
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiSettings:ClaudeApiKey"] = "test-claude-key",
                ["AiSettings:OpenAiApiKey"] = "test-openai-key",
                ["AiSettings:HuggingFaceApiKey"] = "test-hf-key",
                ["AiSettings:ClaudeApiUrl"] = "https://api.anthropic.com/v1/messages",
                ["AiSettings:OpenAiApiUrl"] = "https://api.openai.com/v1/chat/completions",
                ["AiSettings:HuggingFaceApiUrl"] = "https://api-inference.huggingface.co/models",
                ["AiSettings:RequestTimeoutSeconds"] = "30",
                ["AiSettings:MaxRetryAttempts"] = "3"
            })
            .Build();
    }

    [Fact]
    public async Task AnalyzeTextAsync_WithValidRequest_ShouldReturnAnalysis()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(@"{
                ""content"": [{
                    ""text"": ""Risk Analysis:\n- Potential liability issues\n- Unclear termination terms\n\nSuggestions:\n- Add specific notice requirements\n- Define termination procedures""
                }]
            }")
        };

        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockResponse);

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        var aiService = new AiService(httpClient, _configuration, _mockLogger.Object);
        var request = new AnalyzeTextRequest("Sample legal text for analysis", "risk");

        // Act
        var result = await aiService.AnalyzeTextAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("risk", result.Type);
        Assert.NotEmpty(result.Risks);
        Assert.NotEmpty(result.Suggestions);
    }

    [Fact]
    public async Task ExplainSimpleAsync_WithValidRequest_ShouldReturnExplanation()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        var mockResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(@"{
                ""choices"": [{
                    ""message"": {
                        ""content"": ""This legal text means that both parties agree to keep information secret. Key points:\n- Information must be kept confidential\n- Sharing is not allowed without permission\n- This protects business secrets""
                    }
                }]
            }")
        };

        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(mockResponse);

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        var aiService = new AiService(httpClient, _configuration, _mockLogger.Object);
        var request = new ExplainSimpleRequest("Complex legal confidentiality clause");

        // Act
        var result = await aiService.ExplainSimpleAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Complex legal confidentiality clause", result.Original);
        Assert.NotEmpty(result.Simplified);
        Assert.NotEmpty(result.KeyPoints);
    }

    [Fact]
    public async Task ExtractClausesAsync_WithValidRequest_ShouldReturnClauses()
    {
        // Arrange
        var httpClient = new HttpClient();
        var aiService = new AiService(httpClient, _configuration, _mockLogger.Object);
        var request = new ExtractClausesRequest("This agreement may be terminated by either party with 30 days notice. The parties agree to maintain confidentiality of all proprietary information.");

        // Act
        var result = await aiService.ExtractClausesAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains(result, c => c.Type == "termination");
        Assert.Contains(result, c => c.Type == "confidentiality");
    }

    [Fact]
    public void Constructor_WithMissingApiKeys_ShouldThrowException()
    {
        // Arrange
        var invalidConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AiSettings:ClaudeApiKey"] = "", // Missing key
            })
            .Build();

        var httpClient = new HttpClient();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => 
            new AiService(httpClient, invalidConfig, _mockLogger.Object));
    }
}