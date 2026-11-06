namespace PDFOrganizerAPI.Models.Chatbot
{
    public class AiChatbotModelscs
    {
        public class ChatbotIntent
        {
            public string IntentCode { get; set; }
            public string Category { get; set; }
            public List<string> Patterns { get; set; }
            public string ResponseType { get; set; }
            public string Response { get; set; }
            public string ToolCode { get; set; }
            public List<string> Suggestions { get; set; }
        }

        public class ToolDefinition
        {
            public string ToolCode { get; set; }
            public string ToolName { get; set; }
            public string Category { get; set; }
            public string Description { get; set; }
            public string RouteUrl { get; set; }
            public string Icon { get; set; }
            public List<string> Keywords { get; set; }
            public List<string> Aliases { get; set; }
            public List<string> Steps { get; set; }
        }

        public class FaqItem
        {
            public string FaqCode { get; set; }
            public string Question { get; set; }
            public List<string> Keywords { get; set; }
            public string Answer { get; set; }
        }

        public class QuickActionItem
        {
            public string Title { get; set; }
            public string Type { get; set; }
            public string ToolCode { get; set; }
            public string IntentCode { get; set; }
        }

        public class ChatRequest
        {
            public string Message { get; set; }
        }

        public class ChatReplyDto
        {
            public string IntentCode { get; set; }
            public string ResponseType { get; set; }
            public string ReplyText { get; set; }
            public ToolDefinition Tool { get; set; }
            public List<ToolDefinition> Tools { get; set; }
            public List<ToolGroupDto> ToolGroups { get; set; }
            public List<string> Suggestions { get; set; }
            public List<string> Steps { get; set; }
            public string ActionType { get; set; }
            public string ActionValue { get; set; }
        }

        public class ToolGroupDto
        {
            public string Category { get; set; }
            public List<ToolDefinition> Tools { get; set; }
        }

        public class ChatbotResponses
        {
            public List<string> Welcome { get; set; }
            public List<string> Fallback { get; set; }
        }

    }
}
