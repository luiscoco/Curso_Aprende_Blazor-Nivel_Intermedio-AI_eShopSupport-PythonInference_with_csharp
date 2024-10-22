using Microsoft.AspNetCore.Mvc;
using PythonInferenceReplacement.Models;
using PythonInferenceReplacement.Services;
using System.Collections.Generic;

namespace PythonInferenceReplacement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmbedderController : ControllerBase
    {
        private readonly PythonInferenceService _pythonInferenceService;

        public EmbedderController(PythonInferenceService pythonInferenceService)
        {
            _pythonInferenceService = pythonInferenceService;
        }

        [HttpPost("embed")]
        public IActionResult EmbedSentences([FromBody] EmbedRequest request)
        {
            var result = _pythonInferenceService.EmbedSentences(request.Sentences);
            return Ok(result);
        }
    }
}
