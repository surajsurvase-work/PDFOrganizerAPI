using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PDFOrganizerAPI.Filters;
using static PDFOrganizerAPI.Models.Chatbot.AiChatbotModelscs;

namespace PDFOrganizerAPI.Controllers
{
    [HttpRequestFilter]

    [Route("api/[controller]")]
    [ApiController]

    public class AiChatbotController : ControllerBase
    {
        private readonly ChatbotService _chatbotService;

        public AiChatbotController(ChatbotService chatbotService)
        {
            _chatbotService = chatbotService;
        }

        [HttpPost("message")]
        public IActionResult SendMessage([FromBody] ChatRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
                return BadRequest("Message is required.");

            var result = _chatbotService.ProcessMessage(request.Message);
            return Ok(result);
        }

        [HttpGet("quick-actions")]
        public IActionResult GetQuickActions()
        {
            var result = _chatbotService.GetQuickActions();
            return Ok(result);
        }

        [HttpGet("tools")]
        public IActionResult GetTools()
        {
            var result = _chatbotService.GetAllTools();
            return Ok(result);
        }
    }
}
