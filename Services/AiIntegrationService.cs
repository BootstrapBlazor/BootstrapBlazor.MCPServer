using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace BootstrapBlazor.McpServer.Services;

public class AiIntegrationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiIntegrationService> _logger;
    private readonly AppSettingsManager _settingsManager;

    public AiIntegrationService(HttpClient httpClient, AppSettingsManager settingsManager, ILogger<AiIntegrationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settingsManager = settingsManager;
    }

    /// <summary>
    /// Calls the OpenAI-compatible Chat Completions endpoint with a system prompt and user query using Microsoft.Extensions.AI.
    /// </summary>
    public async Task<string> AskExpertAsync(string systemPrompt, string userMessage)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogDebug(
            "[{CorrelationId}] AskExpertAsync called. System prompt length: {SystemPromptLength}, User message length: {UserMessageLength}",
            correlationId, systemPrompt.Length, userMessage.Length);
        
        var settings = _settingsManager.LoadSettings();
        var baseUrl = settings.AiBaseUrl;
        var apiKey = settings.AiApiKey;
        var model = settings.AiModel;

        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_API_KEY_HERE")
        {
            _logger.LogWarning(
                "[{CorrelationId}] AI Integration ApiKey is missing or not configured. Returning local fallback message.",
                correlationId);
            
            return "AI API Key is missing. Please configure 'AI:ApiKey' in appsettings.json or Environment Variables to use the Expert Summarization feature. " +
                   "\n\nHere is the raw data instead:\n\n" + systemPrompt;
        }

        try
        {
            _logger.LogDebug(
                "[{CorrelationId}] Initializing AI client. BaseUrl: {BaseUrl}, Model: {Model}",
                correlationId, baseUrl, model);
            
            var options = new OpenAI.OpenAIClientOptions();
            if (!string.IsNullOrEmpty(baseUrl))
            {
                // Ensure the base URL points to the root of the OpenAI compatible API (e.g. "https://api.openai.com/v1/")
                // The OpenAI library automatically appends "chat/completions" to this endpoint
                if (!baseUrl.EndsWith("/")) baseUrl += "/";
                options.Endpoint = new Uri(baseUrl);
                
                _logger.LogTrace(
                    "[{CorrelationId}] AI endpoint set to: {Endpoint}",
                    correlationId, options.Endpoint);
            }

            var openAiClient = new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), options);

            // In MEAI 10.x preview, AsChatClient was renamed to AsIChatClient
            IChatClient chatClient = openAiClient.GetChatClient(model).AsIChatClient();

            _logger.LogTrace(
                "[{CorrelationId}] AI chat client created successfully",
                correlationId);

            var messages = new System.Collections.Generic.List<Microsoft.Extensions.AI.ChatMessage>
            {
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.System, systemPrompt),
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, userMessage)
            };

            _logger.LogDebug(
                "[{CorrelationId}] Sending request to AI service. Message count: {MessageCount}",
                correlationId, messages.Count);

            // In MEAI 10.x preview, CompleteAsync was renamed to GetResponseAsync
            var response = await chatClient.GetResponseAsync(messages, new Microsoft.Extensions.AI.ChatOptions { Temperature = 0.3f });

            if (response.Messages != null && response.Messages.Count > 0)
            {
                var content = response.Messages[0].Text ?? "No content returned from AI.";
                
                _logger.LogDebug(
                    "[{CorrelationId}] AI response received. Content length: {ContentLength}",
                    correlationId, content.Length);
                
                return content;
            }
            
            _logger.LogWarning(
                "[{CorrelationId}] AI response has no messages",
                correlationId);
            
            return "No content returned from AI.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{CorrelationId}] Exception while communicating with AI service. Message: {Message}",
                correlationId, ex.Message);
            
            return $"Error: An exception occurred while communicating with the AI service: {ex.Message}";
        }
    }
}
