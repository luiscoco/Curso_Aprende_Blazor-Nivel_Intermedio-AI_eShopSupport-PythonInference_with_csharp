using System.Collections.Generic;
using PythonInferenceReplacement.PythonInteropNameSpace;

namespace PythonInferenceReplacement.Services
{
    public class PythonInferenceService
    {
        private readonly PythonInterop _pythonInterop;

        public PythonInferenceService()
        {
            _pythonInterop = new PythonInterop();
        }

        public string ClassifyText(string text, List<string> candidateLabels)
        {
            return _pythonInterop.CallPythonClassifier(text, candidateLabels);
        }

        public List<List<float>> EmbedSentences(List<string> sentences)
        {
            return _pythonInterop.CallPythonEmbedder(sentences);
        }
    }
}
