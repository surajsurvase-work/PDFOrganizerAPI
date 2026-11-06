using PDFOrganizerAPI.Filters;
using PDFOrganizerAPI.Services;

using static PDFOrganizerAPI.Models.Chatbot.AiChatbotModelscs;

namespace PDFOrganizerAPI.Controllers
{
 
        public class ChatbotService
        {
        [HttpRequestFilter]
        private readonly AiJsonChatbotDataService _dataService;

            public ChatbotService(AiJsonChatbotDataService dataService)
            {
                _dataService = dataService;
            }

            public ChatReplyDto ProcessMessage(string message)
            {
                var normalized = Normalize(message);

                var intents = _dataService.LoadIntents();
                var tools = _dataService.LoadTools();
                var faqs = _dataService.LoadFaqs();
                var responses = _dataService.LoadResponses();

                // 1. Open tool command
                var openToolMatch = FindToolFromOpenCommand(normalized, tools);
                if (openToolMatch != null)
                {
                    return new ChatReplyDto
                    {
                        IntentCode = openToolMatch.ToolCode,
                        ResponseType = "tool_card",
                        ReplyText = $"Opening {openToolMatch.ToolName}.",
                        Tool = openToolMatch,
                        ActionType = "open_tool",
                        ActionValue = openToolMatch.RouteUrl,
                        Suggestions = new List<string> { "Show steps", "What does this tool do?" }
                    };
                }

                // 2. Show steps command
                var stepToolMatch = FindToolFromStepCommand(normalized, tools);
                if (stepToolMatch != null)
                {
                    return new ChatReplyDto
                    {
                        IntentCode = stepToolMatch.ToolCode,
                        ResponseType = "steps",
                        ReplyText = $"Here are the steps for {stepToolMatch.ToolName}.",
                        Tool = stepToolMatch,
                        Steps = stepToolMatch.Steps ?? new List<string>(),
                        Suggestions = new List<string> { $"Open {stepToolMatch.ToolName}", "What files are supported?" }
                    };
                }

                // 3. Intent exact or contains
                var matchedIntent = intents
                    .Select(i => new
                    {
                        Intent = i,
                        Score = GetIntentScore(normalized, i.Patterns)
                    })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault()?.Intent;

                if (matchedIntent != null)
                {
                    if (matchedIntent.ResponseType == "tool_list")
                    {
                        var groupedTools = tools
                            .OrderBy(t => t.Category)
                            .ThenBy(t => t.ToolName)
                            .GroupBy(t => t.Category)
                            .Select(g => new ToolGroupDto
                            {
                                Category = g.Key,
                                Tools = g.ToList()
                            })
                            .ToList();

                        return new ChatReplyDto
                        {
                            IntentCode = matchedIntent.IntentCode,
                            ResponseType = "tool_group_list",
                            ReplyText = matchedIntent.Response,
                            ToolGroups = groupedTools,
                            Suggestions = matchedIntent.Suggestions ?? new List<string>()
                        };
                    }

                    ToolDefinition selectedTool = null;

                    if (!string.IsNullOrWhiteSpace(matchedIntent.ToolCode))
                    {
                        selectedTool = tools.FirstOrDefault(t =>
                            string.Equals(t.ToolCode, matchedIntent.ToolCode, StringComparison.OrdinalIgnoreCase));
                    }

                    return new ChatReplyDto
                    {
                        IntentCode = matchedIntent.IntentCode,
                        ResponseType = matchedIntent.ResponseType,
                        ReplyText = matchedIntent.Response,
                        Tool = selectedTool,
                        Suggestions = matchedIntent.Suggestions ?? new List<string>(),
                        ActionType = selectedTool != null ? "open_tool" : null,
                        ActionValue = selectedTool?.RouteUrl
                    };
                }

                // 4. FAQ matching
                var matchedFaq = faqs
                    .Select(f => new
                    {
                        Faq = f,
                        Score = GetKeywordScore(normalized, f.Keywords)
                    })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault()?.Faq;

                if (matchedFaq != null)
                {
                    return new ChatReplyDto
                    {
                        IntentCode = matchedFaq.FaqCode,
                        ResponseType = "faq",
                        ReplyText = matchedFaq.Answer,
                        Suggestions = new List<string> { "What are the tools?", "Compress PDF", "OCR" }
                    };
                }

                // 5. Tool matching by keyword + alias
                var matchedTool = tools
                    .Select(t => new
                    {
                        Tool = t,
                        Score = GetToolScore(normalized, t)
                    })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault()?.Tool;

                if (matchedTool != null)
                {
                    return new ChatReplyDto
                    {
                        IntentCode = matchedTool.ToolCode,
                        ResponseType = "tool_card",
                        ReplyText = $"You can use {matchedTool.ToolName}. {matchedTool.Description}",
                        Tool = matchedTool,
                        Suggestions = new List<string>
                    {
                        $"Open {matchedTool.ToolName}",
                        $"Show steps for {matchedTool.ToolName}",
                        "What files are supported?"
                    },
                        ActionType = "open_tool",
                        ActionValue = matchedTool.RouteUrl
                    };
                }

                return new ChatReplyDto
                {
                    IntentCode = "FALLBACK",
                    ResponseType = "text",
                    ReplyText = responses?.Fallback?.FirstOrDefault()
                                ?? "I can help you with PDF tools. Please tell me what you want to do.",
                    Suggestions = new List<string> { "Merge PDF", "Split PDF", "Compress PDF" }
                };
            }

