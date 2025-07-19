# LegalAssist AI - ASP.NET Core Backend

This is the ASP.NET Core Web API backend for the Legal Document Assistant application.

## 🚀 Getting Started

### Prerequisites
- .NET 8 SDK
- Visual Studio 2022 or VS Code with C# extension

### Installation

1. Navigate to the backend directory:
```bash
cd backend
```

2. Restore NuGet packages:
```bash
dotnet restore
```

3. Update the configuration in `appsettings.json`:
```json
{
  "AiSettings": {
    "ClaudeApiKey": "your-claude-api-key-here",
    "OpenAiApiKey": "your-openai-api-key-here", 
    "HuggingFaceApiKey": "your-huggingface-api-key-here"
  },
  "JwtSettings": {
    "SecretKey": "your-super-secret-key-that-should-be-at-least-32-characters-long"
  }
}
```

4. Run the application:
```bash
dotnet run --project LegalDocumentAssistant.Api
```

The API will be available at:
- HTTPS: https://localhost:7001
- HTTP: http://localhost:5001
- Swagger UI: https://localhost:7001/swagger

## 🏗️ Project Structure

```
backend/
├── LegalDocumentAssistant.Api/
│   ├── Controllers/           # API Controllers
│   ├── Services/             # Business logic services
│   ├── Models/               # Entity models
│   ├── DTOs/                 # Data Transfer Objects
│   ├── Data/                 # Entity Framework context
│   └── Program.cs            # Application entry point
├── LegalDocumentAssistant.Tests/
│   ├── Controllers/          # Controller tests
│   └── Services/            # Service tests
└── LegalDocumentAssistant.sln
```

## 📡 API Endpoints

### Authentication
- `POST /api/auth/login` - User login
- `POST /api/auth/register` - User registration

### File Management
- `GET /api/files` - List user documents
- `POST /api/files` - Upload document
- `GET /api/files/{id}` - Get document details
- `DELETE /api/files/{id}` - Delete document

### AI Services
- `POST /api/analyze-text` - Analyze text with Claude API
- `POST /api/extract-clauses` - Extract clauses with Hugging Face
- `POST /api/explain-simple` - Simplify text with GPT-4o
- `POST /api/chat` - Chat interface for document queries

## 🔧 Configuration

### JWT Settings
```json
{
  "JwtSettings": {
    "SecretKey": "your-secret-key-here",
    "Issuer": "LegalDocumentAssistant",
    "Audience": "LegalDocumentAssistant",
    "ExpirationInMinutes": 60
  }
}
```

### File Storage
```json
{
  "FileStorage": {
    "UploadPath": "wwwroot/uploads"
  }
}
```

### AI API Keys
```json
{
  "AiSettings": {
    "ClaudeApiKey": "your-claude-api-key",
    "OpenAiApiKey": "your-openai-api-key",
    "HuggingFaceApiKey": "your-huggingface-api-key"
  }
}
```

## 🧪 Running Tests

Run all tests:
```bash
dotnet test
```

Run tests with coverage:
```bash
dotnet test --collect:"XPlat Code Coverage"
```

## 🔒 Security Features

- JWT-based authentication
- Password hashing with BCrypt
- CORS configuration for React frontend
- File type validation for uploads
- Authorization middleware for protected endpoints

## 🚀 Deployment

### Development
```bash
dotnet run --project LegalDocumentAssistant.Api
```

### Production
```bash
dotnet publish -c Release -o ./publish
```

## 🤖 AI API Setup

### Getting API Keys

1. **Claude API (Anthropic)**:
   - Visit: https://console.anthropic.com/
   - Create account and get API key
   - Free tier includes generous usage limits

2. **OpenAI GPT-4o**:
   - Visit: https://platform.openai.com/api-keys
   - Create API key
   - Note: GPT-4o requires paid credits

3. **Hugging Face**:
   - Visit: https://huggingface.co/settings/tokens
   - Create a free account and generate token
   - Free tier includes inference API access

### Testing AI Integration

Use the demo endpoint to test your AI setup:

```bash
curl -X POST "https://localhost:7001/api/ai/explain" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"text": "The party of the first part shall indemnify and hold harmless the party of the second part."}'
```

## 🔗 Frontend Integration

The React frontend should make requests to:
- Development: `http://localhost:5001`
- Production: Your deployed API URL

Update the frontend's API base URL in the axios configuration to point to your ASP.NET Core API.

## 📝 API Documentation

When running in development mode, Swagger UI is available at:
- https://localhost:7001/swagger

This provides interactive API documentation and testing capabilities.

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Run tests to ensure they pass
6. Submit a pull request

## 📄 License

This project is licensed under the MIT License.