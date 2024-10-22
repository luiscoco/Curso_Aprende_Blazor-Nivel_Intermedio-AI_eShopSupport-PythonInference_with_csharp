﻿using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;
using Projects;
using Python.Runtime;

var builder = DistributedApplication.CreateBuilder(args);
builder.Configuration.Sources.Add(new JsonConfigurationSource { Path = "appsettings.Local.json", Optional = true });

var isE2ETest = builder.Configuration["E2E_TEST"] == "true";

var dbPassword = builder.AddParameter("PostgresPassword", secret: true);

var postgresServer = builder
    .AddPostgres("eshopsupport-postgres", password: dbPassword);
var backendDb = postgresServer
    .AddDatabase("backenddb");

var vectorDb = builder
    .AddQdrant("vector-db");

var identityServer = builder.AddProject<IdentityServer>("identity-server")
    .WithExternalHttpEndpoints();

var identityEndpoint = identityServer
    .GetEndpoint("https");

// Use this if you want to use Ollama
var chatCompletion = builder.AddOllama("chatcompletion").WithDataVolume();

// ... or use this if you want to use OpenAI (having also configured the API key in appsettings)
//var chatCompletion = builder.AddConnectionString("chatcompletion");

var storage = builder.AddAzureStorage("eshopsupport-storage");
if (builder.Environment.IsDevelopment())
{
    storage.RunAsEmulator(r =>
    {
        if (!isE2ETest)
        {
            r.WithDataVolume();
        }
    });
}

var blobStorage = storage.AddBlobs("eshopsupport-blobs");

//var pythonInference = builder.AddPythonUvicornApp("python-inference",
//    Path.Combine("..", "PythonInference"), port: 62723);

// Instead of Python Uvicorn, reference the C# pythonInference service directly
var pythonInference = builder.AddProject<PythonInferenceReplacement>("python-inference");

// Initialize Python.Runtime for all platforms (ensure Python environment is set correctly)
PythonEngine.Initialize(); // Python.NET's initialize method

// Optional: Set the Python home and path if needed (for multi-platform support)
var pythonHome = Environment.GetEnvironmentVariable("PYTHONHOME") ?? "/usr/local";
var pythonPath = Environment.GetEnvironmentVariable("PYTHONPATH") ?? "/usr/local/lib/python3.8";

// If necessary, you can set PYTHONHOME and PYTHONPATH explicitly for Python.NET
PythonEngine.PythonHome = pythonHome;
PythonEngine.PythonPath = pythonPath;

// Confirm if the Python initialization succeeded
Console.WriteLine("Python Runtime Initialized.");
Console.WriteLine($"Python Version: {PythonEngine.Version}");

// Ensure Python is finalized correctly when the application shuts down
AppDomain.CurrentDomain.ProcessExit += (s, e) => PythonEngine.Shutdown();

var redis = builder.AddRedis("redis");

var backend = builder.AddProject<Backend>("backend")
    .WithReference(backendDb)
    .WithReference(chatCompletion)
    .WithReference(blobStorage)
    .WithReference(vectorDb)
    .WithReference(pythonInference)
    .WithReference(redis)
    .WithEnvironment("IdentityUrl", identityEndpoint)
    .WithEnvironment("ImportInitialDataDir", Path.Combine(builder.AppHostDirectory, "..", "..", "seeddata", isE2ETest ? "test" : "dev"));

var staffWebUi = builder.AddProject<StaffWebUI>("staffwebui")
    .WithExternalHttpEndpoints()
    .WithReference(backend)
    .WithReference(redis)
    .WithEnvironment("IdentityUrl", identityEndpoint);

var customerWebUi = builder.AddProject<CustomerWebUI>("customerwebui")
    .WithReference(backend)
    .WithEnvironment("IdentityUrl", identityEndpoint);

// Circular references: IdentityServer needs to know the endpoints of the web UIs
identityServer
    .WithEnvironment("CustomerWebUIEndpoint", customerWebUi.GetEndpoint("https"))
    .WithEnvironment("StaffWebUIEndpoint", staffWebUi.GetEndpoint("https"));

if (!isE2ETest)
{
    postgresServer.WithDataVolume();
    vectorDb.WithVolume("eshopsupport-vector-db-storage", "/qdrant/storage");
}

builder.Build().Run();
