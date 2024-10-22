using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using PythonInferenceReplacement.Models;
using PythonInferenceReplacement.Services;

namespace PythonInferenceReplacement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ClassifierController : ControllerBase
    {
        private readonly PythonInferenceService _pythonInferenceService;

        public ClassifierController(PythonInferenceService pythonInferenceService)
        {
            _pythonInferenceService = pythonInferenceService;
        }

        [HttpPost("classify")]
        public IActionResult ClassifyText([FromBody] ClassifyRequest request)
        {
            var result = _pythonInferenceService.ClassifyText(request.Text, request.CandidateLabels);
            return Ok(new { label = result });
        }
    }
}

