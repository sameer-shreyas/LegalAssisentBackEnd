namespace LegalDocumentAssistant.Api.Models;

// Cerebras response DTOs
public class CerebrasApiResponse
{
    public Choice[] Choices { get; set; }
}
public class Choice
{
    public Message Message { get; set; }
}
public class Message
{
    public string Content { get; set; } // Contains JSON output
}