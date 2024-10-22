using System.Collections.Generic;

namespace PythonInferenceReplacement.Models
{
    public class ClassifyRequest
    {
        public string Text { get; set; }
        public List<string> CandidateLabels { get; set; }
    }
}
