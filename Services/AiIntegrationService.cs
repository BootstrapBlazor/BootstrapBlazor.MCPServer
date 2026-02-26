using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.AI;

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
        var settings = _settingsManager.LoadSettings();
        var baseUrl = settings.AiBaseUrl;
        var apiKey = settings.AiApiKey;
        var model = settings.AiModel;

        if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_API_KEY_HERE")
        {
            _logger.LogWarning("AI Integration ApiKey is missing or not configured. Returning local fallback message.");
            return "AI API Key is missing. Please configure 'AI:ApiKey' in appsettings.json or Environment Variables to use the Expert Summarization feature. " +
                   "\n\nHere is the raw data instead:\n\n" + systemPrompt;
        }

        try
        {
            var options = new OpenAI.OpenAIClientOptions();
            if (!string.IsNullOrEmpty(baseUrl))
            {
                // Ensure the base URL points to the root of the OpenAI compatible API (e.g. "https://api.openai.com/v1/")
                // The OpenAI library automatically appends "chat/completions" to this endpoint
                if (!baseUrl.EndsWith("/")) baseUrl += "/";
                options.Endpoint = new Uri(baseUrl);
            }
            
            var openAiClient = new OpenAI.OpenAIClient(new System.ClientModel.ApiKeyCredential(apiKey), options);
            
            // In MEAI 10.x preview, AsChatClient was renamed to AsIChatClient
            IChatClient chatClient = openAiClient.GetChatClient(model).AsIChatClient();
            
            var messages = new System.Collections.Generic.List<Microsoft.Extensions.AI.ChatMessage>
            {
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.System, systemPrompt),
                new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, userMessage)
            };
            
            // In MEAI 10.x preview, CompleteAsync was renamed to GetResponseAsync
            var response = await chatClient.GetResponseAsync(messages, new Microsoft.Extensions.AI.ChatOptions { Temperature = 0.3f });

            if (response.Messages != null && response.Messages.Count > 0)
            {
                return response.Messages[0].Text ?? "No content returned from AI.";
            }
            return "No content returned from AI.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while communicating with AI service.");
            return $"Error: An exception occurred while communicating with the AI service: {ex.Message}";
        }
    }
}
