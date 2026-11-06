using static PDFOrganizerAPI.Models.Chatbot.AiChatbotModelscs;
using System.Text.Json;
using PDFOrganizerAPI.Filters;

namespace PDFOrganizerAPI.Services
{
    [HttpRequestFilter]
    public class AiJsonChatbotDataService
    {
        private readonly IWebHostEnvironment _env;
        private readonly JsonSerializerOptions _jsonOptions;

        public AiJsonChatbotDataService(IWebHostEnvironment env)
        {
            _env = env;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        private T ReadJsonFile<T>(string fileName)
        {
            var filePath = Path.Combine(_env.ContentRootPath, "ChatbotData", fileName);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Chatbot file not found: {filePath}");

            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }

        public List<ChatbotIntent> LoadIntents()
            => ReadJsonFile<List<ChatbotIntent>>("intents.json") ?? new List<ChatbotIntent>();

        public List<ToolDefinition> LoadTools()
            => ReadJsonFile<List<ToolDefinition>>("tools.json") ?? new List<ToolDefinition>();

        public List<FaqItem> LoadFaqs()
            => ReadJsonFile<List<FaqItem>>("faqs.json") ?? new List<FaqItem>();

        public List<QuickActionItem> LoadQuickActions()
            => ReadJsonFile<List<QuickActionItem>>("quick-actions.json") ?? new List<QuickActionItem>();

        public ChatbotResponses LoadResponses()
            => ReadJsonFile<ChatbotResponses>("responses.json") ?? new ChatbotResponses();
    }
}