            public List<QuickActionItem> GetQuickActions()
            {
                return _dataService.LoadQuickActions();
            }

            public List<ToolDefinition> GetAllTools()
            {
                return _dataService.LoadTools();
            }

            private ToolDefinition FindToolFromOpenCommand(string normalized, List<ToolDefinition> tools)
            {
                if (!normalized.StartsWith("open ")) return null;

                return tools
                    .Select(t => new
                    {
                        Tool = t,
                        Score = GetToolScore(normalized, t)
                    })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault()?.Tool;
            }

            private ToolDefinition FindToolFromStepCommand(string normalized, List<ToolDefinition> tools)
            {
                var stepTriggers = new[] { "show steps", "how to use", "steps for", "how do i use", "how to" };
                if (!stepTriggers.Any(t => normalized.Contains(t))) return null;

                return tools
                    .Select(t => new
                    {
                        Tool = t,
                        Score = GetToolScore(normalized, t)
                    })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault()?.Tool;
            }

            private int GetIntentScore(string input, List<string> patterns)
            {
                if (patterns == null || !patterns.Any()) return 0;

                var score = 0;
                foreach (var pattern in patterns)
                {
                    var p = Normalize(pattern);
                    if (input == p) score = Math.Max(score, 100);
                    else if (input.Contains(p)) score = Math.Max(score, 70);
                    else if (p.Contains(input) && input.Length > 3) score = Math.Max(score, 50);
                }

                return score;
            }

            private int GetKeywordScore(string input, List<string> keywords)
            {
                if (keywords == null || !keywords.Any()) return 0;

                var score = 0;
                foreach (var keyword in keywords)
                {
                    var k = Normalize(keyword);
                    if (input == k) score = Math.Max(score, 90);
                    else if (input.Contains(k)) score = Math.Max(score, 65);
                }

                return score;
            }

            private int GetToolScore(string input, ToolDefinition tool)
            {
                var score = 0;

                if (!string.IsNullOrWhiteSpace(tool.ToolName))
                {
                    var name = Normalize(tool.ToolName);
                    if (input == name) score = Math.Max(score, 100);
                    else if (input.Contains(name)) score = Math.Max(score, 85);
                }

                if (tool.Keywords != null)
                {
                    foreach (var keyword in tool.Keywords)
                    {
                        var k = Normalize(keyword);
                        if (input == k) score = Math.Max(score, 95);
                        else if (input.Contains(k)) score = Math.Max(score, 75);
                    }
                }

                if (tool.Aliases != null)
                {
                    foreach (var alias in tool.Aliases)
                    {
                        var a = Normalize(alias);
                        if (input == a) score = Math.Max(score, 88);
                        else if (input.Contains(a)) score = Math.Max(score, 68);
                    }
                }

                return score;
            }

            private string Normalize(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    return string.Empty;

                var normalized = input.Trim().ToLowerInvariant();

                normalized = normalized
                    .Replace("?", "")
                    .Replace(".", "")
                    .Replace(",", "")
                    .Replace("  ", " ");

                return normalized;
            }
        }
    }