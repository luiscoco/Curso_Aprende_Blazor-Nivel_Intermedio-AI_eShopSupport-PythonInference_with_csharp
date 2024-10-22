using System;
using System.Collections.Generic;
using Python.Runtime;

namespace PythonInferenceReplacement.PythonInteropNameSpace
{
    public class PythonInterop
    {
        // Call the Python classifier model using Python.NET
        public string CallPythonClassifier(string text, List<string> labels)
        {
            using (Py.GIL()) // Ensures the Python Global Interpreter Lock (GIL) is acquired
            {
                try
                {
                    // Import the transformers library and create the zero-shot classifier pipeline
                    dynamic transformers = Py.Import("transformers");
                    dynamic classifier = transformers.pipeline("zero-shot-classification",
                                                                model: "cross-encoder/nli-MiniLM2-L6-H768",
                                                                device: -1);

                    // Convert the C# List<string> to a Python List (PyList) using PyString for each label
                    PyList pyLabels = new PyList();
                    foreach (var label in labels)
                    {
                        pyLabels.Append(new PyString(label));
                    }

                    // Call the classifier with the input text and the Python list of labels
                    dynamic result = classifier(text, pyLabels);

                    // Return the top result's label
                    return result["labels"][0].ToString();
                }
                catch (Exception ex)
                {
                    // Log any errors that occur during the Python call
                    Console.WriteLine("Error in Python call: " + ex.Message);
                    return "Error in Python classification";
                }
            }
        }

        // Call the Python sentence embedder model using Python.NET
        public List<List<float>> CallPythonEmbedder(List<string> sentences)
        {
            using (Py.GIL())
            {
                try
                {
                    dynamic sentence_transformers = Py.Import("sentence_transformers");
                    dynamic model = sentence_transformers.SentenceTransformer("sentence-transformers/all-MiniLM-L6-v2");

                    dynamic embeddings = model.encode(sentences.ToArray());

                    List<List<float>> result = new List<List<float>>();
                    foreach (var embedding in embeddings)
                    {
                        List<float> row = new List<float>();
                        foreach (var val in embedding)
                        {
                            row.Add((float)val);
                        }
                        result.Add(row);
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error in Python embedding: " + ex.Message);
                    return new List<List<float>>();
                }
            }
        }
    }
}
