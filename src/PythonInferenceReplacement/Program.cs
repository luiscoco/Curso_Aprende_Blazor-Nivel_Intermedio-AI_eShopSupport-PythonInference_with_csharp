using Python.Runtime; // For Python.NET initialization
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Initialize the Python runtime for all platforms using Python.NET
PythonEngine.Initialize();

// Optional: Set PythonHome and PythonPath if needed, for multi-platform support
var pythonHome = Environment.GetEnvironmentVariable("PYTHONHOME") ?? "/usr/local";
var pythonPath = Environment.GetEnvironmentVariable("PYTHONPATH") ?? "/usr/local/lib/python3.8";

// Set the PythonHome and PythonPath for the current environment
PythonEngine.PythonHome = pythonHome;
PythonEngine.PythonPath = pythonPath;

// Confirm Python initialization
Console.WriteLine("Python Runtime Initialized.");
Console.WriteLine($"Python Version: {PythonEngine.Version}");

// Add services to the container
builder.Services.AddControllers();

// Register the Python inference service (dependency injection)
builder.Services.AddSingleton<PythonInferenceReplacement.Services.PythonInferenceService>();

// Optional: Add Swagger/OpenAPI configuration if you're using it
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Ensure Python runtime is shut down properly on application exit
AppDomain.CurrentDomain.ProcessExit += (s, e) => PythonEngine.Shutdown();

app.Run();

